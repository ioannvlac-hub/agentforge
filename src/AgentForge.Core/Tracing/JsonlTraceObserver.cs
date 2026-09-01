using System.Text.Json;
using AgentForge.Core.Tools;

namespace AgentForge.Core.Tracing;

/// <summary>
/// Appends one JSON object per event to a file. Traces are the raw material for
/// evaluation, debugging and cost analysis; the Python harness reads them directly.
/// </summary>
public sealed class JsonlTraceObserver(string path) : IAgentObserver, IDisposable
{
    private readonly StreamWriter _writer = new(path, append: true) { AutoFlush = true };
    private readonly string _runId = Guid.NewGuid().ToString("N")[..12];

    public void OnStep(int step, LlmResponse response) => Write(new
    {
        type = "step", step, stop_reason = response.StopReason.ToString(),
        text = response.Text, tool_calls = response.ToolCalls.Select(c => c.Name),
        input_tokens = response.Usage.InputTokens, output_tokens = response.Usage.OutputTokens
    });

    public void OnToolCall(int step, ToolCallBlock call) => Write(new
    {
        type = "tool_call", step, call.Id, call.Name, input = call.Input
    });

    public void OnToolResult(int step, ToolCallBlock call, ToolResult result, TimeSpan elapsed) => Write(new
    {
        type = "tool_result", step, call.Id, call.Name, result.IsError,
        elapsed_ms = (int)elapsed.TotalMilliseconds, content = Truncate(result.Content, 2000)
    });

    public void OnFinished(AgentRunResult result) => Write(new
    {
        type = "finished", reason = result.Reason.ToString(), steps = result.Steps,
        input_tokens = result.Usage.InputTokens, output_tokens = result.Usage.OutputTokens,
        final_answer = result.FinalAnswer
    });

    private void Write(object payload)
    {
        var envelope = new { run_id = _runId, at = DateTimeOffset.UtcNow, payload };
        _writer.WriteLine(JsonSerializer.Serialize(envelope));
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...";

    public void Dispose() => _writer.Dispose();
}
