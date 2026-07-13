using AutoSwshRng.Core.Profiles;
using AutoSwshRng.Core.Rng;
using owoow.Core.RNG;
using owoow.Core.RNG.Generators.Item;

namespace AutoSwshRng.Upstream.Tests;

public class OwoowSpecialRngToolServiceTests
{
    private static readonly RngState State = new(0x123456789ABCDEF0, 0x0FEDCBA987654321);

    [Test]
    public async Task LotoIdMatchesOriginal()
    {
        var request = CreateRequest(
            SpecialToolKind.LotoId,
            lotoIds: ["123456", "654321"]);
        var expected = await LotoID.Generate(
            State.Seed0,
            State.Seed1,
            0,
            20,
            CreateConfig(request));
        var actual = await new OwoowSpecialRngToolService().SearchAsync(request);

        Assert.That(
            actual.Select(FrameProjection),
            Is.EqualTo(expected.Select(frame =>
                $"{frame.Advances}|{frame.Jump}|{frame.Animation}|{frame.ID}|{frame.Prize}|{frame.Seed0}|{frame.Seed1}||||")));
    }

    [Test]
    public async Task CramOMaticMatchesOriginal()
    {
        var request = CreateRequest(
            SpecialToolKind.CramOMatic,
            cramInputs:
            [
                CramInputItem.BlackApricorn,
                CramInputItem.BlueApricorn,
                CramInputItem.GreenApricorn,
                CramInputItem.PinkApricorn,
            ]);
        var expected = await Cramomatic.Generate(
            State.Seed0,
            State.Seed1,
            0,
            20,
            CreateConfig(request));
        var actual = await new OwoowSpecialRngToolService().SearchAsync(request);

        Assert.That(
            actual.Select(FrameProjection),
            Is.EqualTo(expected.Select(frame =>
                $"{frame.Advances}|{frame.Jump}|{frame.Animation}||{frame.Prize}|{frame.Seed0}|{frame.Seed1}|{frame.Bonus}|||")));
    }

    [Test]
    public async Task WattTraderAndDiggingPaMatchOriginal()
    {
        var service = new OwoowSpecialRngToolService();
        var wattRequest = CreateRequest(SpecialToolKind.WattTrader);
        var paRequest = CreateRequest(SpecialToolKind.DiggingPa);
        var expectedWatt = await WattTrader.Generate(
            State.Seed0,
            State.Seed1,
            0,
            20,
            CreateConfig(wattRequest));
        var expectedPa = await DiggingPa.Generate(
            State.Seed0,
            State.Seed1,
            0,
            20,
            CreateConfig(paRequest));

        var actualWatt = await service.SearchAsync(wattRequest);
        var actualPa = await service.SearchAsync(paRequest);

        Assert.Multiple(() =>
        {
            Assert.That(
                actualWatt.Select(FrameProjection),
                Is.EqualTo(expectedWatt.Select(frame =>
                    $"{frame.Advances}|{frame.Jump}|{frame.Animation}||{frame.Highlight}|{frame.Seed0}|{frame.Seed1}||{frame.Regular}||")));
            Assert.That(
                actualPa.Select(FrameProjection),
                Is.EqualTo(expectedPa.Select(frame =>
                    $"{frame.Advances}|{frame.Jump}|{frame.Animation}||{frame.Actual}|{frame.Seed0}|{frame.Seed1}||{frame.Reported}|{frame.Watts}|")));
        });
    }

