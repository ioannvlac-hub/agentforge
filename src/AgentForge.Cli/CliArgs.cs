namespace AgentForge.Cli;

public sealed class CliArgs
{
    public string? Task { get; private set; }
    public string WorkDir { get; private set; } = Directory.GetCurrentDirectory();
    public string ScriptsDir { get; private set; } = Path.Combine(Directory.GetCurrentDirectory(), "python", "tools");
    public int MaxSteps { get; private set; } = 10;
    public string Model { get; private set; } = Environment.GetEnvironmentVariable("AGENTFORGE_MODEL") ?? "claude-sonnet-4-5";
    public bool AutoApprove { get; private set; }
    public bool Json { get; private set; }
    public string? TracePath { get; private set; }
    public bool ShowHelp { get; private set; }

    public static CliArgs Parse(string[] args)
    {
        var result = new CliArgs();
        if (args.Length == 0) { result.ShowHelp = true; return result; }

        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} requires a value");

            switch (args[i])
            {
                case "--task": result.Task = Next(); break;
                case "--workdir": result.WorkDir = Next(); break;
                case "--scripts": result.ScriptsDir = Next(); break;
                case "--max-steps": result.MaxSteps = int.Parse(Next()); break;
                case "--model": result.Model = Next(); break;
                case "--auto-approve": result.AutoApprove = true; break;
                case "--json": result.Json = true; break;
                case "--trace": result.TracePath = Next(); break;
                case "-h" or "--help": result.ShowHelp = true; break;
                default: throw new ArgumentException($"Unknown argument '{args[i]}'");
            }
        }

        if (result.Task is null && !result.ShowHelp)
            throw new ArgumentException("--task is required");

        return result;
    }
}
