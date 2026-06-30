using AutoSwshRng.Core;

namespace AutoSwshRng.Core.Tests.Architecture;

public class CoreBoundaryTests
{
    [Test]
    public void CoreReferencesNoUpstreamOrUiAssembly()
    {
        var forbiddenFragments = new[]
        {
            "owoow",
            "EasyCon",
            "Windows.Forms",
            "Avalonia",
            "PKHeX",
            "SysBot",
            "OpenCv",
        };

        var referencedAssemblies = typeof(ProjectInfo).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty)
            .ToArray();

        Assert.That(
            referencedAssemblies,
            Has.None.Matches<string>(
                assembly => forbiddenFragments.Any(
                    fragment => assembly.Contains(fragment, StringComparison.OrdinalIgnoreCase))));
    }
}
