using AutoSwshRng.Upstream;
using EasyCon.Core;
using EasyCon.Core.Config;
using EasyCon.WinInput;
using EasyCon2.Avalonia.Core;
using EasyCon2.Avalonia.Core.VPad;
using EasyCon2.Forms;
using EasyCon2.Services;
using EasyCon2.Theme;
using EasyCon2.Views;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace AutoSwshRng.App.Controls;

public sealed class EasyConTabControl : UserControl, IControllerAdapter
{
    private readonly ConfigService originalConfigService = new();
    private readonly DeviceService originalDeviceService = new();
    private readonly CaptureService originalCaptureService = new();
    private VPadService? originalVPadService;
    private string? currentScriptPath;
    private bool currentScriptModified;
    private string selectedCaptureType = "ANY";
    private bool captureSourceConnected;
    private bool virtualControllerBound;
    private EasyConRecordState recordState = EasyConRecordState.Stopped;
    private Func<string?> chooseOpenScriptPath = null!;
    private Func<string?> chooseSaveScriptPath = null!;
    private Func<DialogResult> confirmSaveModifiedScript = null!;
    private Action<string, string> showEasyConMessage = null!;
    private Action<string> dispatchAlert = null!;
    private Action<string> openExternalLink = null!;
    private Action openFindReplacePanel = null!;
    private Func<string[]> getSerialPortNames = null!;
    private Func<IReadOnlyList<(string Name, int Index)>> getVideoSources = null!;
    private Func<int, bool> connectCaptureSource = null!;
    private Action disconnectCaptureSource = null!;
    private Action openCaptureConsole = null!;
    private Func<IReadOnlyDictionary<string, Func<int>>> buildCaptureExternalGetters = null!;
    private Action openAlertConfigDialog = null!;
    private Action openDrawingBoard = null!;
    private Action openMouseJoystickDialog = null!;
    private Action openBluetoothSettingDialog = null!;
    private Action openKeyMappingDialog = null!;
    private Func<bool> openVirtualController = null!;
    private Action deactivateVirtualController = null!;
    private Func<Task<string?>> checkForUpdateMessageAsync = null!;
    private Func<Task<(bool Success, string? Port)>> autoConnectDeviceAsync = null!;
    private Func<string, Task<bool>> manualConnectDeviceAsync = null!;
    private Func<bool> isDeviceConnected = null!;
    private Func<bool> unpairDevice = null!;
    private Action<bool> setDebugLogEnabled = null!;
    private Action<bool> setAutoSaveLogEnabled = null!;
    private Action<bool> setDarkModeEnabled = null!;
    private Action startRecordDevice = null!;
    private Action pauseRecordDevice = null!;
    private Action resumeRecordDevice = null!;
    private Action stopRecordDevice = null!;
    private Func<string> getRecordedScript = null!;
    private Func<string, IReadOnlyDictionary<string, Func<int>>, Task<EasyConScriptResult>> executeKeyScriptAsync = null!;

    Avalonia.Media.Color IControllerAdapter.CurrentLight => Avalonia.Media.Colors.White;

    bool IControllerAdapter.IsRunning() => false;

