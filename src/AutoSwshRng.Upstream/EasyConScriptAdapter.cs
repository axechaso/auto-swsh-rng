using EasyCon.Script;
using EasyCon.Script.Syntax;
using EasyScript;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace AutoSwshRng.Upstream;

public static class EasyConScriptAdapter
{
    public static EasyConScriptResult Evaluate(string scriptText)
    {
        var output = new CapturingOutputAdapter();
        var compilation = Compilation.Create(SyntaxTree.Parse(scriptText));
        var result = compilation.Evaluate(output, pad: null!, ImmutableDictionary<string, Func<int>>.Empty, CancellationToken.None);

        return new EasyConScriptResult(
            result.Diagnostics.HasErrors(),
            result.Diagnostics.Select(diagnostic => diagnostic.Message).ToArray(),
            output.Printed,
            output.Alerted);
    }

    public static EasyConScriptFormatResult Format(string scriptText)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(scriptText));
        var diagnostics = compilation.Compile(ImmutableHashSet<string>.Empty);
        if (diagnostics.HasErrors())
        {
            return new EasyConScriptFormatResult(
                HasErrors: true,
                FormattedCode: null,
                Diagnostics: diagnostics.Select(diagnostic => diagnostic.Message).ToArray());
        }

        var formatted = compilation.FormatCode().Trim();
        formatted = Regex.Replace(formatted, @",(?! )", ", ");

        return new EasyConScriptFormatResult(
            HasErrors: false,
            FormattedCode: formatted,
            Diagnostics: []);
    }

    public static string ToggleCommentLines(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var separator = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Split([separator], StringSplitOptions.None);
        var shouldComment = lines.Any(CanComment);
        return string.Join(separator, lines.Select(line => ToggleCommentLine(line, shouldComment)));
    }

    private static bool CanComment(string input)
    {
        var firstNonWhitespaceIndex = FindFirstNonWhitespaceIndex(input);
        return firstNonWhitespaceIndex >= 0 && input[firstNonWhitespaceIndex] != '#';
    }

    private static string ToggleCommentLine(string input, bool shouldComment)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return input ?? string.Empty;
        }

        var firstNonWhitespaceIndex = FindFirstNonWhitespaceIndex(input);
        if (firstNonWhitespaceIndex < 0)
        {
            return input;
        }

        if (shouldComment)
        {
            return input.Insert(firstNonWhitespaceIndex, "# ");
        }

        var removeCount = firstNonWhitespaceIndex + 1 < input.Length && input[firstNonWhitespaceIndex + 1] == ' '
            ? 2
            : 1;
        return input.Remove(firstNonWhitespaceIndex, removeCount);
    }

    private static int FindFirstNonWhitespaceIndex(string input)
    {
        for (var i = 0; i < input.Length; i++)
        {
            if (!char.IsWhiteSpace(input[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private sealed class CapturingOutputAdapter : IOutputAdapter
    {
        private readonly List<string> printed = [];
        private readonly List<string> alerted = [];

        public IReadOnlyList<string> Printed => printed;

        public IReadOnlyList<string> Alerted => alerted;

        public void Print(string message, bool newline)
        {
            printed.Add(newline ? message + Environment.NewLine : message);
        }

        public void Alert(string message)
        {
            alerted.Add(message);
        }
    }
}

public sealed record EasyConScriptResult(
    bool HasErrors,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<string> Printed,
    IReadOnlyList<string> Alerted);

public sealed record EasyConScriptFormatResult(
    bool HasErrors,
    string? FormattedCode,
    IReadOnlyList<string> Diagnostics);
