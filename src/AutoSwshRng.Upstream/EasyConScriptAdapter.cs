using EasyCon.Script;
using EasyCon.Script.Syntax;
using EasyScript;
using System.Collections.Immutable;

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
