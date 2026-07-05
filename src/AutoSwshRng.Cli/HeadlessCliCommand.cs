using System.Globalization;
using AutoSwshRng.Core.Encounters;
using AutoSwshRng.Core.Profiles;
using AutoSwshRng.Core.Rng;
using AutoSwshRng.Core.SpreadFinder;
using AutoSwshRng.Upstream;

namespace AutoSwshRng.Cli;

public sealed record HeadlessCliServices(
    IEncounterCatalogService Catalog,
    IXoroshiroService Xoroshiro,
    ICalibrationService Calibration,
    ISpecialRngToolService SpecialTools,
    ISpreadFinderService SpreadFinder)
{
    public static HeadlessCliServices CreateDefaults() => new(
        new OwoowEncounterCatalogService(),
        new OwoowXoroshiroService(),
        new OwoowCalibrationService(),
        new OwoowSpecialRngToolService(),
        new OwoowSpreadFinderService());
}

public static class HeadlessCliCommand
{
    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        HeadlessCliServices services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(services);
        if (arguments.Count == 0)
        {
            await error.WriteLineAsync("A command is required.").ConfigureAwait(false);
            return 2;
        }

        try
        {
            return arguments[0].ToLowerInvariant() switch
            {
                "audit" => await AuditAsync(output).ConfigureAwait(false),
                "catalog" => await CatalogAsync(arguments, output, services, cancellationToken)
                    .ConfigureAwait(false),
                "rng" => await RngAsync(arguments, output, services, cancellationToken)
                    .ConfigureAwait(false),
                "calibrate" => await CalibrateAsync(arguments, output, services, cancellationToken)
                    .ConfigureAwait(false),
                "tool" => await ToolAsync(arguments, output, services, cancellationToken)
                    .ConfigureAwait(false),
                "sequence-dry-run" => await SequenceDryRunAsync(arguments, output)
                    .ConfigureAwait(false),
                "spread" => await SpreadFinderCliCommand.RunAsync(
                        arguments,
                        output,
                        error,
                        services.SpreadFinder,
                        cancellationToken)
                    .ConfigureAwait(false),
                _ => await UnknownCommandAsync(arguments[0], error)
                    .ConfigureAwait(false),
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task<int> AuditAsync(TextWriter output)
    {
        await output.WriteLineAsync(UpstreamSmokeReport.Create().ToDisplayText())
            .ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> UnknownCommandAsync(
        string command,
        TextWriter error)
    {
        await error.WriteLineAsync($"Unknown command '{command}'.").ConfigureAwait(false);
        return 2;
    }

    private static async Task<int> CatalogAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        HeadlessCliServices services,
        CancellationToken token)
    {
        RequireCount(arguments, 3);
        var game = Enum.Parse<GameVersion>(arguments[1], true);
        var kind = Enum.Parse<EncounterKind>(arguments[2], true);
        var areas = await services.Catalog.GetAreasAsync(game, kind, token)
            .ConfigureAwait(false);
        foreach (var area in areas)
        {
            await output.WriteLineAsync(area).ConfigureAwait(false);
        }

        return 0;
    }

    private static async Task<int> RngAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        HeadlessCliServices services,
        CancellationToken token)
    {
        RequireCount(arguments, 5);
        var operation = arguments[1].ToLowerInvariant() switch
        {
            "next" => XoroshiroOperation.Next,
            "previous" => XoroshiroOperation.Previous,
            "nextint" => XoroshiroOperation.NextInteger,
            "findinitial" => XoroshiroOperation.FindInitial,
            _ => throw new ArgumentException("Unknown RNG operation."),
        };
        var result = await services.Xoroshiro.CalculateAsync(
                new XoroshiroRequest(
                    new RngState(ParseHex(arguments[2]), ParseHex(arguments[3])),
                    operation,
                    ParseUnsigned(arguments[4])),
                token)
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                $"{result.State.Seed0:X16} {result.State.Seed1:X16} distance={result.Distance}")
            .ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> CalibrateAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        HeadlessCliServices services,
        CancellationToken token)
    {
        RequireCount(arguments, 7);
        if (!arguments[1].Equals("menu", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only menu calibration is supported by this command.");
        }

        var result = await services.Calibration.CalculateMenuCloseAsync(
                new MenuCloseCalibrationRequest(
                    new RngState(ParseHex(arguments[2]), ParseHex(arguments[3])),
                    uint.Parse(arguments[4], CultureInfo.InvariantCulture),
                    bool.Parse(arguments[5]),
                    Enum.Parse<Weather>(arguments[6], true)),
                token)
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                $"advances={result.Advances} {result.State.Seed0:X16} {result.State.Seed1:X16}")
            .ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> ToolAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        HeadlessCliServices services,
        CancellationToken token)
    {
        RequireCount(arguments, 6);
        var kind = arguments[1].ToLowerInvariant() switch
        {
            "loto" => SpecialToolKind.LotoId,
            "cram" => SpecialToolKind.CramOMatic,
            "watt" => SpecialToolKind.WattTrader,
            "digging-pa" => SpecialToolKind.DiggingPa,
            "digging-bro" => SpecialToolKind.DiggingBro,
            "wailord" => SpecialToolKind.WailordRespawn,
            _ => throw new ArgumentException("Unknown special tool."),
        };
        var results = await services.SpecialTools.SearchAsync(
                new SpecialToolSearchRequest(
                    kind,
                    new RngState(ParseHex(arguments[2]), ParseHex(arguments[3])),
                    ParseUnsigned(arguments[4]),
                    ParseUnsigned(arguments[5])),
                cancellationToken: token)
            .ConfigureAwait(false);
        foreach (var frame in results)
        {
            await output.WriteLineAsync(
                    $"{frame.Advance} {frame.PrimaryResult ?? frame.Success?.ToString() ?? string.Empty}")
                .ConfigureAwait(false);
        }

        if (results.Count == 0)
        {
            await output.WriteLineAsync("No results.").ConfigureAwait(false);
        }

        return 0;
    }

    private static async Task<int> SequenceDryRunAsync(
        IReadOnlyList<string> arguments,
        TextWriter output)
    {
        if (arguments.Count < 2)
        {
            throw new ArgumentException("At least one action is required.");
        }

        foreach (var action in arguments.Skip(1))
        {
            var parts = action.Split(':', 2);
            if (parts.Length != 2
                || !int.TryParse(parts[1], out var duration)
                || duration < 0)
            {
                throw new ArgumentException($"Invalid dry-run action '{action}'.");
            }

            await output.WriteLineAsync($"{parts[0]} duration={duration}ms")
                .ConfigureAwait(false);
        }

        return 0;
    }

    private static void RequireCount(IReadOnlyCollection<string> arguments, int count)
    {
        if (arguments.Count != count)
        {
            throw new ArgumentException("Invalid command arguments.");
        }
    }

    private static ulong ParseHex(string value) =>
        ulong.Parse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);

    private static ulong ParseUnsigned(string value) =>
        ulong.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);
}
