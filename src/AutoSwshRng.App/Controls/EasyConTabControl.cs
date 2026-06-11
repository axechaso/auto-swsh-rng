using AutoSwshRng.Upstream;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace AutoSwshRng.App.Controls;

public sealed class EasyConTabControl : UserControl
{
    private string? currentScriptPath;
    private bool currentScriptModified;
    private string selectedCaptureType = "ANY";
    private Func<string?> chooseOpenScriptPath = null!;
    private Func<string?> chooseSaveScriptPath = null!;
    private Func<DialogResult> confirmSaveModifiedScript = null!;
    private Action<string, string> showEasyConMessage = null!;
    private Action<string> openExternalLink = null!;
    private Func<string[]> getSerialPortNames = null!;
    private Func<IReadOnlyList<(string Name, int Index)>> getVideoSources = null!;
    private Action openCaptureConsole = null!;
    private Action openScriptSyntaxHelp = null!;
    private Action openAlertConfigDialog = null!;
    private Action openEspConfigDialog = null!;
    private Action openDrawingBoard = null!;
    private Action openKeyMappingDialog = null!;
    private Func<Task<string?>> checkForUpdateMessageAsync = null!;
    private Func<Task<(bool Success, string? Port)>> autoConnectDeviceAsync = null!;
    private Func<string, Task<bool>> manualConnectDeviceAsync = null!;
    private Func<bool> isDeviceConnected = null!;
    private Func<bool> unpairDevice = null!;

    private static readonly string[] MenuItems =
    [
        "文件",
        "编辑",
        "脚本",
        "搜图",
        "设置",
        "蓝牙",
        "ESP32",
        "画图",
        "帮助",
    ];

    public EasyConTabControl()
    {
        Dock = DockStyle.Fill;
        chooseOpenScriptPath = ShowOpenScriptDialog;
        chooseSaveScriptPath = ShowSaveScriptDialog;
        confirmSaveModifiedScript = ShowSaveModifiedDialog;
        showEasyConMessage = ShowEasyConMessageBox;
        openExternalLink = OpenExternalLink;
        getSerialPortNames = GetSerialPortNames;
        getVideoSources = GetVideoSources;
        openCaptureConsole = ShowCaptureConsolePendingMessage;
        openScriptSyntaxHelp = ShowScriptSyntaxHelp;
        openAlertConfigDialog = ShowAlertConfigDialog;
        openEspConfigDialog = () => ShowPendingOriginalDialog("ESP32设置");
        openDrawingBoard = () => ShowPendingOriginalDialog("画图工具");
        openKeyMappingDialog = ShowKeyMappingDialog;
        checkForUpdateMessageAsync = GetOriginalUpdateMessageAsync;
        autoConnectDeviceAsync = () => Task.FromResult((false, (string?)null));
        manualConnectDeviceAsync = _ => Task.FromResult(false);
        isDeviceConnected = () => false;
        unpairDevice = () => false;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.Controls.Add(CreateOriginalMenu(), 0, 0);
        root.Controls.Add(CreateMainArea(), 0, 1);
        root.Controls.Add(CreateStatusStrip(), 0, 2);

        Controls.Add(root);
        WireEditorState();
        WireFileActions();
        WireEditActions();
        WireCaptureActions();
        WireHelpActions();
        WireDeviceGuardActions();
        WireSettingsActions();
        WireDeviceListActions();
        WirePageButtons();
        WireScriptActions();
        WireFirmwareActions();
        PopulateFirmwareBoards();
        InitializeOriginalStartupState();
    }

    public bool CloseForParentForm()
    {
        return CloseCurrentScript();
    }

    private void WireFileActions()
    {
        FindRequiredMenuItem("menuItemNew").Click += (_, _) => NewCurrentScript();
        FindRequiredMenuItem("menuItemOpen").Click += (_, _) => OpenCurrentScript();
        FindRequiredMenuItem("menuItemSave").Click += (_, _) => SaveCurrentScript();
        FindRequiredMenuItem("menuItemSaveAs").Click += (_, _) => SaveCurrentScript(asNew: true);
        FindRequiredMenuItem("menuItemClose").Click += (_, _) => CloseCurrentScript();
        FindRequiredMenuItem("menuItemExit").Click += (_, _) =>
        {
            if (CloseCurrentScript())
            {
                FindForm()?.Close();
            }
        };
    }

    private void WireEditorState()
    {
        FindRequiredControl<TextBox>("easyConScriptEditor").TextChanged += (_, _) => currentScriptModified = true;
    }

    private void WireEditActions()
    {
        FindRequiredMenuItem("menuItemToggleComment").Click += (_, _) => ToggleCurrentComment();
    }

    private void WireCaptureActions()
    {
        FindRequiredMenuItem("captureTypeMenu").DropDownOpening += (_, _) => PopulateCaptureTypeMenu();
        FindRequiredMenuItem("setEnvVarMenuItem").Click += (_, _) => SetCaptureEnvironmentVariable();
        FindRequiredMenuItem("captureHelpMenuItem").Click += (_, _) => ShowCaptureHelp();
    }

    private void WireHelpActions()
    {
        FindRequiredMenuItem("menuItemFirmwareMode").Click += (_, _) => ShowFirmwareModeHelp();
        FindRequiredMenuItem("menuItemOnlineMode").Click += (_, _) => ShowOnlineModeHelp();
        FindRequiredMenuItem("menuItemFlashMode").Click += (_, _) => ShowFlashModeHelp();
        FindRequiredMenuItem("menuItemScriptSyntax").Click += (_, _) => openScriptSyntaxHelp();
        FindRequiredMenuItem("menuItemAbout").Click += (_, _) => ShowAboutMessage();
    }

