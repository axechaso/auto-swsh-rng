using AutoSwshRng.Upstream;

namespace AutoSwshRng.Upstream.Tests;

public class UpstreamInfoTests
{
    [Test]
    public void OwoowInfoIdentifiesTheReferencedCoreAssembly()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OwoowUpstreamInfo.AssemblyName, Is.EqualTo("owoow.Core"));
            Assert.That(OwoowUpstreamInfo.GameEnumTypeName, Is.EqualTo("owoow.Core.Enums.Game"));
            Assert.That(OwoowUpstreamInfo.CalculateShinyValue(0x1234, 0x00FF), Is.EqualTo(0x12CB));
        });
    }

    [Test]
    public void EasyConInfoIdentifiesTheReferencedCoreAssemblies()
    {
        Assert.Multiple(() =>
        {
            Assert.That(EasyConUpstreamInfo.DeviceAssemblyName, Is.EqualTo("EasyCon.Device"));
            Assert.That(EasyConUpstreamInfo.ScriptAssemblyName, Is.EqualTo("EasyCon.Script"));
            Assert.That(EasyConUpstreamInfo.DirectionKeyTypeName, Is.EqualTo("EasyDevice.DirectionKey"));
            Assert.That(EasyConUpstreamInfo.GamePadKeyTypeName, Is.EqualTo("EasyScript.GamePadKey"));
        });
    }
}