    public EasyConTabControl()
    {
        SuspendLayout();
        Dock = DockStyle.Fill;
        Font = new Font("Microsoft YaHei UI", 9F);
        chooseOpenScriptPath = ShowOpenScriptDialog;
        chooseSaveScriptPath = ShowSaveScriptDialog;
        confirmSaveModifiedScript = ShowSaveModifiedDialog;
        showEasyConMessage = ShowEasyConMessageBox;
        dispatchAlert = DispatchOriginalAlert;
        openExternalLink = OpenExternalLink;
        openFindReplacePanel = ShowFindReplacePanel;
        getSerialPortNames = originalDeviceService.GetPortNames;
        getVideoSources = GetOriginalVideoSources;
        connectCaptureSource = ConnectOriginalCaptureSource;
        disconnectCaptureSource = originalCaptureService.Disconnect;
        openCaptureConsole = originalCaptureService.ShowCaptureConsole;
        buildCaptureExternalGetters = () => originalCaptureService.BuildExternalGetters();
        openAlertConfigDialog = ShowAlertConfigDialog;
        openDrawingBoard = OpenOriginalDrawingBoard;
        openMouseJoystickDialog = OpenOriginalMouseJoystickDialog;
        openBluetoothSettingDialog = OpenOriginalBluetoothSettingDialog;
        openKeyMappingDialog = OpenOriginalKeyMappingDialog;
        openVirtualController = OpenOriginalVirtualController;
        deactivateVirtualController = () => originalVPadService?.Deactivate();
        checkForUpdateMessageAsync = GetOriginalUpdateMessageAsync;
        autoConnectDeviceAsync = originalDeviceService.AutoConnectAsync;
        manualConnectDeviceAsync = originalDeviceService.ManualConnectAsync;
        isDeviceConnected = () => originalDeviceService.IsConnected;
        unpairDevice = originalDeviceService.UnPair;
        setDebugLogEnabled = enabled => originalDeviceService.DebugLogEnabled = enabled;
        setAutoSaveLogEnabled = enabled =>
        {
            originalConfigService.Config.AutoSaveLog = enabled;
            originalConfigService.Save();
        };
        setDarkModeEnabled = enabled =>
        {
            originalConfigService.Config.DarkMode = enabled;
            originalConfigService.Save();
            ThemeManager.Toggle(enabled);
        };
        startRecordDevice = originalDeviceService.StartRecord;
        pauseRecordDevice = originalDeviceService.PauseRecord;
        resumeRecordDevice = () => originalDeviceService.Device.recordState = EasyDevice.RecordState.RECORD_START;
        stopRecordDevice = originalDeviceService.StopRecord;
        getRecordedScript = originalDeviceService.GetRecordScript;
        executeKeyScriptAsync = (script, externalGetters) => EasyConScriptAdapter.ExecuteAsync(
            script,
            new GamePadAdapter(originalDeviceService.Device),
            externalGetters);
        originalDeviceService.ConnectionStateChanged += connected => PostToUi(() => UpdateDeviceStatus(connected));
        originalDeviceService.StatusChanged += message => PostToUi(() => ShowStatus(message));
        originalDeviceService.Log += message => PostToUi(() => AppendLogLine(message));
        originalCaptureService.ConnectionStateChanged += connected => PostToUi(() => UpdateCaptureStatus(connected));
        originalCaptureService.StatusChanged += message => PostToUi(() => ShowStatus(message));
        originalConfigService.Load();
        ThemeManager.Init(originalConfigService.Config.DarkMode);
        selectedCaptureType = string.IsNullOrWhiteSpace(originalConfigService.Config.CaptureType)
            ? "ANY"
            : originalConfigService.Config.CaptureType;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
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
        InitializeOriginalStartupState();
        ApplyTheme();
        ThemeManager.ThemeChanged += ThemeChanged;
        ResumeLayout(performLayout: true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ThemeManager.ThemeChanged -= ThemeChanged;
        }
        base.Dispose(disposing);
    }

    public bool CloseForParentForm()
    {
        if (!CloseCurrentScript())
        {
            return false;
        }
        if (captureSourceConnected)
        {
            disconnectCaptureSource();
            captureSourceConnected = false;
        }
        if (recordState != EasyConRecordState.Stopped)
        {
            stopRecordDevice();
            recordState = EasyConRecordState.Stopped;
        }
        if (virtualControllerBound)
        {
            deactivateVirtualController();
            virtualControllerBound = false;
        }
        if (originalDeviceService.IsConnected)
        {
            originalDeviceService.Disconnect();
        }
        return true;
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
        FindRequiredControl<TextBox>("easyConScriptEditor").TextChanged += (_, _) =>
        {
            currentScriptModified = true;
            var title = currentScriptPath is null ? "未命名脚本" : Path.GetFileName(currentScriptPath);
            FindRequiredControl<Label>("scriptTitleLabel").Text = $"{title}(已编辑)";
        };
    }

    private void WireEditActions()
    {
        FindRequiredMenuItem("menuItemFindReplace").Click += (_, _) => openFindReplacePanel();
        FindRequiredMenuItem("menuItemFindNext").Click += (_, _) => FindNextMatch();
        FindRequiredControl<Button>("btnFindNext").Click += (_, _) => FindNextMatch();
        FindRequiredControl<Button>("btnReplaceNext").Click += (_, _) => ReplaceCurrentMatch();
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
        FindRequiredMenuItem("menuItemOnlineMode").Click += (_, _) => ShowOnlineModeHelp();
        FindRequiredMenuItem("menuItemAbout").Click += (_, _) => ShowAboutMessage();
    }

    private void WireDeviceGuardActions()
    {
        FindRequiredControl<Button>("btnAutoConnect").Click += (_, _) => _ = AutoConnectDeviceAsync();
        FindRequiredControl<Button>("btnManualConnect").Click += (_, _) => _ = ManualConnectDeviceAsync();
        FindRequiredControl<Button>("btnCaptureToggle").Click += (_, _) => ToggleCaptureSource();
        FindRequiredControl<Button>("btnRecord").Click += (_, _) => ToggleScriptRecording();
        FindRequiredControl<Button>("btnRecordPause").Click += (_, _) => ToggleScriptRecordingPause();
        FindRequiredControl<Button>("btnShowController").Click += (_, _) => ShowVirtualController();
    }

