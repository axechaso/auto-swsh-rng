using System.Globalization;
using AutoSwshRng.Core.SpreadFinder;

namespace AutoSwshRng.Cli;

public static class SpreadFinderCliCommand
{
    private const string Usage =
        "Usage: auto-swsh-rng spread <8-digit-hex-seed> <guaranteed-ivs-0..6>";

    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        ISpreadFinderService spreadFinder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(spreadFinder);

        if (!TryParseArguments(arguments, out var seed, out var guaranteedIndividualValues))
        {
            await error.WriteLineAsync(Usage).ConfigureAwait(false);
            return 2;
        }

        var request = new SpreadSearchRequest(
            new SpreadSearchScope.Seeds([seed]),
            Enumerable.Repeat(IndividualValueRange.Any, 6),
            guaranteedIndividualValues);
        var results = await spreadFinder.SearchAsync(request, cancellationToken).ConfigureAwait(false);

        foreach (var result in results)
        {
            var ivs = result.IndividualValues;
            await output.WriteLineAsync(
                    $"Seed={result.Seed:X8} EC={result.EncryptionConstant:X8} " +
                    $"IVs={ivs.HP}/{ivs.Attack}/{ivs.Defense}/{ivs.SpecialAttack}/{ivs.SpecialDefense}/{ivs.Speed} " +
                    $"Height={result.Height} Scale={result.Scale}")
                .ConfigureAwait(false);
        }

        if (results.Count == 0)
        {
            await output.WriteLineAsync("No results.").ConfigureAwait(false);
        }

        return 0;
    }

    private static bool TryParseArguments(
        IReadOnlyList<string> arguments,
        out uint seed,
        out int guaranteedIndividualValues)
    {
        seed = 0;
        guaranteedIndividualValues = 0;

        return arguments.Count == 3
            && string.Equals(arguments[0], "spread", StringComparison.OrdinalIgnoreCase)
            && arguments[1].Length == 8
            && uint.TryParse(
                arguments[1],
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out seed)
            && int.TryParse(
                arguments[2],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out guaranteedIndividualValues)
            && guaranteedIndividualValues is >= 0 and <= 6;
    }
}
