using System.Diagnostics;
using AgentForge.Core.Tools;
using AgentForge.Core.Tracing;

namespace AgentForge.Core;

public sealed class AgentOptions
{
    public string SystemPrompt { get; init; } =
        "You are a careful, capable assistant. Use the available tools when they help; " +
        "never guess at facts you could verify with a tool. When the task is complete, " +
        "reply with a clear final answer and nothing else.";

    /// <summary>Hard cap on model round-trips. Prevents runaway loops regardless of what the model does.</summary>
    public int MaxSteps { get; init; } = 10;

    public int MaxTokensPerResponse { get; init; } = 2048;

    /// <summary>Total input plus output tokens across the whole run before the agent stops.</summary>
    public int TokenBudget { get; init; } = 200_000;

    public string WorkingDirectory { get; init; } = Directory.GetCurrentDirectory();
}

public enum AgentStopReason { Completed, StepLimit, TokenBudget, Cancelled, Error }

public sealed record AgentRunResult(
    string FinalAnswer,
    int Steps,
    TokenUsage Usage,
    AgentStopReason Reason,
    IReadOnlyList<ChatMessage> Transcript);

/// <summary>
/// The agent loop. Deliberately small and readable: ask the model, run whatever tools
/// it asked for (subject to approval), feed the results back, repeat until the model
/// answers without tool calls or a budget is exhausted.
/// </summary>
public sealed class Agent(
    ILlmClient llm,
    ToolRegistry tools,
    IApprovalPolicy approval,
    AgentOptions options,
    IAgentObserver? observer = null)
{
    private readonly IAgentObserver _observer = observer ?? NullObserver.Instance;

    public async Task<AgentRunResult> RunAsync(string task, CancellationToken ct = default)
    {
        var messages = new List<ChatMessage> { ChatMessage.FromUser(task) };
        var usage = TokenUsage.Zero;
        var context = new ToolContext { WorkingDirectory = options.WorkingDirectory };
        var lastText = string.Empty;
        var step = 0;

        try
        {
            for (step = 1; step <= options.MaxSteps; step++)
            {
                ct.ThrowIfCancellationRequested();

                var request = new LlmRequest(options.SystemPrompt, messages, tools.Definitions(), options.MaxTokensPerResponse);
                var response = await llm.CompleteAsync(request, ct);

                usage += response.Usage;
                messages.Add(new ChatMessage(Role.Assistant, response.Content));
                _observer.OnStep(step, response);

                if (!string.IsNullOrWhiteSpace(response.Text))
                    lastText = response.Text;

                if (response.ToolCalls.Count == 0)
                    return Finish(lastText, step, usage, AgentStopReason.Completed, messages);

                if (usage.Total > options.TokenBudget)
                    return Finish(lastText, step, usage, AgentStopReason.TokenBudget, messages);

                var results = new List<ContentBlock>();
                foreach (var call in response.ToolCalls)
                {
                    _observer.OnToolCall(step, call);
                    var watch = Stopwatch.StartNew();
                    var result = await ExecuteToolAsync(call, context, ct);
                    _observer.OnToolResult(step, call, result, watch.Elapsed);
                    results.Add(new ToolResultBlock(call.Id, result.Content, result.IsError));
                }

                messages.Add(new ChatMessage(Role.User, results));
            }

            return Finish(lastText, options.MaxSteps, usage, AgentStopReason.StepLimit, messages);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Finish(lastText, step, usage, AgentStopReason.Cancelled, messages);
        }
        catch (Exception ex)
        {
            return Finish($"Agent failed: {ex.Message}", step, usage, AgentStopReason.Error, messages);
        }
    }

    private async Task<ToolResult> ExecuteToolAsync(ToolCallBlock call, ToolContext context, CancellationToken ct)
    {
        var tool = tools.Find(call.Name);
        if (tool is null)
            return ToolResult.Error($"Unknown tool '{call.Name}'. Available tools: {string.Join(", ", tools.Names)}.");

        if (tool.RequiresApproval && !await approval.ApproveAsync(call, tool, ct))
            return ToolResult.Error($"The operator denied permission to run '{call.Name}'. Do not retry it; explain what you would have done instead.");

        try
        {
            return await tool.ExecuteAsync(call.Input, context, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Tool '{call.Name}' threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    private AgentRunResult Finish(string text, int steps, TokenUsage usage, AgentStopReason reason, List<ChatMessage> transcript)
    {
        var result = new AgentRunResult(text, steps, usage, reason, transcript.AsReadOnly());
        _observer.OnFinished(result);
        return result;
    }
}
