namespace AutoSwshRng.Core.Tests.Architecture;

public class ProjectReferenceBoundaryTests
{
    [Test]
    public void AppProjectHasNoThirdPartyProjectReference()
    {
        var project = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "AutoSwshRng.App",
            "AutoSwshRng.App.csproj"));

        Assert.That(project, Does.Not.Contain("third_party"));
    }

    [Test]
    public void UpstreamIsTheOnlySourceProjectReferencingThirdParty()
    {
        var offenders = Directory
            .EnumerateFiles(
                Path.Combine(FindRepositoryRoot(), "src"),
                "*.csproj",
                SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains(
                "third_party",
                StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .ToArray();

        Assert.That(offenders, Is.EqualTo(new[] { "AutoSwshRng.Upstream.csproj" }));
    }

    [Test]
    public void CapabilityMatrixContainsNoIncompleteStatus()
    {
        var matrix = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "docs",
            "upstream-capability-matrix.md"));

        Assert.Multiple(() =>
        {
            Assert.That(matrix, Does.Not.Contain("待抽象"));
            Assert.That(matrix, Does.Not.Contain("部分完成"));
            Assert.That(matrix, Does.Not.Contain("（计划）"));
        });
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "AutoSwshRng.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