    private void WireSettingsActions()
    {
        FindRequiredControl<CheckBox>("chkDebugLog").CheckedChanged += (_, _) =>
            UpdateLinkedSetting("chkDebugLog", "debugLogMenuItem", setDebugLogEnabled);
        FindRequiredControl<CheckBox>("chkAutoSaveLog").CheckedChanged += (_, _) =>
            setAutoSaveLogEnabled(FindRequiredControl<CheckBox>("chkAutoSaveLog").Checked);
        FindRequiredControl<Button>("btnAlertConfig").Click += (_, _) => openAlertConfigDialog();
        FindRequiredControl<Button>("btnDrawingBoard").Click += (_, _) => openDrawingBoard();
        FindRequiredControl<Button>("btnKeyMapping").Click += (_, _) => openKeyMappingDialog();
        FindRequiredControl<Button>("btnUnpair").Click += (_, _) => UnpairDevice();
        FindRequiredControl<Button>("btnCheckUpdate").Click += (_, _) => _ = CheckForUpdatesAsync();
        FindRequiredControl<Button>("btnSource").Click += (_, _) => openExternalLink("https://github.com/EasyConNS/EasyCon");
        FindRequiredMenuItem("alertConfigMenuItem").Click += (_, _) => openAlertConfigDialog();
        FindRequiredMenuItem("debugLogMenuItem").Click += (_, _) => ToggleLinkedCheckBox("chkDebugLog");
        FindRequiredMenuItem("darkModeMenuItem").Click += (_, _) => ToggleDarkModeMenuItem();
        FindRequiredMenuItem("bluetoothSettingMenuItem").Click += (_, _) => openBluetoothSettingDialog();
        FindRequiredMenuItem("unpairMenuItem").Click += (_, _) => UnpairDevice();
        FindRequiredMenuItem("drawingBoardMenuItem").Click += (_, _) => openDrawingBoard();
        FindRequiredMenuItem("mouseJoystickMenuItem").Click += (_, _) => openMouseJoystickDialog();
        FindRequiredMenuItem("checkUpdateMenuItem").Click += (_, _) => _ = CheckForUpdatesAsync();
        FindRequiredMenuItem("sourceMenuItem").Click += (_, _) => openExternalLink("https://github.com/EasyConNS/EasyCon");
    }

    private void UpdateLinkedSetting(string checkBoxName, string menuItemName, Action<bool> update)
    {
        var enabled = FindRequiredControl<CheckBox>(checkBoxName).Checked;
        update(enabled);
        FindRequiredMenuItem(menuItemName).Checked = enabled;
    }

    private void ToggleLinkedCheckBox(string checkBoxName)
    {
        var checkBox = FindRequiredControl<CheckBox>(checkBoxName);
        checkBox.Checked = !checkBox.Checked;
    }

    private void ToggleDarkModeMenuItem()
    {
        var menuItem = FindRequiredMenuItem("darkModeMenuItem");
        menuItem.Checked = !menuItem.Checked;
        setDarkModeEnabled(menuItem.Checked);
    }

    private void ThemeChanged(bool _) => PostToUi(ApplyTheme);

    private void ApplyTheme()
    {
        SuspendLayout();
        BackColor = ThemeManager.PageBackground;
        ForeColor = ThemeManager.TextPrimary;
        ApplyControlTheme(this);
        ApplyPageButtonTheme("btnPageLog", "logPanel");
        ApplyPageButtonTheme("btnPageEditor", "editorHost");
        ApplyPageButtonTheme("btnPageSettings", "settingsPanel");

        var menu = FindRequiredControl<MenuStrip>("easyConOriginalMenu");
        var status = FindRequiredControl<StatusStrip>("easyConStatusStrip");
        menu.BackColor = ThemeManager.MenuBackground;
        status.BackColor = ThemeManager.StatusStripBackground;
        var renderer = new ToolStripProfessionalRenderer(
            ThemeManager.IsDark ? new DarkMenuColors() : new ProfessionalColorTable())
        {
            RoundedEdges = false,
        };
        menu.Renderer = renderer;
        status.Renderer = renderer;
        SetToolStripItemColors(menu.Items);
        SetToolStripItemColors(status.Items);
        ResumeLayout();
    }

    private void ApplyPageButtonTheme(string buttonName, string pageName)
    {
        FindRequiredControl<Button>(buttonName).BackColor = FindRequiredControl<Control>(pageName).Visible
            ? ThemeManager.ButtonBackground
            : ThemeManager.SurfaceBackground;
    }

