namespace AutoSwshRng.Upstream.Tests;

public class UpstreamSmokeReportTests
{
    [Test]
    public void CreatesReportWithOwoowAndEasyConEntries()
    {
        var report = UpstreamSmokeReport.Create();

        Assert.Multiple(() =>
        {
            Assert.That(report.Entries.Select(entry => entry.Key), Is.EqualTo(new[] { "owoow", "easycon" }));
            Assert.That(report.Entries, Has.All.Property(nameof(UpstreamSmokeEntry.Passed)).True);
            Assert.That(report.GetRequired("owoow").Summary, Does.Contain("Square"));
            Assert.That(report.GetRequired("easycon").Summary, Does.Contain("hello"));
        });
    }

    [Test]
    public void FormatsProviderStatusForCliAndUi()
    {
        var report = UpstreamSmokeReport.Create();

        Assert.That(report.ToDisplayText(), Does.Contain("owoow: OK"));
        Assert.That(report.ToDisplayText(), Does.Contain("EasyCon: OK"));
    }
}
