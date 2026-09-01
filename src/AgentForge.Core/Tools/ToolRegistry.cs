namespace AgentForge.Core.Tools;

public sealed class ToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new(StringComparer.Ordinal);

    public ToolRegistry Add(ITool tool)
    {
        if (!_tools.TryAdd(tool.Name, tool))
            throw new InvalidOperationException($"A tool named '{tool.Name}' is already registered.");
        return this;
    }

    public ITool? Find(string name) => _tools.GetValueOrDefault(name);

    public IReadOnlyList<ToolDefinition> Definitions() =>
        _tools.Values.Select(t => new ToolDefinition(t.Name, t.Description, t.InputSchema)).ToList();

    public IReadOnlyCollection<string> Names => _tools.Keys;
}
