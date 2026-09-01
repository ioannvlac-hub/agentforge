using System.Text.Json;
using AgentForge.Core.Tools;
using AgentForge.Tools;
using Xunit;

namespace AgentForge.Tests;

public class ToolTests
{
    private static JsonElement Input(object o) => JsonSerializer.SerializeToElement(o);
    private static readonly ToolContext Ctx = new() { WorkingDirectory = Path.GetTempPath() };

    [Theory]
    [InlineData("1 + 2 * 3", "7")]
    [InlineData("(1 + 2) * 3", "9")]
    [InlineData("2 ^ 10", "1024")]
    [InlineData("-4 + 10", "6")]
    [InlineData("sqrt(144)", "12")]
    [InlineData("(1200 * 1.19) / 12", "119")]
    [InlineData("10 % 4", "2")]
    public async Task Calculator_EvaluatesCorrectly(string expression, string expected)
    {
        var result = await new CalculatorTool().ExecuteAsync(Input(new { expression }), Ctx);

        Assert.False(result.IsError, result.Content);
        Assert.Equal(expected, result.Content);
    }

    [Theory]
    [InlineData("1 / 0")]
    [InlineData("2 +")]
    [InlineData("foo(3)")]
    [InlineData("(1 + 2")]
    public async Task Calculator_ReportsErrors(string expression)
    {
        var result = await new CalculatorTool().ExecuteAsync(Input(new { expression }), Ctx);
        Assert.True(result.IsError);
    }

    [Fact]
    public async Task ReadFile_RejectsPathTraversal()
    {
        var tool = new ReadFileTool();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            tool.ExecuteAsync(Input(new { path = "../../etc/passwd" }), Ctx));
    }

    [Fact]
    public async Task WriteThenRead_RoundTripsInsideSandbox()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"agentforge-{Guid.NewGuid():N}");
        var ctx = new ToolContext { WorkingDirectory = dir };

        var write = await new WriteFileTool().ExecuteAsync(Input(new { path = "notes/a.txt", content = "hello" }), ctx);
        Assert.False(write.IsError);

        var read = await new ReadFileTool().ExecuteAsync(Input(new { path = "notes/a.txt" }), ctx);
        Assert.Equal("hello", read.Content);

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Registry_RejectsDuplicateNames()
    {
        var registry = new ToolRegistry().Add(new CalculatorTool());
        Assert.Throws<InvalidOperationException>(() => registry.Add(new CalculatorTool()));
    }
}
