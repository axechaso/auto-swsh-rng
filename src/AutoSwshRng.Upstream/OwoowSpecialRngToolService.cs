using System.Globalization;
using AutoSwshRng.Core.Common;
using AutoSwshRng.Core.Profiles;
using AutoSwshRng.Core.Rng;
using owoow.Core.Interfaces;
using owoow.Core.RNG;
using OwoowCramomatic = owoow.Core.RNG.Generators.Item.Cramomatic;
using OwoowDiggingPa = owoow.Core.RNG.Generators.Item.DiggingPa;
using OwoowLotoId = owoow.Core.RNG.Generators.Item.LotoID;
using OwoowSkillBro = owoow.Core.RNG.Generators.Item.SkillBro;
using OwoowWailord = owoow.Core.RNG.Generators.Item.Wailord;
using OwoowWattTrader = owoow.Core.RNG.Generators.Item.WattTrader;

namespace AutoSwshRng.Upstream;

public sealed class OwoowSpecialRngToolService : ISpecialRngToolService
{
    private const ulong ChunkSize = 4_096;

    public async Task<IReadOnlyList<SpecialToolFrame>> SearchAsync(
        SpecialToolSearchRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var total = request.EndAdvance - request.StartAdvance + 1;
        var results = new List<SpecialToolFrame>();
        var config = CreateConfig(request);
        progress?.Report(new OperationProgress(
            OperationState.Running,
            0,
            total,
            $"Searching {request.Kind}."));

        try
        {
            for (var start = request.StartAdvance; start <= request.EndAdvance;)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = Math.Min(ChunkSize, request.EndAdvance - start + 1);
                var end = start + count - 1;
                results.AddRange(await GenerateChunkAsync(request, start, end, config)
                    .ConfigureAwait(false));
                cancellationToken.ThrowIfCancellationRequested();

                var completed = end - request.StartAdvance + 1;
                progress?.Report(new OperationProgress(
                    OperationState.Running,
                    completed,
                    total,
                    $"Searching {request.Kind}."));
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
                0,
                total,
                $"{request.Kind} search cancelled."));
            throw;
        }
        catch (Exception exception) when (exception is not UpstreamOperationException)
        {
            progress?.Report(new OperationProgress(
                OperationState.Failed,
                0,
                total,
                $"{request.Kind} search failed."));
            throw new UpstreamOperationException(
                UpstreamErrorCode.UpstreamFailure,
                $"owoow {request.Kind} search failed.",
                exception);
        }

        progress?.Report(new OperationProgress(
            OperationState.Completed,
            total,
            total,
            $"{request.Kind} search complete."));
        return results.OrderBy(result => result.Advance).ToArray();
    }

    private static async Task<IReadOnlyList<SpecialToolFrame>> GenerateChunkAsync(
        SpecialToolSearchRequest request,
        ulong start,
        ulong end,
        GeneratorConfig config)
    {
        return request.Kind switch
        {
            SpecialToolKind.LotoId => (await OwoowLotoId.Generate(
                    request.State.Seed0,
                    request.State.Seed1,
                    start,
                    end,
                    config)
                .ConfigureAwait(false)).Select(MapLotoId).ToArray(),
            SpecialToolKind.CramOMatic => (await OwoowCramomatic.Generate(
                    request.State.Seed0,
                    request.State.Seed1,
                    start,
                    end,
                    config)
                .ConfigureAwait(false)).Select(MapCramomatic).ToArray(),
            SpecialToolKind.WattTrader => (await OwoowWattTrader.Generate(
                    request.State.Seed0,
                    request.State.Seed1,
                    start,
                    end,
                    config)
                .ConfigureAwait(false)).Select(MapWattTrader).ToArray(),
            SpecialToolKind.DiggingPa => (await OwoowDiggingPa.Generate(
                    request.State.Seed0,
                    request.State.Seed1,
                    start,
                    end,
                    config)
                .ConfigureAwait(false)).Select(MapDiggingPa).ToArray(),
            SpecialToolKind.DiggingBro => (await OwoowSkillBro.Generate(
                    request.State.Seed0,
                    request.State.Seed1,
                    start,
                    end,
                    config)
                .ConfigureAwait(false)).Select(MapDiggingBro).ToArray(),
            SpecialToolKind.WailordRespawn => (await OwoowWailord.Generate(
                    request.State.Seed0,
                    request.State.Seed1,
                    start,
                    end,
                    config)
                .ConfigureAwait(false)).Select(MapWailord).ToArray(),
            _ => throw new ArgumentOutOfRangeException(
                nameof(request),
                request.Kind,
                "Unknown special RNG tool."),
        };
    }

    private static GeneratorConfig CreateConfig(SpecialToolSearchRequest request)
    {
        var rewardMinimums = new byte[Enum.GetValues<DiggingBroReward>().Length];
        foreach (var (reward, count) in request.DiggingBroMinimumRewards)
        {
            rewardMinimums[(byte)reward] = count;
        }

        return new GeneratorConfig
        {
            IDs = request.LotoIds.ToList(),
            LotoIDTargetType = (owoow.Core.Enums.LotoIDTargetType)request.LotoPrize,
            SuccessType = (owoow.Core.Enums.SuccessType)request.Success,
            CramomaticTargetType = (owoow.Core.Enums.CramomaticTargetType)request.CramPrize,
            CramomaticInputs = request.CramInputs
                .Select(value => (owoow.Core.Enums.CramomaticInputItemType)value)
                .ToArray(),
            BonusOnly = request.BonusOnly,
            WattTraderSlotMin = request.WattTraderSlotMinimum,
            WattTraderSlotMax = request.WattTraderSlotMaximum,
            DiggingPaMinWatts = request.DiggingPaMinimumWatts,
            SkillBroItemsMin = rewardMinimums,
            SkillBroItemsMinCount = request.DiggingBroMinimumTotal,
            ConsiderMenuClose = request.MenuClose.Enabled,
            MenuCloseNPCs = request.MenuClose.NonPlayerCharacters,
            MenuCloseIsHoldingDirection = request.MenuClose.HoldDirection,
            Weather = MapWeather(request.MenuClose.Weather),
            Game = request.Game == GameVersion.Sword
                ? owoow.Core.Enums.Game.Sword
                : owoow.Core.Enums.Game.Shield,
            FiltersEnabled = true,
        };
    }

    private static SpecialToolFrame MapLotoId(LotoIDFrame frame) =>
        CreateFrame(
            SpecialToolKind.LotoId,
            frame.Advances,
            frame.Jump,
            frame.Animation,
            frame.Seed0,
            frame.Seed1,
            identifier: frame.ID,
            primaryResult: frame.Prize);

    private static SpecialToolFrame MapCramomatic(CramomaticFrame frame) =>
        CreateFrame(
            SpecialToolKind.CramOMatic,
            frame.Advances,
            frame.Jump,
            frame.Animation,
            frame.Seed0,
            frame.Seed1,
            primaryResult: frame.Prize,
            bonus: frame.Bonus);

    private static SpecialToolFrame MapWattTrader(WattTraderFrame frame) =>
        CreateFrame(
            SpecialToolKind.WattTrader,
            frame.Advances,
            frame.Jump,
            frame.Animation,
            frame.Seed0,
            frame.Seed1,
            primaryResult: frame.Highlight,
            secondaryResult: frame.Regular);

    private static SpecialToolFrame MapDiggingPa(DiggingPaFrame frame) =>
        CreateFrame(
            SpecialToolKind.DiggingPa,
            frame.Advances,
            frame.Jump,
            frame.Animation,
            frame.Seed0,
            frame.Seed1,
            primaryResult: frame.Actual,
            secondaryResult: frame.Reported,
            watts: frame.Watts);

    private static SpecialToolFrame MapDiggingBro(SkillBroFrame frame)
    {
        var rewards = new Dictionary<DiggingBroReward, byte>
        {
            [DiggingBroReward.GoldBottleCap] = frame.GoldBottleCap,
            [DiggingBroReward.BottleCap] = frame.BottleCap,
            [DiggingBroReward.NormalGem] = frame.NormalGem,
            [DiggingBroReward.StickyBarb] = frame.StickyBarb,
            [DiggingBroReward.LightClay] = frame.LightClay,
            [DiggingBroReward.LaggingTail] = frame.LaggingTail,
            [DiggingBroReward.IronBall] = frame.IronBall,
            [DiggingBroReward.MetalCoat] = frame.MetalCoat,
            [DiggingBroReward.IceStone] = frame.IceStone,
            [DiggingBroReward.DawnStone] = frame.DawnStone,
            [DiggingBroReward.DuskStone] = frame.DuskStone,
            [DiggingBroReward.ShinyStone] = frame.ShinyStone,
            [DiggingBroReward.MoonStone] = frame.MoonStone,
            [DiggingBroReward.SunStone] = frame.SunStone,
            [DiggingBroReward.FossilizedFish] = frame.FossilizedFish,
            [DiggingBroReward.FossilizedDrake] = frame.FossilizedDrake,
            [DiggingBroReward.FossilizedDino] = frame.FossilizedDino,
            [DiggingBroReward.FossilizedBird] = frame.FossilizedBird,
            [DiggingBroReward.WishingPiece] = frame.WishingPiece,
            [DiggingBroReward.CometShard] = frame.CometShard,
            [DiggingBroReward.RareBone] = frame.RareBone,
        };
        return CreateFrame(
            SpecialToolKind.DiggingBro,
            frame.Advances,
            frame.Jump,
            frame.Animation,
            frame.Seed0,
            frame.Seed1,
            total: frame.Total,
            rewards: rewards);
    }

    private static SpecialToolFrame MapWailord(WailordFrame frame) =>
        CreateFrame(
            SpecialToolKind.WailordRespawn,
            frame.Advances,
            frame.Jump,
            frame.Animation,
            frame.Seed0,
            frame.Seed1,
            success: frame.Respawn == 'Y');

    private static SpecialToolFrame CreateFrame(
        SpecialToolKind kind,
        string advances,
        string jump,
        char animation,
        string seed0,
        string seed1,
        string? identifier = null,
        string? primaryResult = null,
        string? secondaryResult = null,
        bool? bonus = null,
        ulong? watts = null,
        int? total = null,
        bool? success = null,
        IReadOnlyDictionary<DiggingBroReward, byte>? rewards = null)
    {
        return new SpecialToolFrame(
            kind,
            ParseUnsigned(advances),
            checked((uint)ParseUnsigned(jump.TrimStart('+'))),
            animation,
            new RngState(ParseHex64(seed0), ParseHex64(seed1)),
            identifier,
            primaryResult,
            secondaryResult,
            bonus,
            watts,
            total,
            success,
            rewards);
    }

    private static ulong ParseUnsigned(string value) =>
        ulong.Parse(
            value.Replace(",", string.Empty, StringComparison.Ordinal),
            NumberStyles.None,
            CultureInfo.InvariantCulture);

    private static ulong ParseHex64(string value) =>
        ulong.Parse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);

    private static owoow.Core.Enums.WeatherType MapWeather(Weather weather) => weather switch
    {
        Weather.Normal => owoow.Core.Enums.WeatherType.NormalWeather,
        Weather.Overcast => owoow.Core.Enums.WeatherType.Overcast,
        Weather.Raining => owoow.Core.Enums.WeatherType.Raining,
        Weather.Thunderstorm => owoow.Core.Enums.WeatherType.Thunderstorm,
        Weather.IntenseSun => owoow.Core.Enums.WeatherType.IntenseSun,
        Weather.Snowing => owoow.Core.Enums.WeatherType.Snowing,
        Weather.Snowstorm => owoow.Core.Enums.WeatherType.Snowstorm,
        Weather.Sandstorm => owoow.Core.Enums.WeatherType.Sandstorm,
        Weather.HeavyFog => owoow.Core.Enums.WeatherType.HeavyFog,
        _ => throw new ArgumentOutOfRangeException(nameof(weather), weather, "Unknown weather."),
    };
}
