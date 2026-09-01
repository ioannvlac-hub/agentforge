using AgentForge.Core;
using AgentForge.Core.Tools;
using AgentForge.Tools;
using Xunit;

namespace AgentForge.Tests;

public class AgentLoopTests
{
    private static Agent Build(FakeLlmClient llm, IApprovalPolicy? approval = null, int maxSteps = 10, ToolRegistry? tools = null)
    {
        tools ??= new ToolRegistry().Add(new CalculatorTool()).Add(new WriteFileTool());
        var options = new AgentOptions { MaxSteps = maxSteps, WorkingDirectory = Path.GetTempPath() };
        return new Agent(llm, tools, approval ?? new AutoApprovePolicy(), options);
    }

    [Fact]
    public async Task Completes_WhenModelAnswersWithoutTools()
    {
        var llm = new FakeLlmClient().Reply("42");

        var result = await Build(llm).RunAsync("What is the answer?");

        Assert.Equal(AgentStopReason.Completed, result.Reason);
        Assert.Equal("42", result.FinalAnswer);
        Assert.Equal(1, result.Steps);
        Assert.Single(llm.Requests);
    }

    [Fact]
    public async Task ExecutesTool_AndFeedsResultBackToModel()
    {
        var llm = new FakeLlmClient()
            .CallTool("calculate", new { expression = "6 * 7" })
            .Reply("The answer is 42.");

        var result = await Build(llm).RunAsync("Compute 6 times 7");

        Assert.Equal(AgentStopReason.Completed, result.Reason);
        Assert.Equal(2, result.Steps);

        // The second request must carry the tool result back to the model.
        var second = llm.Requests[1];
        var toolResult = second.Messages.Last().Content.OfType<ToolResultBlock>().Single();
        Assert.Equal("call_1", toolResult.ToolCallId);
        Assert.Equal("42", toolResult.Content);
        Assert.False(toolResult.IsError);
    }

    [Fact]
    public async Task UnknownTool_IsReportedAsErrorResult_NotException()
    {
        var llm = new FakeLlmClient()
            .CallTool("launch_rockets", new { count = 3 })
            .Reply("I could not do that.");

        var result = await Build(llm).RunAsync("Launch rockets");

        var toolResult = llm.Requests[1].Messages.Last().Content.OfType<ToolResultBlock>().Single();
        Assert.True(toolResult.IsError);
        Assert.Contains("Unknown tool", toolResult.Content);
        Assert.Equal(AgentStopReason.Completed, result.Reason);
    }

    [Fact]
    public async Task DeniedApproval_BlocksSideEffect_AndTellsModel()
    {
        var path = Path.Combine(Path.GetTempPath(), $"agentforge-{Guid.NewGuid():N}.txt");
        var llm = new FakeLlmClient()
            .CallTool("write_file", new { path = Path.GetFileName(path), content = "hello" })
            .Reply("Understood, I will not write the file.");

        await Build(llm, approval: new DenyAllPolicy()).RunAsync("Write a file");

        Assert.False(File.Exists(path));
        var toolResult = llm.Requests[1].Messages.Last().Content.OfType<ToolResultBlock>().Single();
        Assert.True(toolResult.IsError);
        Assert.Contains("denied", toolResult.Content);
    }

    [Fact]
    public async Task StepLimit_StopsRunawayLoop()
    {
        var llm = new FakeLlmClient();
        for (var i = 0; i < 5; i++)
            llm.CallTool("calculate", new { expression = "1 + 1" }, id: $"call_{i}");

        var result = await Build(llm, maxSteps: 3).RunAsync("Loop forever");

        Assert.Equal(AgentStopReason.StepLimit, result.Reason);
        Assert.Equal(3, result.Steps);
        Assert.Equal(3, llm.Requests.Count);
    }

    [Fact]
    public async Task ToolException_BecomesErrorResult_AndRunContinues()
    {
        var tools = new ToolRegistry().Add(new ThrowingTool());
        var llm = new FakeLlmClient()
            .CallTool("explode", new { })
            .Reply("Recovered.");

        var result = await Build(llm, tools: tools).RunAsync("Try the tool");

        var toolResult = llm.Requests[1].Messages.Last().Content.OfType<ToolResultBlock>().Single();
        Assert.True(toolResult.IsError);
        Assert.Contains("InvalidOperationException", toolResult.Content);
        Assert.Equal(AgentStopReason.Completed, result.Reason);
    }

    private sealed class ThrowingTool : ITool
    {
        public string Name => "explode";
        public string Description => "Always fails.";
        public System.Text.Json.JsonElement InputSchema { get; } = JsonSchema.Object();
        public bool RequiresApproval => false;
        public Task<ToolResult> ExecuteAsync(System.Text.Json.JsonElement input, ToolContext context, CancellationToken ct = default) =>
            throw new InvalidOperationException("boom");
    }
}
