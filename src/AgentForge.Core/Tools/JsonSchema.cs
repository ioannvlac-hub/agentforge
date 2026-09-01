using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentForge.Core.Tools;

/// <summary>Tiny builder for the JSON Schema objects tools expose. Keeps schemas honest and typo-free.</summary>
public static class JsonSchema
{
    public sealed record Property(string Name, string Type, string Description, bool Required = true);

    public static JsonElement Object(params Property[] properties)
    {
        var props = new JsonObject();
        var required = new JsonArray();

        foreach (var p in properties)
        {
            props[p.Name] = new JsonObject { ["type"] = p.Type, ["description"] = p.Description };
            if (p.Required) required.Add(p.Name);
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = props,
            ["required"] = required
        };

        return JsonSerializer.SerializeToElement(schema);
    }

    public static string? GetString(this JsonElement input, string name) =>
        input.ValueKind == JsonValueKind.Object && input.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    public static int? GetInt(this JsonElement input, string name) =>
        input.ValueKind == JsonValueKind.Object && input.TryGetProperty(name, out var v) && v.TryGetInt32(out var i)
            ? i
            : null;
}
