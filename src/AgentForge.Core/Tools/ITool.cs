using System.Text.Json;

namespace AgentForge.Core.Tools;

public sealed record ToolResult(string Content, bool IsError = false)
{
    public static ToolResult Ok(string content) => new(content);
    public static ToolResult Error(string message) => new(message, IsError: true);
}

public sealed class ToolContext
{
    /// <summary>Sandbox root. Tools that touch the file system must never escape it.</summary>
    public required string WorkingDirectory { get; init; }
}

/// <summary>
/// A capability the model may invoke. Tools declare a JSON schema for their input so
/// the model knows how to call them, and flag whether a human should approve first.
/// </summary>
public interface ITool
{
    string Name { get; }
    string Description { get; }
    JsonElement InputSchema { get; }

    /// <summary>True for anything with side effects: writing files, running code, sending messages.</summary>
    bool RequiresApproval { get; }

    Task<ToolResult> ExecuteAsync(JsonElement input, ToolContext context, CancellationToken ct = default);
}
