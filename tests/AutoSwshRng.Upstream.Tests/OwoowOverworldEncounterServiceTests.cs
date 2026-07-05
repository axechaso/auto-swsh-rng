using AutoSwshRng.Core.Encounters;
using AutoSwshRng.Core.Profiles;
using AutoSwshRng.Core.Rng;
using owoow.Core.EncounterTable;
using owoow.Core.Enums;
using owoow.Core.Interfaces;
using owoow.Core.RNG;
using owoow.Core.RNG.Generators.Overworld;
using OwoowEncounters = owoow.Core.Encounters;

namespace AutoSwshRng.Upstream.Tests;

public class OwoowOverworldEncounterServiceTests
{
    [TestCase(EncounterKind.Static)]
    [TestCase(EncounterKind.Symbol)]
    [TestCase(EncounterKind.Hidden)]
    [TestCase(EncounterKind.Fishing)]
    public async Task SearchMatchesOriginalGenerator(EncounterKind kind)
    {
        var request = CreateRequest(kind);
        var actual = await new OwoowOverworldEncounterService().SearchAsync(request);
        var expected = await GenerateOriginal(request);

        Assert.That(
            actual.Select(Project),
            Is.EqualTo(expected.Select(Project)));
    }

    [Test]
    public async Task AbilityGenderAndNaturePostFiltersAreApplied()
    {
        var baseline = CreateRequest(EncounterKind.Symbol);
        var service = new OwoowOverworldEncounterService();
        var all = await service.SearchAsync(baseline);
        Assert.That(all, Is.Not.Empty);
        var target = all[0];
        var filtered = new OverworldSearchRequest(
            baseline.InitialState,
            baseline.StartAdvance,
            baseline.EndAdvance,
            baseline.Context,
            baseline.Profile,
            new EncounterFilter(
                targetAbility: target.Ability,
                targetNature: target.Nature,
                targetGender: target.Gender),
            baseline.Environment,
            baseline.AuraKnockouts,
            baseline.HiddenMaximumStep,
            baseline.DexRecommendationSlots);

        var actual = await service.SearchAsync(filtered);

        Assert.That(
            actual.All(
                result => result.Ability == target.Ability
                    && result.Nature == target.Nature
                    && result.Gender == target.Gender),
            Is.True);
    }

    [Test]
    public void SearchHonorsPreCancelledToken()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(
            async () => await new OwoowOverworldEncounterService().SearchAsync(
                CreateRequest(EncounterKind.Fishing),
                cancellationToken: source.Token));
    }

    private static OverworldSearchRequest CreateRequest(EncounterKind kind)
    {
        var owoowKind = (EncounterType)kind;
        const Game game = Game.Sword;
        var area = OwoowEncounters.GetAreaList(game, owoowKind).First();
        var weather = OwoowEncounters.GetWeatherList(game, owoowKind, area).First();
        var species = kind == EncounterKind.Static
            ? OwoowEncounters.GetSpeciesList(game, owoowKind, area, weather).First()
            : null;

        return new OverworldSearchRequest(
            new RngState(0x123456789ABCDEF0, 0x0FEDCBA987654321),
            0,
            30,
            new EncounterCatalogRequest(
                GameVersion.Sword,
                kind,
                area,
                weather,
                string.Empty),
            new RngProfile("main", GameVersion.Sword, 1337, 1390, false, false),
            new EncounterFilter(targetSpecies: species),
            OverworldEnvironmentSettings.None);
    }

    private static async Task<List<OverworldFrame>> GenerateOriginal(
        OverworldSearchRequest request)
    {
        var kind = (EncounterType)request.Context.Kind;
        var table = new EncounterTable(
            (Game)request.Context.Game,
            kind,
            request.Context.Area,
            request.Context.Weather,
            request.Context.LeadAbility);
        var config = new GeneratorConfig
        {
            TargetSpecies = request.Filter.TargetSpecies ?? OwoowEncounters.ANY_SPECIES,
            TID = request.Profile.TrainerId,
            SID = request.Profile.SecretId,
            Weather = owoow.Core.RNG.Util.GetWeatherType(request.Context.Weather),
            FiltersEnabled = false,
        };

        return kind switch
        {
            EncounterType.Static => await Static.Generate(
                request.InitialState.Seed0,
                request.InitialState.Seed1,
                table,
                request.StartAdvance,
                request.EndAdvance,
                config),
            EncounterType.Symbol => await Symbol.Generate(
                request.InitialState.Seed0,
                request.InitialState.Seed1,
                table,
                request.StartAdvance,
                request.EndAdvance,
                config),
            EncounterType.Hidden => await Hidden.Generate(
                request.InitialState.Seed0,
                request.InitialState.Seed1,
                table,
                request.StartAdvance,
                request.EndAdvance,
                config),
            EncounterType.Fishing => await Fishing.Generate(
                request.InitialState.Seed0,
                request.InitialState.Seed1,
                table,
                request.StartAdvance,
                request.EndAdvance,
                config),
            _ => throw new ArgumentOutOfRangeException(),
        };
    }

    private static object Project(OverworldEncounterResult result) => new
    {
        result.Advance,
        result.Jump,
        result.Step,
        result.Animation,
        result.Species,
        result.Shiny,
        result.BrilliantAura,
        result.Level,
        result.Ability,
        result.Nature,
        result.Gender,
        IVs = string.Join("/", result.IndividualValues.ToArray()),
        result.Mark,
        result.EncryptionConstant,
        result.PersonalityId,
        result.HeightDescription,
        result.Item,
        result.EggMove,
        result.State,
    };

    private static object Project(OverworldFrame frame) => new
    {
        Advance = ulong.Parse(frame.Advances.Replace(",", string.Empty)),
        Jump = uint.Parse(frame.Jump.TrimStart('+').Replace(",", string.Empty)),
        Step = frame.Step,
        Animation = frame.Animation,
        Species = frame.Species,
        Shiny = frame.Shiny,
        BrilliantAura = frame.Brilliant == 'Y',
        Level = frame.Level,
        Ability = frame.Ability,
        Nature = frame.Nature,
        Gender = frame.Gender switch
        {
            'M' => PokemonGender.Male,
            'F' => PokemonGender.Female,
            _ => PokemonGender.Genderless,
        },
        IVs = string.Join("/", new[] { frame.H, frame.A, frame.B, frame.C, frame.D, frame.S }),
        Mark = frame.Mark,
        EncryptionConstant = Convert.ToUInt32(frame.EC, 16),
        PersonalityId = Convert.ToUInt32(frame.PID, 16),
        HeightDescription = frame.Height,
        Item = frame.Item,
        EggMove = frame.EggMove,
        State = new RngState(Convert.ToUInt64(frame.Seed0, 16), Convert.ToUInt64(frame.Seed1, 16)),
    };
}
