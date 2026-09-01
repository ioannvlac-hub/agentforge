using System.Globalization;
using System.Text.Json;
using AgentForge.Core.Tools;

namespace AgentForge.Tools;

/// <summary>
/// Arithmetic without eval(): a small recursive-descent parser supporting + - * / % ^,
/// parentheses, unary minus and a few functions. Models are bad at arithmetic; this fixes that.
/// </summary>
public sealed class CalculatorTool : ITool
{
    public string Name => "calculate";
    public string Description => "Evaluate an arithmetic expression exactly. Supports + - * / % ^, parentheses, sqrt(), abs(), round(). Example: (1200 * 1.19) / 12";
    public JsonElement InputSchema { get; } = JsonSchema.Object(
        new JsonSchema.Property("expression", "string", "The arithmetic expression to evaluate."));
    public bool RequiresApproval => false;

    public Task<ToolResult> ExecuteAsync(JsonElement input, ToolContext context, CancellationToken ct = default)
    {
        var expression = input.GetString("expression");
        if (string.IsNullOrWhiteSpace(expression))
            return Task.FromResult(ToolResult.Error("expression is required"));

        try
        {
            var value = new Parser(expression).Parse();
            return Task.FromResult(ToolResult.Ok(value.ToString("G15", CultureInfo.InvariantCulture)));
        }
        catch (Exception ex) when (ex is FormatException or DivideByZeroException or InvalidOperationException)
        {
            return Task.FromResult(ToolResult.Error(ex.Message));
        }
    }

    private sealed class Parser(string text)
    {
        private int _pos;

        public double Parse()
        {
            var value = Expression();
            SkipSpaces();
            if (_pos != text.Length)
                throw new FormatException($"Unexpected '{text[_pos]}' at position {_pos}.");
            return value;
        }

        private double Expression()
        {
            var left = Term();
            while (true)
            {
                SkipSpaces();
                if (Match('+')) left += Term();
                else if (Match('-')) left -= Term();
                else return left;
            }
        }

        private double Term()
        {
            var left = Power();
            while (true)
            {
                SkipSpaces();
                if (Match('*')) left *= Power();
                else if (Match('/'))
                {
                    var right = Power();
                    if (right == 0) throw new DivideByZeroException("Division by zero.");
                    left /= right;
                }
                else if (Match('%'))
                {
                    var right = Power();
                    if (right == 0) throw new DivideByZeroException("Modulo by zero.");
                    left %= right;
                }
                else return left;
            }
        }

        private double Power()
        {
            var b = Unary();
            SkipSpaces();
            return Match('^') ? Math.Pow(b, Power()) : b;
        }

        private double Unary()
        {
            SkipSpaces();
            if (Match('-')) return -Unary();
            if (Match('+')) return Unary();
            return Primary();
        }

        private double Primary()
        {
            SkipSpaces();
            if (Match('('))
            {
                var inner = Expression();
                SkipSpaces();
                if (!Match(')')) throw new FormatException("Missing closing parenthesis.");
                return inner;
            }

            if (_pos < text.Length && char.IsLetter(text[_pos]))
            {
                var start = _pos;
                while (_pos < text.Length && char.IsLetter(text[_pos])) _pos++;
                var name = text[start.._pos].ToLowerInvariant();
                SkipSpaces();
                if (!Match('(')) throw new FormatException($"Expected '(' after function '{name}'.");
                var arg = Expression();
                SkipSpaces();
                if (!Match(')')) throw new FormatException("Missing closing parenthesis.");
                return name switch
                {
                    "sqrt" => Math.Sqrt(arg),
                    "abs" => Math.Abs(arg),
                    "round" => Math.Round(arg, MidpointRounding.AwayFromZero),
                    _ => throw new InvalidOperationException($"Unknown function '{name}'.")
                };
            }

            var numberStart = _pos;
            while (_pos < text.Length && (char.IsDigit(text[_pos]) || text[_pos] == '.')) _pos++;
            if (numberStart == _pos)
                throw new FormatException(_pos < text.Length ? $"Unexpected '{text[_pos]}' at position {_pos}." : "Unexpected end of expression.");

            return double.Parse(text[numberStart.._pos], CultureInfo.InvariantCulture);
        }

        private bool Match(char c)
        {
            if (_pos < text.Length && text[_pos] == c) { _pos++; return true; }
            return false;
        }

        private void SkipSpaces()
        {
            while (_pos < text.Length && char.IsWhiteSpace(text[_pos])) _pos++;
        }
    }
}
