using AutoSwshRng.Core.Encounters;
using AutoSwshRng.Core.Profiles;
using AutoSwshRng.Core.Rng;

namespace AutoSwshRng.Core.Tests.Encounters;

public class OverworldSearchRequestTests
{
    private static readonly RngProfile Profile =
        new("main", GameVersion.Sword, 1337, 1390, true, true);

    [Test]
    public void RequestRejectsReversedAdvanceRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new OverworldSearchRequest(
                new RngState(1, 2),
                10,
                9,
                new EncounterCatalogRequest(
                    GameVersion.Sword,
                    EncounterKind.Symbol,
                    "area",
                    "weather"),
                Profile,
                EncounterFilter.Any,
                OverworldEnvironmentSettings.None));
    }

    [Test]
    public void StaticRequestRequiresTargetSpecies()
    {
        Assert.Throws<ArgumentException>(
            () => new OverworldSearchRequest(
                new RngState(1, 2),
                0,
                9,
                new EncounterCatalogRequest(
                    GameVersion.Sword,
                    EncounterKind.Static,
                    "area",
                    "weather"),
                Profile,
                EncounterFilter.Any,
                OverworldEnvironmentSettings.None));
    }

    [Test]
    public void IndividualValueFilterRequiresSixConstraints()
    {
        Assert.Throws<ArgumentException>(
            () => new EncounterFilter(
                individualValues: Enumerable.Repeat(IndividualValueConstraint.Any, 5)));
    }

    [TestCase(-1, 31)]
    [TestCase(0, 32)]
    [TestCase(10, 9)]
    public void IndividualValueConstraintValidatesBounds(int minimum, int maximum)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new IndividualValueConstraint(
                IndividualValueMatch.Range,
                minimum,
                maximum));
    }

    [Test]
    public void RequestCopiesDexRecommendationSlots()
    {
        var slots = new short[] { 1, 2, 3, 4 };
        var request = CreateSymbolRequest(slots);
        slots[0] = 99;

        Assert.That(request.DexRecommendationSlots[0], Is.EqualTo(1));
    }

    private static OverworldSearchRequest CreateSymbolRequest(short[] slots)
    {
        return new OverworldSearchRequest(
            new RngState(1, 2),
            0,
            9,
            new EncounterCatalogRequest(
                GameVersion.Sword,
                EncounterKind.Symbol,
                "area",
                "weather"),
            Profile,
            EncounterFilter.Any,
            OverworldEnvironmentSettings.None,
            dexRecommendationSlots: slots);
    }
}
