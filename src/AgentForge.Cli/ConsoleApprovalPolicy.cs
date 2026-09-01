using AgentForge.Core;
using AgentForge.Core.Tools;

namespace AgentForge.Cli;

/// <summary>Shows the operator exactly what the model wants to run and waits for y/n.</summary>
public sealed class ConsoleApprovalPolicy : IApprovalPolicy
{
    public Task<bool> ApproveAsync(ToolCallBlock call, ITool tool, CancellationToken ct = default)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Error.WriteLine();
        Console.Error.WriteLine($"  The agent wants to run '{tool.Name}' with input:");
        Console.ResetColor();
        Console.Error.WriteLine(Indent(call.Input.GetRawText()));
        Console.Error.Write("  Allow? [y/N] ");

        var answer = Console.ReadLine();
        return Task.FromResult(answer?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static string Indent(string text) =>
        string.Join('\n', text.Split('\n').Select(l => "    " + l));
}