    private static void ApplyControlTheme(Control root)
    {
        foreach (Control control in root.Controls)
        {
            switch (control)
            {
                case MenuStrip or StatusStrip:
                    break;
                case TextBox textBox when textBox.Name is "logTxtBox" or "easyConScriptEditor":
                    textBox.BackColor = ThemeManager.DarkSurfaceBackground;
                    textBox.ForeColor = ThemeManager.DarkSurfaceText;
                    break;
                case TextBox textBox:
                    textBox.BackColor = ThemeManager.IsDark ? ThemeManager.SurfaceBackground : SystemColors.Window;
                    textBox.ForeColor = ThemeManager.IsDark ? ThemeManager.TextPrimary : SystemColors.WindowText;
                    break;
                case ComboBox comboBox:
                    comboBox.BackColor = ThemeManager.IsDark ? ThemeManager.SurfaceBackground : SystemColors.Window;
                    comboBox.ForeColor = ThemeManager.IsDark ? ThemeManager.TextPrimary : SystemColors.WindowText;
                    break;
                case Button button:
                    if (!button.Name.StartsWith("btnPage", StringComparison.Ordinal))
                    {
                        button.BackColor = button.Name == "runStopBtn"
                            ? ThemeManager.Success
                            : ThemeManager.ButtonBackground;
                    }
                    button.ForeColor = button.Name == "runStopBtn"
                        ? ThemeManager.WhiteOrLight
                        : ThemeManager.TextPrimary;
                    break;
                case Label label:
                    label.ForeColor = label.Name is "lblVersion"
                        ? ThemeManager.TextSecondary
                        : ThemeManager.TextPrimary;
                    if (label.Name == "scriptTitleLabel")
                    {
                        label.BackColor = ThemeManager.SurfaceBackground;
                    }
                    break;
                case CheckBox checkBox:
                    checkBox.ForeColor = ThemeManager.TextPrimary;
                    checkBox.BackColor = ThemeManager.PageBackground;
                    break;
                case GroupBox groupBox:
                    groupBox.ForeColor = ThemeManager.TextPrimary;
                    groupBox.BackColor = ThemeManager.PageBackground;
                    break;
                case SplitContainer splitContainer:
                    splitContainer.BackColor = ThemeManager.SurfaceBackground;
                    break;
                case Panel panel when panel.Name is "contentPanel" or "settingsPanel" or "rightPanel":
                    panel.BackColor = ThemeManager.PageBackground;
                    break;
                case Panel panel:
                    panel.BackColor = ThemeManager.SurfaceBackground;
                    break;
            }
            ApplyControlTheme(control);
        }
    }

    private static void SetToolStripItemColors(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            item.ForeColor = ThemeManager.TextPrimary;
            if (item is ToolStripMenuItem menuItem)
            {
                SetToolStripItemColors(menuItem.DropDownItems);
            }
        }
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
        FindRequiredControl<Button>("btnPageSettings").Click += (_, _) => ShowPage("settingsPanel", "btnPageSettings", showScriptTitle: false);
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

