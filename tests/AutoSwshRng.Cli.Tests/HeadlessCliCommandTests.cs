using AutoSwshRng.Cli;

namespace AutoSwshRng.Cli.Tests;

public class HeadlessCliCommandTests
{
    [TestCase("audit")]
    [TestCase("catalog Sword Symbol")]
    [TestCase("rng next 1234 5678 1")]
    [TestCase("calibrate menu 1234 5678 0 false Normal")]
    [TestCase("tool wailord 1234 5678 0 1")]
    [TestCase("sequence-dry-run A:50 wait:10")]
    public async Task HeadlessCommandsRunWithoutUi(string command)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await HeadlessCliCommand.RunAsync(
            command.Split(' '),
            output,
            error,
            HeadlessCliServices.CreateDefaults());

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.Zero);
            Assert.That(error.ToString(), Is.Empty);
            Assert.That(output.ToString(), Is.Not.Empty);
        });
    }

    [Test]
    public async Task UnknownCommandReportsAnActionableError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await HeadlessCliCommand.RunAsync(
            ["unknown"],
            output,
            error,
            HeadlessCliServices.CreateDefaults());

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(2));
            Assert.That(error.ToString(), Does.Contain("Unknown command"));
        });
    }
}
