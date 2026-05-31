namespace AutoSwshRng.Upstream.Tests;

public class EasyConScriptAdapterTests
{
    [Test]
    public void EvaluatesPrintScriptWithoutSerialDevice()
    {
        var result = EasyConScriptAdapter.Evaluate("PRINT \"hello\"");

        Assert.Multiple(() =>
        {
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Printed, Is.EqualTo(new[] { "hello\r\n" }));
            Assert.That(result.Alerted, Is.Empty);
        });
    }

    [Test]
    public void FormatsScriptThroughOriginalEasyConFormatter()
    {
        var result = EasyConScriptAdapter.Format("PRINT \"hello\",\"world\"");

        Assert.Multiple(() =>
        {
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.FormattedCode, Is.EqualTo("PRINT \"hello\", \"world\""));
            Assert.That(result.Diagnostics, Is.Empty);
        });
    }
}