        FindRequiredControl<Button>(selectedButtonName).BackColor = ThemeManager.ButtonBackground;
    }

    private void ResetPages()
    {
        foreach (var pageName in new[] { "settingsPanel", "logPanel", "editorHost" })
        {
            FindRequiredControl<Control>(pageName).Visible = false;
        }

        FindRequiredControl<Label>("scriptTitleLabel").Visible = false;

        foreach (var buttonName in new[] { "btnPageEditor", "btnPageLog", "btnPageSettings" })
        {
            FindRequiredControl<Button>(buttonName).BackColor = ThemeManager.SurfaceBackground;
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
        originalConfigService.Config.CaptureType = selectedCaptureType;
        originalConfigService.Save();

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
        showEasyConMessage("采集卡",
            "默认采集卡类型选择any，会自动选择合适的采集卡" + Environment.NewLine +
            "- 常见采集卡类型是DSHOW，MSMF，DC1394等" + Environment.NewLine +
            "- obs30+版本已支持内置虚拟摄像头，无需安装额外插件" + Environment.NewLine +
            "- 如果出现黑屏、颜色不正确等情况，请切换其他采集卡类型，然后重新打开" + Environment.NewLine +
            "- 如果遇到搜图卡顿问题可尝试点击一次<设置环境变量>菜单" + Environment.NewLine +
            "- 详细使用教程见群946057081文档");
    }

    private void ShowOnlineModeHelp()
    {
        showEasyConMessage("联机模式",
            "- 使用电脑控制单片机的模式" + Environment.NewLine +
            "- 支持超长脚本" + Environment.NewLine +
            "- 可使用虚拟手柄，用键盘玩游戏" + Environment.NewLine + Environment.NewLine +
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
            $"伊机控 v{version}  QQ群:946057081" + Environment.NewLine + Environment.NewLine +
            "Copyright © 2020. 铃落(Nukieberry)" + Environment.NewLine +
            "Copyright © 2021. elmagnifico" + Environment.NewLine +
            "Copyright © 2025. 卡尔(ca1e)");
    }

    private void ShowPendingOriginalDialog(string title)
    {
        showEasyConMessage(title, $"{title}窗口正在接入原版实现。");
    }

    private void OpenOriginalDrawingBoard()
    {
        var form = new DrawingBoard(originalDeviceService.Device);
        form.Show();
    }

    private void OpenOriginalMouseJoystickDialog()
    {
        var form = new Mouse(originalDeviceService.Device);
        form.Show();
    }

    private static void OpenOriginalBluetoothSettingDialog()
    {
        using var form = new EasyCon2.Forms.win32.BTDeviceForm();
        form.ShowDialog();
    }

    private void ShowFindReplacePanel()
    {
        ShowPage("editorHost", "btnPageEditor", showScriptTitle: true);

        var editor = FindRequiredControl<TextBox>("easyConScriptEditor");
        var findBox = FindRequiredControl<TextBox>("findTextBox");
        if (editor.SelectionLength > 0)
        {
            findBox.Text = editor.SelectedText;
        }

        FindRequiredControl<Panel>("findReplacePanel").Visible = true;
        findBox.Focus();
        ShowStatus("查找替换");
    }

    private void FindNextMatch()
    {
        var editor = FindRequiredControl<TextBox>("easyConScriptEditor");
        var findText = FindRequiredControl<TextBox>("findTextBox").Text;
        if (string.IsNullOrEmpty(findText))
        {
            return;
        }

        var startIndex = editor.SelectionStart + editor.SelectionLength;
        var index = editor.Text.IndexOf(findText, startIndex, StringComparison.OrdinalIgnoreCase);
        if (index < 0 && startIndex > 0)
        {
            index = editor.Text.IndexOf(findText, 0, StringComparison.OrdinalIgnoreCase);
        }

        if (index >= 0)
        {
            editor.Select(index, findText.Length);
            editor.Focus();
        }
    }

    private void ReplaceCurrentMatch()
    {
        var editor = FindRequiredControl<TextBox>("easyConScriptEditor");
        var findText = FindRequiredControl<TextBox>("findTextBox").Text;
        if (string.IsNullOrEmpty(findText))
        {
            return;
        }

        if (!string.Equals(editor.SelectedText, findText, StringComparison.OrdinalIgnoreCase))
        {
            FindNextMatch();
            return;
        }

        var replaceText = FindRequiredControl<TextBox>("replaceTextBox").Text;
        var selectionStart = editor.SelectionStart;
        editor.SelectedText = replaceText;
        editor.Select(selectionStart + replaceText.Length, 0);
        editor.Focus();
    }

    private void ShowCaptureConsoleDisconnectedStatus()
    {
        ShowStatus("请先连接视频源");
    }

    private bool ConnectOriginalCaptureSource(int sourceIndex)
    {
        var imageLabelPath = currentScriptPath is not null
            ? Path.Combine(Path.GetDirectoryName(currentScriptPath)!, "ImgLabel")
            : string.Empty;

        return originalCaptureService.Connect(sourceIndex, GetSelectedCaptureTypeValue(), imageLabelPath);
    }

    private int GetSelectedCaptureTypeValue()
    {
        if (string.Equals(selectedCaptureType, "ANY", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        foreach (var captureType in EasyConScriptAdapter.GetCaptureTypes())
        {
            if (captureType.Name == selectedCaptureType)
            {
                return captureType.Value;
            }
        }

        return 0;
    }

    private void OpenOriginalKeyMappingDialog()
    {
        using var dialog = new FormKeyMapping(originalConfigService.KeyMapping);
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            originalConfigService.UpdateKeyMapping(dialog.KeyMapping);
            originalVPadService?.UpdateKeyMapping(originalConfigService.KeyMapping);
        }
    }

    private bool OpenOriginalVirtualController()
    {
        AvaloniaRuntime.EnsureInitialized();
        originalVPadService ??= new VPadService(originalDeviceService.Device, this);
        originalVPadService.SwitchInput(new KeyboardInputBinder(originalDeviceService.Device, originalConfigService.KeyMapping));
        originalVPadService.Show();
        return true;
    }

    private void ShowAlertConfigDialog()
    {
        using var form = new AlertConfigForm();
        form.ShowDialog(this);
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
        catch (Exception exception)
        {
            Debug.WriteLine($"update failed:{exception.Message}");
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

    private static void ShowControllerHelp()
    {
        new HelpTxtDialog(
            "鼠标左键：启用/禁用" + Environment.NewLine +
            "鼠标右键：拖动移动位置，右键点击重置初始位置" + Environment.NewLine +
            "鼠标中键：禁用并隐藏",
            "关于虚拟手柄").Show();
    }

    private void ShowVirtualController()
    {
        if (!isDeviceConnected())
        {
            ShowDeviceNotConnectedWarning();
            return;
        }

        virtualControllerBound = openVirtualController();
        if (virtualControllerBound && originalConfigService.Config.ShowControllerHelp)
        {
            ShowControllerHelp();
            originalConfigService.Config.ShowControllerHelp = false;
            originalConfigService.Save();
        }
    }

    private void ToggleScriptRecording()
    {
        if (recordState == EasyConRecordState.Stopped)
        {
            if (!isDeviceConnected())
            {
                ShowDeviceNotConnectedWarning();
                return;
            }

            if (!virtualControllerBound)
            {
                showEasyConMessage(string.Empty, "请先绑定虚拟手柄");
                return;
            }

            if (!openVirtualController())
            {
                showEasyConMessage(string.Empty, "请先绑定虚拟手柄");
                return;
            }

            recordState = EasyConRecordState.Started;
            FindRequiredControl<Button>("btnRecord").Text = "停止录制";
            FindRequiredControl<Button>("btnRecordPause").Enabled = true;
            FindRequiredControl<TextBox>("easyConScriptEditor").ReadOnly = true;
            startRecordDevice();
            ShowStatus("开始录制");
            return;
        }

        recordState = EasyConRecordState.Stopped;
        var pauseButton = FindRequiredControl<Button>("btnRecordPause");
        FindRequiredControl<Button>("btnRecord").Text = "录制脚本";
        pauseButton.Text = "暂停录制";
        pauseButton.Enabled = false;
        FindRequiredControl<TextBox>("easyConScriptEditor").ReadOnly = false;
        stopRecordDevice();
        FindRequiredControl<TextBox>("easyConScriptEditor").Text = getRecordedScript() ?? string.Empty;
        ShowStatus("录制完成");
    }

    private void ToggleScriptRecordingPause()
    {
        var pauseButton = FindRequiredControl<Button>("btnRecordPause");
        if (recordState == EasyConRecordState.Started)
        {
            recordState = EasyConRecordState.Paused;
            pauseRecordDevice();
            pauseButton.Text = "继续录制";
            ShowStatus("录制已暂停");
        }
        else if (recordState == EasyConRecordState.Paused)
        {
            recordState = EasyConRecordState.Started;
            resumeRecordDevice();
            pauseButton.Text = "暂停录制";
            ShowStatus("继续录制");
        }
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
            "1.设备已安装兼容控制程序" + Environment.NewLine +
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

    private void ToggleCaptureSource()
    {
        if (captureSourceConnected)
        {
            disconnectCaptureSource();
            UpdateCaptureStatus(connected: false);
            return;
        }

        if (FindRequiredControl<ComboBox>("comboVideoSource").SelectedItem is not VideoSourceItem item)
        {
            showEasyConMessage(string.Empty, "请先选择视频源");
            return;
        }

        if (connectCaptureSource(item.Index))
        {
            UpdateCaptureStatus(connected: true);
        }
    }

    private void UpdateCaptureStatus(bool connected)
    {
        var label = FindRequiredControl<StatusStrip>("easyConStatusStrip")
            .Items
            .OfType<ToolStripStatusLabel>()
            .Single(item => item.Name == "labelCaptureStatus");
        label.Text = connected ? "采集卡已连接" : "采集卡未连接";
        label.ForeColor = connected ? Color.FromArgb(31, 138, 101) : Color.FromArgb(140, 139, 132);
        FindRequiredControl<Button>("btnCaptureToggle").Text = connected ? "断开视频源" : "连接视频源";
        captureSourceConnected = connected;
    }

    private void UpdateDeviceStatus(bool connected)
    {
        var label = FindRequiredControl<StatusStrip>("easyConStatusStrip")
            .Items
            .OfType<ToolStripStatusLabel>()
            .Single(item => item.Name == "labelSerialStatus");
        label.Text = connected ? "单片机已连接" : "单片机未连接";
        label.ForeColor = connected ? Color.FromArgb(31, 138, 101) : Color.FromArgb(140, 139, 132);
    }

    private void AppendLogLine(string message)
    {
        var log = FindRequiredControl<TextBox>("logTxtBox");
        log.AppendText(message);
        log.AppendText(Environment.NewLine);
    }

    private void DispatchOriginalAlert(string message)
    {
        Task.Run(async () =>
        {
            try
            {
                var dispatcher = new AlertDispatcher(ConfigManager.LoadAlert());
                dispatcher.OnResult += (_, result) => PostToUi(() => AppendLogLine(result));
                await dispatcher.DispatchAsync(message);
            }
            catch (Exception exception)
            {
                PostToUi(() => AppendLogLine($"推送失败:{exception.Message}"));
            }
        });
    }

    private void PostToUi(Action action)
    {
        if (IsDisposed)
        {
            return;
        }

        if (IsHandleCreated && InvokeRequired)
        {
            BeginInvoke(action);
            return;
        }

        action();
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
        FindRequiredControl<CheckBox>("chkAutoSaveLog").Checked = originalConfigService.Config.AutoSaveLog;
        FindRequiredControl<CheckBox>("chkDebugLog").Checked = originalDeviceService.DebugLogEnabled;
        FindRequiredMenuItem("debugLogMenuItem").Checked = originalDeviceService.DebugLogEnabled;
        FindRequiredMenuItem("darkModeMenuItem").Checked = originalConfigService.Config.DarkMode;

        var log = FindRequiredControl<TextBox>("logTxtBox");
        log.Text = "正在初始化伊机控..." + Environment.NewLine +
            "准备就绪，欢迎使用伊机控！" + Environment.NewLine +
            "请通过文件菜单打开脚本，然后点击运行开始执行脚本";
    }

    private async void RunCurrentScript()
    {
        if (currentScriptPath is not null && currentScriptModified)
        {
            showEasyConMessage(string.Empty, "您还没有保存脚本，请先保存后再运行");
            return;
        }

        var editor = FindRequiredControl<TextBox>("easyConScriptEditor");
        var log = FindRequiredControl<TextBox>("logTxtBox");
        var externalGetters = buildCaptureExternalGetters();
        var compileResult = EasyConScriptAdapter.Compile(editor.Text, externalGetters.Keys.ToArray());
        if (compileResult.HasErrors)
        {
            showEasyConMessage("脚本编译出错", string.Join(Environment.NewLine, compileResult.Diagnostics));
            return;
        }

        if (compileResult.HasKeyAction && !isDeviceConnected())
        {
            showEasyConMessage(string.Empty, "需要连接单片机才能运行脚本");
            return;
        }

        log.AppendText("-- 开始运行 --" + Environment.NewLine);
        ShowStatus("运行中");

        deactivateVirtualController();

        EasyConScriptResult result;
        try
        {
            result = compileResult.HasKeyAction
                ? await executeKeyScriptAsync(editor.Text, externalGetters)
                : EasyConScriptAdapter.Evaluate(editor.Text, externalGetters);
        }
        catch (Exception exception)
        {
            log.AppendText(exception.Message + Environment.NewLine);
            log.AppendText("-- 运行出错 --" + Environment.NewLine);
            ShowStatus("运行出错");
            showEasyConMessage("脚本运行出错", exception.Message);
            return;
        }

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

        foreach (var alert in result.Alerted)
        {
            dispatchAlert(alert);
        }

        log.AppendText("-- 运行结束 --" + Environment.NewLine);
        ShowStatus("运行结束");
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
        FindRequiredControl<Label>("scriptTitleLabel").Text = Path.GetFileName(currentScriptPath);
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

    private IReadOnlyList<(string Name, int Index)> GetOriginalVideoSources()
    {
        return CaptureService.GetVideoSources()
            .Select(source => (source.name, source.index))
            .ToArray();
    }

    private void FormatCurrentScript()
    {
        var editor = FindRequiredControl<TextBox>("easyConScriptEditor");
        var externalGetters = buildCaptureExternalGetters();
        var result = EasyConScriptAdapter.Format(editor.Text, externalGetters.Keys.ToArray());

        if (result.HasErrors)
        {
            ShowStatus("格式化失败");
            showEasyConMessage("格式化出错", string.Join(Environment.NewLine, result.Diagnostics));
            return;
        }

        editor.Text = result.FormattedCode ?? string.Empty;
        editor.Select(0, 0);
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
        menu.Items.Add(CreateMenuItem("settingsMenu", "设置",
            CreateMenuItem("alertConfigMenuItem", "推送设置"),
            CreateMenuItem("debugLogMenuItem", "显示调试信息"),
            CreateMenuItem("darkModeMenuItem", "深色模式")));
        menu.Items.Add(CreateMenuItem("bluetoothMenu", "蓝牙",
            CreateMenuItem("bluetoothSettingMenuItem", "蓝牙设备驱动配置")));
        menu.Items.Add(CreateMenuItem("deviceMenu", "设备",
            CreateMenuItem("unpairMenuItem", "取消配对")));
        menu.Items.Add(CreateMenuItem("drawingMenu", "画图",
            CreateMenuItem("drawingBoardMenuItem", "喷射"),
            CreateMenuItem("mouseJoystickMenuItem", "自由画板鼠标代替摇杆")));
        menu.Items.Add(CreateMenuItem("helpMenu", "帮助",
            CreateMenuItem("menuItemOnlineMode", "联机模式"),
            new ToolStripSeparator { Name = "toolStripSeparator2" },
            CreateMenuItem("checkUpdateMenuItem", "检查更新"),
            CreateMenuItem("sourceMenuItem", "项目源码"),
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

    private enum EasyConRecordState
    {
        Stopped,
        Started,
        Paused,
    }

    private static Control CreateMainArea()
    {
        var mainSplit = new SplitContainer
        {
            Name = "mainSplit",
            Width = 916,
            Height = 681,
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(230, 229, 224),
            FixedPanel = FixedPanel.Panel2,
            SplitterWidth = 6,
            SplitterDistance = 644,
            Panel2MinSize = 266,
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
        sideBar.Controls.Add(CreatePageButton("btnPageSettings", "⚙", Color.FromArgb(230, 229, 224), 90));
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
        editorHost.Controls.Add(CreateFindReplacePanel());
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
            Text = string.Empty,
            WordWrap = false,
        };
        editorHost.Controls.Add(editor);
        return editorHost;
    }

    private static Control CreateFindReplacePanel()
    {
        var panel = new TableLayoutPanel
        {
            Name = "findReplacePanel",
            Dock = DockStyle.Top,
            Height = 34,
            ColumnCount = 6,
            RowCount = 1,
            BackColor = Color.FromArgb(230, 229, 224),
            Padding = new Padding(6, 4, 6, 4),
            Visible = false,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
        panel.Controls.Add(CreateFindPanelLabel("查找:"), 0, 0);
        panel.Controls.Add(CreateFindPanelTextBox("findTextBox"), 1, 0);
        panel.Controls.Add(CreateFindPanelLabel("替换:"), 2, 0);
        panel.Controls.Add(CreateFindPanelTextBox("replaceTextBox"), 3, 0);
        panel.Controls.Add(CreateOriginalButton("btnFindNext", "下一个", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 0, 0, 62, 23), 4, 0);
        panel.Controls.Add(CreateOriginalButton("btnReplaceNext", "替换", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 0, 0, 62, 23), 5, 0);
        return panel;
    }

    private static Label CreateFindPanelLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            Font = new Font("微软雅黑", 9F),
            ForeColor = Color.FromArgb(38, 37, 30),
            TextAlign = ContentAlignment.MiddleLeft,
        };
    }

    private static TextBox CreateFindPanelTextBox(string name)
    {
        return new TextBox
        {
            Name = name,
            Dock = DockStyle.Fill,
            Font = new Font("微软雅黑", 9F),
            Margin = new Padding(0, 0, 6, 0),
        };
    }

    private static Control CreateLogPanel()
    {
        var logPanel = new Panel
        {
            Name = "logPanel",
            Dock = DockStyle.Fill,
            BackColor = SystemColors.Control,
            ForeColor = Color.White,
            Size = new Size(604, 657),
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
            Text = "×",
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            BackColor = Color.Transparent,
            ForeColor = Color.FromArgb(222, 226, 228),
            Font = new Font("Segoe UI Symbol", 13F, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Location = new Point(564, 5),
            Size = new Size(30, 30),
            TextAlign = ContentAlignment.MiddleCenter,
            UseVisualStyleBackColor = false,
        };
        clearLog.FlatAppearance.BorderSize = 0;
        clearLog.FlatAppearance.MouseOverBackColor = Color.FromArgb(78, 78, 78);
        clearLog.FlatAppearance.MouseDownBackColor = Color.FromArgb(92, 92, 92);
        clearLog.Click += (_, _) => log.Clear();

        logPanel.Controls.Add(clearLog);
        logPanel.Controls.Add(log);
        return logPanel;
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
        settingsPanel.Controls.Add(CreateSettingsCheckBox("chkDebugLog", "显示调试信息", 19, 79));
        settingsPanel.Controls.Add(CreateSettingsHeader("lblNotifySettings", "通知设置", 199, 39));
        settingsPanel.Controls.Add(CreateOriginalButton("btnAlertConfig", "推送配置", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 199, 79, 85, 30));
        settingsPanel.Controls.Add(CreateSettingsCheckBox("chkAutoSaveLog", "自动保存日志", 289, 84));
        settingsPanel.Controls.Add(CreateSettingsHeader("lblToolSettings", "工具", 19, 129));
        settingsPanel.Controls.Add(CreateOriginalButton("btnUnpair", "取消蓝牙配对", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 19, 169, 100, 30));
        settingsPanel.Controls.Add(CreateOriginalButton("btnDrawingBoard", "画图工具", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 125, 169, 85, 30));
        settingsPanel.Controls.Add(CreateSettingsHeader("lblAbout", "关于", 19, 219));
        settingsPanel.Controls.Add(new Label
        {
            Name = "lblVersion",
            Text = "版本: --",
            AutoSize = true,
            Font = new Font("微软雅黑", 9F),
            ForeColor = Color.FromArgb(140, 139, 132),
            Location = new Point(19, 259),
        });
        settingsPanel.Controls.Add(CreateOriginalButton("btnCheckUpdate", "检查更新", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 19, 289, 85, 30));
        settingsPanel.Controls.Add(CreateOriginalButton("btnSource", "项目源码", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 109, 289, 85, 30));
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
            Name = "rightPanel",
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.FromArgb(242, 241, 237),
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(4),
            WrapContents = false,
        };

        panel.Controls.Add(CreateRunPanel());
        panel.Controls.Add(CreateSerialPanel());
        panel.Controls.Add(CreateCapturePanel());
        panel.Controls.Add(CreateRecordPanel());
        panel.Controls.Add(CreateControllerPanel());
        return panel;
    }

    private static Control CreateRunPanel()
    {
        var group = CreateOriginalGroupBox("grpScriptRun", "脚本运行", 228, 162);
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
        var group = CreateOriginalGroupBox("grpDevice", "设备连接", 228, 130);
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
        var group = CreateOriginalGroupBox("grpVideoSource", "视频源", 228, 150);
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
        var group = CreateOriginalGroupBox("grpRecord", "录制", 228, 90);
        group.Controls.Add(CreateOriginalButton("btnRecord", "录制脚本", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 8, 22, 206, 28));
        var pauseButton = CreateOriginalButton("btnRecordPause", "暂停录制", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 8, 54, 206, 28);
        pauseButton.Enabled = false;
        group.Controls.Add(pauseButton);
        return group;
    }

    private static Control CreateControllerPanel()
    {
        var group = CreateOriginalGroupBox("grpController", "手柄", 228, 90);
        group.Controls.Add(CreateOriginalButton("btnShowController", "虚拟手柄", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 8, 22, 206, 28));
        group.Controls.Add(CreateOriginalButton("btnKeyMapping", "按键映射", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 8, 54, 206, 28));
        return group;
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
