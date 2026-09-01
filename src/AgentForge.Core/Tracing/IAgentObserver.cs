using AgentForge.Core.Tools;

namespace AgentForge.Core.Tracing;

/// <summary>Receives every significant event in a run. Used for console output, JSONL traces and tests.</summary>
public interface IAgentObserver
{
    void OnStep(int step, LlmResponse response);
    void OnToolCall(int step, ToolCallBlock call);
    void OnToolResult(int step, ToolCallBlock call, ToolResult result, TimeSpan elapsed);
    void OnFinished(AgentRunResult result);
}

public sealed class NullObserver : IAgentObserver
{
    public static readonly NullObserver Instance = new();
    public void OnStep(int step, LlmResponse response) { }
    public void OnToolCall(int step, ToolCallBlock call) { }
    public void OnToolResult(int step, ToolCallBlock call, ToolResult result, TimeSpan elapsed) { }
    public void OnFinished(AgentRunResult result) { }
}

public sealed class CompositeObserver(params IAgentObserver[] observers) : IAgentObserver
{
    public void OnStep(int step, LlmResponse response) { foreach (var o in observers) o.OnStep(step, response); }
    public void OnToolCall(int step, ToolCallBlock call) { foreach (var o in observers) o.OnToolCall(step, call); }
    public void OnToolResult(int step, ToolCallBlock call, ToolResult result, TimeSpan elapsed) { foreach (var o in observers) o.OnToolResult(step, call, result, elapsed); }
    public void OnFinished(AgentRunResult result) { foreach (var o in observers) o.OnFinished(result); }
}
