using EasyCon.Script;
using EasyCon.Script.Syntax;
using EasyCon.Core.Config;
using EasyCon.Core;
using EasyCon.Script.Assembly;
using EasyScript;
using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;

namespace AutoSwshRng.Upstream;

public static class EasyConScriptAdapter
{
    private const string ScriptSyntaxHelpResourceName = "AutoSwshRng.Upstream.Resources.EasyCon.scriptdoc.txt";
    private const string CaptureHelpResourceName = "AutoSwshRng.Upstream.Resources.EasyCon.capturedoc.txt";

    private static readonly EasyConBoardDefinition[] SupportedBoards =
    [
        new("Leonardo", "Leonardo", 924),
        new("Teensy 2.0", "Teensy2", 924),
        new("Teensy 2.0++", "Teensy2pp", 3996),
        new("Beetle", "Beetle", 924),
        new("Arduino UNO R3", "UNO", 412),
    ];

    private static readonly EasyConCaptureTypeDefinition[] CaptureTypes =
    [
        new("ANY", 0),
        new("VFW", 200),
        new("V4L", 200),
        new("V4L2", 200),
        new("FIREWIRE", 300),
        new("FIREWARE", 300),
        new("IEEE1394", 300),
        new("DC1394", 300),
        new("CMU1394", 300),
        new("QT", 500),
        new("UNICAP", 600),
        new("DSHOW", 700),
        new("PVAPI", 800),
        new("OPENNI", 900),
        new("OPENNI_ASUS", 910),
        new("ANDROID", 1000),
        new("XIAPI", 1100),
        new("AVFOUNDATION", 1200),
        new("GIGANETIX", 1300),
        new("MSMF", 1400),
        new("WINRT", 1410),
        new("INTELPERC", 1500),
        new("REALSENSE", 1500),
        new("OPENNI2", 1600),
        new("OPENNI2_ASUS", 1610),
        new("GPHOTO2", 1700),
        new("GSTREAMER", 1800),
        new("FFMPEG", 1900),
        new("IMAGES", 2000),
        new("ARAVIS", 2100),
        new("OPENCV_MJPEG", 2200),
        new("INTEL_MFX", 2300),
        new("XINE", 2400),
    ];

    private static readonly string[] KeyMappingOrder =
    [
        "A",
        "B",
        "X",
        "Y",
        "L",
        "R",
        "ZL",
        "ZR",
        "Plus",
        "Minus",
        "Capture",
        "Home",
        "LClick",
        "RClick",
        "Up",
        "Down",
        "Left",
        "Right",
        "UpRight",
        "DownRight",
        "UpLeft",
        "DownLeft",
        "LSUp",
        "LSDown",
        "LSLeft",
        "LSRight",
        "RSUp",
        "RSDown",
        "RSLeft",
        "RSRight",
    ];

    public static EasyConScriptResult Evaluate(
        string scriptText,
        IReadOnlyDictionary<string, Func<int>>? externalGetters = null)
    {
        var output = new CapturingOutputAdapter();
        var compilation = Compilation.Create(SyntaxTree.Parse(scriptText));
        var externalGetterMap = externalGetters?.ToImmutableDictionary(pair => pair.Key, pair => pair.Value)
            ?? ImmutableDictionary<string, Func<int>>.Empty;
        var result = compilation.Evaluate(output, pad: null!, externalGetterMap, CancellationToken.None);

        return new EasyConScriptResult(
            result.Diagnostics.HasErrors(),
            result.Diagnostics.Select(diagnostic => diagnostic.Message).ToArray(),
            output.Printed,
            output.Alerted);
    }