    private void WireDeviceGuardActions()
    {
        FindRequiredControl<Button>("btnAutoConnect").Click += (_, _) => _ = AutoConnectDeviceAsync();
        FindRequiredControl<Button>("btnManualConnect").Click += (_, _) => _ = ManualConnectDeviceAsync();
        FindRequiredControl<Button>("btnCaptureToggle").Click += (_, _) => ShowCaptureSourceRequiredWarning();
        FindRequiredControl<Button>("btnRemoteStart").Click += (_, _) => ShowDeviceNotConnectedWarning();
        FindRequiredControl<Button>("btnRemoteStop").Click += (_, _) => ShowDeviceNotConnectedWarning();
        FindRequiredControl<Button>("btnFlash").Click += (_, _) => ShowDeviceNotConnectedWarning();
        FindRequiredControl<Button>("btnFlashClear").Click += (_, _) => ShowDeviceNotConnectedWarning();
        FindRequiredControl<Button>("btnRecord").Click += (_, _) => ShowDeviceNotConnectedWarning();
        FindRequiredControl<Button>("btnShowController").Click += (_, _) => ShowDeviceNotConnectedWarning();
    }

    private void WireSettingsActions()
    {
        FindRequiredControl<Button>("btnAlertConfig").Click += (_, _) => openAlertConfigDialog();
        FindRequiredControl<Button>("btnESPConfig").Click += (_, _) => openEspConfigDialog();
        FindRequiredControl<Button>("btnDrawingBoard").Click += (_, _) => openDrawingBoard();
        FindRequiredControl<Button>("btnKeyMapping").Click += (_, _) => openKeyMappingDialog();
        FindRequiredControl<Button>("btnUnpair").Click += (_, _) => UnpairDevice();
        FindRequiredControl<Button>("btnCheckUpdate").Click += (_, _) => _ = CheckForUpdatesAsync();
        FindRequiredControl<Button>("btnSource").Click += (_, _) => openExternalLink("https://github.com/EasyConNS/EasyCon");
    }

    private void WireDeviceListActions()
    {
        FindRequiredControl<ComboBox>("comboComPort").DropDown += (_, _) => RefreshSerialPorts();
        FindRequiredControl<ComboBox>("comboVideoSource").DropDown += (_, _) => RefreshVideoSources();
        FindRequiredControl<Button>("btnOpenCaptureConsole").Click += (_, _) => openCaptureConsole();
    }

    private void WirePageButtons()
    {
        FindRequiredControl<Button>("btnPageEditor").Click += (_, _) => ShowPage("editorHost", "btnPageEditor", showScriptTitle: true);
        FindRequiredControl<Button>("btnPageLog").Click += (_, _) => ShowPage("logPanel", "btnPageLog", showScriptTitle: false);
        FindRequiredControl<Button>("btnPageBurn").Click += (_, _) => ShowPage("burnPanel", "btnPageBurn", showScriptTitle: false);
        FindRequiredControl<Button>("btnPageSettings").Click += (_, _) => ShowPage("settingsPanel", "btnPageSettings", showScriptTitle: false);
    }

    private void WireFirmwareActions()
    {
        FindRequiredControl<Button>("btnGenFirmware").Click += (_, _) => GenerateFirmware();
    }

    private void ShowPage(string pageName, string selectedButtonName, bool showScriptTitle)
    {
        ResetPages();

        var page = FindRequiredControl<Control>(pageName);
        page.Visible = true;
        page.BringToFront();

        var scriptTitle = FindRequiredControl<Label>("scriptTitleLabel");
        scriptTitle.Visible = showScriptTitle;
        if (showScriptTitle)
        {
            scriptTitle.BringToFront();
        }

        FindRequiredControl<Button>(selectedButtonName).BackColor = Color.FromArgb(235, 234, 229);
    }

    private void ResetPages()
    {
        foreach (var pageName in new[] { "burnPanel", "settingsPanel", "logPanel", "editorHost" })
        {
            FindRequiredControl<Control>(pageName).Visible = false;
        }

        FindRequiredControl<Label>("scriptTitleLabel").Visible = false;

        foreach (var buttonName in new[] { "btnPageEditor", "btnPageLog", "btnPageBurn", "btnPageSettings" })
        {
            FindRequiredControl<Button>(buttonName).BackColor = Color.FromArgb(230, 229, 224);
        }
    }

    private T FindRequiredControl<T>(string name)
        where T : Control
    {
        return Controls.Find(name, searchAllChildren: true).OfType<T>().First();
    }

    private void WireScriptActions()
    {
        var runButton = FindRequiredControl<Button>("runStopBtn");
        runButton.Click += (_, _) => RunCurrentScript();
        FindRequiredMenuItem("runMenuItem").Click += (_, _) => RunCurrentScript();

        var formatButton = FindRequiredControl<Button>("formatBtn");
        formatButton.Click += (_, _) => FormatCurrentScript();
        FindRequiredMenuItem("formatMenuItem").Click += (_, _) => FormatCurrentScript();
    }

    private void PopulateFirmwareBoards()
    {
        var boardType = FindRequiredControl<ComboBox>("comboBoardType");
        boardType.Items.AddRange(EasyConScriptAdapter.GetSupportedBoards().Cast<object>().ToArray());
        if (boardType.Items.Count > 0)
        {
            boardType.SelectedIndex = 0;
        }
    }

    private void PopulateCaptureTypeMenu()
    {
        var captureTypeMenu = FindRequiredMenuItem("captureTypeMenu");
        captureTypeMenu.DropDownItems.Clear();

        foreach (var captureType in EasyConScriptAdapter.GetCaptureTypes())
        {
            var item = new ToolStripMenuItem
            {
                Name = "captureType_" + captureType.Name,
                Text = captureType.Name,
                Tag = captureType.Value,
                Checked = captureType.Name == selectedCaptureType,
            };
            item.Click += CaptureTypeItemClick;
            captureTypeMenu.DropDownItems.Add(item);
        }
    }

    private void CaptureTypeItemClick(object? sender, EventArgs e)
    {
        var selected = (ToolStripMenuItem)sender!;
        selectedCaptureType = selected.Text ?? "ANY";

        var captureTypeMenu = FindRequiredMenuItem("captureTypeMenu");
        foreach (var item in captureTypeMenu.DropDownItems.OfType<ToolStripMenuItem>())
        {
            item.Checked = item == selected;
        }
    }

