using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutoSwshRng.Core.Common;
using AutoSwshRng.Core.Encounters;
using AutoSwshRng.Core.Profiles;
using AutoSwshRng.Core.Rng;
using AutoSwshRng.Upstream;

namespace AutoSwshRng.Cli;

/// <summary>One read-only desktop request per process; stdout is UTF-8 JSON lines.</summary>
public static class DesktopJsonCommand
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<int> RunAsync(TextReader input, TextWriter output)
    {
        try
        {
            var line = await input.ReadLineAsync() ?? throw new ArgumentException("请求为空。");
            var request = JsonSerializer.Deserialize<DesktopRequest>(line, Json)
                ?? throw new ArgumentException("请求为空。");
            var result = await ExecuteAsync(request, new JsonProgress(output));
            await output.WriteLineAsync(JsonSerializer.Serialize(new { type = "result", data = result }, Json));
            return 0;
        }
        catch (Exception exception)
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(new { type = "error", message = exception.Message }, Json));
            return 1;
        }
    }

    private static async Task<object> ExecuteAsync(DesktopRequest r, IProgress<OperationProgress> progress)
    {
        var catalog = new OwoowEncounterCatalogService();
        if (r.Operation == "catalog")
        {
            var areas = await catalog.GetAreasAsync(r.Game, r.Kind);
            var area = areas.Contains(r.Area) ? r.Area : areas.FirstOrDefault();
            var weathers = area is null ? [] : await catalog.GetWeatherAsync(r.Game, r.Kind, area);
            var weather = weathers.Contains(r.Weather) ? r.Weather : weathers.FirstOrDefault();
            var species = area is null || weather is null ? []
                : await catalog.GetSpeciesAsync(r.Game, r.Kind, area, weather);
            return new { areas, area, weathers, weather, species };
        }

        var state = ParseState(r.Seed0, r.Seed1);
        if (r.Operation == "rng")
        {
            if (r.Amount is < 1 or > 1_000_000)
                throw new ArgumentException("推进数必须在 1～1,000,000 之间。");
            var result = await new OwoowXoroshiroService().CalculateAsync(new XoroshiroRequest(
                state, r.Reverse ? XoroshiroOperation.Previous : XoroshiroOperation.Next, r.Amount));
            return new { seed0 = result.State.Seed0.ToString("X16"), seed1 = result.State.Seed1.ToString("X16"), result.Distance };
        }
        if (r.Operation != "search")
            throw new ArgumentException("未知操作。");
        if (r.End < r.Start || r.End - r.Start >= 100_000 || r.End > 1_000_000_000)
            throw new ArgumentException("结束推进数不能小于起始值；单次最多搜索 100,000 帧，最大推进数为 1,000,000,000。");
        var context = new EncounterCatalogRequest(r.Game, r.Kind, r.Area ?? "", r.Weather ?? "");
        // Validate the exact context before invoking the generator, which assumes a valid table.
        await catalog.GetTableAsync(context);
        var availableSpecies = await catalog.GetSpeciesAsync(r.Game, r.Kind, context.Area, context.Weather);
        if (r.Species is not null && !availableSpecies.Contains(r.Species))
            throw new ArgumentException("所选宝可梦不在当前遭遇表中。");
        if (r.Ivs is null || r.Ivs.Length != 6 || r.Ivs.Any(pair => pair is null || pair.Length != 2))
            throw new ArgumentException("需要六项个体值范围。");
        var filter = new EncounterFilter(targetSpecies: r.Species, shiny: r.Shiny,
            individualValues: r.Ivs.Select(pair => new IndividualValueConstraint(IndividualValueMatch.Range, pair[0], pair[1])),
            targetNature: r.Nature, targetGender: r.Gender,
            mark: new MarkFilter(r.Mark));
        var profile = new RngProfile("Desktop", r.Game, r.Tid, r.Sid, r.ShinyCharm, r.MarkCharm);
        var results = await new OwoowOverworldEncounterService().SearchAsync(new OverworldSearchRequest(
            state, r.Start, r.End, context, profile, filter, OverworldEnvironmentSettings.None,
            auraKnockouts: r.Knockouts, hiddenMaximumStep: r.HiddenStep), progress);
        const int limit = 10_000;
        return new
        {
            total = results.Count,
            truncated = results.Count > limit,
            rows = results.Take(limit).Select(row => new
            {
                row.Advance, row.Species, row.Level, row.Shiny, row.Nature, row.Ability, row.Gender,
                ivs = row.IndividualValues.ToArray().Select(value => (int)value).ToArray(),
                row.Mark, row.BrilliantAura, row.Height,
                ec = row.EncryptionConstant.ToString("X8"), pid = row.PersonalityId.ToString("X8"),
                seed0 = row.State.Seed0.ToString("X16"), seed1 = row.State.Seed1.ToString("X16"),
            }).ToArray(),
        };
    }

    private static RngState ParseState(string seed0, string seed1)
    {
        static ulong Parse(string value)
        {
            value = value.Trim();
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) value = value[2..];
            if (value.Length is < 1 or > 16 || !ulong.TryParse(value, NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture, out var parsed))
                throw new ArgumentException("Seed 必须是 1～16 位十六进制数。");
            return parsed;
        }
        var state = new RngState(Parse(seed0), Parse(seed1));
        if (state is { Seed0: 0, Seed1: 0 }) throw new ArgumentException("两个 Seed 不能同时为零。");
        return state;
    }

    private sealed class JsonProgress(TextWriter output) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value)
        {
            output.WriteLine(JsonSerializer.Serialize(new { type = "progress", value.Completed, value.Total }, Json));
            output.Flush();
        }
    }
}

public sealed record DesktopRequest
{
    public string Operation { get; init; } = "";
    public GameVersion Game { get; init; }
    public EncounterKind Kind { get; init; } = EncounterKind.Symbol;
    public string? Area { get; init; }
    public string? Weather { get; init; }
    public string? Species { get; init; }
    public string Seed0 { get; init; } = "";
    public string Seed1 { get; init; } = "";
    public ulong Start { get; init; }
    public ulong End { get; init; }
    public ulong Amount { get; init; } = 1;
    public bool Reverse { get; init; }
    public int Tid { get; init; }
    public int Sid { get; init; }
    public bool ShinyCharm { get; init; }
    public bool MarkCharm { get; init; }
    public int Knockouts { get; init; }
    public int HiddenStep { get; init; }
    public ShinyFilter Shiny { get; init; }
    public MarkFilterMode Mark { get; init; }
    public string? Nature { get; init; }
    public PokemonGender? Gender { get; init; }
    public int[][] Ivs { get; init; } = Enumerable.Range(0, 6).Select(_ => new[] { 0, 31 }).ToArray();
}