    public static EasyConScriptFormatResult Format(
        string scriptText,
        IReadOnlyCollection<string>? externalGetterNames = null)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(scriptText));
        var externalNames = externalGetterNames?.ToImmutableHashSet()
            ?? ImmutableHashSet<string>.Empty;
        var diagnostics = compilation.Compile(externalNames);
        if (diagnostics.HasErrors())
        {
            return new EasyConScriptFormatResult(
                HasErrors: true,
                FormattedCode: null,
                Diagnostics: diagnostics.Select(diagnostic => diagnostic.Message).ToArray());
        }

        var formatted = compilation.FormatCode().Trim();
        formatted = Regex.Replace(formatted, @",(?! )", ", ");

        return new EasyConScriptFormatResult(
            HasErrors: false,
            FormattedCode: formatted,
            Diagnostics: []);
    }

    public static EasyConFirmwareAssemblyResult AssembleFirmwareScript(string scriptText)
    {
        return AssembleFirmwareScript(scriptText, null);
    }

    public static EasyConFirmwareAssemblyResult AssembleFirmwareScript(
        string scriptText,
        IReadOnlyDictionary<string, Func<int>>? externalGetters)
    {
        var scripter = new Scripter();
        var getterMap = externalGetters?.ToDictionary(pair => pair.Key, pair => pair.Value)
            ?? [];
        var diagnostics = scripter.Parse(scriptText, null!, getterMap);
        if (diagnostics.HasErrors())
        {
            return new EasyConFirmwareAssemblyResult(
                Success: false,
                Bytes: [],
                ErrorMessage: string.Join(Environment.NewLine, diagnostics.Where(diagnostic => diagnostic.IsError).Select(diagnostic => diagnostic.Message)));
        }

        try
        {
            return new EasyConFirmwareAssemblyResult(
                Success: true,
                Bytes: scripter.Assemble(auto: true),
                ErrorMessage: null);
        }
        catch (NotImplementedException)
        {
            return new EasyConFirmwareAssemblyResult(
                Success: false,
                Bytes: [],
                ErrorMessage: "此版本暂不支持编译");
        }
        catch (AssembleException ex)
        {
            return new EasyConFirmwareAssemblyResult(
                Success: false,
                Bytes: [],
                ErrorMessage: ex.Message);
        }
    }

    public static IReadOnlyList<EasyConBoardDefinition> GetSupportedBoards()
    {
        return SupportedBoards;
    }

    public static IReadOnlyList<EasyConCaptureTypeDefinition> GetCaptureTypes()
    {
        return CaptureTypes;
    }

    public static IReadOnlyList<EasyConKeyMappingEntry> GetDefaultKeyMapping()
    {
        var config = new KeyMappingConfig();
        return KeyMappingOrder
            .Select(name =>
            {
                var property = typeof(KeyMappingConfig).GetProperty(name)
                    ?? throw new InvalidOperationException($"EasyCon key mapping property '{name}' was not found.");
                return new EasyConKeyMappingEntry(name, (int)property.GetValue(config)!);
            })
            .ToArray();
    }

    public static EasyConAlertConfigDefinition GetDefaultAlertConfig()
    {
        var config = new AlertConfig
        {
            timeout = 10,
            alerts =
            [
                new AlertItem
                {
                    name = "PushPlus",
                    enable = false,
                    url = "https://www.pushplus.plus/send/{{token}}?content={{content}}&title={{title}}",
                    token = string.Empty,
                },
                new AlertItem
                {
                    name = "Bark",
                    enable = false,
                    url = "https://api.day.app/{{token}}/{{title}}/{{content}}?group={{group}}&icon={{icon}}",
                    token = string.Empty,
                    variables = new Dictionary<string, string>
                    {
                        ["group"] = "伊机控",
                        ["icon"] = "https://avatars.githubusercontent.com/u/107608104?s=48&v=4",
                    },
                },
                new AlertItem
                {
                    name = "自定义Webhook",
                    enable = false,
                    method = "POST",
                    url = "https://example.com/webhook",
                    token = string.Empty,
                    headers = new Dictionary<string, string>
                    {
                        ["Authorization"] = "Bearer {{token}}",
                        ["Content-Type"] = "application/json",
                    },
                    body = "{\"msg\":\"{{content}}\"}",
                    variables = new Dictionary<string, string>
                    {
                        ["chat_id"] = string.Empty,
                    },
                },
            ],
        };

        return new EasyConAlertConfigDefinition(
            config.timeout,
            config.alerts.Select(alert => new EasyConAlertProviderDefinition(alert.name, alert.enable, alert.method)).ToArray());
    }

    public static string GetScriptSyntaxHelp()
    {
        return ReadEmbeddedUtf8Resource(ScriptSyntaxHelpResourceName);
    }

    public static string GetCaptureHelp()
    {
        return ReadEmbeddedUtf8Resource(CaptureHelpResourceName);
    }

    private static string ReadEmbeddedUtf8Resource(string resourceName)
    {
        using var stream = typeof(EasyConScriptAdapter).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public static string ToggleCommentLines(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var separator = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Split([separator], StringSplitOptions.None);
        var shouldComment = lines.Any(CanComment);
        return string.Join(separator, lines.Select(line => ToggleCommentLine(line, shouldComment)));
    }

    private static bool CanComment(string input)
    {
        var firstNonWhitespaceIndex = FindFirstNonWhitespaceIndex(input);
        return firstNonWhitespaceIndex >= 0 && input[firstNonWhitespaceIndex] != '#';
    }

    private static string ToggleCommentLine(string input, bool shouldComment)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return input ?? string.Empty;
        }

        var firstNonWhitespaceIndex = FindFirstNonWhitespaceIndex(input);
        if (firstNonWhitespaceIndex < 0)
        {
            return input;
        }

        if (shouldComment)
        {
            return input.Insert(firstNonWhitespaceIndex, "# ");
        }

        var removeCount = firstNonWhitespaceIndex + 1 < input.Length && input[firstNonWhitespaceIndex + 1] == ' '
            ? 2
            : 1;
        return input.Remove(firstNonWhitespaceIndex, removeCount);
    }

    private static int FindFirstNonWhitespaceIndex(string input)
    {
        for (var i = 0; i < input.Length; i++)
        {
            if (!char.IsWhiteSpace(input[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private sealed class CapturingOutputAdapter : IOutputAdapter
    {
        private readonly List<string> printed = [];
        private readonly List<string> alerted = [];

        public IReadOnlyList<string> Printed => printed;

        public IReadOnlyList<string> Alerted => alerted;

        public void Print(string message, bool newline)
        {
            printed.Add(newline ? message + Environment.NewLine : message);
        }

        public void Alert(string message)
        {
            alerted.Add(message);
        }
    }
}

public sealed record EasyConScriptResult(
    bool HasErrors,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<string> Printed,
    IReadOnlyList<string> Alerted);

public sealed record EasyConScriptFormatResult(
    bool HasErrors,
    string? FormattedCode,
    IReadOnlyList<string> Diagnostics);

public sealed record EasyConFirmwareAssemblyResult(
    bool Success,
    IReadOnlyList<byte> Bytes,
    string? ErrorMessage);

public sealed record EasyConBoardDefinition(
    string DisplayName,
    string CoreName,
    int DataSize)
{
    public override string ToString()
    {
        return DisplayName;
    }
}

public sealed record EasyConCaptureTypeDefinition(
    string Name,
    int Value);

public sealed record EasyConKeyMappingEntry(
    string ControlName,
    int KeyCode);

public sealed record EasyConAlertConfigDefinition(
    int TimeoutSeconds,
    IReadOnlyList<EasyConAlertProviderDefinition> Providers);

public sealed record EasyConAlertProviderDefinition(
    string Name,
    bool Enabled,
    string Method);