    private void SetCaptureEnvironmentVariable()
    {
        Environment.SetEnvironmentVariable("OPENCV_VIDEOIO_MSMF_ENABLE_HW_TRANSFORMS", "0");
        var value = Environment.GetEnvironmentVariable("OPENCV_VIDEOIO_MSMF_ENABLE_HW_TRANSFORMS");
        ShowStatus($"环境变量设置成功：{value}");
    }

    private void ShowCaptureHelp()
    {
        showEasyConMessage("搜图说明", EasyConScriptAdapter.GetCaptureHelp());
    }

    private void ShowFirmwareModeHelp()
    {
        showEasyConMessage("固件模式",
            "- 生成固件后手动刷入单片机的模式" + Environment.NewLine +
            "- 独立挂机，即插即用" + Environment.NewLine +
            "- 支持极限效率脚本" + Environment.NewLine +
            "- 不需要任何额外配件" + Environment.NewLine + Environment.NewLine +
            "详细使用教程见群946057081文档");
    }

    private void ShowOnlineModeHelp()
    {
        showEasyConMessage("联机模式",
            "- 使用电脑控制单片机的模式" + Environment.NewLine +
            "- 可视化运行，一键切换脚本（即将实装）" + Environment.NewLine +
            "- 无需反复刷固件" + Environment.NewLine +
            "- 支持超长脚本" + Environment.NewLine +
            "- 可使用虚拟手柄，用键盘玩游戏" + Environment.NewLine + Environment.NewLine +
            "详细使用教程见群946057081文档");
    }

    private void ShowFlashModeHelp()
    {
        showEasyConMessage("烧录模式",
            "- 连线烧录后脱机运行的模式" + Environment.NewLine +
            "- 独立挂机，即插即用" + Environment.NewLine +
            "- 一键烧录，可控运行" + Environment.NewLine +
            "- 无需反复刷固件" + Environment.NewLine +
            "- 支持极限效率脚本" + Environment.NewLine + Environment.NewLine +
            "详细使用教程见群946057081文档");
    }

    private void ShowAboutMessage()
    {
        var version = typeof(EasyConTabControl).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
        var plusIndex = version.IndexOf('+', StringComparison.Ordinal);
        if (plusIndex > 0)
        {
            version = version[..plusIndex];
        }

        showEasyConMessage("关于",
            $"伊机控 v{version}  QQ群 946057081" + Environment.NewLine + Environment.NewLine +
            "Copyright © 2020. 铃落(Nukieberry)" + Environment.NewLine +
            "Copyright © 2021. elmagnifico" + Environment.NewLine +
            "Copyright © 2025. 卡尔(ca1e)");
    }

    private void ShowPendingOriginalDialog(string title)
    {
        showEasyConMessage(title, $"{title}窗口正在接入原版实现。");
    }

    private void ShowScriptSyntaxHelp()
    {
        showEasyConMessage("脚本语法", EasyConScriptAdapter.GetScriptSyntaxHelp());
    }

    private void ShowCaptureConsolePendingMessage()
    {
        showEasyConMessage("搜图", "搜图控制台功能开发中");
    }

    private void ShowKeyMappingDialog()
    {
        var message = new StringBuilder();
        foreach (var entry in EasyConScriptAdapter.GetDefaultKeyMapping())
        {
            message.Append(entry.ControlName);
            message.Append(": ");
            message.Append(KeyCodeToDisplayName(entry.KeyCode));
            message.AppendLine();
        }

        showEasyConMessage("按键映射", message.ToString().TrimEnd());
    }

    private void ShowAlertConfigDialog()
    {
        var config = EasyConScriptAdapter.GetDefaultAlertConfig();
        var message = new StringBuilder();
        message.Append("超时: ");
        message.Append(config.TimeoutSeconds);
        message.AppendLine(" 秒");

        foreach (var provider in config.Providers)
        {
            message.Append(provider.Name);
            message.Append(": ");
            message.AppendLine(provider.Enabled ? "开启" : "关闭");
        }

        showEasyConMessage("推送配置", message.ToString().TrimEnd());
    }