    [Test]
    public async Task DiggingBroMapsEveryRewardAndMatchesOriginal()
    {
        var request = CreateRequest(
            SpecialToolKind.DiggingBro,
            game: GameVersion.Shield);
        var expected = await SkillBro.Generate(
            State.Seed0,
            State.Seed1,
            0,
            20,
            CreateConfig(request));
        var actual = await new OwoowSpecialRngToolService().SearchAsync(request);

        Assert.That(actual, Has.Count.EqualTo(expected.Count));
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Multiple(() =>
            {
                Assert.That(actual[index].Total, Is.EqualTo(expected[index].Total));
                Assert.That(actual[index].Rewards, Has.Count.EqualTo(21));
                Assert.That(
                    actual[index].Rewards[DiggingBroReward.GoldBottleCap],
                    Is.EqualTo(expected[index].GoldBottleCap));
                Assert.That(
                    actual[index].Rewards[DiggingBroReward.RareBone],
                    Is.EqualTo(expected[index].RareBone));
            });
        }
    }

    [Test]
    public async Task WailordRespawnMatchesOriginal()
    {
        var request = CreateRequest(SpecialToolKind.WailordRespawn);
        var expected = await Wailord.Generate(
            State.Seed0,
            State.Seed1,
            0,
            100,
            CreateConfig(request));
        var actual = await new OwoowSpecialRngToolService().SearchAsync(request);

        Assert.That(
            actual.Select(FrameProjection),
            Is.EqualTo(expected.Select(frame =>
                $"{frame.Advances}|{frame.Jump}|{frame.Animation}|||{frame.Seed0}|{frame.Seed1}||||{(frame.Respawn == 'Y')}")));
    }

    [Test]
    public async Task AnyWeatherMenuCloseMatchesOriginalAllWeather()
    {
        var request = new SpecialToolSearchRequest(
            SpecialToolKind.CramOMatic,
            State,
            0,
            20,
            menuClose: new SpecialToolMenuClose(true, 3, false, Weather.Any));
        var expected = await Cramomatic.Generate(
            State.Seed0,
            State.Seed1,
            0,
            20,
            CreateConfig(request));

        var actual = await new OwoowSpecialRngToolService().SearchAsync(request);

        Assert.That(
            actual.Select(FrameProjection),
            Is.EqualTo(expected.Select(frame =>
                $"{frame.Advances}|{frame.Jump}|{frame.Animation}||{frame.Prize}|{frame.Seed0}|{frame.Seed1}|{frame.Bonus}|||")));
    }

    [Test]
    public void HonorsCancellationBetweenBoundedChunks()
    {
        var request = new SpecialToolSearchRequest(
            SpecialToolKind.WailordRespawn,
            State,
            0,
            10_000_000);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(5));

        Assert.ThrowsAsync<OperationCanceledException>(
            async () => await new OwoowSpecialRngToolService().SearchAsync(
                request,
                cancellationToken: cancellation.Token));
    }

    private static SpecialToolSearchRequest CreateRequest(
        SpecialToolKind kind,
        GameVersion game = GameVersion.Sword,
        IReadOnlyList<string>? lotoIds = null,
        IReadOnlyList<CramInputItem>? cramInputs = null)
    {
        return new SpecialToolSearchRequest(
            kind,
            State,
            0,
            kind == SpecialToolKind.WailordRespawn ? 100UL : 20UL,
            game: game,
            lotoIds: lotoIds,
            cramInputs: cramInputs);
    }

    private static GeneratorConfig CreateConfig(SpecialToolSearchRequest request)
    {
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
            SkillBroItemsMin = Enum.GetValues<DiggingBroReward>()
                .Select(reward => request.DiggingBroMinimumRewards.GetValueOrDefault(reward))
                .ToArray(),
            SkillBroItemsMinCount = request.DiggingBroMinimumTotal,
            ConsiderMenuClose = request.MenuClose.Enabled,
            MenuCloseNPCs = request.MenuClose.NonPlayerCharacters,
            MenuCloseIsHoldingDirection = request.MenuClose.HoldDirection,
            Weather = (owoow.Core.Enums.WeatherType)request.MenuClose.Weather,
            Game = request.Game == GameVersion.Sword
                ? owoow.Core.Enums.Game.Sword
                : owoow.Core.Enums.Game.Shield,
            FiltersEnabled = true,
        };
    }

    private static string FrameProjection(SpecialToolFrame frame)
    {
        return $"{frame.Advance:N0}|+{frame.Jump}|{frame.Animation}|{frame.Identifier}|{frame.PrimaryResult}|{frame.State.Seed0:X16}|{frame.State.Seed1:X16}"
            + $"|{frame.Bonus}|{frame.SecondaryResult}|{frame.Watts}|{frame.Success}";
    }
}
