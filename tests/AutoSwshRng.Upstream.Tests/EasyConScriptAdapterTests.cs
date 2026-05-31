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
}
