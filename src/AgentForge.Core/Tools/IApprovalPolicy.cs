namespace AgentForge.Core.Tools;

/// <summary>
/// Human-in-the-loop gate. Consulted before any tool with RequiresApproval runs.
/// The agent loop never bypasses it; a denial is reported back to the model as a tool error.
/// </summary>
public interface IApprovalPolicy
{
    Task<bool> ApproveAsync(ToolCallBlock call, ITool tool, CancellationToken ct = default);
}

public sealed class AutoApprovePolicy : IApprovalPolicy
{
    public Task<bool> ApproveAsync(ToolCallBlock call, ITool tool, CancellationToken ct = default) => Task.FromResult(true);
}

public sealed class DenyAllPolicy : IApprovalPolicy
{
    public Task<bool> ApproveAsync(ToolCallBlock call, ITool tool, CancellationToken ct = default) => Task.FromResult(false);
}
