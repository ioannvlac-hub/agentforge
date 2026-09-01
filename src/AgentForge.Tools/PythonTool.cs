using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AgentForge.Core.Tools;

namespace AgentForge.Tools;

/// <summary>
/// Lets the model run arbitrary Python inside the working directory, with a wall-clock
/// timeout and output cap. Requires approval every time: this is the most powerful tool
/// in the box and the one an operator most wants to see before it runs.
/// </summary>
public sealed class PythonTool(string pythonExecutable = "python3", int timeoutSeconds = 30) : ITool
{
    private const int MaxOutputChars = 8_000;

    public string Name => "run_python";
    public string Description => "Execute a Python 3 script (standard library only) in the working directory and return stdout and stderr. Use print() to emit results. Requires operator approval.";
    public JsonElement InputSchema { get; } = JsonSchema.Object(
        new JsonSchema.Property("code", "string", "Complete Python source to execute."));
    public bool RequiresApproval => true;

    public async Task<ToolResult> ExecuteAsync(JsonElement input, ToolContext context, CancellationToken ct = default)
    {
        var code = input.GetString("code");
        if (string.IsNullOrWhiteSpace(code))
            return ToolResult.Error("code is required");

        var scratch = Path.Combine(context.WorkingDirectory, ".agentforge");
        Directory.CreateDirectory(scratch);
        var scriptPath = Path.Combine(scratch, $"run_{Guid.NewGuid():N}.py");
        await File.WriteAllTextAsync(scriptPath, code, Encoding.UTF8, ct);

        try
        {
            var (stdout, stderr, exitCode, timedOut) = await ProcessRunner.RunAsync(
                pythonExecutable, [scriptPath], context.WorkingDirectory, stdin: null,
                TimeSpan.FromSeconds(timeoutSeconds), ct);

            if (timedOut)
                return ToolResult.Error($"Python script exceeded the {timeoutSeconds}s timeout and was killed.");

            var output = new StringBuilder();
            if (stdout.Length > 0) output.Append(stdout);
            if (stderr.Length > 0) output.Append("\n[stderr]\n").Append(stderr);
            if (output.Length == 0) output.Append("(no output)");

            var text = output.ToString();
            if (text.Length > MaxOutputChars) text = text[..MaxOutputChars] + "\n...[truncated]";

            return exitCode == 0 ? ToolResult.Ok(text) : ToolResult.Error($"Exit code {exitCode}\n{text}");
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { /* best effort */ }
        }
    }
}

/// <summary>
/// Exposes a specific, reviewed Python script as a first-class tool with its own schema.
/// Input arrives on stdin as JSON; the script prints JSON. Because the code is fixed and
/// reviewed, it does not require approval, unlike run_python.
/// </summary>
public sealed class PythonScriptTool(
    string name,
    string description,
    string scriptPath,
    JsonElement inputSchema,
    string pythonExecutable = "python3",
    int timeoutSeconds = 30) : ITool
{
    public string Name => name;
    public string Description => description;
    public JsonElement InputSchema => inputSchema;
    public bool RequiresApproval => false;

    public async Task<ToolResult> ExecuteAsync(JsonElement input, ToolContext context, CancellationToken ct = default)
    {
        if (!File.Exists(scriptPath))
            return ToolResult.Error($"Script not found: {scriptPath}");

        var (stdout, stderr, exitCode, timedOut) = await ProcessRunner.RunAsync(
            pythonExecutable, [scriptPath], context.WorkingDirectory, stdin: input.GetRawText(),
            TimeSpan.FromSeconds(timeoutSeconds), ct);

        if (timedOut)
            return ToolResult.Error($"Script exceeded the {timeoutSeconds}s timeout.");

        return exitCode == 0
            ? ToolResult.Ok(stdout.Trim())
            : ToolResult.Error($"Exit code {exitCode}: {stderr.Trim()}");
    }
}

internal static class ProcessRunner
{
    public static async Task<(string Stdout, string Stderr, int ExitCode, bool TimedOut)> RunAsync(
        string fileName, string[] args, string workingDirectory, string? stdin, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start '{fileName}'.");

        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin);
            process.StandardInput.Close();
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            if (ct.IsCancellationRequested) throw;
            return (string.Empty, string.Empty, -1, true);
        }

        return (await stdoutTask, await stderrTask, process.ExitCode, false);
    }
}
