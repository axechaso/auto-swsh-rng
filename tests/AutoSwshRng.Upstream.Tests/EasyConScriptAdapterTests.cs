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
    public void CompileReportsOriginalKeyActionRequirement()
    {
        var result = EasyConScriptAdapter.Compile("A");

        Assert.Multiple(() =>
        {
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.HasKeyAction, Is.True);
            Assert.That(result.NeedsImageLabels, Is.False);
        });
    }

    [Test]
    public void FirmwareAssemblyReportsOriginalUnsupportedCompilerMessage()
    {
        var result = EasyConScriptAdapter.AssembleFirmwareScript("PRINT \"hello\"");

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Bytes, Is.Empty);
            Assert.That(result.ErrorMessage, Is.EqualTo("此版本暂不支持编译"));
        });
    }

    [Test]
    public void FirmwareAssemblyAcceptsOriginalCaptureExternalVariables()
    {
        var result = EasyConScriptAdapter.AssembleFirmwareScript(
            "PRINT @target",
            new Dictionary<string, Func<int>>
            {
                ["target"] = () => 7,
            });

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Bytes, Is.Empty);
            Assert.That(result.ErrorMessage, Is.EqualTo("此版本暂不支持编译"));
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

    [Test]
    public void CaptureHelpUsesOriginalEasyConDocument()
    {
        var help = EasyConScriptAdapter.GetCaptureHelp();

        Assert.Multiple(() =>
        {
            Assert.That(help, Does.Contain("操作说明："));
            Assert.That(help, Does.Contain("点击圈选范围"));
            Assert.That(help, Does.Contain("【搜图语法】"));
        });
    }

    [Test]
    public void DefaultKeyMappingUsesOriginalEasyConConfig()
    {
        var mapping = EasyConScriptAdapter.GetDefaultKeyMapping();

        Assert.Multiple(() =>
        {
            Assert.That(mapping, Has.Count.EqualTo(30));
            Assert.That(mapping.Single(entry => entry.ControlName == "A").KeyCode, Is.EqualTo(76));
            Assert.That(mapping.Single(entry => entry.ControlName == "B").KeyCode, Is.EqualTo(75));
            Assert.That(mapping.Single(entry => entry.ControlName == "Plus").KeyCode, Is.EqualTo(107));
            Assert.That(mapping.Single(entry => entry.ControlName == "LSUp").KeyCode, Is.EqualTo(87));
            Assert.That(mapping.Single(entry => entry.ControlName == "RSUp").KeyCode, Is.EqualTo(38));
        });
    }

    [Test]
    public void DefaultAlertConfigUsesOriginalEasyConProviders()
    {
        var config = EasyConScriptAdapter.GetDefaultAlertConfig();

        Assert.Multiple(() =>
        {
            Assert.That(config.TimeoutSeconds, Is.EqualTo(10));
            Assert.That(config.Providers.Select(provider => provider.Name), Is.EqualTo(new[] { "PushPlus", "Bark", "自定义Webhook" }));
            Assert.That(config.Providers.Select(provider => provider.Enabled), Is.EqualTo(new[] { false, false, false }));
            Assert.That(config.Providers.Single(provider => provider.Name == "PushPlus").Method, Is.EqualTo("GET"));
            Assert.That(config.Providers.Single(provider => provider.Name == "Bark").Method, Is.EqualTo("GET"));
            Assert.That(config.Providers.Single(provider => provider.Name == "自定义Webhook").Method, Is.EqualTo("POST"));
        });
    }
}
