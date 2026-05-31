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
