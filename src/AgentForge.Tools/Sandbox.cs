namespace AgentForge.Tools;

/// <summary>Resolves a model-supplied path inside the working directory, or refuses.</summary>
internal static class Sandbox
{
    public static string Resolve(string workingDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("A path is required.");

        var root = Path.GetFullPath(workingDirectory);
        var full = Path.GetFullPath(Path.Combine(root, relativePath));

        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) && full != root)
            throw new UnauthorizedAccessException($"Path '{relativePath}' escapes the working directory.");

        return full;
    }
}
