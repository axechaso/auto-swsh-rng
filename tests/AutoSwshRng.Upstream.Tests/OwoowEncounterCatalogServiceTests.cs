using AutoSwshRng.Core.Encounters;
using AutoSwshRng.Core.Common;
using AutoSwshRng.Core.Profiles;
using owoow.Core.Enums;
using OwoowEncounters = owoow.Core.Encounters;

namespace AutoSwshRng.Upstream.Tests;

public class OwoowEncounterCatalogServiceTests
{
    [Test]
    public void InvalidCatalogSelectionUsesProjectError()
    {
        var service = new OwoowEncounterCatalogService();

        var error = Assert.ThrowsAsync<UpstreamOperationException>(
            async () => await service.GetTableAsync(new EncounterCatalogRequest(
                GameVersion.Sword,
                EncounterKind.Symbol,
                "not-an-area",
                "not-weather")));

        Assert.That(error!.Code, Is.EqualTo(UpstreamErrorCode.Validation));
    }

    [Test]
    public async Task AreasMatchOriginalCatalog()
    {
        var service = new OwoowEncounterCatalogService();

        var actual = await service.GetAreasAsync(GameVersion.Sword, EncounterKind.Symbol);
        var expected = OwoowEncounters
            .GetAreaList(Game.Sword, EncounterType.Symbol)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public async Task WeatherAndSpeciesMatchOriginalCatalog()
    {
        var service = new OwoowEncounterCatalogService();
        var area = OwoowEncounters.GetAreaList(Game.Shield, EncounterType.Fishing).First();
        var weather = OwoowEncounters.GetWeatherList(Game.Shield, EncounterType.Fishing, area).First();

        var actualWeather = await service.GetWeatherAsync(
            GameVersion.Shield,
            EncounterKind.Fishing,
            area);
        var actualSpecies = await service.GetSpeciesAsync(
            GameVersion.Shield,
            EncounterKind.Fishing,
            area,
            weather);

        Assert.Multiple(() =>
        {
            Assert.That(
                actualWeather,
                Is.EqualTo(OwoowEncounters.GetWeatherList(Game.Shield, EncounterType.Fishing, area)));
            Assert.That(
                actualSpecies,
                Is.EqualTo(
                    OwoowEncounters
                        .GetSpeciesList(Game.Shield, EncounterType.Fishing, area, weather)
                        .Order(StringComparer.Ordinal)));
        });
    }

    [Test]
    public async Task LookupMapsOriginalEntriesWithoutLeakingTypes()
    {
        var service = new OwoowEncounterCatalogService();
        var original = OwoowEncounters.GetEncounterLookupForGame(Game.Sword);
        var species = original.Keys.First();

        var actual = await service.LookupAsync(GameVersion.Sword, species);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Has.Count.EqualTo(original[species].Count));
            Assert.That(actual[0].Species, Is.EqualTo(original[species][0].Species));
            Assert.That(actual[0].Area, Is.EqualTo(original[species][0].Area));
            Assert.That(actual[0].Weather, Is.EqualTo(original[species][0].Weather));
            Assert.That(actual[0].EncounterKind, Is.EqualTo(original[species][0].EncounterType));
        });
    }

    [Test]
    public async Task EncounterTableContainsPersonalAndAbilityData()
    {
        var service = new OwoowEncounterCatalogService();
        var area = OwoowEncounters.GetAreaList(Game.Sword, EncounterType.Symbol).First();
        var weather = OwoowEncounters.GetWeatherList(Game.Sword, EncounterType.Symbol, area).First();

        var result = await service.GetTableAsync(
            new EncounterCatalogRequest(
                GameVersion.Sword,
                EncounterKind.Symbol,
                area,
                weather,
                "Static"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Slots, Is.Not.Empty);
            Assert.That(result.Slots.All(slot => slot.Personal.Abilities.Count > 0), Is.True);
            Assert.That(result.Slots.All(slot => slot.Personal.Types.Count > 0), Is.True);
        });
    }
}
