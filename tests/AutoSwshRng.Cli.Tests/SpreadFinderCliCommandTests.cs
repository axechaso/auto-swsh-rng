using AutoSwshRng.Cli;
using AutoSwshRng.Upstream;

namespace AutoSwshRng.Cli.Tests;

public class SpreadFinderCliCommandTests
{
    [Test]
    public async Task RunsHeadlessSpreadSearchForAHexSeed()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await SpreadFinderCliCommand.RunAsync(
            ["spread", "17033091", "0"],
            output,
            error,
            new OwoowSpreadFinderService());

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(0));
            Assert.That(error.ToString(), Is.Empty);
            Assert.That(output.ToString(), Does.Contain("Seed=17033091"));
            Assert.That(output.ToString(), Does.Contain("EC="));
            Assert.That(output.ToString(), Does.Contain("IVs=0/0/0/0/0/0"));
            Assert.That(output.ToString(), Does.Contain("Height="));
            Assert.That(output.ToString(), Does.Contain("Scale="));
        });
    }

    [TestCase("spread", "not-hex", "0")]
    [TestCase("spread", "17033091", "7")]
    [TestCase("unknown", "17033091", "0")]
    public async Task RejectsInvalidArgumentsWithUsage(string command, string seed, string guaranteedIndividualValues)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await SpreadFinderCliCommand.RunAsync(
            [command, seed, guaranteedIndividualValues],
            output,
            error,
            new OwoowSpreadFinderService());

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(2));
            Assert.That(output.ToString(), Is.Empty);
            Assert.That(error.ToString(), Does.Contain("Usage:"));
        });
    }
}
