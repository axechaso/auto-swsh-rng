using System.Globalization;
using AutoSwshRng.Core.Common;
using AutoSwshRng.Core.Encounters;
using AutoSwshRng.Core.Profiles;
using AutoSwshRng.Core.Rng;
using owoow.Core.EncounterTable;
using owoow.Core.Interfaces;
using owoow.Core.RNG;
using PKHeX.Core;
using OwoowAura = owoow.Core.Enums.AuraType;
using OwoowEncounterKind = owoow.Core.Enums.EncounterType;
using OwoowEncounters = owoow.Core.Encounters;
using OwoowFishing = owoow.Core.RNG.Generators.Overworld.Fishing;
using OwoowGame = owoow.Core.Enums.Game;
using OwoowHeight = owoow.Core.Enums.ScaleType;
using OwoowHidden = owoow.Core.RNG.Generators.Overworld.Hidden;
using OwoowIvMatch = owoow.Core.Enums.IVSearchType;
using OwoowShiny = owoow.Core.Enums.ShinyType;
using OwoowStatic = owoow.Core.RNG.Generators.Overworld.Static;
using OwoowSymbol = owoow.Core.RNG.Generators.Overworld.Symbol;
using OwoowWeather = owoow.Core.Enums.WeatherType;
using CoreGameVersion = AutoSwshRng.Core.Profiles.GameVersion;

namespace AutoSwshRng.Upstream;

public sealed class OwoowOverworldEncounterService : IOverworldEncounterService
{
    private const ulong ChunkSize = 1_000;

