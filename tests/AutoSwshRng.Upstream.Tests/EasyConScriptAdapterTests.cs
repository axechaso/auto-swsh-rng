using EasyScript;

namespace AutoSwshRng.Upstream.Tests;

public class EasyConScriptAdapterTests
{
    [Test]
    public void ExcludedFirmwareAndSyntaxApisAreNotExposed()
    {
        var methodNames = typeof(EasyConScriptAdapter)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Select(method => method.Name);
        var assembly = typeof(EasyConScriptAdapter).Assembly;

        Assert.Multiple(() =>
        {
            Assert.That(methodNames, Does.Not.Contain("AssembleFirmwareScript"));
            Assert.That(methodNames, Does.Not.Contain("GetSupportedBoards"));
            Assert.That(methodNames, Does.Not.Contain("GetScriptSyntaxHelp"));
            Assert.That(assembly.GetType("AutoSwshRng.Upstream.EasyConFirmwareAssemblyResult"), Is.Null);
            Assert.That(assembly.GetType("AutoSwshRng.Upstream.EasyConBoardDefinition"), Is.Null);
        });
    }

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
    public async Task ExecutesKeyScriptThroughProvidedGamePadAdapter()
    {
        var gamePad = new RecordingGamePad();

        var result = await EasyConScriptAdapter.ExecuteAsync("A", gamePad);

        Assert.Multiple(() =>
        {
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(gamePad.ClickedKeys, Is.EqualTo(new[] { GamePadKey.A }));
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

    private sealed class RecordingGamePad : ICGamePad
    {
        public List<GamePadKey> ClickedKeys { get; } = [];

        public DelayType DelayMethod => DelayType.Normal;

        public void ClickButtons(GamePadKey key, int duration, CancellationToken token)
        {
            ClickedKeys.Add(key);
        }

        public void PressButtons(GamePadKey key)
        {
        }

        public void ReleaseButtons(GamePadKey key)
        {
        }

        public void ClickStick(GamePadKey key, byte x, byte y, int duration, CancellationToken token)
        {
        }

        public void SetStick(GamePadKey key, byte x, byte y)
        {
        }

        public void ChangeAmiibo(uint index)
        {
        }
    }
}
