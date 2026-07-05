using AutoSwshRng.Core.Connection;
using AutoSwshRng.Core.Encounters;
using AutoSwshRng.Core.Rng;

namespace AutoSwshRng.Core.Tests.Connection;

public class OwoowConnectionContractsTests
{
    [Test]
    public void WifiRequiresHostAndValidPort()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => new OwoowConnectionSettings(
                ConnectionProtocol.Wifi,
                " ",
                6000));
            Assert.Throws<ArgumentOutOfRangeException>(() => new OwoowConnectionSettings(
                ConnectionProtocol.Wifi,
                "192.168.1.2",
                70_000));
        });
    }

    [Test]
    public void DexRecommendationCopiesSpeciesIds()
    {
        ushort[] species = [1, 2, 3, 4];
        var snapshot = new DexRecommendationSnapshot(species, "Route 1", 99);

        species[0] = 25;

        Assert.That(snapshot.SpeciesIds, Is.EqualTo(new ushort[] { 1, 2, 3, 4 }));
    }

    [Test]
    public void SnapshotCollectionsCannotBeMutatedThroughPublicContract()
    {
        var dex = new DexRecommendationSnapshot([1, 2, 3, 4], null, null);
        var pokemon = new PokemonSnapshot(
            25,
            "Pikachu",
            0,
            10,
            PokemonGender.Male,
            PokemonShinyType.None,
            "Hardy",
            1,
            0,
            1,
            2,
            new RngIndividualValues([1, 2, 3, 4, 5, 6]),
            0,
            null,
            [10]);
        var world = new WorldSnapshot(
            1,
            new WorldPosition(0, 0, 0),
            [new FieldObjectSnapshot(new WorldPosition(0, 0, 0), 1, pokemon)]);

        Assert.Multiple(() =>
        {
            Assert.Throws<NotSupportedException>(
                () => ((IList<ushort>)dex.SpeciesIds)[0] = 99);
            Assert.Throws<NotSupportedException>(
                () => ((IList<ushort>)pokemon.Moves)[0] = 99);
            Assert.Throws<NotSupportedException>(
                () => ((IList<FieldObjectSnapshot>)world.Objects).Clear());
        });
    }

    [Test]
    public void WatchIntervalMustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RngWatchRequest(TimeSpan.Zero));
    }
}
