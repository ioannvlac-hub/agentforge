using System.Text.Json;
using AgentForge.Core;

namespace AgentForge.Tests;

/// <summary>Scripted model: returns queued responses and records every request it received.</summary>
public sealed class FakeLlmClient : ILlmClient
{
    private readonly Queue<LlmResponse> _responses = new();
    public List<LlmRequest> Requests { get; } = [];

    public FakeLlmClient Reply(string text) =>
        Enqueue(new LlmResponse([new TextBlock(text)], StopReason.EndTurn, new TokenUsage(10, 5)));

    public FakeLlmClient CallTool(string name, object input, string? text = null, string id = "call_1")
    {
        var blocks = new List<ContentBlock>();
        if (text is not null) blocks.Add(new TextBlock(text));
        blocks.Add(new ToolCallBlock(id, name, JsonSerializer.SerializeToElement(input)));
        return Enqueue(new LlmResponse(blocks, StopReason.ToolUse, new TokenUsage(10, 5)));
    }

    private FakeLlmClient Enqueue(LlmResponse response)
    {
        _responses.Enqueue(response);
        return this;
    }

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        Requests.Add(request);
        if (_responses.Count == 0)
            throw new InvalidOperationException("FakeLlmClient ran out of scripted responses.");
        return Task.FromResult(_responses.Dequeue());
    }
}
