using System.Text.Json;
using AgentForge.Cli;
using AgentForge.Core;
using AgentForge.Core.Tools;
using AgentForge.Core.Tracing;
using AgentForge.Providers.Anthropic;
using AgentForge.Tools;

var cli = CliArgs.Parse(args);
if (cli.ShowHelp)
{
    Console.WriteLine("""
        agentforge - run a tool-using agent against a task

        Usage: agentforge --task "<task>" [options]

          --task <text>        The task to perform (required)
          --workdir <path>     Sandbox directory for file and Python tools (default: current dir)
          --max-steps <n>      Model round-trip limit (default: 10)
          --model <id>         Model id (default: $AGENTFORGE_MODEL or claude-sonnet-4-5)
          --auto-approve       Skip the human approval prompt for side-effect tools
          --json               Emit a single JSON result on stdout (for the eval harness)
          --trace <file>       Append a JSONL trace of the run to this file
          --scripts <dir>      Directory of reviewed Python scripts to expose as tools (default: ./python/tools)

        Requires ANTHROPIC_API_KEY in the environment.
        """);
    return 0;
}

var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("error: ANTHROPIC_API_KEY is not set.");
    return 2;
}

var workdir = Path.GetFullPath(cli.WorkDir);
Directory.CreateDirectory(workdir);

var tools = new ToolRegistry()
    .Add(new CalculatorTool())
    .Add(new ListFilesTool())
    .Add(new ReadFileTool())
    .Add(new WriteFileTool())
    .Add(new PythonTool());

// Reviewed Python scripts become named tools with their own schemas.
var scriptsDir = Path.GetFullPath(cli.ScriptsDir);
var csvScript = Path.Combine(scriptsDir, "analyze_csv.py");
if (File.Exists(csvScript))
{
    tools.Add(new PythonScriptTool(
        "analyze_csv",
        "Summarise a CSV file: row count, columns, and min/max/mean for numeric columns. Faster and safer than writing Python by hand.",
        csvScript,
        JsonSchema.Object(new JsonSchema.Property("path", "string", "CSV path relative to the working directory."))));
}

var llm = new AnthropicClient(new HttpClient { Timeout = TimeSpan.FromSeconds(120) }, new AnthropicOptions
{
    ApiKey = apiKey,
    Model = cli.Model
});

IApprovalPolicy approval = cli.AutoApprove ? new AutoApprovePolicy() : new ConsoleApprovalPolicy();

var observers = new List<IAgentObserver>();
if (!cli.Json) observers.Add(new ConsoleObserver());
JsonlTraceObserver? trace = null;
if (cli.TracePath is not null)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(cli.TracePath))!);
    trace = new JsonlTraceObserver(cli.TracePath);
    observers.Add(trace);
}

var agent = new Agent(llm, tools, approval, new AgentOptions
{
    MaxSteps = cli.MaxSteps,
    WorkingDirectory = workdir
}, new CompositeObserver(observers.ToArray()));

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var result = await agent.RunAsync(cli.Task!, cts.Token);
trace?.Dispose();

if (cli.Json)
{
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        final_answer = result.FinalAnswer,
        steps = result.Steps,
        stop_reason = result.Reason.ToString(),
        input_tokens = result.Usage.InputTokens,
        output_tokens = result.Usage.OutputTokens
    }));
}
else
{
    Console.WriteLine();
    Console.WriteLine(result.FinalAnswer);
}

return result.Reason is AgentStopReason.Completed ? 0 : 1;
