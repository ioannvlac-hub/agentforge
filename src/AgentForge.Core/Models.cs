using System.Text.Json;

namespace AgentForge.Core;

public enum Role { User, Assistant }

/// <summary>Provider-neutral message content. Providers translate to and from their wire format.</summary>
public abstract record ContentBlock;

public sealed record TextBlock(string Text) : ContentBlock;

public sealed record ToolCallBlock(string Id, string Name, JsonElement Input) : ContentBlock;

public sealed record ToolResultBlock(string ToolCallId, string Content, bool IsError = false) : ContentBlock;

public sealed record ChatMessage(Role Role, IReadOnlyList<ContentBlock> Content)
{
    public static ChatMessage FromUser(string text) => new(Role.User, [new TextBlock(text)]);

    public string Text => string.Concat(Content.OfType<TextBlock>().Select(t => t.Text));
}

public enum StopReason { EndTurn, ToolUse, MaxTokens, Other }

public sealed record TokenUsage(int InputTokens, int OutputTokens)
{
    public static readonly TokenUsage Zero = new(0, 0);
    public int Total => InputTokens + OutputTokens;
    public static TokenUsage operator +(TokenUsage a, TokenUsage b) =>
        new(a.InputTokens + b.InputTokens, a.OutputTokens + b.OutputTokens);
}

public sealed record ToolDefinition(string Name, string Description, JsonElement InputSchema);

public sealed record LlmRequest(
    string SystemPrompt,
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<ToolDefinition> Tools,
    int MaxTokens = 2048);

public sealed record LlmResponse(IReadOnlyList<ContentBlock> Content, StopReason StopReason, TokenUsage Usage)
{
    public string Text => string.Concat(Content.OfType<TextBlock>().Select(t => t.Text));
    public IReadOnlyList<ToolCallBlock> ToolCalls => Content.OfType<ToolCallBlock>().ToList();
}

/// <summary>The one seam between the agent loop and any model provider.</summary>
public interface ILlmClient
{
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default);
}