    public async Task<IReadOnlyList<OverworldEncounterResult>> SearchAsync(
        OverworldSearchRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var total = request.EndAdvance - request.StartAdvance + 1;
        ulong completed = 0;
        var results = new List<OverworldEncounterResult>();
        progress?.Report(new OperationProgress(OperationState.Running, 0, total, "Searching overworld encounters."));

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var table = new EncounterTable(
                Map(request.Context.Game),
                Map(request.Context.Kind),
                request.Context.Area,
                request.Context.Weather,
                request.Context.LeadAbility);
            var config = CreateConfig(request);

            for (var start = request.StartAdvance; start <= request.EndAdvance;)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = request.EndAdvance - start + 1;
                var count = Math.Min(ChunkSize, remaining);
                var end = start + count - 1;
                var state = owoow.Core.RNG.Util.XoroshiroJump(
                    request.InitialState.Seed0,
                    request.InitialState.Seed1,
                    start);
                var frames = await GenerateAsync(
                        request.Context.Kind,
                        state.s0,
                        state.s1,
                        table,
                        start,
                        end,
                        config)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                results.AddRange(frames.Select(MapResult).Where(
                    result => !request.FiltersEnabled
                        || PassPostFilters(result, request.Filter)));

                completed = end - request.StartAdvance + 1;
                progress?.Report(
                    new OperationProgress(
                        OperationState.Running,
                        completed,
                        total,
                        "Searching overworld encounters."));
                if (end == ulong.MaxValue)
                {
                    break;
                }

                start = end + 1;
            }
        }
        catch (OperationCanceledException)
        {
            progress?.Report(new OperationProgress(
                OperationState.Cancelled,
                completed,
                total,
                "Overworld search cancelled."));
            throw;
        }
        catch (UpstreamOperationException)
        {
            progress?.Report(new OperationProgress(
                OperationState.Failed,
                completed,
                total,
                "Overworld search failed."));
            throw;
        }
        catch (ArgumentException exception)
        {
            progress?.Report(new OperationProgress(
                OperationState.Failed,
                completed,
                total,
                "Overworld search failed validation."));
            throw new UpstreamOperationException(
                UpstreamErrorCode.Validation,
                "Invalid overworld search input.",
                exception);
        }
        catch (FormatException exception)
        {
            progress?.Report(new OperationProgress(
                OperationState.Failed,
                completed,
                total,
                "Overworld search returned invalid data."));
            throw new UpstreamOperationException(
                UpstreamErrorCode.InvalidData,
                "owoow returned invalid overworld encounter data.",
                exception);
        }
        catch (Exception exception)
        {
            progress?.Report(new OperationProgress(
                OperationState.Failed,
                completed,
                total,
                "Overworld search failed."));
            throw new UpstreamOperationException(
                UpstreamErrorCode.UpstreamFailure,
                "owoow overworld search failed.",
                exception);
        }

        progress?.Report(new OperationProgress(OperationState.Completed, total, total, "Overworld search complete."));
        return results.OrderBy(result => result.Advance).ToArray();
    }

    private static Task<List<OverworldFrame>> GenerateAsync(
        EncounterKind kind,
        ulong seed0,
        ulong seed1,
        EncounterTable table,
        ulong start,
        ulong end,
        GeneratorConfig config)
    {
        return kind switch
        {
            EncounterKind.Static => OwoowStatic.Generate(seed0, seed1, table, start, end, config),
            EncounterKind.Symbol => OwoowSymbol.Generate(seed0, seed1, table, start, end, config),
            EncounterKind.Hidden => OwoowHidden.Generate(seed0, seed1, table, start, end, config),
            EncounterKind.Fishing => OwoowFishing.Generate(seed0, seed1, table, start, end, config),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown encounter kind."),
        };
    }

    private static GeneratorConfig CreateConfig(OverworldSearchRequest request)
    {
        var ivs = request.Filter.IndividualValues;
        return new GeneratorConfig
        {
            TargetSpecies = request.Filter.TargetSpecies ?? OwoowEncounters.ANY_SPECIES,
            TargetAura = Map(request.Filter.Aura),
            TargetNature = MapNature(request.Filter.TargetNature),
            TargetShiny = Map(request.Filter.Shiny),
            TargetMark = Map(request.Filter.Mark),
            TargetScale = Map(request.Filter.Height),
            TargetMinIVs = ivs.Select(value => (uint)value.Minimum).ToArray(),
            TargetMaxIVs = ivs.Select(value => (uint)value.Maximum).ToArray(),
            SearchTypes = ivs.Select(value => Map(value.Match)).ToArray(),
            RareEC = request.Filter.RareEncryptionConstant,
            LeadAbility = request.Context.LeadAbility,
            TID = request.Profile.TrainerId,
            SID = request.Profile.SecretId,
            ShinyRolls = request.Profile.HasShinyCharm ? 3 : 1,
            MarkRolls = request.Profile.HasMarkCharm ? 3 : 1,
            AuraKOs = request.AuraKnockouts,
            MaxStep = request.HiddenMaximumStep,
            Weather = MapWeather(request.Context.Weather),
            DexRecSlots = request.DexRecommendationSlots.ToArray(),
            ConsiderMenuClose = request.Environment.ConsiderMenuClose,
            MenuCloseIsHoldingDirection = request.Environment.HoldDirection,
            MenuCloseNPCs = request.Environment.MenuCloseNonPlayerCharacters,
            ConsiderFly = request.Environment.ConsiderFlying,
            AreaLoadAdvances = request.Environment.AreaLoadAdvances,
            AreaLoadNPCs = request.Environment.AreaLoadNonPlayerCharacters,
            ConsiderRain = request.Environment.ConsiderRain,
            RainTicksAreaLoad = request.Environment.RainTicksDuringAreaLoad,
            RainTicksEncounter = request.Environment.RainTicksBeforeEncounter,
            FiltersEnabled = request.FiltersEnabled,
            Game = Map(request.Context.Game),
        };
    }

    private static bool PassPostFilters(
        OverworldEncounterResult result,
        EncounterFilter filter)
    {
        return (filter.TargetAbility is null || result.Ability == filter.TargetAbility)
            && (filter.TargetGender is null || result.Gender == filter.TargetGender);
    }

    private static OverworldEncounterResult MapResult(OverworldFrame frame)
    {
        return new OverworldEncounterResult(
            ParseUnsigned(frame.Advances),
            checked((uint)ParseUnsigned(frame.Jump.TrimStart('+'))),
            frame.Step,
            frame.Animation,
            frame.Species,
            frame.Shiny,
            frame.Brilliant == 'Y',
            frame.Level,
            frame.Ability,
            frame.Nature,
            MapGender(frame.Gender),
            new RngIndividualValues([frame.H, frame.A, frame.B, frame.C, frame.D, frame.S]),
            frame.Mark,
            ParseHex32(frame.EC),
            ParseHex32(frame.PID),
            ParseHeight(frame.Height),
            frame.Height,
            frame.Item,
            frame.EggMove,
            new RngState(ParseHex64(frame.Seed0), ParseHex64(frame.Seed1)));
    }

    private static ulong ParseUnsigned(string value) =>
        ulong.Parse(
            value.Replace(",", string.Empty, StringComparison.Ordinal),
            NumberStyles.None,
            CultureInfo.InvariantCulture);

    private static uint ParseHex32(string value) =>
        uint.Parse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);

    private static ulong ParseHex64(string value) =>
        ulong.Parse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);

    private static byte ParseHeight(string value)
    {
        var opening = value.LastIndexOf('(');
        var closing = value.LastIndexOf(')');
        if (opening < 0 || closing <= opening)
        {
            throw new FormatException($"Unexpected owoow height value '{value}'.");
        }

        return byte.Parse(
            value.AsSpan(opening + 1, closing - opening - 1),
            NumberStyles.None,
            CultureInfo.InvariantCulture);
    }

    private static PokemonGender MapGender(char gender) => gender switch
    {
        'M' => PokemonGender.Male,
        'F' => PokemonGender.Female,
        _ => PokemonGender.Genderless,
    };

    private static OwoowGame Map(CoreGameVersion game) => (OwoowGame)game;

    private static OwoowEncounterKind Map(EncounterKind kind) => (OwoowEncounterKind)kind;

    private static OwoowAura Map(AuraFilter filter) => (OwoowAura)filter;

    private static OwoowShiny Map(ShinyFilter filter) => (OwoowShiny)filter;

    private static OwoowIvMatch Map(IndividualValueMatch match) => (OwoowIvMatch)match;

    private static OwoowHeight Map(HeightFilter filter) => filter switch
    {
        HeightFilter.Any => OwoowHeight.Any,
        HeightFilter.XXXS => OwoowHeight.XXXS,
        HeightFilter.XXS => OwoowHeight.XXS,
        HeightFilter.XS => OwoowHeight.XS,
        HeightFilter.Small => OwoowHeight.S,
        HeightFilter.Medium => OwoowHeight.M,
        HeightFilter.Large => OwoowHeight.L,
        HeightFilter.XL => OwoowHeight.XL,
        HeightFilter.XXL => OwoowHeight.XXL,
        HeightFilter.XXXL => OwoowHeight.XXXL,
        HeightFilter.MinOrMax => OwoowHeight.MinOrMax,
        _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, "Unknown height filter."),
    };

    private static RibbonIndex Map(MarkFilter filter) => filter.Mode switch
    {
        MarkFilterMode.Ignore => RibbonIndex.MAX_COUNT + 1,
        MarkFilterMode.None => RibbonIndex.MAX_COUNT,
        MarkFilterMode.Any => RibbonIndex.MAX_COUNT + 2,
        MarkFilterMode.Personality => RibbonIndex.MAX_COUNT + 3,
        MarkFilterMode.PersonalityOrRare => RibbonIndex.MAX_COUNT + 4,
        MarkFilterMode.Specific => ParseMark(filter.SpecificMark!),
        _ => throw new ArgumentOutOfRangeException(nameof(filter), filter.Mode, "Unknown mark filter."),
    };

    private static RibbonIndex ParseMark(string value)
    {
        var name = value.StartsWith("Mark", StringComparison.Ordinal)
            ? value
            : $"Mark{value.Replace(" ", string.Empty, StringComparison.Ordinal)}";
        return Enum.TryParse<RibbonIndex>(name, true, out var mark)
            ? mark
            : throw new ArgumentException($"Unknown mark '{value}'.", nameof(value));
    }

    private static Nature MapNature(string? value)
    {
        if (value is null)
        {
            return Nature.Random;
        }

        return Enum.TryParse<Nature>(value.Replace(" ", string.Empty, StringComparison.Ordinal), true, out var nature)
            ? nature
            : throw new ArgumentException($"Unknown nature '{value}'.", nameof(value));
    }

    private static OwoowWeather MapWeather(string value) =>
        owoow.Core.RNG.Util.GetWeatherType(value);
}
