using AutoSwshRng.Core.Profiles;

namespace AutoSwshRng.Upstream.Tests;

public class OwoowProfileStoreTests
{
    [Test]
    public async Task SettingsRoundTripThroughJson()
    {
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.json");
        try
        {
            var expected = new RngApplicationSettings(
                "main",
                [
                    new RngProfile("main", GameVersion.Sword, 1337, 1390, true, false),
                    new RngProfile("alt", GameVersion.Shield, 42, 24, false, true),
                ]);
            var store = new OwoowProfileStore(path);

            await store.SaveAsync(expected);
            var actual = await store.LoadAsync();

            Assert.That(actual, Is.EqualTo(expected));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
