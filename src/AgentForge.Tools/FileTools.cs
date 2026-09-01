using System.Text;
using System.Text.Json;
using AgentForge.Core.Tools;

namespace AgentForge.Tools;

public sealed class ListFilesTool : ITool
{
    public string Name => "list_files";
    public string Description => "List files and folders under a directory inside the working directory. Use '.' for the root.";
    public JsonElement InputSchema { get; } = JsonSchema.Object(
        new JsonSchema.Property("path", "string", "Directory path relative to the working directory."));
    public bool RequiresApproval => false;

    public Task<ToolResult> ExecuteAsync(JsonElement input, ToolContext context, CancellationToken ct = default)
    {
        var path = Sandbox.Resolve(context.WorkingDirectory, input.GetString("path") ?? ".");
        if (!Directory.Exists(path))
            return Task.FromResult(ToolResult.Error("Directory not found."));

        var entries = Directory.EnumerateFileSystemEntries(path)
            .Where(e => !Path.GetFileName(e).StartsWith('.'))
            .OrderBy(e => e)
            .Select(e => Directory.Exists(e) ? Path.GetFileName(e) + "/" : $"{Path.GetFileName(e)} ({new FileInfo(e).Length} bytes)");

        return Task.FromResult(ToolResult.Ok(string.Join('\n', entries) is { Length: > 0 } list ? list : "(empty)"));
    }
}

public sealed class ReadFileTool : ITool
{
    private const int MaxChars = 20_000;

    public string Name => "read_file";
    public string Description => "Read a UTF-8 text file inside the working directory. Large files are truncated.";
    public JsonElement InputSchema { get; } = JsonSchema.Object(
        new JsonSchema.Property("path", "string", "File path relative to the working directory."));
    public bool RequiresApproval => false;

    public async Task<ToolResult> ExecuteAsync(JsonElement input, ToolContext context, CancellationToken ct = default)
    {
        var path = Sandbox.Resolve(context.WorkingDirectory, input.GetString("path") ?? string.Empty);
        if (!File.Exists(path))
            return ToolResult.Error("File not found.");

        var text = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
        return ToolResult.Ok(text.Length > MaxChars ? text[..MaxChars] + $"\n...[truncated {text.Length - MaxChars} chars]" : text);
    }
}

public sealed class WriteFileTool : ITool
{
    public string Name => "write_file";
    public string Description => "Create or overwrite a UTF-8 text file inside the working directory. Requires operator approval.";
    public JsonElement InputSchema { get; } = JsonSchema.Object(
        new JsonSchema.Property("path", "string", "File path relative to the working directory."),
        new JsonSchema.Property("content", "string", "The full file content."));
    public bool RequiresApproval => true;

    public async Task<ToolResult> ExecuteAsync(JsonElement input, ToolContext context, CancellationToken ct = default)
    {
        var path = Sandbox.Resolve(context.WorkingDirectory, input.GetString("path") ?? string.Empty);
        var content = input.GetString("content") ?? string.Empty;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, Encoding.UTF8, ct);
        return ToolResult.Ok($"Wrote {content.Length} characters to {Path.GetRelativePath(context.WorkingDirectory, path)}.");
    }
}
