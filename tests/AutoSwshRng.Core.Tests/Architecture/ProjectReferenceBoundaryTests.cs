namespace AutoSwshRng.Core.Tests.Architecture;

public class ProjectReferenceBoundaryTests
{
    [Test]
    public void AppProjectHasNoThirdPartyProjectReference()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "AutoSwshRng.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "Repository root was not found.");
        var project = File.ReadAllText(Path.Combine(
            directory!.FullName,
            "src",
            "AutoSwshRng.App",
            "AutoSwshRng.App.csproj"));

        Assert.That(project, Does.Not.Contain("third_party"));
    }
}
