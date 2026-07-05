using AutoSwshRng.Core.Connection;

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
    public void WatchIntervalMustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RngWatchRequest(TimeSpan.Zero));
    }
}
