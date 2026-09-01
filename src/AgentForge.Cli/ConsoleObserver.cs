using AgentForge.Core;
using AgentForge.Core.Tools;
using AgentForge.Core.Tracing;

namespace AgentForge.Cli;

public sealed class ConsoleObserver : IAgentObserver
{
    public void OnStep(int step, LlmResponse response)
    {
        Write(ConsoleColor.Cyan, $"[step {step}] {response.StopReason} ({response.Usage.InputTokens} in / {response.Usage.OutputTokens} out)");
        if (!string.IsNullOrWhiteSpace(response.Text))
            Console.Error.WriteLine($"  model: {Truncate(response.Text, 300)}");
    }

    public void OnToolCall(int step, ToolCallBlock call) =>
        Write(ConsoleColor.Magenta, $"  -> {call.Name} {Truncate(call.Input.GetRawText(), 200)}");

    public void OnToolResult(int step, ToolCallBlock call, ToolResult result, TimeSpan elapsed) =>
        Write(result.IsError ? ConsoleColor.Red : ConsoleColor.Green,
            $"  <- {call.Name} ({elapsed.TotalMilliseconds:F0}ms) {Truncate(result.Content, 200)}");

    public void OnFinished(AgentRunResult result) =>
        Write(ConsoleColor.Cyan, $"[done] {result.Reason} in {result.Steps} step(s), {result.Usage.Total} tokens");

    private static void Write(ConsoleColor color, string message)
    {
        Console.ForegroundColor = color;
        Console.Error.WriteLine(message);
        Console.ResetColor();
    }

    private static string Truncate(string s, int max)
    {
        s = s.Replace('\n', ' ');
        return s.Length <= max ? s : s[..max] + "...";
    }
}
