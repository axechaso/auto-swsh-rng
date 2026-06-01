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

    [Test]
    public void ToggleCommentCommentsUncommentedLinesLikeOriginalEasyCon()
    {
        var toggled = EasyConScriptAdapter.ToggleCommentLines("PRINT \"hello\"\n  WAIT 10");

        Assert.That(toggled, Is.EqualTo("# PRINT \"hello\"\n  # WAIT 10"));
    }

    [Test]
    public void ToggleCommentUncommentsAlreadyCommentedLinesLikeOriginalEasyCon()
    {
        var toggled = EasyConScriptAdapter.ToggleCommentLines("# PRINT \"hello\"\n  # WAIT 10");

        Assert.That(toggled, Is.EqualTo("PRINT \"hello\"\n  WAIT 10"));
    }

    [Test]
    public void SupportedBoardsMatchOriginalEasyConOrder()
    {
        var boards = EasyConScriptAdapter.GetSupportedBoards();

        Assert.That(
            boards.Select(board => board.DisplayName),
            Is.EqualTo(new[] { "Leonardo", "Teensy 2.0", "Teensy 2.0++", "Beetle", "Arduino UNO R3" }));
    }

    [Test]
    public void CaptureTypesExposeOriginalOpenCvApiNames()
    {
        var captureTypes = EasyConScriptAdapter.GetCaptureTypes();

        Assert.Multiple(() =>
        {
            Assert.That(captureTypes.First().Name, Is.EqualTo("ANY"));
            Assert.That(captureTypes.Select(type => type.Name), Is.SupersetOf(new[] { "DSHOW", "MSMF", "FFMPEG" }));
        });
    }

    [Test]
    public void ScriptSyntaxHelpUsesOriginalEasyConDocument()
    {
        var help = EasyConScriptAdapter.GetScriptSyntaxHelp();

        Assert.Multiple(() =>
        {
            Assert.That(help, Does.Contain("所有代码不区分大小写"));
            Assert.That(help, Does.Contain("语法：PRINT 输出内容"));
            Assert.That(help, Does.Contain("语法：ALERT 输出内容"));
        });
    }
}
