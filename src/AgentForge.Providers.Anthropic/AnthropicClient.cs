using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgentForge.Core;

namespace AgentForge.Providers.Anthropic;

public sealed class AnthropicOptions
{
    public required string ApiKey { get; init; }
    public string Model { get; init; } = "claude-sonnet-4-5";
    public string ApiVersion { get; init; } = "2023-06-01";
    public Uri BaseAddress { get; init; } = new("https://api.anthropic.com/");
    public int MaxRetries { get; init; } = 3;
}

/// <summary>
/// ILlmClient over the Anthropic Messages API using raw HTTP and System.Text.Json.
/// No SDK dependency: the wire format is small, stable and worth understanding.
/// Handles tool_use / tool_result blocks and retries transient failures with backoff.
/// </summary>
public sealed class AnthropicClient(HttpClient http, AnthropicOptions options) : ILlmClient
{
    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        var body = BuildRequestBody(request);

        for (var attempt = 1; ; attempt++)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(options.BaseAddress, "v1/messages"))
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
            };
            message.Headers.Add("x-api-key", options.ApiKey);
            message.Headers.Add("anthropic-version", options.ApiVersion);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await http.SendAsync(message, ct);
            var json = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
                return ParseResponse(json);

            var transient = response.StatusCode is HttpStatusCode.TooManyRequests
                or HttpStatusCode.InternalServerError
                or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable
                or (HttpStatusCode)529;

            if (!transient || attempt >= options.MaxRetries)
                throw new HttpRequestException($"Anthropic API returned {(int)response.StatusCode}: {json}");

            var delay = TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1) + Random.Shared.Next(0, 250));
            await Task.Delay(delay, ct);
        }
    }

    private JsonObject BuildRequestBody(LlmRequest request)
    {
        var body = new JsonObject
        {
            ["model"] = options.Model,
            ["max_tokens"] = request.MaxTokens,
            ["system"] = request.SystemPrompt,
            ["messages"] = new JsonArray(request.Messages.Select(ToWireMessage).ToArray())
        };

        if (request.Tools.Count > 0)
        {
            body["tools"] = new JsonArray(request.Tools.Select(t => (JsonNode)new JsonObject
            {
                ["name"] = t.Name,
                ["description"] = t.Description,
                ["input_schema"] = JsonNode.Parse(t.InputSchema.GetRawText())
            }).ToArray());
        }

        return body;
    }

    private static JsonNode ToWireMessage(ChatMessage message) => new JsonObject
    {
        ["role"] = message.Role == Role.User ? "user" : "assistant",
        ["content"] = new JsonArray(message.Content.Select(ToWireBlock).ToArray())
    };

    private static JsonNode ToWireBlock(ContentBlock block) => block switch
    {
        TextBlock t => new JsonObject { ["type"] = "text", ["text"] = t.Text },
        ToolCallBlock c => new JsonObject
        {
            ["type"] = "tool_use",
            ["id"] = c.Id,
            ["name"] = c.Name,
            ["input"] = JsonNode.Parse(c.Input.GetRawText())
        },
        ToolResultBlock r => new JsonObject
        {
            ["type"] = "tool_result",
            ["tool_use_id"] = r.ToolCallId,
            ["content"] = r.Content,
            ["is_error"] = r.IsError
        },
        _ => throw new NotSupportedException($"Unsupported content block {block.GetType().Name}")
    };

    private static LlmResponse ParseResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var blocks = new List<ContentBlock>();
        foreach (var item in root.GetProperty("content").EnumerateArray())
        {
            switch (item.GetProperty("type").GetString())
            {
                case "text":
                    blocks.Add(new TextBlock(item.GetProperty("text").GetString() ?? string.Empty));
                    break;
                case "tool_use":
                    blocks.Add(new ToolCallBlock(
                        item.GetProperty("id").GetString()!,
                        item.GetProperty("name").GetString()!,
                        item.GetProperty("input").Clone()));
                    break;
            }
        }

        var stop = root.TryGetProperty("stop_reason", out var s) ? s.GetString() : null;
        var stopReason = stop switch
        {
            "end_turn" => StopReason.EndTurn,
            "tool_use" => StopReason.ToolUse,
            "max_tokens" => StopReason.MaxTokens,
            _ => StopReason.Other
        };

        var usage = root.TryGetProperty("usage", out var u)
            ? new TokenUsage(u.GetProperty("input_tokens").GetInt32(), u.GetProperty("output_tokens").GetInt32())
            : TokenUsage.Zero;

        return new LlmResponse(blocks, stopReason, usage);
    }
}
