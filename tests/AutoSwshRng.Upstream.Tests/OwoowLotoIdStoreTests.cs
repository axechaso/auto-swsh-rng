using AutoSwshRng.Upstream;

namespace AutoSwshRng.Upstream.Tests;

public class OwoowLotoIdStoreTests
{
    [Test]
    public async Task IdsRoundTripSortedAndDeduplicated()
    {
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.json");
        try
        {
            var store = new OwoowLotoIdStore(path);

            await store.SaveAsync(["654321", "123456", "654321"]);
            var actual = await store.LoadAsync();

            Assert.That(actual, Is.EqualTo(new[] { "123456", "654321" }));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Test]
    public void RejectsIdsThatAreNotSixDigits()
    {
        var store = new OwoowLotoIdStore(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.json"));

        Assert.ThrowsAsync<ArgumentException>(async () => await store.SaveAsync(["12345A"]));
    }
}