    private static string KeyCodeToDisplayName(int keyCode)
    {
        return keyCode == 0
            ? string.Empty
            : Enum.GetName(typeof(Keys), (Keys)keyCode) ?? keyCode.ToString();
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var message = await checkForUpdateMessageAsync();
            if (!string.IsNullOrWhiteSpace(message))
            {
                showEasyConMessage(string.Empty, message);
            }
        }
        catch
        {
            ShowStatus("检查更新失败");
        }
    }

    private static async Task<string?> GetOriginalUpdateMessageAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var data = await client.GetStringAsync("https://gitee.com/api/v5/repos/EasyConNS/EasyCon/tags");
        var tags = JsonSerializer.Deserialize<UpdateTag[]>(data, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });

        var newVersion = tags?
            .Select(tag => TryParseTagVersion(tag.Name))
            .OfType<Version>()
            .OrderDescending()
            .FirstOrDefault();
        if (newVersion is null)
        {
            return null;
        }

        var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
        return newVersion > currentVersion
            ? $"发现新版本{newVersion}，快去群文件里看看吧"
            : "暂时没有发现新版本";
    }

    private static Version? TryParseTagVersion(string? tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return null;
        }

        var value = tagName.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        var versionText = new string(value.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
        return Version.TryParse(versionText, out var version)
            ? version
            : null;
    }

    private void ShowDeviceNotConnectedWarning()
    {
        showEasyConMessage(string.Empty, "请先连接设备");
    }

    private void UnpairDevice()
    {
        if (!isDeviceConnected())
        {
            return;
        }

        if (unpairDevice())
        {
            ShowStatus("取消配对成功");
        }
        else
        {
            ShowStatus("取消配对失败");
        }
    }

    private async Task AutoConnectDeviceAsync()
    {
        ShowStatus("尝试连接...");
        var (success, port) = await autoConnectDeviceAsync();

        if (success && !string.IsNullOrWhiteSpace(port))
        {
            FindRequiredControl<ComboBox>("comboComPort").Text = port;
            return;
        }

        var ports = getSerialPortNames();
        showEasyConMessage(
            string.Empty,
            "找不到设备！请确认：" + Environment.NewLine +
            "1.已经为单片机烧好固件" + Environment.NewLine +
            "2.已经连好TTL线" + Environment.NewLine +
            "3.以上两步操作正确的话，点击搜索时单片机上的TX灯会闪烁" + Environment.NewLine + Environment.NewLine +
            $"可用端口：{string.Join("、", ports)}");
    }

    private async Task ManualConnectDeviceAsync()
    {
        var port = FindRequiredControl<ComboBox>("comboComPort").Text;
        if (string.IsNullOrWhiteSpace(port))
        {
            showEasyConMessage(string.Empty, "请先选择或输入串口");
            return;
        }

        ShowStatus("尝试连接...");
        var success = await manualConnectDeviceAsync(port);
        if (!success)
        {
            showEasyConMessage(
                "连接失败",
                $"连接失败！端口 {port} 不存在、无法使用或已被占用。" + Environment.NewLine +
                "请在设备管理器确认 TTL 所在串口正确识别。关闭其他占用USB的程序，并重启软件再试。");
        }
    }

    private void ShowCaptureSourceRequiredWarning()
    {
        if (FindRequiredControl<ComboBox>("comboVideoSource").SelectedItem is null)
        {
            showEasyConMessage(string.Empty, "请先选择视频源");
        }
    }

    private void RefreshSerialPorts()
    {
        var combo = FindRequiredControl<ComboBox>("comboComPort");
        combo.Items.Clear();
        combo.Items.AddRange(getSerialPortNames().Cast<object>().ToArray());
    }

    private void RefreshVideoSources()
    {
        var combo = FindRequiredControl<ComboBox>("comboVideoSource");
        combo.Items.Clear();
        combo.Items.AddRange(getVideoSources().Select(source => new VideoSourceItem(source.Name, source.Index)).Cast<object>().ToArray());
    }

    private void InitializeOriginalStartupState()
    {
        var version = typeof(EasyConTabControl).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
        var plusIndex = version.IndexOf('+', StringComparison.Ordinal);
        if (plusIndex > 0)
        {
            version = version[..plusIndex];
        }

        FindRequiredControl<Label>("lblVersion").Text = $"版本: {version}";
        FindRequiredControl<CheckBox>("chkFolding").Checked = true;

        var log = FindRequiredControl<TextBox>("logTxtBox");
        log.Text = "正在初始化伊机控..." + Environment.NewLine +
            "准备就绪，欢迎使用伊机控！" + Environment.NewLine +
            "将脚本文件直接拖入窗口打开，然后点击运行开始执行脚本";
    }

    private void RunCurrentScript()
    {
        var editor = FindRequiredControl<TextBox>("easyConScriptEditor");
        var log = FindRequiredControl<TextBox>("logTxtBox");
        var result = EasyConScriptAdapter.Evaluate(editor.Text);

        log.AppendText("-- 开始运行 --" + Environment.NewLine);
        ShowStatus("运行中");

        if (result.HasErrors)
        {
            log.AppendText(string.Join(Environment.NewLine, result.Diagnostics) + Environment.NewLine);
            log.AppendText("-- 运行出错 --" + Environment.NewLine);
            ShowStatus("运行出错");
            return;
        }

        foreach (var line in result.Printed)
        {
            log.AppendText(line);
        }

        log.AppendText("-- 运行结束 --" + Environment.NewLine);
        ShowStatus("运行结束");
    }

    private void GenerateFirmware()
    {
        if (FindRequiredControl<ComboBox>("comboBoardType").SelectedItem is null)
        {
            showEasyConMessage(string.Empty, "请先选择板型");
            return;
        }

        var editor = FindRequiredControl<TextBox>("easyConScriptEditor");
        var result = EasyConScriptAdapter.Format(editor.Text);
        if (result.HasErrors)
        {
            showEasyConMessage("编译出错", string.Join(Environment.NewLine, result.Diagnostics));
            return;
        }

        var assembly = EasyConScriptAdapter.AssembleFirmwareScript(editor.Text);
        if (!assembly.Success)
        {
            showEasyConMessage(string.Empty, $"生成固件失败：{assembly.ErrorMessage}");
        }
    }

    private void NewCurrentScript()
    {
        if (CloseCurrentScript())
        {
            ShowStatus("新建完毕");
        }
    }

    private void OpenCurrentScript()
    {
        var path = chooseOpenScriptPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!CloseCurrentScript())
        {
            return;
        }

        currentScriptPath = path;
        FindRequiredControl<TextBox>("easyConScriptEditor").Text = File.ReadAllText(path);
        FindRequiredControl<Label>("scriptTitleLabel").Text = Path.GetFileName(path);
        currentScriptModified = false;
        ShowStatus("文件已打开");
    }

    private bool CloseCurrentScript()
    {
        if (!ConfirmCloseModifiedScript())
        {
            return false;
        }

        currentScriptPath = null;
        FindRequiredControl<TextBox>("easyConScriptEditor").Clear();
        FindRequiredControl<Label>("scriptTitleLabel").Text = "未命名脚本";
        currentScriptModified = false;
        ShowStatus("文件已关闭");
        return true;
    }

    private bool SaveCurrentScript(bool asNew = false)
    {
        if (asNew || currentScriptPath is null)
        {
            var path = chooseSaveScriptPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            currentScriptPath = path;
            FindRequiredControl<Label>("scriptTitleLabel").Text = Path.GetFileName(path);
        }

        File.WriteAllText(currentScriptPath, FindRequiredControl<TextBox>("easyConScriptEditor").Text);
        currentScriptModified = false;
        ShowStatus("文件已保存");
        return true;
    }

    private bool ConfirmCloseModifiedScript()
    {
        if (!currentScriptModified)
        {
            return true;
        }

        var result = confirmSaveModifiedScript();
        return result switch
        {
            DialogResult.Cancel => false,
            DialogResult.Yes => SaveCurrentScript(),
            _ => true,
        };
    }

    private string? ShowOpenScriptDialog()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "脚本文件 (*.txt，*.ecs)|*.txt;*.ecs|所有文件(*.*)|*.*",
        };

        return dialog.ShowDialog(FindForm()) == DialogResult.OK
            ? dialog.FileName
            : null;
    }

    private string? ShowSaveScriptDialog()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "脚本文件 (*.txt，*.ecs)|*.txt;*.ecs|所有文件(*.*)|*.*",
            FileName = "未命名脚本.txt",
        };

        return dialog.ShowDialog(FindForm()) == DialogResult.OK
            ? dialog.FileName
            : null;
    }

    private DialogResult ShowSaveModifiedDialog()
    {
        return MessageBox.Show("文件已编辑，是否保存？", string.Empty, MessageBoxButtons.YesNoCancel);
    }

    private static void ShowEasyConMessageBox(string title, string message)
    {
        MessageBox.Show(message, title);
    }

    private static void OpenExternalLink(string url)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    }

    private static string[] GetSerialPortNames()
    {
        return [.. EasyDevice.ECDevice.GetPortNames()];
    }

    private static IReadOnlyList<(string Name, int Index)> GetVideoSources()
    {
        return [];
    }

    private void FormatCurrentScript()
    {
        var editor = FindRequiredControl<TextBox>("easyConScriptEditor");
        var log = FindRequiredControl<TextBox>("logTxtBox");
        var result = EasyConScriptAdapter.Format(editor.Text);

        if (result.HasErrors)
        {
            log.AppendText(string.Join(Environment.NewLine, result.Diagnostics));
            ShowStatus("格式化失败");
            return;
        }

        editor.Text = result.FormattedCode ?? string.Empty;
    }

    private void ShowStatus(string message)
    {
        var status = FindRequiredControl<StatusStrip>("easyConStatusStrip");
        var item = status.Items.OfType<ToolStripStatusLabel>().First(candidate => candidate.Name == "toolStripStatusLabel1");
        item.Text = message;
    }

    private void ToggleCurrentComment()
    {
        var editor = FindRequiredControl<TextBox>("easyConScriptEditor");
        if (string.IsNullOrEmpty(editor.Text))
        {
            return;
        }

        var selectionStart = Math.Clamp(editor.SelectionStart, 0, editor.Text.Length);
        var selectionEnd = Math.Clamp(editor.SelectionStart + editor.SelectionLength, 0, editor.Text.Length);
        var lineStart = FindLineStart(editor.Text, selectionStart);
        var lineEnd = FindLineContentEnd(editor.Text, selectionEnd);
        var selectedLines = editor.Text[lineStart..lineEnd];
        var toggledLines = EasyConScriptAdapter.ToggleCommentLines(selectedLines);

        editor.Text = editor.Text[..lineStart] + toggledLines + editor.Text[lineEnd..];
        editor.SelectionStart = lineStart;
        editor.SelectionLength = toggledLines.Length;
    }

    private static int FindLineStart(string text, int index)
    {
        if (index <= 0)
        {
            return 0;
        }

        var previousLineFeed = text.LastIndexOf('\n', index - 1);
        return previousLineFeed < 0 ? 0 : previousLineFeed + 1;
    }

    private static int FindLineContentEnd(string text, int index)
    {
        var lineFeed = text.IndexOf('\n', index);
        if (lineFeed < 0)
        {
            return text.Length;
        }

        return lineFeed > 0 && text[lineFeed - 1] == '\r'
            ? lineFeed - 1
            : lineFeed;
    }

    private ToolStripMenuItem FindRequiredMenuItem(string name)
    {
        return FindMenuItem(
            FindRequiredControl<MenuStrip>("easyConOriginalMenu").Items,
            name) ?? throw new InvalidOperationException($"Menu item '{name}' was not found.");
    }

    private static ToolStripMenuItem? FindMenuItem(ToolStripItemCollection items, string name)
    {
        foreach (ToolStripItem item in items)
        {
            if (item is ToolStripMenuItem menuItem)
            {
                if (menuItem.Name == name)
                {
                    return menuItem;
                }

                var child = FindMenuItem(menuItem.DropDownItems, name);
                if (child is not null)
                {
                    return child;
                }
            }
        }

        return null;
    }

    private static Control CreateOriginalMenu()
    {
        var menu = new MenuStrip
        {
            Name = "easyConOriginalMenu",
            Dock = DockStyle.Fill,
            Font = new Font("微软雅黑", 9F),
            ImageScalingSize = new Size(20, 20),
        };

        menu.Items.Add(CreateMenuItem("fileMenu", "文件",
            CreateMenuItem("menuItemNew", "新建", Keys.Control | Keys.N),
            CreateMenuItem("menuItemOpen", "打开", Keys.Control | Keys.O),
            CreateMenuItem("menuItemSave", "保存", Keys.Control | Keys.S),
            CreateMenuItem("menuItemSaveAs", "另存为", Keys.Control | Keys.Shift | Keys.S),
            CreateMenuItem("menuItemClose", "关闭", Keys.Control | Keys.W),
            new ToolStripSeparator { Name = "toolStripSeparator1" },
            CreateMenuItem("menuItemExit", "退出", Keys.Control | Keys.Q)));
        menu.Items.Add(CreateMenuItem("editMenu", "编辑",
            CreateMenuItem("menuItemFindReplace", "查找替换", Keys.Control | Keys.F),
            CreateMenuItem("menuItemFindNext", "查找下一个", Keys.F3),
            CreateMenuItem("menuItemToggleComment", "注释/取消注释", Keys.Control | Keys.Oem2)));
        var scriptMenu = CreateMenuItem("scriptMenu", "脚本");
        scriptMenu.DropDownItems.Add(CreateMenuItem("formatMenuItem", "格式化", Keys.Control | Keys.R));
        scriptMenu.DropDownItems.Add(CreateMenuItem("runMenuItem", "运行", Keys.F5));
        scriptMenu.Visible = false;
        menu.Items.Add(scriptMenu);
        menu.Items.Add(CreateMenuItem("captureMenu", "搜图",
            CreateMenuItem("captureTypeMenu", "采集卡类型"),
            CreateMenuItem("setEnvVarMenuItem", "设置环境变量"),
            CreateMenuItem("captureHelpMenuItem", "搜图说明")));
        menu.Items.Add(CreateMenuItem("helpMenu", "帮助",
            CreateMenuItem("menuItemFirmwareMode", "固件模式"),
            CreateMenuItem("menuItemOnlineMode", "联机模式"),
            CreateMenuItem("menuItemFlashMode", "烧录模式"),
            CreateMenuItem("menuItemScriptSyntax", "脚本语法"),
            new ToolStripSeparator { Name = "toolStripSeparator2" },
            CreateMenuItem("menuItemAbout", "关于")));

        return menu;
    }

    private static ToolStripMenuItem CreateMenuItem(string name, string text, params ToolStripItem[] dropDownItems)
    {
        var item = new ToolStripMenuItem
        {
            Name = name,
            Text = text,
        };
        item.DropDownItems.AddRange(dropDownItems);
        return item;
    }

    private static ToolStripMenuItem CreateMenuItem(string name, string text, Keys shortcutKeys)
    {
        return new ToolStripMenuItem
        {
            Name = name,
            Text = text,
            ShortcutKeys = shortcutKeys,
        };
    }

    private sealed record VideoSourceItem(string Name, int Index)
    {
        public override string ToString()
        {
            return Name;
        }
    }

    private sealed record UpdateTag(string Name);

    private static Control CreateMainArea()
    {
        var mainSplit = new SplitContainer
        {
            Name = "mainSplit",
            Width = 916,
            Height = 681,
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(230, 229, 224),
            SplitterWidth = 6,
            Panel2MinSize = 240,
        };
        mainSplit.Panel1.Controls.Add(CreateContentPanel());
        mainSplit.Panel1.Controls.Add(CreatePageSidebar());
        mainSplit.Panel2.Controls.Add(CreateRightPanel());
        return mainSplit;
    }

    private static Control CreatePageSidebar()
    {
        var sideBar = new Panel
        {
            Name = "sideBar",
            Dock = DockStyle.Left,
            Width = 40,
            BackColor = Color.FromArgb(230, 229, 224),
        };
        sideBar.Controls.Add(CreatePageButton("btnPageLog", "📄", Color.FromArgb(235, 234, 229), 10));
        sideBar.Controls.Add(CreatePageButton("btnPageEditor", "📝", Color.FromArgb(230, 229, 224), 50));
        sideBar.Controls.Add(CreatePageButton("btnPageBurn", "🔥", Color.FromArgb(230, 229, 224), 90));
        sideBar.Controls.Add(CreatePageButton("btnPageSettings", "⚙", Color.FromArgb(230, 229, 224), 130));
        return sideBar;
    }

    private static Button CreatePageButton(string name, string text, Color backColor, int top)
    {
        var button = new Button
        {
            Name = name,
            Text = text,
            Width = 36,
            Height = 36,
            Left = 2,
            Top = top,
            BackColor = backColor,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Emoji", 14F),
            ForeColor = Color.FromArgb(38, 37, 30),
            UseVisualStyleBackColor = false,
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private static Control CreateContentPanel()
    {
        var contentPanel = new Panel
        {
            Name = "contentPanel",
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(242, 241, 237),
        };

        contentPanel.Controls.Add(CreateEditorHost());
        contentPanel.Controls.Add(CreateLogPanel());
        contentPanel.Controls.Add(CreateBurnPanel());
        contentPanel.Controls.Add(CreateSettingsPanel());
        contentPanel.Controls.Add(new Label
        {
            Name = "scriptTitleLabel",
            Text = "未命名脚本",
            Dock = DockStyle.Top,
            BackColor = Color.FromArgb(230, 229, 224),
            Font = new Font("微软雅黑", 9F),
            ForeColor = Color.FromArgb(38, 37, 30),
            Height = 24,
            Padding = new Padding(4, 0, 0, 0),
            TextAlign = ContentAlignment.MiddleLeft,
            Visible = false,
        });
        return contentPanel;
    }

    private static Control CreateEditorHost()
    {
        var editorHost = new Panel
        {
            Name = "editorHost",
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9F),
            Visible = false,
        };
        var editor = new TextBox
        {
            Name = "easyConScriptEditor",
            Dock = DockStyle.Fill,
            AcceptsReturn = true,
            AcceptsTab = true,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.FromArgb(212, 212, 212),
            Font = new Font(FontFamily.GenericMonospace, 10),
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            Text = "PRINT \"hello\"" + Environment.NewLine +
                "WAIT 1000" + Environment.NewLine +
                "PRESS A 30" + Environment.NewLine +
                "WAIT 200" + Environment.NewLine +
                "PRESS B 30",
            WordWrap = false,
        };
        editorHost.Controls.Add(editor);
        return editorHost;
    }

    private static Control CreateLogPanel()
    {
        var logPanel = new Panel
        {
            Name = "logPanel",
            Dock = DockStyle.Fill,
            BackColor = SystemColors.Control,
            ForeColor = Color.White,
        };

        var log = new TextBox
        {
            Name = "logTxtBox",
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = Color.FromArgb(64, 64, 64),
            ForeColor = Color.White,
            Font = new Font(FontFamily.GenericMonospace, 9),
            Location = new Point(6, 10),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Size = new Size(590, 644),
            Text = "[EasyCon] 等待设备连接..." + Environment.NewLine +
                "[EasyCon] 脚本编辑器已就绪",
            WordWrap = false,
        };

        var clearLog = new Button
        {
            Name = "clsLogBtn",
            AccessibleName = "清除日志输出",
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            BackColor = Color.Transparent,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(564, 5),
            Size = new Size(30, 30),
            UseVisualStyleBackColor = false,
        };
        clearLog.FlatAppearance.BorderSize = 0;
        clearLog.Click += (_, _) => log.Clear();

        logPanel.Controls.Add(clearLog);
        logPanel.Controls.Add(log);
        return logPanel;
    }

    private static Control CreateBurnPanel()
    {
        var burnPanel = new Panel
        {
            Name = "burnPanel",
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(242, 241, 237),
            Visible = false,
        };
        burnPanel.Controls.Add(CreateBurnGroup());
        burnPanel.Controls.Add(CreateFirmwareGroup());
        return burnPanel;
    }

    private static Control CreateBurnGroup()
    {
        var group = CreateOriginalGroupBox("grpBurn", "烧录", 300, 130);
        group.Location = new Point(20, 20);
        group.Margin = Padding.Empty;
        group.Controls.Add(CreateOriginalButton("btnRemoteStart", "远程运行", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 8, 22, 130, 28));
        group.Controls.Add(CreateOriginalButton("btnRemoteStop", "远程停止", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 150, 22, 130, 28));
        group.Controls.Add(CreateOriginalButton("btnFlash", "编译烧录", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 8, 54, 272, 30));
        group.Controls.Add(CreateOriginalButton("btnFlashClear", "清除烧录", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 8, 90, 130, 28));
        return group;
    }

    private static Control CreateFirmwareGroup()
    {
        var group = CreateOriginalGroupBox("grpFirmware", "固件", 300, 90);
        group.Location = new Point(20, 160);
        group.Margin = Padding.Empty;
        group.Controls.Add(new ComboBox
        {
            Name = "comboBoardType",
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(8, 22),
            Size = new Size(272, 28),
        });
        group.Controls.Add(CreateOriginalButton("btnGenFirmware", "生成固件", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 8, 54, 130, 28));
        return group;
    }

    private static Control CreateSettingsPanel()
    {
        var settingsPanel = new Panel
        {
            Name = "settingsPanel",
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(242, 241, 237),
            Visible = false,
        };
        settingsPanel.Controls.Add(CreateSettingsHeader("lblEditorSettings", "编辑器设置", 19, 39));
        settingsPanel.Controls.Add(CreateSettingsCheckBox("chkAutoCompletion", "代码自动补全", 19, 79));
        settingsPanel.Controls.Add(CreateSettingsCheckBox("chkFolding", "显示代码折叠", 19, 109));
        settingsPanel.Controls.Add(CreateSettingsCheckBox("chkDebugLog", "显示调试信息", 19, 139));
        settingsPanel.Controls.Add(CreateSettingsHeader("lblRunSettings", "运行设置", 199, 39));
        settingsPanel.Controls.Add(CreateSettingsCheckBox("chkAutoRunAfterFlash", "烧录后自动运行", 199, 79));
        settingsPanel.Controls.Add(CreateSettingsHeader("lblNotifySettings", "通知设置", 199, 119));
        settingsPanel.Controls.Add(CreateOriginalButton("btnAlertConfig", "推送配置", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 199, 159, 85, 30));
        settingsPanel.Controls.Add(CreateSettingsCheckBox("chkAutoSaveLog", "自动保存日志", 289, 164));
        settingsPanel.Controls.Add(CreateSettingsHeader("lblToolSettings", "工具", 19, 209));
        settingsPanel.Controls.Add(CreateOriginalButton("btnESPConfig", "ESP32设置", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 19, 249, 100, 30));
        settingsPanel.Controls.Add(CreateOriginalButton("btnUnpair", "取消蓝牙配对", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 125, 249, 100, 30));
        settingsPanel.Controls.Add(CreateOriginalButton("btnDrawingBoard", "画图工具", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 19, 289, 85, 30));
        var bluetoothSetting = CreateOriginalButton("btnBluetoothSetting", "蓝牙设置", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 199, 308, 85, 30);
        bluetoothSetting.Visible = false;
        settingsPanel.Controls.Add(bluetoothSetting);
        settingsPanel.Controls.Add(CreateSettingsHeader("lblAbout", "关于", 19, 330));
        settingsPanel.Controls.Add(new Label
        {
            Name = "lblVersion",
            Text = "版本: --",
            AutoSize = true,
            Font = new Font("微软雅黑", 9F),
            ForeColor = Color.FromArgb(140, 139, 132),
            Location = new Point(19, 370),
        });
        settingsPanel.Controls.Add(CreateOriginalButton("btnCheckUpdate", "检查更新", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 19, 400, 85, 30));
        settingsPanel.Controls.Add(CreateOriginalButton("btnSource", "项目源码", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 109, 400, 85, 30));
        return settingsPanel;
    }

    private static Label CreateSettingsHeader(string name, string text, int left, int top)
    {
        return new Label
        {
            Name = name,
            Text = text,
            AutoSize = true,
            Font = new Font("微软雅黑", 11F, FontStyle.Bold),
            ForeColor = Color.FromArgb(38, 37, 30),
            Location = new Point(left, top),
        };
    }

    private static CheckBox CreateSettingsCheckBox(string name, string text, int left, int top)
    {
        return new CheckBox
        {
            Name = name,
            Text = text,
            AutoSize = true,
            Font = new Font("微软雅黑", 9F),
            ForeColor = Color.FromArgb(38, 37, 30),
            Location = new Point(left, top),
        };
    }

    private static Control CreateRightPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.FromArgb(245, 245, 245),
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(8),
            WrapContents = false,
        };

        panel.Controls.Add(CreateRunPanel());
        panel.Controls.Add(CreateSerialPanel());
        panel.Controls.Add(CreateCapturePanel());
        panel.Controls.Add(CreateRecordPanel());
        panel.Controls.Add(CreateControllerPanel());
        panel.Controls.Add(CreateFirmwarePanel());
        return panel;
    }

    private static Control CreateRunPanel()
    {
        var group = CreateOriginalGroupBox("grpScriptRun", "脚本运行", 265, 162);
        group.Controls.Add(CreateOriginalButton("runStopBtn", "运行脚本", Color.FromArgb(31, 138, 101), Color.White, 8, 22, 206, 55));
        group.Controls.Add(CreateOriginalButton("formatBtn", "格式化", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 8, 85, 206, 30));
        group.Controls.Add(new Label
        {
            Name = "timerLabel",
            Text = "00:00:00",
            Font = new Font("Consolas", 12F),
            ForeColor = Color.FromArgb(38, 37, 30),
            Location = new Point(8, 123),
            Size = new Size(206, 30),
            TextAlign = ContentAlignment.MiddleCenter,
        });
        return group;
    }

    private static Control CreateSerialPanel()
    {
        var group = CreateOriginalGroupBox("grpDevice", "设备连接", 265, 130);
        group.Controls.Add(new Panel
        {
            Name = "easyConSerialPanel",
            Width = 1,
            Height = 1,
            Visible = false,
        });
        group.Controls.Add(new ComboBox
        {
            Name = "comboComPort",
            Location = new Point(8, 22),
            Size = new Size(206, 28),
        });
        group.Controls.Add(CreateOriginalButton("btnAutoConnect", "自动连接", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 8, 54, 206, 30));
        group.Controls.Add(CreateOriginalButton("btnManualConnect", "手动连接", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 8, 90, 206, 30));
        return group;
    }

    private static Control CreateCapturePanel()
    {
        var group = CreateOriginalGroupBox("grpVideoSource", "视频源", 265, 150);
        group.Controls.Add(new Panel
        {
            Name = "easyConCapturePanel",
            Width = 1,
            Height = 1,
            Visible = false,
        });
        group.Controls.Add(new ComboBox
        {
            Name = "comboVideoSource",
            Location = new Point(8, 22),
            Size = new Size(206, 28),
        });
        group.Controls.Add(CreateOriginalButton("btnCaptureToggle", "连接视频源", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 8, 54, 206, 30));
        group.Controls.Add(CreateOriginalButton("btnOpenCaptureConsole", "搜图控制台", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 8, 90, 206, 30));
        return group;
    }

    private static Control CreateRecordPanel()
    {
        var group = CreateOriginalGroupBox("grpRecord", "录制", 265, 90);
        group.Controls.Add(new Panel
        {
            Name = "easyConRecordPanel",
            Width = 1,
            Height = 1,
            Visible = false,
        });
        group.Controls.Add(CreateOriginalButton("btnRecord", "录制脚本", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 8, 22, 206, 28));
        var pauseButton = CreateOriginalButton("btnRecordPause", "暂停录制", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 8, 54, 206, 28);
        pauseButton.Enabled = false;
        group.Controls.Add(pauseButton);
        return group;
    }

    private static Control CreateControllerPanel()
    {
        var group = CreateOriginalGroupBox("grpController", "手柄", 265, 90);
        group.Controls.Add(new Panel
        {
            Name = "easyConControllerPanel",
            Width = 1,
            Height = 1,
            Visible = false,
        });
        group.Controls.Add(CreateOriginalButton("btnShowController", "虚拟手柄", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 8, 22, 206, 28));
        group.Controls.Add(CreateOriginalButton("btnKeyMapping", "按键映射", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 8, 54, 206, 28));
        return group;
    }

    private static Control CreateFirmwarePanel()
    {
        var panel = CreateGroupPanel("固件", 265, 112);
        panel.Name = "easyConFirmwarePanel";
        AddTextRow(panel, "开发板:", "Leonardo");
        AddButtonGrid(panel, "生成固件", "烧录");
        return panel;
    }

    private static Control CreateStatusStrip()
    {
        var status = new StatusStrip
        {
            Name = "easyConStatusStrip",
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(230, 229, 224),
            ImageScalingSize = new Size(20, 20),
        };
        status.Items.Add(new ToolStripStatusLabel
        {
            Name = "toolStripStatusLabel1",
            AutoSize = false,
            ForeColor = Color.FromArgb(38, 37, 30),
            Size = new Size(300, 20),
        });
        status.Items.Add(CreateStatusLabel("toolStripStatusLabel2", " | "));
        status.Items.Add(CreateStatusLabel("labelSerialStatus", "单片机未连接"));
        status.Items.Add(CreateStatusLabel("toolStripStatusLabel3", " | "));
        status.Items.Add(CreateStatusLabel("labelCaptureStatus", "采集卡未连接"));
        return status;
    }

    private static ToolStripStatusLabel CreateStatusLabel(string name, string text)
    {
        return new ToolStripStatusLabel
        {
            Name = name,
            Text = text,
            ForeColor = Color.FromArgb(140, 139, 132),
        };
    }

    private static FlowLayoutPanel CreateGroupPanel(string title, int width, int height)
    {
        var panel = new FlowLayoutPanel
        {
            Width = width,
            Height = height,
            BorderStyle = BorderStyle.FixedSingle,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(8),
            WrapContents = false,
        };
        panel.Controls.Add(new Label
        {
            Text = title,
            AutoSize = false,
            Width = width - 20,
            Height = 24,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
        });
        return panel;
    }

    private static GroupBox CreateOriginalGroupBox(string name, string title, int width, int height)
    {
        return new GroupBox
        {
            Name = name,
            Text = title,
            Width = width,
            Height = height,
            ForeColor = Color.FromArgb(38, 37, 30),
            Margin = new Padding(10, 3, 10, 0),
        };
    }

    private static Button CreateOriginalButton(
        string name,
        string text,
        Color backColor,
        Color foreColor,
        int left,
        int top,
        int width,
        int height)
    {
        var button = new Button
        {
            Name = name,
            Text = text,
            BackColor = backColor,
            ForeColor = foreColor,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("微软雅黑", 9F),
            Location = new Point(left, top),
            Size = new Size(width, height),
            UseVisualStyleBackColor = false,
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private static void AddTextRow(FlowLayoutPanel parent, string label, string value)
    {
        var row = new TableLayoutPanel
        {
            Width = parent.Width - 20,
            Height = 30,
            ColumnCount = 2,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.Controls.Add(CreateLabel(label, 70), 0, 0);
        row.Controls.Add(new TextBox { Text = value, Dock = DockStyle.Fill }, 1, 0);
        parent.Controls.Add(row);
    }

    private static void AddButtonGrid(FlowLayoutPanel parent, string first, string second)
    {
        var row = new TableLayoutPanel
        {
            Width = parent.Width - 20,
            Height = 32,
            ColumnCount = 2,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        row.Controls.Add(CreateButton(first), 0, 0);
        row.Controls.Add(CreateButton(second), 1, 0);
        parent.Controls.Add(row);
    }

    private static Label CreateLabel(string text, int width)
    {
        return new Label
        {
            Text = text,
            AutoSize = false,
            Width = width,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft,
        };
    }

    private static Button CreateButton(string text)
    {
        return new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.System,
        };
    }
}
