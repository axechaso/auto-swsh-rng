namespace AutoSwshRng.Upstream.Tests;

public class OwoowRngAdapterTests
{
    [Test]
    public void CalculatesBasicShinyAndHeightValuesThroughOwoow()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OwoowRngAdapter.GetShinyValue(0x1234, 0x00FF), Is.EqualTo(0x12CB));
            Assert.That(OwoowRngAdapter.GetShinyType(0), Is.EqualTo("Square"));
            Assert.That(OwoowRngAdapter.GetHeightString(100), Is.EqualTo("M (100)"));
        });
    }
}
