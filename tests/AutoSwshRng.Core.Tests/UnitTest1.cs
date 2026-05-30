using AutoSwshRng.Core;

namespace AutoSwshRng.Core.Tests;

public class ProjectInfoTests
{
    [Test]
    public void MetadataDescribesTheProject()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProjectInfo.Name, Is.EqualTo("auto-swsh-rng"));
            Assert.That(ProjectInfo.Description, Is.EqualTo("尝试剑盾乱数自动化"));
        });
    }
}
