using AutoSwshRng.App;
using EasyCon.Core.Config;
using EasyCon2.Avalonia.Core.VPad;
using EasyCon2.Services;
using EasyCon2.Theme;
using System.Collections;
using System.Drawing;
using System.Windows.Forms;

namespace AutoSwshRng.App.Tests;

public class MainFormTests
{
    [Test]
    [Apartment(ApartmentState.STA)]
    public void MainFormCreatesExpectedTopLevelTabs()
    {
        using var form = new MainForm();

        Assert.That(
            form.TabTitles,
            Is.EqualTo(new[] { "owoow", "伊机控", "自动化流程" }));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowTabUsesVerticalOriginalToolMenu()
    {
        using var form = new MainForm();

        var menu = FindControl(form, "owoowToolMenu");
        var labels = GetDescendantTexts(menu);

        Assert.That(
            labels,
            Is.SupersetOf(new[]
            {
                "配置档",
                "遭遇查询",
                "个体帧搜索",
                "ID抽奖",
                "机器鹕",
                "瓦特商店",
                "挖挖伯",
                "挖洞兄弟（技巧型）",
                "吼鲸王再出现",
                "Xoroshiro 工具",
            }));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowToolMenuDefaultsToSpreadFinderSelection()
    {
        using var form = new MainForm();

        var spreadFinder = FindControl(form, "owoowMenuSpreadFinder");
        var profiles = FindControl(form, "owoowMenuProfiles");

        Assert.Multiple(() =>
        {
            Assert.That(GetProperty<bool>(spreadFinder, "UseVisualStyleBackColor"), Is.False);
            Assert.That(GetProperty<Color>(spreadFinder, "BackColor"), Is.EqualTo(Color.FromArgb(220, 236, 255)));
            Assert.That(GetProperty<bool>(profiles, "UseVisualStyleBackColor"), Is.True);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowTabContainsOriginalAlignedSearchLabels()
    {
        using var form = new MainForm();

        var workArea = FindControl(form, "owoowOriginalWorkArea");
        var labels = GetDescendantTexts(workArea);

        Assert.That(
            labels,
            Is.SupersetOf(new[]
            {
                "Seed[0]:",
                "Seed[1]:",
                "Switch IP:",
                "闪耀护符?",
                "证章护符?",
                "游戏:",
                "遭遇设置 - 定点",
                "区域:",
                "天气:",
                "目标:",
                "宝可梦图鉴“现在推荐”",
                "高级设置",
                "异色:",
                "证章:",
                "CFW 工具",
                "实机工具",
            }));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowResultGridMatchesOriginalColumnOrder()
    {
        using var form = new MainForm();

        var grid = FindControl(form, "owoowResultsGrid");

        Assert.That(
            GetDataGridViewColumnHeaders(grid),
            Is.EqualTo(new[]
            {
                "推进数",
                "跳跃",
                "步数",
                "动画",
                "宝可梦",
                "异色",
                "气场",
                "等级",
                "特性",
                "性格",
                "性别",
                "HP",
                "攻击",
                "防御",
                "特攻",
                "特防",
                "速度",
                "证章",
                "EC",
                "PID",
                "身高",
            }));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowEncounterModesUseOriginalTabPages()
    {
        using var form = new MainForm();

        var tabs = FindControl(form, "owoowEncounterModeTabs");

        Assert.That(GetTabPageTitles(tabs), Is.EqualTo(new[] { "定点", "符号", "隐藏", "垂钓" }));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowIvFiltersUseOriginalNumericControls()
    {
        using var form = new MainForm();

        _ = FindControl(form, "owoowIvFilterPanel");

        foreach (var stat in new[] { "Hp", "Atk", "Def", "Spa", "Spd", "Spe" })
        {
            Assert.Multiple(() =>
            {
                Assert.That(GetProperty<decimal>(FindControl(form, $"owoow{stat}IvMin"), "Value"), Is.EqualTo(0));
                Assert.That(GetProperty<decimal>(FindControl(form, $"owoow{stat}IvMax"), "Value"), Is.EqualTo(31));
                Assert.That(GetProperty<decimal>(FindControl(form, $"owoow{stat}IvCurrent"), "Value"), Is.EqualTo(0));
                Assert.That(GetProperty<decimal>(FindControl(form, $"owoow{stat}IvTarget"), "Value"), Is.EqualTo(31));
            });
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowCfwAndRetailToolsMatchOriginalInitialEnabledStates()
    {
        using var form = new MainForm();

        Assert.Multiple(() =>
        {
            Assert.That(GetProperty<bool>(FindControl(form, "owoowCfwSkipInput"), "Enabled"), Is.False);
            Assert.That(GetProperty<bool>(FindControl(form, "owoowCfwCancelButton"), "Enabled"), Is.False);
            Assert.That(GetProperty<bool>(FindControl(form, "owoowDaysPlusButton"), "Enabled"), Is.False);
            Assert.That(GetProperty<bool>(FindControl(form, "owoowDaysMinusButton"), "Enabled"), Is.False);
            Assert.That(GetProperty<bool>(FindControl(form, "owoowAdvanceButton"), "Enabled"), Is.False);
            Assert.That(GetProperty<bool>(FindControl(form, "owoowNtpButton"), "Enabled"), Is.False);
            Assert.That(GetProperty<bool>(FindControl(form, "owoowTurboButton"), "Enabled"), Is.False);
            Assert.That(GetProperty<bool>(FindControl(form, "owoowTurboControlsButton"), "Enabled"), Is.True);
            Assert.That(GetProperty<bool>(FindControl(form, "owoowResetForSeedButton"), "Enabled"), Is.False);
            Assert.That(GetProperty<bool>(FindControl(form, "owoowSettingsButton"), "Enabled"), Is.True);
            Assert.That(GetProperty<bool>(FindControl(form, "owoowRetailSeedFinderButton"), "Enabled"), Is.True);
            Assert.That(GetProperty<bool>(FindControl(form, "owoowRetailGenerateButton"), "Enabled"), Is.True);
            Assert.That(GetProperty<bool>(FindControl(form, "owoowRetailUpdateSeedsButton"), "Enabled"), Is.True);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowConnectionPanelMatchesOriginalDisconnectedState()
    {
        using var form = new MainForm();

        Assert.Multiple(() =>
        {
            Assert.That(GetProperty<bool>(FindControl(form, "owoowConnectButton"), "Enabled"), Is.True);
            Assert.That(GetProperty<bool>(FindControl(form, "owoowDisconnectButton"), "Enabled"), Is.False);
            Assert.That(GetProperty<bool>(FindControl(form, "owoowAdvInput"), "Enabled"), Is.False);
            Assert.That(GetProperty<bool>(FindControl(form, "owoowAdvSubInput"), "Enabled"), Is.False);
            Assert.That(GetProperty<bool>(FindControl(form, "owoowCurrentSeed0Input"), "Enabled"), Is.False);
            Assert.That(GetProperty<bool>(FindControl(form, "owoowCurrentSeed1Input"), "Enabled"), Is.False);
            Assert.That(GetProperty<bool>(FindControl(form, "owoowUpdateSeedsButton"), "Enabled"), Is.False);
            Assert.That(GetProperty<string>(FindControl(form, "owoowConnectionStatusLabel"), "Text"), Is.EqualTo("未连接。"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConTabRestoresOriginalMenuAndPanels()
    {
        using var form = new MainForm();

        var menu = FindControl(form, "easyConOriginalMenu");
        var status = FindControl(form, "easyConStatusStrip");

        Assert.Multiple(() =>
        {
            Assert.That(menu.GetType().Name, Is.EqualTo("MenuStrip"));
            Assert.That(GetToolStripItemTexts(menu), Is.EqualTo(new[] { "文件", "编辑", "脚本", "搜图", "设置", "蓝牙", "ESP32", "画图", "帮助" }));
            Assert.That(FindControl(form, "easyConScriptEditor"), Is.Not.Null);
            Assert.That(FindControl(form, "logTxtBox"), Is.Not.Null);
            Assert.That(FindControl(form, "easyConSerialPanel"), Is.Not.Null);
            Assert.That(FindControl(form, "easyConCapturePanel"), Is.Not.Null);
            Assert.That(FindControl(form, "easyConRecordPanel"), Is.Not.Null);
            Assert.That(FindControl(form, "easyConControllerPanel"), Is.Not.Null);
            Assert.That(FindControl(form, "easyConFirmwarePanel"), Is.Not.Null);
            Assert.That(status.GetType().Name, Is.EqualTo("StatusStrip"));
            Assert.That(GetToolStripItemTexts(status), Does.Contain("单片机未连接"));
            Assert.That(GetToolStripItemTexts(status), Does.Contain("采集卡未连接"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConTabRestoresOriginalMenuDropDownItems()
    {
        using var form = new MainForm();
        var menu = FindControl(form, "easyConOriginalMenu");

        Assert.Multiple(() =>
        {
            Assert.That(GetToolStripDropDownItemTexts(FindToolStripItem(menu, "fileMenu")), Is.EqualTo(new[] { "新建", "打开", "保存", "另存为", "关闭", "退出" }));
            Assert.That(GetToolStripDropDownItemTexts(FindToolStripItem(menu, "editMenu")), Is.EqualTo(new[] { "查找替换", "查找下一个", "注释/取消注释" }));
            Assert.That(GetProperty<bool>(FindToolStripItem(menu, "scriptMenu"), "Visible"), Is.False);
            Assert.That(GetToolStripDropDownItemTexts(FindToolStripItem(menu, "scriptMenu")), Is.EqualTo(new[] { "格式化", "运行" }));
            Assert.That(GetToolStripDropDownItemTexts(FindToolStripItem(menu, "captureMenu")), Is.EqualTo(new[] { "采集卡类型", "设置环境变量", "搜图说明" }));
            Assert.That(GetToolStripDropDownItemTexts(FindToolStripItem(menu, "settingsMenu")), Is.EqualTo(new[] { "推送设置", "显示调试信息", "烧录自动运行", "显示折叠", "代码自动补全", "深色模式" }));
            Assert.That(GetToolStripDropDownItemTexts(FindToolStripItem(menu, "bluetoothMenu")), Is.EqualTo(new[] { "蓝牙设备驱动配置" }));
            Assert.That(GetToolStripDropDownItemTexts(FindToolStripItem(menu, "esp32Menu")), Is.EqualTo(new[] { "手柄设置", "取消配对" }));
            Assert.That(GetToolStripDropDownItemTexts(FindToolStripItem(menu, "drawingMenu")), Is.EqualTo(new[] { "喷射", "自由画板鼠标代替摇杆" }));
            Assert.That(GetToolStripDropDownItemTexts(FindToolStripItem(menu, "helpMenu")), Is.EqualTo(new[] { "固件模式", "联机模式", "烧录模式", "脚本语法", "检查更新", "项目源码", "关于" }));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConFindReplaceMenuOpensOriginalSearchPanel()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var menu = FindControl(form, "easyConOriginalMenu");
        var opened = 0;

        SetField(easyCon, "openFindReplacePanel", new Action(() => opened++));

        InvokeClick(FindToolStripItem(menu, "menuItemFindReplace"));

        Assert.That(opened, Is.EqualTo(1));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConOriginalUtilityMenusUseExistingActions()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var menu = FindControl(form, "easyConOriginalMenu");
        var actions = new List<string>();

        SetField(easyCon, "openAlertConfigDialog", new Action(() => actions.Add("alert")));
        SetField(easyCon, "openBluetoothSettingDialog", new Action(() => actions.Add("bluetooth")));
        SetField(easyCon, "openEspConfigDialog", new Action(() => actions.Add("esp")));
        SetField(easyCon, "openDrawingBoard", new Action(() => actions.Add("drawing")));
        SetField(easyCon, "openExternalLink", new Action<string>(url => actions.Add(url)));

        InvokeClick(FindToolStripItem(menu, "alertConfigMenuItem"));
        InvokeClick(FindToolStripItem(menu, "bluetoothSettingMenuItem"));
        InvokeClick(FindToolStripItem(menu, "espConfigMenuItem"));
        InvokeClick(FindToolStripItem(menu, "drawingBoardMenuItem"));
        InvokeClick(FindToolStripItem(menu, "sourceMenuItem"));

        Assert.That(actions, Is.EqualTo(new[]
        {
            "alert",
            "bluetooth",
            "esp",
            "drawing",
            "https://github.com/EasyConNS/EasyCon",
        }));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConFindReplacePanelBecomesVisibleWithSelectedText()
    {
        using var form = new MainForm();
        form.Show();
        SetProperty(FindControlByType(form, "TabControl"), "SelectedIndex", 1);

        var menu = FindControl(form, "easyConOriginalMenu");
        var editor = FindControl(form, "easyConScriptEditor");

        SetProperty(editor, "Text", "WAIT 100" + Environment.NewLine + "PRESS A");
        SetProperty(editor, "SelectionStart", 5);
        SetProperty(editor, "SelectionLength", 3);

        Assert.That(GetProperty<bool>(FindControl(form, "findReplacePanel"), "Visible"), Is.False);

        InvokeClick(FindToolStripItem(menu, "menuItemFindReplace"));

        Assert.Multiple(() =>
        {
            Assert.That(GetProperty<bool>(FindControl(form, "findReplacePanel"), "Visible"), Is.True);
            Assert.That(GetProperty<string>(FindControl(form, "findTextBox"), "Text"), Is.EqualTo("100"));
            Assert.That(GetProperty<bool>(FindControl(form, "editorHost"), "Visible"), Is.True);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConFindNextButtonSelectsNextMatch()
    {
        using var form = new MainForm();
        form.Show();
        SetProperty(FindControlByType(form, "TabControl"), "SelectedIndex", 1);

        var menu = FindControl(form, "easyConOriginalMenu");
        var editor = FindControl(form, "easyConScriptEditor");

        SetProperty(editor, "Text", "WAIT 100" + Environment.NewLine + "PRESS A" + Environment.NewLine + "PRESS B");
        SetProperty(editor, "SelectionStart", 0);
        SetProperty(editor, "SelectionLength", 0);
        InvokeClick(FindToolStripItem(menu, "menuItemFindReplace"));
        SetProperty(FindControl(form, "findTextBox"), "Text", "PRESS");

        InvokeClick(FindControl(form, "btnFindNext"));

        Assert.Multiple(() =>
        {
            Assert.That(GetProperty<int>(editor, "SelectionStart"), Is.EqualTo(("WAIT 100" + Environment.NewLine).Length));
            Assert.That(GetProperty<int>(editor, "SelectionLength"), Is.EqualTo("PRESS".Length));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConReplaceButtonReplacesSelectedMatch()
    {
        using var form = new MainForm();
        form.Show();
        SetProperty(FindControlByType(form, "TabControl"), "SelectedIndex", 1);

        var menu = FindControl(form, "easyConOriginalMenu");
        var editor = FindControl(form, "easyConScriptEditor");

        SetProperty(editor, "Text", "PRESS A" + Environment.NewLine + "PRESS B");
        SetProperty(editor, "SelectionStart", 0);
        SetProperty(editor, "SelectionLength", 0);
        InvokeClick(FindToolStripItem(menu, "menuItemFindReplace"));
        SetProperty(FindControl(form, "findTextBox"), "Text", "PRESS");
        SetProperty(FindControl(form, "replaceTextBox"), "Text", "CLICK");
        InvokeClick(FindControl(form, "btnFindNext"));

        InvokeClick(FindControl(form, "btnReplaceNext"));

        Assert.Multiple(() =>
        {
            Assert.That(GetProperty<string>(editor, "Text"), Is.EqualTo("CLICK A" + Environment.NewLine + "PRESS B"));
            Assert.That(GetProperty<int>(editor, "SelectionStart"), Is.EqualTo("CLICK".Length));
            Assert.That(GetProperty<int>(editor, "SelectionLength"), Is.EqualTo(0));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConCaptureTypeMenuPopulatesOriginalOpenCvApis()
    {
        using var configRestore = PreserveEasyConConfig(new ConfigState { CaptureType = "ANY" });
        using var form = new MainForm();
        var menu = FindControl(form, "easyConOriginalMenu");
        var captureTypeMenu = FindToolStripItem(menu, "captureTypeMenu");

        InvokeDropDownOpening(captureTypeMenu);

        Assert.Multiple(() =>
        {
            Assert.That(GetToolStripDropDownItemTexts(captureTypeMenu), Is.SupersetOf(new[] { "ANY", "DSHOW", "MSMF", "FFMPEG" }));
            Assert.That(GetProperty<bool>(FindToolStripItem(captureTypeMenu, "captureType_ANY"), "Checked"), Is.True);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConCaptureTypeMenuSelectionSwitchesCheckedItem()
    {
        using var configRestore = PreserveEasyConConfig(new ConfigState { CaptureType = "ANY" });
        using var form = new MainForm();
        var menu = FindControl(form, "easyConOriginalMenu");
        var captureTypeMenu = FindToolStripItem(menu, "captureTypeMenu");

        InvokeDropDownOpening(captureTypeMenu);
        InvokeClick(FindToolStripItem(captureTypeMenu, "captureType_MSMF"));

        Assert.Multiple(() =>
        {
            Assert.That(GetProperty<bool>(FindToolStripItem(captureTypeMenu, "captureType_ANY"), "Checked"), Is.False);
            Assert.That(GetProperty<bool>(FindToolStripItem(captureTypeMenu, "captureType_MSMF"), "Checked"), Is.True);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConCaptureTypeMenuInitializesAndPersistsOriginalConfigFlag()
    {
        using var configRestore = PreserveEasyConConfig(new ConfigState { CaptureType = "DSHOW" });
        using var form = new MainForm();
        var menu = FindControl(form, "easyConOriginalMenu");
        var captureTypeMenu = FindToolStripItem(menu, "captureTypeMenu");

        InvokeDropDownOpening(captureTypeMenu);

        Assert.Multiple(() =>
        {
            Assert.That(GetProperty<bool>(FindToolStripItem(captureTypeMenu, "captureType_DSHOW"), "Checked"), Is.True);
            Assert.That(GetProperty<bool>(FindToolStripItem(captureTypeMenu, "captureType_ANY"), "Checked"), Is.False);
        });

        InvokeClick(FindToolStripItem(captureTypeMenu, "captureType_MSMF"));

        Assert.That(ConfigManager.LoadConfig().CaptureType, Is.EqualTo("MSMF"));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConSetEnvVarMenuMatchesOriginalStatusFeedback()
    {
        var previous = Environment.GetEnvironmentVariable("OPENCV_VIDEOIO_MSMF_ENABLE_HW_TRANSFORMS");
        try
        {
            Environment.SetEnvironmentVariable("OPENCV_VIDEOIO_MSMF_ENABLE_HW_TRANSFORMS", null);
            using var form = new MainForm();
            var menu = FindControl(form, "easyConOriginalMenu");
            var status = FindControl(form, "easyConStatusStrip");

            InvokeClick(FindToolStripItem(menu, "setEnvVarMenuItem"));

            Assert.Multiple(() =>
            {
                Assert.That(Environment.GetEnvironmentVariable("OPENCV_VIDEOIO_MSMF_ENABLE_HW_TRANSFORMS"), Is.EqualTo("0"));
                Assert.That(GetProperty<string>(FindToolStripItem(status, "toolStripStatusLabel1"), "Text"), Is.EqualTo("环境变量设置成功：0"));
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENCV_VIDEOIO_MSMF_ENABLE_HW_TRANSFORMS", previous);
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConHelpMenusShowOriginalModeAndAboutMessages()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var menu = FindControl(form, "easyConOriginalMenu");
        var messages = new List<(string Title, string Message)>();

        SetField(easyCon, "showEasyConMessage", new Action<string, string>((title, message) => messages.Add((title, message))));

        InvokeClick(FindToolStripItem(menu, "menuItemFirmwareMode"));
        InvokeClick(FindToolStripItem(menu, "menuItemOnlineMode"));
        InvokeClick(FindToolStripItem(menu, "menuItemFlashMode"));
        InvokeClick(FindToolStripItem(menu, "captureHelpMenuItem"));
        InvokeClick(FindToolStripItem(menu, "menuItemAbout"));

        Assert.Multiple(() =>
        {
            Assert.That(messages.Select(item => item.Title), Is.EqualTo(new[] { "固件模式", "联机模式", "烧录模式", "采集卡", "关于" }));
            Assert.That(messages[0].Message, Does.Contain("生成固件"));
            Assert.That(messages[1].Message, Does.Contain("电脑控制"));
            Assert.That(messages[2].Message, Does.Contain("连线烧录"));
            Assert.That(messages[3].Message, Does.Contain("默认采集卡类型选择any"));
            Assert.That(messages[3].Message, Does.Contain("设置环境变量"));
            Assert.That(messages[4].Message, Does.Contain("伊机控 v"));
            Assert.That(messages[4].Message, Does.Contain("QQ群:946057081"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConStartupLogMatchesOriginalWelcomeMessages()
    {
        using var form = new MainForm();

        var logText = FindControl(form, "logTxtBox");

        Assert.Multiple(() =>
        {
            Assert.That(GetProperty<string>(logText, "Text"), Does.Contain("正在初始化伊机控..."));
            Assert.That(GetProperty<string>(logText, "Text"), Does.Contain("准备就绪，欢迎使用伊机控！"));
            Assert.That(GetProperty<string>(logText, "Text"), Does.Contain("将脚本文件直接拖入窗口打开，然后点击运行开始执行脚本"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConTabRestoresOriginalPageSidebar()
    {
        using var form = new MainForm();

        var log = FindControl(form, "btnPageLog");
        var editor = FindControl(form, "btnPageEditor");
        var burn = FindControl(form, "btnPageBurn");
        var settings = FindControl(form, "btnPageSettings");

        Assert.Multiple(() =>
        {
            Assert.That(GetProperty<string>(log, "Text"), Is.EqualTo("📄"));
            Assert.That(GetProperty<string>(editor, "Text"), Is.EqualTo("📝"));
            Assert.That(GetProperty<string>(burn, "Text"), Is.EqualTo("🔥"));
            Assert.That(GetProperty<string>(settings, "Text"), Is.EqualTo("⚙"));
            Assert.That(GetProperty<Color>(log, "BackColor"), Is.EqualTo(Color.FromArgb(235, 234, 229)));
            Assert.That(GetProperty<Color>(editor, "BackColor"), Is.EqualTo(Color.FromArgb(230, 229, 224)));
            Assert.That(GetProperty<bool>(log, "UseVisualStyleBackColor"), Is.False);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConSidebarButtonsSwitchOriginalPages()
    {
        using var form = new MainForm();
        form.Show();
        SetProperty(FindControlByType(form, "TabControl"), "SelectedIndex", 1);

        var logButton = FindControl(form, "btnPageLog");
        var editorButton = FindControl(form, "btnPageEditor");
        var burnButton = FindControl(form, "btnPageBurn");
        var settingsButton = FindControl(form, "btnPageSettings");

        InvokeClick(editorButton);
        Assert.Multiple(() =>
        {
            Assert.That(GetProperty<bool>(FindControl(form, "editorHost"), "Visible"), Is.True);
            Assert.That(GetProperty<bool>(FindControl(form, "scriptTitleLabel"), "Visible"), Is.True);
            Assert.That(GetProperty<bool>(FindControl(form, "logPanel"), "Visible"), Is.False);
            Assert.That(GetProperty<Color>(editorButton, "BackColor"), Is.EqualTo(Color.FromArgb(235, 234, 229)));
            Assert.That(GetProperty<Color>(logButton, "BackColor"), Is.EqualTo(Color.FromArgb(230, 229, 224)));
        });

        InvokeClick(burnButton);
        Assert.Multiple(() =>
        {
            Assert.That(GetProperty<bool>(FindControl(form, "burnPanel"), "Visible"), Is.True);
            Assert.That(GetProperty<bool>(FindControl(form, "editorHost"), "Visible"), Is.False);
            Assert.That(GetProperty<Color>(burnButton, "BackColor"), Is.EqualTo(Color.FromArgb(235, 234, 229)));
            Assert.That(GetProperty<Color>(editorButton, "BackColor"), Is.EqualTo(Color.FromArgb(230, 229, 224)));
        });

        InvokeClick(settingsButton);
        Assert.Multiple(() =>
        {
            Assert.That(GetProperty<bool>(FindControl(form, "settingsPanel"), "Visible"), Is.True);
            Assert.That(GetProperty<bool>(FindControl(form, "burnPanel"), "Visible"), Is.False);
            Assert.That(GetProperty<Color>(settingsButton, "BackColor"), Is.EqualTo(Color.FromArgb(235, 234, 229)));
            Assert.That(GetProperty<Color>(burnButton, "BackColor"), Is.EqualTo(Color.FromArgb(230, 229, 224)));
        });

        InvokeClick(logButton);
        Assert.Multiple(() =>
        {
            Assert.That(GetProperty<bool>(FindControl(form, "logPanel"), "Visible"), Is.True);
            Assert.That(GetProperty<bool>(FindControl(form, "settingsPanel"), "Visible"), Is.False);
            Assert.That(GetProperty<Color>(logButton, "BackColor"), Is.EqualTo(Color.FromArgb(235, 234, 229)));
            Assert.That(GetProperty<Color>(settingsButton, "BackColor"), Is.EqualTo(Color.FromArgb(230, 229, 224)));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConClearLogButtonClearsLogText()
    {
        using var form = new MainForm();
        var logText = FindControl(form, "logTxtBox");
        var clearLog = FindControl(form, "clsLogBtn");

        SetProperty(logText, "Text", "line 1" + Environment.NewLine + "line 2");
        InvokeClick(clearLog);

        Assert.That(GetProperty<string>(logText, "Text"), Is.Empty);
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConRunButtonEvaluatesPrintScriptWithoutSerialDevice()
    {
        using var form = new MainForm();
        var editor = FindControl(form, "easyConScriptEditor");
        var logText = FindControl(form, "logTxtBox");
        var runButton = FindControl(form, "runStopBtn");

        SetProperty(editor, "Text", "PRINT \"hello\"");
        SetProperty(logText, "Text", string.Empty);
        InvokeClick(runButton);

        Assert.That(GetProperty<string>(logText, "Text"), Does.Contain("hello"));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConRunMenuUsesRunButtonPath()
    {
        using var form = new MainForm();
        var editor = FindControl(form, "easyConScriptEditor");
        var logText = FindControl(form, "logTxtBox");
        var menu = FindControl(form, "easyConOriginalMenu");
        var runMenu = FindToolStripItem(menu, "runMenuItem");

        SetProperty(editor, "Text", "PRINT \"from menu\"");
        SetProperty(logText, "Text", string.Empty);
        InvokeClick(runMenu);

        Assert.That(GetProperty<string>(logText, "Text"), Does.Contain("from menu"));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConRunButtonShowsOriginalRunLogAndStatus()
    {
        using var form = new MainForm();
        var editor = FindControl(form, "easyConScriptEditor");
        var logText = FindControl(form, "logTxtBox");
        var runButton = FindControl(form, "runStopBtn");
        var status = FindControl(form, "easyConStatusStrip");

        SetProperty(editor, "Text", "PRINT \"hello\"");
        SetProperty(logText, "Text", string.Empty);
        InvokeClick(runButton);

        Assert.Multiple(() =>
        {
            Assert.That(GetProperty<string>(logText, "Text"), Does.Contain("-- 开始运行 --"));
            Assert.That(GetProperty<string>(logText, "Text"), Does.Contain("hello"));
            Assert.That(GetProperty<string>(logText, "Text"), Does.Contain("-- 运行结束 --"));
            Assert.That(GetProperty<string>(FindToolStripItem(status, "toolStripStatusLabel1"), "Text"), Is.EqualTo("运行结束"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConRunButtonDeactivatesOriginalVirtualControllerForValidScript()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var editor = FindControl(form, "easyConScriptEditor");
        var runButton = FindControl(form, "runStopBtn");
        var deactivateCalls = 0;

        SetField(easyCon, "deactivateVirtualController", new Action(() => deactivateCalls++));
        SetProperty(editor, "Text", "PRINT \"hello\"");

        InvokeClick(runButton);

        Assert.That(deactivateCalls, Is.EqualTo(1));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConRunButtonKeepsOriginalVirtualControllerActiveWhenScriptHasErrors()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var editor = FindControl(form, "easyConScriptEditor");
        var runButton = FindControl(form, "runStopBtn");
        var deactivateCalls = 0;

        SetField(easyCon, "deactivateVirtualController", new Action(() => deactivateCalls++));
        SetField(easyCon, "showEasyConMessage", new Action<string, string>((_, _) => { }));
        SetProperty(editor, "Text", "PRINT");

        InvokeClick(runButton);

        Assert.That(deactivateCalls, Is.EqualTo(0));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConRunButtonShowsOriginalCompileFailureMessage()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var editor = FindControl(form, "easyConScriptEditor");
        var logText = FindControl(form, "logTxtBox");
        var messages = new List<(string Title, string Message)>();
        var deactivateCalls = 0;

        SetField(easyCon, "deactivateVirtualController", new Action(() => deactivateCalls++));
        SetField(easyCon, "showEasyConMessage", new Action<string, string>((title, message) => messages.Add((title, message))));
        SetProperty(editor, "Text", "PRINT");
        SetProperty(logText, "Text", string.Empty);

        InvokeClick(FindControl(form, "runStopBtn"));

        Assert.Multiple(() =>
        {
            Assert.That(messages, Has.Count.EqualTo(1));
            Assert.That(messages[0].Title, Is.EqualTo("脚本编译出错"));
            Assert.That(messages[0].Message, Is.Not.Empty);
            Assert.That(deactivateCalls, Is.EqualTo(0));
            Assert.That(GetProperty<string>(logText, "Text"), Is.Empty);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConRunButtonUsesOriginalCaptureExternalGetters()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var editor = FindControl(form, "easyConScriptEditor");
        var runButton = FindControl(form, "runStopBtn");
        var calls = 0;

        SetField(easyCon, "buildCaptureExternalGetters", new Func<IReadOnlyDictionary<string, Func<int>>>(() =>
        {
            calls++;
            return new Dictionary<string, Func<int>>();
        }));
        SetProperty(editor, "Text", "PRINT \"hello\"");

        InvokeClick(runButton);

        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConRunButtonRequiresSavingModifiedOpenedScriptLikeOriginal()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var editor = FindControl(form, "easyConScriptEditor");
        var logText = FindControl(form, "logTxtBox");
        var messages = new List<(string Title, string Message)>();
        var buildExternalGetterCalls = 0;

        SetField(easyCon, "currentScriptPath", Path.Combine(TestContext.CurrentContext.WorkDirectory, "opened.ecs"));
        SetField(easyCon, "currentScriptModified", true);
        SetField(easyCon, "buildCaptureExternalGetters", new Func<IReadOnlyDictionary<string, Func<int>>>(() =>
        {
            buildExternalGetterCalls++;
            return new Dictionary<string, Func<int>>();
        }));
        SetField(easyCon, "showEasyConMessage", new Action<string, string>((title, message) => messages.Add((title, message))));
        SetProperty(editor, "Text", "PRINT \"hello\"");
        SetProperty(logText, "Text", string.Empty);

        InvokeClick(FindControl(form, "runStopBtn"));

        Assert.Multiple(() =>
        {
            Assert.That(messages, Is.EqualTo(new[] { (string.Empty, "您还没有保存脚本，请先保存后再运行") }));
            Assert.That(buildExternalGetterCalls, Is.EqualTo(0));
            Assert.That(GetProperty<string>(logText, "Text"), Is.Empty);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConRunButtonRequiresConnectedDeviceForKeyActionScriptLikeOriginal()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var editor = FindControl(form, "easyConScriptEditor");
        var logText = FindControl(form, "logTxtBox");
        var messages = new List<(string Title, string Message)>();
        var deactivateCalls = 0;

        SetField(easyCon, "isDeviceConnected", new Func<bool>(() => false));
        SetField(easyCon, "deactivateVirtualController", new Action(() => deactivateCalls++));
        SetField(easyCon, "showEasyConMessage", new Action<string, string>((title, message) => messages.Add((title, message))));
        SetProperty(editor, "Text", "A");
        SetProperty(logText, "Text", string.Empty);

        InvokeClick(FindControl(form, "runStopBtn"));

        Assert.Multiple(() =>
        {
            Assert.That(messages, Is.EqualTo(new[] { (string.Empty, "需要连接单片机才能运行脚本") }));
            Assert.That(deactivateCalls, Is.EqualTo(0));
            Assert.That(GetProperty<string>(logText, "Text"), Is.Empty);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConRunButtonRequiresRemoteStopForKeyActionScriptLikeOriginal()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var editor = FindControl(form, "easyConScriptEditor");
        var logText = FindControl(form, "logTxtBox");
        var messages = new List<(string Title, string Message)>();
        var remoteStopCalls = 0;
        var deactivateCalls = 0;

        SetField(easyCon, "isDeviceConnected", new Func<bool>(() => true));
        SetField(easyCon, "remoteStopDevice", new Func<bool>(() =>
        {
            remoteStopCalls++;
            return false;
        }));
        SetField(easyCon, "deactivateVirtualController", new Action(() => deactivateCalls++));
        SetField(easyCon, "showEasyConMessage", new Action<string, string>((title, message) => messages.Add((title, message))));
        SetProperty(editor, "Text", "A");
        SetProperty(logText, "Text", string.Empty);

        InvokeClick(FindControl(form, "runStopBtn"));

        Assert.Multiple(() =>
        {
            Assert.That(messages, Is.EqualTo(new[] { (string.Empty, "需要先停止烧录脚本运行，请点击<远程停止>按钮") }));
            Assert.That(remoteStopCalls, Is.EqualTo(1));
            Assert.That(deactivateCalls, Is.EqualTo(0));
            Assert.That(GetProperty<string>(logText, "Text"), Is.Empty);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConFormatButtonFormatsCurrentScript()
    {
        using var form = new MainForm();
        var editor = FindControl(form, "easyConScriptEditor");
        var formatButton = FindControl(form, "formatBtn");

        SetProperty(editor, "Text", "PRINT \"hello\",\"world\"");
        InvokeClick(formatButton);

        Assert.That(GetProperty<string>(editor, "Text"), Is.EqualTo("PRINT \"hello\", \"world\""));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConFormatButtonUsesOriginalCaptureExternalGetters()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var editor = FindControl(form, "easyConScriptEditor");
        var formatButton = FindControl(form, "formatBtn");
        var calls = 0;

        SetField(easyCon, "buildCaptureExternalGetters", new Func<IReadOnlyDictionary<string, Func<int>>>(() =>
        {
            calls++;
            return new Dictionary<string, Func<int>>();
        }));
        SetProperty(editor, "Text", "PRINT \"hello\",\"world\"");

        InvokeClick(formatButton);

        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConFormatMenuUsesFormatButtonPath()
    {
        using var form = new MainForm();
        var editor = FindControl(form, "easyConScriptEditor");
        var menu = FindControl(form, "easyConOriginalMenu");
        var formatMenu = FindToolStripItem(menu, "formatMenuItem");

        SetProperty(editor, "Text", "PRINT \"menu\",\"format\"");
        InvokeClick(formatMenu);

        Assert.That(GetProperty<string>(editor, "Text"), Is.EqualTo("PRINT \"menu\", \"format\""));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConFormatButtonShowsOriginalFailureMessage()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var editor = FindControl(form, "easyConScriptEditor");
        var logText = FindControl(form, "logTxtBox");
        var formatButton = FindControl(form, "formatBtn");
        var status = FindControl(form, "easyConStatusStrip");
        var messages = new List<(string Title, string Message)>();

        SetField(easyCon, "showEasyConMessage", new Action<string, string>((title, message) => messages.Add((title, message))));
        SetProperty(editor, "Text", "PRINT");
        SetProperty(logText, "Text", string.Empty);
        InvokeClick(formatButton);

        Assert.Multiple(() =>
        {
            Assert.That(GetProperty<string>(editor, "Text"), Is.EqualTo("PRINT"));
            Assert.That(GetProperty<string>(logText, "Text"), Is.Empty);
            Assert.That(GetProperty<string>(FindToolStripItem(status, "toolStripStatusLabel1"), "Text"), Is.EqualTo("格式化失败"));
            Assert.That(messages, Has.Count.EqualTo(1));
            Assert.That(messages[0].Title, Is.EqualTo("格式化出错"));
            Assert.That(messages[0].Message, Is.Not.Empty);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConNewMenuClearsScriptAndShowsOriginalStatus()
    {
        using var form = new MainForm();
        var editor = FindControl(form, "easyConScriptEditor");
        var title = FindControl(form, "scriptTitleLabel");
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var menu = FindControl(form, "easyConOriginalMenu");
        var status = FindControl(form, "easyConStatusStrip");

        SetField(easyCon, "confirmSaveModifiedScript", new Func<System.Windows.Forms.DialogResult>(() => System.Windows.Forms.DialogResult.No));
        SetProperty(editor, "Text", "PRINT \"old\"");
        SetProperty(title, "Text", "旧脚本.ecs");
        InvokeClick(FindToolStripItem(menu, "menuItemNew"));

        Assert.Multiple(() =>
        {
            Assert.That(GetProperty<string>(editor, "Text"), Is.Empty);
            Assert.That(GetProperty<string>(title, "Text"), Is.EqualTo("未命名脚本"));
            Assert.That(GetProperty<string>(FindToolStripItem(status, "toolStripStatusLabel1"), "Text"), Is.EqualTo("新建完毕"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConOpenMenuLoadsSelectedScriptFileLikeOriginal()
    {
        var tempPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "easycon-open-script.ecs");
        File.WriteAllText(tempPath, "PRINT \"opened\"" + Environment.NewLine + "WAIT 1");

        try
        {
            using var form = new MainForm();
            var easyCon = FindControlByType(form, "EasyConTabControl");
            var editor = FindControl(form, "easyConScriptEditor");
            var title = FindControl(form, "scriptTitleLabel");
            var menu = FindControl(form, "easyConOriginalMenu");
            var status = FindControl(form, "easyConStatusStrip");

            SetField(easyCon, "chooseOpenScriptPath", new Func<string?>(() => tempPath));
            SetField(easyCon, "confirmSaveModifiedScript", new Func<System.Windows.Forms.DialogResult>(() => System.Windows.Forms.DialogResult.No));
            SetProperty(editor, "Text", "PRINT \"old\"");
            SetProperty(title, "Text", "old.ecs");
            InvokeClick(FindToolStripItem(menu, "menuItemOpen"));

            Assert.Multiple(() =>
            {
                Assert.That(GetProperty<string>(editor, "Text"), Is.EqualTo("PRINT \"opened\"" + Environment.NewLine + "WAIT 1"));
                Assert.That(GetProperty<string>(title, "Text"), Is.EqualTo(Path.GetFileName(tempPath)));
                Assert.That(GetProperty<string>(FindToolStripItem(status, "toolStripStatusLabel1"), "Text"), Is.EqualTo("文件已打开"));
            });
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConSaveMenuWritesCurrentScriptFileLikeOriginal()
    {
        var tempPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "easycon-save-script.ecs");
        File.WriteAllText(tempPath, "PRINT \"old\"");

        try
        {
            using var form = new MainForm();
            var easyCon = FindControlByType(form, "EasyConTabControl");
            var editor = FindControl(form, "easyConScriptEditor");
            var menu = FindControl(form, "easyConOriginalMenu");
            var status = FindControl(form, "easyConStatusStrip");

            SetField(easyCon, "chooseOpenScriptPath", new Func<string?>(() => tempPath));
            InvokeClick(FindToolStripItem(menu, "menuItemOpen"));
            SetProperty(editor, "Text", "PRINT \"saved\"");
            InvokeClick(FindToolStripItem(menu, "menuItemSave"));

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(tempPath), Is.EqualTo("PRINT \"saved\""));
                Assert.That(GetProperty<string>(FindToolStripItem(status, "toolStripStatusLabel1"), "Text"), Is.EqualTo("文件已保存"));
            });
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConSaveAsMenuWritesSelectedScriptFileLikeOriginal()
    {
        var tempPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "easycon-save-as-script.ecs");
        File.Delete(tempPath);

        try
        {
            using var form = new MainForm();
            var easyCon = FindControlByType(form, "EasyConTabControl");
            var editor = FindControl(form, "easyConScriptEditor");
            var title = FindControl(form, "scriptTitleLabel");
            var menu = FindControl(form, "easyConOriginalMenu");
            var status = FindControl(form, "easyConStatusStrip");

            SetField(easyCon, "chooseSaveScriptPath", new Func<string?>(() => tempPath));
            SetProperty(editor, "Text", "PRINT \"save as\"");
            SetProperty(title, "Text", "old.ecs");
            InvokeClick(FindToolStripItem(menu, "menuItemSaveAs"));

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(tempPath), Is.EqualTo("PRINT \"save as\""));
                Assert.That(GetProperty<string>(title, "Text"), Is.EqualTo(Path.GetFileName(tempPath)));
                Assert.That(GetProperty<string>(FindToolStripItem(status, "toolStripStatusLabel1"), "Text"), Is.EqualTo("文件已保存"));
            });
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConCloseMenuClearsScriptAndShowsOriginalStatus()
    {
        using var form = new MainForm();
        var editor = FindControl(form, "easyConScriptEditor");
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var menu = FindControl(form, "easyConOriginalMenu");
        var status = FindControl(form, "easyConStatusStrip");

        SetField(easyCon, "confirmSaveModifiedScript", new Func<System.Windows.Forms.DialogResult>(() => System.Windows.Forms.DialogResult.No));
        SetProperty(editor, "Text", "PRINT \"old\"");
        InvokeClick(FindToolStripItem(menu, "menuItemClose"));

        Assert.Multiple(() =>
        {
            Assert.That(GetProperty<string>(editor, "Text"), Is.Empty);
            Assert.That(GetProperty<string>(FindToolStripItem(status, "toolStripStatusLabel1"), "Text"), Is.EqualTo("文件已关闭"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConCloseMenuCancelKeepsModifiedScriptLikeOriginal()
    {
        var tempPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "easycon-close-cancel-script.ecs");
        File.WriteAllText(tempPath, "PRINT \"old\"");

        try
        {
            using var form = new MainForm();
            var easyCon = FindControlByType(form, "EasyConTabControl");
            var editor = FindControl(form, "easyConScriptEditor");
            var title = FindControl(form, "scriptTitleLabel");
            var menu = FindControl(form, "easyConOriginalMenu");
            var status = FindControl(form, "easyConStatusStrip");

            SetField(easyCon, "chooseOpenScriptPath", new Func<string?>(() => tempPath));
            InvokeClick(FindToolStripItem(menu, "menuItemOpen"));
            SetField(easyCon, "confirmSaveModifiedScript", new Func<System.Windows.Forms.DialogResult>(() => System.Windows.Forms.DialogResult.Cancel));
            SetProperty(editor, "Text", "PRINT \"changed\"");
            InvokeClick(FindToolStripItem(menu, "menuItemClose"));

            Assert.Multiple(() =>
            {
                Assert.That(GetProperty<string>(editor, "Text"), Is.EqualTo("PRINT \"changed\""));
                Assert.That(GetProperty<string>(title, "Text"), Is.EqualTo(Path.GetFileName(tempPath)));
                Assert.That(GetProperty<string>(FindToolStripItem(status, "toolStripStatusLabel1"), "Text"), Is.EqualTo("文件已打开"));
            });
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConExitMenuClosesParentFormLikeOriginal()
    {
        using var form = new MainForm();
        form.Show();
        var menu = FindControl(form, "easyConOriginalMenu");

        InvokeClick(FindToolStripItem(menu, "menuItemExit"));

        Assert.That(GetProperty<bool>(form, "IsDisposed"), Is.True);
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConWindowCloseCancelKeepsParentFormOpenLikeOriginal()
    {
        var tempPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "easycon-window-close-cancel-script.ecs");
        File.WriteAllText(tempPath, "PRINT \"old\"");

        try
        {
            using var form = new MainForm();
            form.Show();
            var easyCon = FindControlByType(form, "EasyConTabControl");
            var editor = FindControl(form, "easyConScriptEditor");
            var menu = FindControl(form, "easyConOriginalMenu");

            SetField(easyCon, "chooseOpenScriptPath", new Func<string?>(() => tempPath));
            InvokeClick(FindToolStripItem(menu, "menuItemOpen"));
            SetField(easyCon, "confirmSaveModifiedScript", new Func<System.Windows.Forms.DialogResult>(() => System.Windows.Forms.DialogResult.Cancel));
            SetProperty(editor, "Text", "PRINT \"changed\"");

            form.Close();

            Assert.That(GetProperty<bool>(form, "IsDisposed"), Is.False);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConToggleCommentMenuCommentsSelectedLine()
    {
        using var form = new MainForm();
        var editor = FindControl(form, "easyConScriptEditor");
        var menu = FindControl(form, "easyConOriginalMenu");

        SetProperty(editor, "Text", "PRINT \"hello\"" + Environment.NewLine + "WAIT 10");
        SetProperty(editor, "SelectionStart", "PRINT \"hello\"".Length + Environment.NewLine.Length);
        SetProperty(editor, "SelectionLength", 0);
        InvokeClick(FindToolStripItem(menu, "menuItemToggleComment"));

        Assert.That(GetProperty<string>(editor, "Text"), Is.EqualTo("PRINT \"hello\"" + Environment.NewLine + "# WAIT 10"));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConTabRestoresOriginalContentShell()
    {
        using var form = new MainForm();

        var mainSplit = FindControl(form, "mainSplit");
        var contentPanel = FindControl(form, "contentPanel");
        var editorHost = FindControl(form, "editorHost");
        var logPanel = FindControl(form, "logPanel");
        var clearLog = FindControl(form, "clsLogBtn");
        var logText = FindControl(form, "logTxtBox");
        var burnPanel = FindControl(form, "burnPanel");
        var settingsPanel = FindControl(form, "settingsPanel");
        var title = FindControl(form, "scriptTitleLabel");

        Assert.Multiple(() =>
        {
            Assert.That(mainSplit.GetType().Name, Is.EqualTo("SplitContainer"));
            Assert.That(GetProperty<Color>(mainSplit, "BackColor"), Is.EqualTo(Color.FromArgb(230, 229, 224)));
            Assert.That(GetProperty<Color>(contentPanel, "BackColor"), Is.EqualTo(Color.FromArgb(242, 241, 237)));
            Assert.That(GetProperty<string>(title, "Text"), Is.EqualTo("未命名脚本"));
            Assert.That(GetProperty<bool>(title, "Visible"), Is.False);
            Assert.That(GetProperty<bool>(editorHost, "Visible"), Is.False);
            Assert.That(GetProperty<object>(logPanel, "Dock").ToString(), Is.EqualTo("Fill"));
            Assert.That(GetProperty<string>(clearLog, "AccessibleName"), Is.EqualTo("清除日志输出"));
            Assert.That(GetProperty<Color>(logText, "BackColor"), Is.EqualTo(Color.FromArgb(64, 64, 64)));
            Assert.That(GetProperty<Color>(logText, "ForeColor"), Is.EqualTo(Color.White));
            Assert.That(GetProperty<bool>(burnPanel, "Visible"), Is.False);
            Assert.That(GetProperty<bool>(settingsPanel, "Visible"), Is.False);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConTabRestoresOriginalBurnPanelControls()
    {
        using var form = new MainForm();

        var burnGroup = FindControl(form, "grpBurn");
        var remoteStart = FindControl(form, "btnRemoteStart");
        var remoteStop = FindControl(form, "btnRemoteStop");
        var flash = FindControl(form, "btnFlash");
        var clear = FindControl(form, "btnFlashClear");
        var firmwareGroup = FindControl(form, "grpFirmware");
        var boardType = FindControl(form, "comboBoardType");
        var generateFirmware = FindControl(form, "btnGenFirmware");

        Assert.Multiple(() =>
        {
            Assert.That(GetProperty<string>(burnGroup, "Text"), Is.EqualTo("烧录"));
            Assert.That(GetProperty<string>(remoteStart, "Text"), Is.EqualTo("远程运行"));
            Assert.That(GetProperty<string>(remoteStop, "Text"), Is.EqualTo("远程停止"));
            Assert.That(GetProperty<string>(flash, "Text"), Is.EqualTo("编译烧录"));
            Assert.That(GetProperty<string>(clear, "Text"), Is.EqualTo("清除烧录"));
            Assert.That(GetProperty<string>(firmwareGroup, "Text"), Is.EqualTo("固件"));
            Assert.That(boardType.GetType().Name, Is.EqualTo("ComboBox"));
            Assert.That(GetProperty<object>(boardType, "DropDownStyle").ToString(), Is.EqualTo("DropDownList"));
            Assert.That(GetProperty<string>(generateFirmware, "Text"), Is.EqualTo("生成固件"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConFirmwareBoardComboUsesOriginalBoardList()
    {
        using var form = new MainForm();

        var boardType = FindControl(form, "comboBoardType");

        Assert.Multiple(() =>
        {
            Assert.That(
                GetComboBoxItemTexts(boardType),
                Is.EqualTo(new[] { "Leonardo", "Teensy 2.0", "Teensy 2.0++", "Beetle", "Arduino UNO R3" }));
            Assert.That(GetProperty<string>(GetProperty<object>(boardType, "SelectedItem"), "DisplayName"), Is.EqualTo("Leonardo"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConGenerateFirmwareRequiresOriginalBoardSelection()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var boardType = FindControl(form, "comboBoardType");
        var messages = new List<(string Title, string Message)>();

        SetProperty(boardType, "SelectedIndex", -1);
        SetField(easyCon, "showEasyConMessage", new Action<string, string>((title, message) => messages.Add((title, message))));

        InvokeClick(FindControl(form, "btnGenFirmware"));

        Assert.That(messages, Is.EqualTo(new[] { (string.Empty, "请先选择板型") }));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConGenerateFirmwareShowsOriginalCompileError()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var editor = FindControl(form, "easyConScriptEditor");
        var messages = new List<(string Title, string Message)>();

        SetProperty(editor, "Text", "PRINT");
        SetField(easyCon, "showEasyConMessage", new Action<string, string>((title, message) => messages.Add((title, message))));

        InvokeClick(FindControl(form, "btnGenFirmware"));

        Assert.Multiple(() =>
        {
            Assert.That(messages.Single().Title, Is.EqualTo("编译出错"));
            Assert.That(messages.Single().Message, Is.Not.Empty);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConGenerateFirmwareShowsOriginalAssemblyFailure()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var editor = FindControl(form, "easyConScriptEditor");
        var messages = new List<(string Title, string Message)>();

        SetProperty(editor, "Text", "PRINT \"hello\"");
        SetField(easyCon, "showEasyConMessage", new Action<string, string>((title, message) => messages.Add((title, message))));

        InvokeClick(FindControl(form, "btnGenFirmware"));

        Assert.That(messages, Is.EqualTo(new[] { (string.Empty, "生成固件失败：此版本暂不支持编译") }));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConGenerateFirmwareAcceptsOriginalCaptureExternalVariables()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var editor = FindControl(form, "easyConScriptEditor");
        var messages = new List<(string Title, string Message)>();

        SetProperty(editor, "Text", "PRINT @target");
        SetField(easyCon, "buildCaptureExternalGetters", new Func<IReadOnlyDictionary<string, Func<int>>>(() =>
            new Dictionary<string, Func<int>> { ["target"] = () => 7 }));
        SetField(easyCon, "showEasyConMessage", new Action<string, string>((title, message) => messages.Add((title, message))));

        InvokeClick(FindControl(form, "btnGenFirmware"));

        Assert.That(messages, Is.EqualTo(new[] { (string.Empty, "生成固件失败：此版本暂不支持编译") }));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConGenerateFirmwareUsesOriginalFirmwareAutoRunMenuState()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var editor = FindControl(form, "easyConScriptEditor");
        var menu = FindControl(form, "easyConOriginalMenu");
        var messages = new List<(string Title, string Message)>();
        var autoRunValues = new List<bool>();

        SetProperty(editor, "Text", "PRINT \"hello\"");
        SetProperty(FindToolStripItem(menu, "autoRunAfterFlashMenuItem"), "Checked", false);
        SetField(easyCon, "assembleFirmwareScript", new Func<string, bool, Upstream.EasyConFirmwareAssemblyResult>((_, autoRun) =>
        {
            autoRunValues.Add(autoRun);
            return new Upstream.EasyConFirmwareAssemblyResult(false, [], "此版本暂不支持编译");
        }));
        SetField(easyCon, "showEasyConMessage", new Action<string, string>((title, message) => messages.Add((title, message))));

        InvokeClick(FindControl(form, "btnGenFirmware"));

        Assert.Multiple(() =>
        {
            Assert.That(autoRunValues, Is.EqualTo(new[] { false }));
            Assert.That(messages, Is.EqualTo(new[] { (string.Empty, "生成固件失败：此版本暂不支持编译") }));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConTabRestoresOriginalSettingsPanelControls()
    {
        using var form = new MainForm();

        Assert.Multiple(() =>
        {
            Assert.That(GetProperty<string>(FindControl(form, "lblEditorSettings"), "Text"), Is.EqualTo("编辑器设置"));
            Assert.That(GetProperty<string>(FindControl(form, "chkAutoCompletion"), "Text"), Is.EqualTo("代码自动补全"));
            Assert.That(GetProperty<string>(FindControl(form, "chkFolding"), "Text"), Is.EqualTo("显示代码折叠"));
            Assert.That(GetProperty<string>(FindControl(form, "chkDebugLog"), "Text"), Is.EqualTo("显示调试信息"));
            Assert.That(GetProperty<string>(FindControl(form, "lblRunSettings"), "Text"), Is.EqualTo("运行设置"));
            Assert.That(GetProperty<string>(FindControl(form, "chkAutoRunAfterFlash"), "Text"), Is.EqualTo("烧录后自动运行"));
            Assert.That(GetProperty<string>(FindControl(form, "lblNotifySettings"), "Text"), Is.EqualTo("通知设置"));
            Assert.That(GetProperty<string>(FindControl(form, "btnAlertConfig"), "Text"), Is.EqualTo("推送配置"));
            Assert.That(GetProperty<string>(FindControl(form, "chkAutoSaveLog"), "Text"), Is.EqualTo("自动保存日志"));
            Assert.That(GetProperty<string>(FindControl(form, "lblToolSettings"), "Text"), Is.EqualTo("工具"));
            Assert.That(GetProperty<string>(FindControl(form, "btnESPConfig"), "Text"), Is.EqualTo("ESP32设置"));
            Assert.That(GetProperty<string>(FindControl(form, "btnUnpair"), "Text"), Is.EqualTo("取消蓝牙配对"));
            Assert.That(GetProperty<string>(FindControl(form, "btnDrawingBoard"), "Text"), Is.EqualTo("画图工具"));
            Assert.That(GetProperty<string>(FindControl(form, "btnBluetoothSetting"), "Text"), Is.EqualTo("蓝牙设置"));
            Assert.That(GetProperty<bool>(FindControl(form, "btnBluetoothSetting"), "Visible"), Is.False);
            Assert.That(GetProperty<string>(FindControl(form, "lblAbout"), "Text"), Is.EqualTo("关于"));
            Assert.That(GetProperty<string>(FindControl(form, "lblVersion"), "Text"), Does.StartWith("版本: "));
            Assert.That(GetProperty<string>(FindControl(form, "lblVersion"), "Text"), Does.Not.Contain("--"));
            Assert.That(GetProperty<string>(FindControl(form, "btnCheckUpdate"), "Text"), Is.EqualTo("检查更新"));
            Assert.That(GetProperty<string>(FindControl(form, "btnSource"), "Text"), Is.EqualTo("项目源码"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConSettingsInitializeAndPersistThroughOriginalConfigService()
    {
        using var configRestore = PreserveEasyConConfig(new ConfigState
        {
            EnableAutoCompletion = true,
            ShowControllerHelp = false,
            AutoRunAfterFlash = true,
            AutoSaveLog = true,
        });
        using var form = new MainForm();

        var autoCompletion = FindControl(form, "chkAutoCompletion");
        var folding = FindControl(form, "chkFolding");
        var autoRun = FindControl(form, "chkAutoRunAfterFlash");
        var autoSaveLog = FindControl(form, "chkAutoSaveLog");

        Assert.Multiple(() =>
        {
            Assert.That(GetProperty<bool>(autoCompletion, "Checked"), Is.True);
            Assert.That(GetProperty<bool>(folding, "Checked"), Is.False);
            Assert.That(GetProperty<bool>(autoRun, "Checked"), Is.True);
            Assert.That(GetProperty<bool>(autoSaveLog, "Checked"), Is.True);
        });

        SetProperty(autoCompletion, "Checked", false);
        SetProperty(folding, "Checked", true);
        SetProperty(autoRun, "Checked", false);
        SetProperty(autoSaveLog, "Checked", false);

        var savedConfig = ConfigManager.LoadConfig();

        Assert.Multiple(() =>
        {
            Assert.That(savedConfig.EnableAutoCompletion, Is.False);
            Assert.That(savedConfig.ShowControllerHelp, Is.True);
            Assert.That(savedConfig.AutoRunAfterFlash, Is.False);
            Assert.That(savedConfig.AutoSaveLog, Is.False);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConDebugLogSettingUpdatesOriginalDeviceFlag()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var debugLog = FindControl(form, "chkDebugLog");
        var states = new List<bool>();

        SetField(easyCon, "setDebugLogEnabled", new Action<bool>(states.Add));

        SetProperty(debugLog, "Checked", true);
        SetProperty(debugLog, "Checked", false);

        Assert.That(states, Is.EqualTo(new[] { true, false }));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConDeviceDelegatesUseOriginalDeviceServiceByDefault()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var deviceService = GetField<object>(easyCon, "originalDeviceService");

        var getPorts = GetField<Func<string[]>>(easyCon, "getSerialPortNames");
        var autoConnect = GetField<Func<Task<(bool Success, string? Port)>>>(easyCon, "autoConnectDeviceAsync");
        var manualConnect = GetField<Func<string, Task<bool>>>(easyCon, "manualConnectDeviceAsync");
        var startRecord = GetField<Action>(easyCon, "startRecordDevice");
        var pauseRecord = GetField<Action>(easyCon, "pauseRecordDevice");
        var stopRecord = GetField<Action>(easyCon, "stopRecordDevice");
        var setDebugLog = GetField<Action<bool>>(easyCon, "setDebugLogEnabled");

        setDebugLog(true);

        Assert.Multiple(() =>
        {
            Assert.That(getPorts.Target, Is.SameAs(deviceService));
            Assert.That(getPorts.Method.Name, Is.EqualTo("GetPortNames"));
            Assert.That(autoConnect.Target, Is.SameAs(deviceService));
            Assert.That(autoConnect.Method.Name, Is.EqualTo("AutoConnectAsync"));
            Assert.That(manualConnect.Target, Is.SameAs(deviceService));
            Assert.That(manualConnect.Method.Name, Is.EqualTo("ManualConnectAsync"));
            Assert.That(startRecord.Target, Is.SameAs(deviceService));
            Assert.That(startRecord.Method.Name, Is.EqualTo("StartRecord"));
            Assert.That(pauseRecord.Target, Is.SameAs(deviceService));
            Assert.That(pauseRecord.Method.Name, Is.EqualTo("PauseRecord"));
            Assert.That(stopRecord.Target, Is.SameAs(deviceService));
            Assert.That(stopRecord.Method.Name, Is.EqualTo("StopRecord"));
            Assert.That(GetProperty<bool>(deviceService, "DebugLogEnabled"), Is.True);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConAutoSaveLogSettingUpdatesOriginalConfigFlag()
    {
        using var configRestore = PreserveEasyConConfig(new ConfigState { AutoSaveLog = false });
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var autoSaveLog = FindControl(form, "chkAutoSaveLog");
        var states = new List<bool>();

        SetField(easyCon, "setAutoSaveLogEnabled", new Action<bool>(states.Add));

        SetProperty(autoSaveLog, "Checked", true);
        SetProperty(autoSaveLog, "Checked", false);

        Assert.That(states, Is.EqualTo(new[] { true, false }));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConAutoRunAfterFlashSettingUpdatesOriginalConfigFlag()
    {
        using var configRestore = PreserveEasyConConfig(new ConfigState { AutoRunAfterFlash = false });
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var autoRun = FindControl(form, "chkAutoRunAfterFlash");
        var states = new List<bool>();

        SetField(easyCon, "setAutoRunAfterFlashEnabled", new Action<bool>(states.Add));

        SetProperty(autoRun, "Checked", true);
        SetProperty(autoRun, "Checked", false);

        Assert.That(states, Is.EqualTo(new[] { true, false }));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConAutoRunFirmwareMenuMatchesOriginalDefaultAndDoesNotPersistConfig()
    {
        using var configRestore = PreserveEasyConConfig(new ConfigState { AutoRunAfterFlash = false });
        using var form = new MainForm();
        var menu = FindControl(form, "easyConOriginalMenu");
        var firmwareAutoRun = FindToolStripItem(menu, "autoRunAfterFlashMenuItem");
        var afterFlashAutoRun = FindControl(form, "chkAutoRunAfterFlash");

        Assert.Multiple(() =>
        {
            Assert.That(GetProperty<bool>(firmwareAutoRun, "Checked"), Is.True);
            Assert.That(GetProperty<bool>(afterFlashAutoRun, "Checked"), Is.False);
        });

        InvokeClick(firmwareAutoRun);

        Assert.Multiple(() =>
        {
            Assert.That(GetProperty<bool>(firmwareAutoRun, "Checked"), Is.False);
            Assert.That(GetProperty<bool>(afterFlashAutoRun, "Checked"), Is.False);
            Assert.That(ConfigManager.LoadConfig().AutoRunAfterFlash, Is.False);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConFoldingMenuMatchesOriginalDefaultAndDoesNotPersistConfig()
    {
        using var configRestore = PreserveEasyConConfig(new ConfigState { ShowControllerHelp = false });
        using var form = new MainForm();
        var menu = FindControl(form, "easyConOriginalMenu");
        var foldingMenu = FindToolStripItem(menu, "foldingMenuItem");
        var foldingSetting = FindControl(form, "chkFolding");

        Assert.Multiple(() =>
        {
            Assert.That(GetProperty<bool>(foldingMenu, "Checked"), Is.True);
            Assert.That(GetProperty<bool>(foldingSetting, "Checked"), Is.False);
        });

        InvokeClick(foldingMenu);

        Assert.Multiple(() =>
        {
            Assert.That(GetProperty<bool>(foldingMenu, "Checked"), Is.False);
            Assert.That(GetProperty<bool>(foldingSetting, "Checked"), Is.False);
            Assert.That(ConfigManager.LoadConfig().ShowControllerHelp, Is.False);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConAutoCompletionSettingUpdatesOriginalEditorConfig()
    {
        using var configRestore = PreserveEasyConConfig(new ConfigState { EnableAutoCompletion = false });
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var autoCompletion = FindControl(form, "chkAutoCompletion");
        var states = new List<bool>();

        SetField(easyCon, "setAutoCompletionEnabled", new Action<bool>(states.Add));

        SetProperty(autoCompletion, "Checked", true);
        SetProperty(autoCompletion, "Checked", false);

        Assert.That(states, Is.EqualTo(new[] { true, false }));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConCodeFoldingSettingUpdatesOriginalEditorConfig()
    {
        using var configRestore = PreserveEasyConConfig(new ConfigState { ShowControllerHelp = true });
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var folding = FindControl(form, "chkFolding");
        var states = new List<bool>();

        SetField(easyCon, "setCodeFoldingEnabled", new Action<bool>(states.Add));

        SetProperty(folding, "Checked", false);
        SetProperty(folding, "Checked", true);

        Assert.That(states, Is.EqualTo(new[] { false, true }));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConDarkModeMenuInitializesOriginalThemeAndBroadcastsChanges()
    {
        using var configRestore = PreserveEasyConConfig(new ConfigState { DarkMode = true });
        ThemeManager.Init(false);
        var themeChanges = new List<bool>();
        void OnThemeChanged(bool isDark) => themeChanges.Add(isDark);

        ThemeManager.ThemeChanged += OnThemeChanged;
        try
        {
            using var form = new MainForm();
            var menu = FindControl(form, "easyConOriginalMenu");
            var darkMode = FindToolStripItem(menu, "darkModeMenuItem");

            Assert.Multiple(() =>
            {
                Assert.That(GetProperty<bool>(darkMode, "Checked"), Is.True);
                Assert.That(ThemeManager.IsDark, Is.True);
            });

            InvokeClick(darkMode);

            Assert.Multiple(() =>
            {
                Assert.That(GetProperty<bool>(darkMode, "Checked"), Is.False);
                Assert.That(ConfigManager.LoadConfig().DarkMode, Is.False);
                Assert.That(ThemeManager.IsDark, Is.False);
                Assert.That(themeChanges, Is.EqualTo(new[] { false }));
            });
        }
        finally
        {
            ThemeManager.ThemeChanged -= OnThemeChanged;
            ThemeManager.Init(false);
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConTabRestoresOriginalRunAndDeviceControls()
    {
        using var form = new MainForm();

        var scriptGroup = FindControl(form, "grpScriptRun");
        var runStop = FindControl(form, "runStopBtn");
        var format = FindControl(form, "formatBtn");
        var timer = FindControl(form, "timerLabel");
        var deviceGroup = FindControl(form, "grpDevice");
        var combo = FindControl(form, "comboComPort");
        var autoConnect = FindControl(form, "btnAutoConnect");
        var manualConnect = FindControl(form, "btnManualConnect");

        Assert.Multiple(() =>
        {
            Assert.That(scriptGroup.GetType().Name, Is.EqualTo("GroupBox"));
            Assert.That(GetProperty<string>(scriptGroup, "Text"), Is.EqualTo("脚本运行"));
            Assert.That(GetProperty<string>(runStop, "Text"), Is.EqualTo("运行脚本"));
            Assert.That(GetProperty<Color>(runStop, "BackColor"), Is.EqualTo(Color.FromArgb(31, 138, 101)));
            Assert.That(GetProperty<bool>(runStop, "UseVisualStyleBackColor"), Is.False);
            Assert.That(GetProperty<string>(format, "Text"), Is.EqualTo("格式化"));
            Assert.That(GetProperty<string>(timer, "Text"), Is.EqualTo("00:00:00"));
            Assert.That(GetProperty<string>(deviceGroup, "Text"), Is.EqualTo("设备连接"));
            Assert.That(combo.GetType().Name, Is.EqualTo("ComboBox"));
            Assert.That(GetProperty<string>(autoConnect, "Text"), Is.EqualTo("自动连接"));
            Assert.That(GetProperty<string>(manualConnect, "Text"), Is.EqualTo("手动连接"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConComPortDropDownRefreshesOriginalPortList()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var combo = FindControl(form, "comboComPort");

        SetField(easyCon, "getSerialPortNames", new Func<string[]>(() => ["COM3", "COM9"]));
        InvokeDropDown(combo);

        Assert.That(GetComboBoxItemTexts(combo), Is.EqualTo(new[] { "COM3", "COM9" }));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConAutoConnectFailureShowsOriginalDeviceHelp()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var status = FindControl(form, "easyConStatusStrip");
        var messages = new List<(string Title, string Message)>();
        using var shown = new ManualResetEventSlim();

        SetField(easyCon, "autoConnectDeviceAsync", new Func<Task<(bool Success, string? Port)>>(() => Task.FromResult((false, (string?)null))));
        SetField(easyCon, "getSerialPortNames", new Func<string[]>(() => ["COM3", "COM9"]));
        SetField(easyCon, "showEasyConMessage", new Action<string, string>((title, message) =>
        {
            messages.Add((title, message));
            shown.Set();
        }));

        InvokeClick(FindControl(form, "btnAutoConnect"));

        Assert.Multiple(() =>
        {
            Assert.That(shown.Wait(TimeSpan.FromSeconds(2)), Is.True);
            Assert.That(GetProperty<string>(FindToolStripItem(status, "toolStripStatusLabel1"), "Text"), Is.EqualTo("尝试连接..."));
            Assert.That(messages.Single().Title, Is.EqualTo(string.Empty));
            Assert.That(messages.Single().Message, Does.Contain("找不到设备！请确认："));
            Assert.That(messages.Single().Message, Does.Contain("可用端口：COM3、COM9"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConAutoConnectSuccessSelectsOriginalConnectedPort()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var combo = FindControl(form, "comboComPort");
        var messages = new List<(string Title, string Message)>();

        SetField(easyCon, "autoConnectDeviceAsync", new Func<Task<(bool Success, string? Port)>>(() => Task.FromResult((true, (string?)"COM7"))));
        SetField(easyCon, "showEasyConMessage", new Action<string, string>((title, message) => messages.Add((title, message))));

        InvokeClick(FindControl(form, "btnAutoConnect"));

        Assert.Multiple(() =>
        {
            Assert.That(GetProperty<string>(combo, "Text"), Is.EqualTo("COM7"));
            Assert.That(messages, Is.Empty);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConManualConnectFailureShowsOriginalPortHelp()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var combo = FindControl(form, "comboComPort");
        var status = FindControl(form, "easyConStatusStrip");
        var messages = new List<(string Title, string Message)>();
        var requestedPorts = new List<string>();

        SetProperty(combo, "Text", "COM7");
        SetField(easyCon, "manualConnectDeviceAsync", new Func<string, Task<bool>>(port =>
        {
            requestedPorts.Add(port);
            return Task.FromResult(false);
        }));
        SetField(easyCon, "showEasyConMessage", new Action<string, string>((title, message) => messages.Add((title, message))));

        InvokeClick(FindControl(form, "btnManualConnect"));

        Assert.Multiple(() =>
        {
            Assert.That(requestedPorts, Is.EqualTo(new[] { "COM7" }));
            Assert.That(GetProperty<string>(FindToolStripItem(status, "toolStripStatusLabel1"), "Text"), Is.EqualTo("尝试连接..."));
            Assert.That(messages.Single().Title, Is.EqualTo("连接失败"));
            Assert.That(messages.Single().Message, Does.Contain("连接失败！端口 COM7 不存在、无法使用或已被占用。"));
            Assert.That(messages.Single().Message, Does.Contain("请在设备管理器确认 TTL 所在串口正确识别。关闭其他占用USB的程序，并重启软件再试。"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConTabRestoresOriginalCaptureRecordAndControllerControls()
    {
        using var form = new MainForm();

        var videoGroup = FindControl(form, "grpVideoSource");
        var videoSource = FindControl(form, "comboVideoSource");
        var captureToggle = FindControl(form, "btnCaptureToggle");
        var captureConsole = FindControl(form, "btnOpenCaptureConsole");
        var recordGroup = FindControl(form, "grpRecord");
        var record = FindControl(form, "btnRecord");
        var recordPause = FindControl(form, "btnRecordPause");
        var controllerGroup = FindControl(form, "grpController");
        var showController = FindControl(form, "btnShowController");
        var keyMapping = FindControl(form, "btnKeyMapping");

        Assert.Multiple(() =>
        {
            Assert.That(GetProperty<string>(videoGroup, "Text"), Is.EqualTo("视频源"));
            Assert.That(videoSource.GetType().Name, Is.EqualTo("ComboBox"));
            Assert.That(GetProperty<string>(captureToggle, "Text"), Is.EqualTo("连接视频源"));
            Assert.That(GetProperty<string>(captureConsole, "Text"), Is.EqualTo("搜图控制台"));
            Assert.That(GetProperty<string>(recordGroup, "Text"), Is.EqualTo("录制"));
            Assert.That(GetProperty<string>(record, "Text"), Is.EqualTo("录制脚本"));
            Assert.That(GetProperty<string>(recordPause, "Text"), Is.EqualTo("暂停录制"));
            Assert.That(GetProperty<bool>(recordPause, "Enabled"), Is.False);
            Assert.That(GetProperty<string>(controllerGroup, "Text"), Is.EqualTo("手柄"));
            Assert.That(GetProperty<string>(showController, "Text"), Is.EqualTo("虚拟手柄"));
            Assert.That(GetProperty<string>(keyMapping, "Text"), Is.EqualTo("按键映射"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConVideoSourceDropDownRefreshesOriginalSourceList()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var combo = FindControl(form, "comboVideoSource");

        SetField(easyCon, "getVideoSources", new Func<IReadOnlyList<(string Name, int Index)>>(() => [("OBS Virtual Camera", 0), ("Capture Card", 1)]));
        InvokeDropDown(combo);

        Assert.That(GetComboBoxItemTexts(combo), Is.EqualTo(new[] { "OBS Virtual Camera", "Capture Card" }));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConCaptureDelegatesUseOriginalCaptureServiceByDefault()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var captureService = GetField<CaptureService>(easyCon, "originalCaptureService");

        var getSources = GetField<Func<IReadOnlyList<(string Name, int Index)>>>(easyCon, "getVideoSources");
        var connect = GetField<Func<int, bool>>(easyCon, "connectCaptureSource");
        var disconnect = GetField<Action>(easyCon, "disconnectCaptureSource");
        var openConsole = GetField<Action>(easyCon, "openCaptureConsole");

        Assert.Multiple(() =>
        {
            Assert.That(getSources.Target, Is.SameAs(easyCon));
            Assert.That(getSources.Method.Name, Is.EqualTo("GetOriginalVideoSources"));
            Assert.That(connect.Target, Is.SameAs(easyCon));
            Assert.That(connect.Method.Name, Is.EqualTo("ConnectOriginalCaptureSource"));
            Assert.That(disconnect.Target, Is.SameAs(captureService));
            Assert.That(disconnect.Method.Name, Is.EqualTo("Disconnect"));
            Assert.That(openConsole.Target, Is.SameAs(captureService));
            Assert.That(openConsole.Method.Name, Is.EqualTo("ShowCaptureConsole"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConCaptureToggleShowsOriginalConnectedStateWhenSourceConnects()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var combo = FindControl(form, "comboVideoSource");
        var captureToggle = FindControl(form, "btnCaptureToggle");
        var captureStatus = FindToolStripItem(FindControl(form, "easyConStatusStrip"), "labelCaptureStatus");
        var connectedSources = new List<int>();

        SetField(easyCon, "getVideoSources", new Func<IReadOnlyList<(string Name, int Index)>>(() => [("OBS Virtual Camera", 0), ("Capture Card", 1)]));
        SetField(easyCon, "connectCaptureSource", new Func<int, bool>(index =>
        {
            connectedSources.Add(index);
            return true;
        }));
        InvokeDropDown(combo);
        SetProperty(combo, "SelectedIndex", 1);

        InvokeClick(captureToggle);

        Assert.Multiple(() =>
        {
            Assert.That(connectedSources, Is.EqualTo(new[] { 1 }));
            Assert.That(GetProperty<string>(captureStatus, "Text"), Is.EqualTo("采集卡已连接"));
            Assert.That(GetProperty<string>(captureToggle, "Text"), Is.EqualTo("断开视频源"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConCaptureToggleShowsOriginalDisconnectedStateWhenAlreadyConnected()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var combo = FindControl(form, "comboVideoSource");
        var captureToggle = FindControl(form, "btnCaptureToggle");
        var captureStatus = FindToolStripItem(FindControl(form, "easyConStatusStrip"), "labelCaptureStatus");
        var disconnectCalls = 0;

        SetField(easyCon, "getVideoSources", new Func<IReadOnlyList<(string Name, int Index)>>(() => [("OBS Virtual Camera", 0)]));
        SetField(easyCon, "connectCaptureSource", new Func<int, bool>(_ => true));
        SetField(easyCon, "disconnectCaptureSource", new Action(() => disconnectCalls++));
        InvokeDropDown(combo);
        SetProperty(combo, "SelectedIndex", 0);
        InvokeClick(captureToggle);

        InvokeClick(captureToggle);

        Assert.Multiple(() =>
        {
            Assert.That(disconnectCalls, Is.EqualTo(1));
            Assert.That(GetProperty<string>(captureStatus, "Text"), Is.EqualTo("采集卡未连接"));
            Assert.That(GetProperty<string>(captureToggle, "Text"), Is.EqualTo("连接视频源"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConCaptureConsoleButtonUsesOriginalOpenAction()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var opened = false;

        SetField(easyCon, "openCaptureConsole", new Action(() => opened = true));
        InvokeClick(FindControl(form, "btnOpenCaptureConsole"));

        Assert.That(opened, Is.True);
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConCaptureConsoleButtonShowsOriginalDisconnectedStatusByDefault()
    {
        using var form = new MainForm();
        var status = FindControl(form, "easyConStatusStrip");

        InvokeClick(FindControl(form, "btnOpenCaptureConsole"));

        Assert.That(GetProperty<string>(FindToolStripItem(status, "toolStripStatusLabel1"), "Text"), Is.EqualTo("请先连接视频源"));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConUtilityEntrypointsUseOriginalDialogActions()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var menu = FindControl(form, "easyConOriginalMenu");
        var opened = new List<string>();

        SetField(easyCon, "openScriptSyntaxHelp", new Action(() => opened.Add("script-syntax")));
        SetField(easyCon, "openAlertConfigDialog", new Action(() => opened.Add("alert-config")));
        SetField(easyCon, "openEspConfigDialog", new Action(() => opened.Add("esp-config")));
        SetField(easyCon, "openDrawingBoard", new Action(() => opened.Add("drawing-board")));
        SetField(easyCon, "openBluetoothSettingDialog", new Action(() => opened.Add("bluetooth-setting")));
        SetField(easyCon, "openKeyMappingDialog", new Action(() => opened.Add("key-mapping")));

        InvokeClick(FindToolStripItem(menu, "menuItemScriptSyntax"));
        InvokeClick(FindControl(form, "btnAlertConfig"));
        InvokeClick(FindControl(form, "btnESPConfig"));
        InvokeClick(FindControl(form, "btnDrawingBoard"));
        InvokeClick(FindControl(form, "btnBluetoothSetting"));
        InvokeClick(FindControl(form, "btnKeyMapping"));

        Assert.That(opened, Is.EqualTo(new[] { "script-syntax", "alert-config", "esp-config", "drawing-board", "bluetooth-setting", "key-mapping" }));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConAlertConfigButtonOpensOriginalFormByDefault()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var messages = new List<(string Title, string Message)>();
        var existingForms = Application.OpenForms.Cast<Form>().ToHashSet();
        string? openedFormName = null;
        string? openedFormText = null;

        SetField(easyCon, "showEasyConMessage", new Action<string, string>((title, message) => messages.Add((title, message))));

        using var closeDialogTimer = new System.Windows.Forms.Timer { Interval = 25 };
        closeDialogTimer.Tick += (_, _) =>
        {
            foreach (var candidate in Application.OpenForms.Cast<Form>().Where(openForm => !existingForms.Contains(openForm)).ToArray())
            {
                if (candidate.Name != "AlertConfigForm")
                {
                    continue;
                }

                openedFormName = candidate.Name;
                openedFormText = candidate.Text;
                candidate.DialogResult = DialogResult.Cancel;
                candidate.Close();
            }
        };

        try
        {
            closeDialogTimer.Start();
            InvokeClick(FindControl(form, "btnAlertConfig"));
            closeDialogTimer.Stop();

            Assert.Multiple(() =>
            {
                Assert.That(messages, Is.Empty);
                Assert.That(openedFormName, Is.EqualTo("AlertConfigForm"));
                Assert.That(openedFormText, Is.EqualTo("推送配置"));
            });
        }
        finally
        {
            closeDialogTimer.Stop();
            foreach (var candidate in Application.OpenForms.Cast<Form>().Where(openForm => !existingForms.Contains(openForm)).ToArray())
            {
                candidate.Close();
            }
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConEspConfigButtonOpensOriginalFormByDefault()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var messages = new List<(string Title, string Message)>();
        var existingForms = Application.OpenForms.Cast<Form>().ToHashSet();
        string? openedFormName = null;
        string? openedFormText = null;

        SetField(easyCon, "showEasyConMessage", new Action<string, string>((title, message) => messages.Add((title, message))));

        using var closeDialogTimer = new System.Windows.Forms.Timer { Interval = 25 };
        closeDialogTimer.Tick += (_, _) =>
        {
            foreach (var candidate in Application.OpenForms.Cast<Form>().Where(openForm => !existingForms.Contains(openForm)).ToArray())
            {
                if (candidate.Name != "ESPConfig")
                {
                    continue;
                }

                openedFormName = candidate.Name;
                openedFormText = candidate.Text;
                candidate.DialogResult = DialogResult.Cancel;
                candidate.Close();
            }
        };

        try
        {
            closeDialogTimer.Start();
            InvokeClick(FindControl(form, "btnESPConfig"));
            closeDialogTimer.Stop();

            Assert.Multiple(() =>
            {
                Assert.That(messages, Is.Empty);
                Assert.That(openedFormName, Is.EqualTo("ESPConfig"));
                Assert.That(openedFormText, Is.EqualTo("手柄设置"));
            });
        }
        finally
        {
            closeDialogTimer.Stop();
            foreach (var candidate in Application.OpenForms.Cast<Form>().Where(openForm => !existingForms.Contains(openForm)).ToArray())
            {
                candidate.Close();
            }
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConDrawingBoardButtonOpensOriginalFormByDefault()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var deviceService = GetField<DeviceService>(easyCon, "originalDeviceService");
        var expectedDevice = GetProperty<object>(deviceService, "Device");
        var messages = new List<(string Title, string Message)>();
        var existingForms = Application.OpenForms.Cast<Form>().ToHashSet();
        Form? drawingBoard = null;

        SetField(easyCon, "showEasyConMessage", new Action<string, string>((title, message) => messages.Add((title, message))));

        try
        {
            InvokeClick(FindControl(form, "btnDrawingBoard"));
            drawingBoard = Application.OpenForms
                .Cast<Form>()
                .SingleOrDefault(openForm => !existingForms.Contains(openForm) && openForm.Name == "DrawingBoard");

            Assert.Multiple(() =>
            {
                Assert.That(messages, Is.Empty);
                Assert.That(drawingBoard, Is.Not.Null);
                Assert.That(drawingBoard!.Text, Is.EqualTo("画板"));
                Assert.That(GetField<object>(drawingBoard, "NS"), Is.SameAs(expectedDevice));
            });
        }
        finally
        {
            drawingBoard?.Close();
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConMouseJoystickMenuOpensOriginalFormWithDevice()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var deviceService = GetField<DeviceService>(easyCon, "originalDeviceService");
        var expectedDevice = GetProperty<object>(deviceService, "Device");
        var menu = FindControl(form, "easyConOriginalMenu");
        var messages = new List<(string Title, string Message)>();
        var existingForms = Application.OpenForms.Cast<Form>().ToHashSet();
        Form? mouseForm = null;

        SetField(easyCon, "showEasyConMessage", new Action<string, string>((title, message) => messages.Add((title, message))));

        try
        {
            InvokeClick(FindToolStripItem(menu, "mouseJoystickMenuItem"));
            mouseForm = Application.OpenForms
                .Cast<Form>()
                .SingleOrDefault(openForm => !existingForms.Contains(openForm) && openForm.Name == "Mouse");

            Assert.Multiple(() =>
            {
                Assert.That(messages, Is.Empty);
                Assert.That(mouseForm, Is.Not.Null);
                Assert.That(mouseForm!.Text, Is.EqualTo("Mouse"));
                Assert.That(GetField<object>(mouseForm, "NS"), Is.SameAs(expectedDevice));
            });
        }
        finally
        {
            mouseForm?.Close();
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConBluetoothSettingButtonOpensOriginalFormByDefault()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var messages = new List<(string Title, string Message)>();
        var existingForms = Application.OpenForms.Cast<Form>().ToHashSet();
        string? openedFormName = null;
        string? openedFormText = null;

        SetField(easyCon, "showEasyConMessage", new Action<string, string>((title, message) => messages.Add((title, message))));

        using var closeDialogTimer = new System.Windows.Forms.Timer { Interval = 25 };
        closeDialogTimer.Tick += (_, _) =>
        {
            foreach (var candidate in Application.OpenForms.Cast<Form>().Where(openForm => !existingForms.Contains(openForm)).ToArray())
            {
                if (candidate.Name != "BTDeviceForm")
                {
                    continue;
                }

                openedFormName = candidate.Name;
                openedFormText = candidate.Text;
                candidate.DialogResult = DialogResult.Cancel;
                candidate.Close();
            }
        };

        try
        {
            closeDialogTimer.Start();
            InvokeClick(FindControl(form, "btnBluetoothSetting"));
            closeDialogTimer.Stop();

            Assert.Multiple(() =>
            {
                Assert.That(messages, Is.Empty);
                Assert.That(openedFormName, Is.EqualTo("BTDeviceForm"));
                Assert.That(openedFormText, Is.EqualTo("选择蓝牙设备"));
            });
        }
        finally
        {
            closeDialogTimer.Stop();
            foreach (var candidate in Application.OpenForms.Cast<Form>().Where(openForm => !existingForms.Contains(openForm)).ToArray())
            {
                candidate.Close();
            }
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConUnpairButtonShowsOriginalSuccessStatusWhenConnected()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var status = FindControl(form, "easyConStatusStrip");
        var unpairCalls = 0;

        SetField(easyCon, "isDeviceConnected", new Func<bool>(() => true));
        SetField(easyCon, "unpairDevice", new Func<bool>(() =>
        {
            unpairCalls++;
            return true;
        }));

        InvokeClick(FindControl(form, "btnUnpair"));

        Assert.Multiple(() =>
        {
            Assert.That(unpairCalls, Is.EqualTo(1));
            Assert.That(GetProperty<string>(FindToolStripItem(status, "toolStripStatusLabel1"), "Text"), Is.EqualTo("取消配对成功"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConUnpairButtonShowsOriginalFailureStatusWhenConnected()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var status = FindControl(form, "easyConStatusStrip");

        SetField(easyCon, "isDeviceConnected", new Func<bool>(() => true));
        SetField(easyCon, "unpairDevice", new Func<bool>(() => false));

        InvokeClick(FindControl(form, "btnUnpair"));

        Assert.That(GetProperty<string>(FindToolStripItem(status, "toolStripStatusLabel1"), "Text"), Is.EqualTo("取消配对失败"));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConScriptSyntaxMenuOpensOriginalHelpDialog()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var menu = FindControl(form, "easyConOriginalMenu");
        var messages = new List<(string Title, string Message)>();
        var existingForms = Application.OpenForms.Cast<Form>().ToHashSet();
        Form? helpDialog = null;

        SetField(easyCon, "showEasyConMessage", new Action<string, string>((title, message) => messages.Add((title, message))));

        try
        {
            InvokeClick(FindToolStripItem(menu, "menuItemScriptSyntax"));
            helpDialog = Application.OpenForms
                .Cast<Form>()
                .SingleOrDefault(openForm => !existingForms.Contains(openForm) && openForm.Name == "HelpTxtDialog");
            var helpText = helpDialog is null ? null : FindControl(helpDialog, "textBox1");

            Assert.Multiple(() =>
            {
                Assert.That(messages, Is.Empty);
                Assert.That(helpDialog, Is.Not.Null);
                Assert.That(helpDialog!.Text, Is.EqualTo("帮助说明"));
                Assert.That(GetProperty<string>(helpText!, "Text"), Does.Contain("所有代码不区分大小写"));
                Assert.That(GetProperty<string>(helpText!, "Text"), Does.Contain("语法：PRINT 输出内容"));
            });
        }
        finally
        {
            helpDialog?.Close();
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConCheckUpdateButtonUsesOriginalUpdateChecker()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var messages = new List<(string Title, string Message)>();
        using var shown = new ManualResetEventSlim();

        SetField(easyCon, "checkForUpdateMessageAsync", new Func<Task<string?>>(() => Task.FromResult<string?>("暂时没有发现新版本")));
        SetField(easyCon, "showEasyConMessage", new Action<string, string>((title, message) =>
        {
            messages.Add((title, message));
            shown.Set();
        }));

        InvokeClick(FindControl(form, "btnCheckUpdate"));

        Assert.Multiple(() =>
        {
            Assert.That(shown.Wait(TimeSpan.FromSeconds(2)), Is.True);
            Assert.That(messages, Is.EqualTo(new[] { (string.Empty, "暂时没有发现新版本") }));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConCheckUpdateFailureKeepsOriginalSilentUiBehavior()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var status = FindControl(form, "easyConStatusStrip");
        var statusText = FindToolStripItem(status, "toolStripStatusLabel1");
        var initialStatus = GetProperty<string>(statusText, "Text");
        var messages = new List<(string Title, string Message)>();
        using var attempted = new ManualResetEventSlim();

        SetField(easyCon, "checkForUpdateMessageAsync", new Func<Task<string?>>(() =>
        {
            attempted.Set();
            throw new InvalidOperationException("network unavailable");
        }));
        SetField(easyCon, "showEasyConMessage", new Action<string, string>((title, message) => messages.Add((title, message))));

        InvokeClick(FindControl(form, "btnCheckUpdate"));

        Assert.Multiple(() =>
        {
            Assert.That(attempted.Wait(TimeSpan.FromSeconds(2)), Is.True);
            Assert.That(messages, Is.Empty);
            Assert.That(GetProperty<string>(statusText, "Text"), Is.EqualTo(initialStatus));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConKeyMappingButtonOpensOriginalFormByDefault()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var messages = new List<(string Title, string Message)>();
        var existingForms = Application.OpenForms.Cast<Form>().ToHashSet();
        string? openedFormName = null;
        string? openedFormText = null;

        SetField(easyCon, "showEasyConMessage", new Action<string, string>((title, message) => messages.Add((title, message))));

        using var closeDialogTimer = new System.Windows.Forms.Timer { Interval = 25 };
        closeDialogTimer.Tick += (_, _) =>
        {
            foreach (var candidate in Application.OpenForms.Cast<Form>().Where(openForm => !existingForms.Contains(openForm)).ToArray())
            {
                if (candidate.GetType().Name != "FormKeyMapping")
                {
                    continue;
                }

                openedFormName = candidate.GetType().Name;
                openedFormText = candidate.Text;
                candidate.DialogResult = DialogResult.Cancel;
                candidate.Close();
            }
        };

        closeDialogTimer.Start();
        InvokeClick(FindControl(form, "btnKeyMapping"));
        closeDialogTimer.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(openedFormName, Is.EqualTo("FormKeyMapping"));
            Assert.That(openedFormText, Is.EqualTo("按键设置"));
            Assert.That(messages, Is.Empty);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConDisconnectedDeviceButtonsShowOriginalWarning()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var messages = new List<(string Title, string Message)>();

        SetField(easyCon, "showEasyConMessage", new Action<string, string>((title, message) => messages.Add((title, message))));
        InvokeClick(FindControl(form, "btnRecord"));
        InvokeClick(FindControl(form, "btnShowController"));

        Assert.That(messages.Select(item => item.Message), Is.EqualTo(new[] { "请先连接设备", "请先连接设备" }));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConDisconnectedFlashButtonsShowOriginalWarning()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var messages = new List<(string Title, string Message)>();

        SetField(easyCon, "showEasyConMessage", new Action<string, string>((title, message) => messages.Add((title, message))));
        InvokeClick(FindControl(form, "btnFlash"));
        InvokeClick(FindControl(form, "btnFlashClear"));

        Assert.That(messages.Select(item => item.Message), Is.EqualTo(new[] { "请先连接设备", "请先连接设备" }));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConFlashClearShowsOriginalSuccessStatusWhenConnected()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var status = FindControl(form, "easyConStatusStrip");
        var flashCalls = 0;

        SetField(easyCon, "isDeviceConnected", new Func<bool>(() => true));
        SetField(easyCon, "flashClearDevice", new Func<bool>(() =>
        {
            flashCalls++;
            return true;
        }));

        InvokeClick(FindControl(form, "btnFlashClear"));

        Assert.Multiple(() =>
        {
            Assert.That(flashCalls, Is.EqualTo(1));
            Assert.That(GetProperty<string>(FindToolStripItem(status, "toolStripStatusLabel1"), "Text"), Is.EqualTo("清除烧录成功"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConFlashClearShowsOriginalFailureStatusWhenConnected()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var status = FindControl(form, "easyConStatusStrip");

        SetField(easyCon, "isDeviceConnected", new Func<bool>(() => true));
        SetField(easyCon, "flashClearDevice", new Func<bool>(() => false));

        InvokeClick(FindControl(form, "btnFlashClear"));

        Assert.That(GetProperty<string>(FindToolStripItem(status, "toolStripStatusLabel1"), "Text"), Is.EqualTo("清除烧录失败"));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConFlashShowsOriginalCompileErrorWhenConnected()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var editor = FindControl(form, "easyConScriptEditor");
        var messages = new List<(string Title, string Message)>();

        SetProperty(editor, "Text", "PRINT");
        SetField(easyCon, "isDeviceConnected", new Func<bool>(() => true));
        SetField(easyCon, "getDeviceFirmwareVersion", new Func<int>(() => 0x45));
        SetField(easyCon, "showEasyConMessage", new Action<string, string>((title, message) => messages.Add((title, message))));

        InvokeClick(FindControl(form, "btnFlash"));

        Assert.Multiple(() =>
        {
            Assert.That(messages.Single().Title, Is.EqualTo("编译出错"));
            Assert.That(messages.Single().Message, Is.Not.Empty);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConFlashRequiresOriginalBoardSelectionWhenConnected()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var boardType = FindControl(form, "comboBoardType");
        var messages = new List<(string Title, string Message)>();

        SetProperty(boardType, "SelectedIndex", -1);
        SetField(easyCon, "isDeviceConnected", new Func<bool>(() => true));
        SetField(easyCon, "showEasyConMessage", new Action<string, string>((title, message) => messages.Add((title, message))));

        InvokeClick(FindControl(form, "btnFlash"));

        Assert.That(messages, Is.EqualTo(new[] { (string.Empty, "请先选择板型") }));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConFlashShowsOriginalFirmwareVersionMismatchWhenConnected()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var messages = new List<(string Title, string Message)>();

        SetField(easyCon, "isDeviceConnected", new Func<bool>(() => true));
        SetField(easyCon, "getDeviceFirmwareVersion", new Func<int>(() => 0x44));
        SetField(easyCon, "showEasyConMessage", new Action<string, string>((title, message) => messages.Add((title, message))));

        InvokeClick(FindControl(form, "btnFlash"));

        Assert.That(messages, Is.EqualTo(new[] { (string.Empty, "单片机固件版本不匹配，请先更新固件") }));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConFlashShowsOriginalAssemblyFailureWhenConnected()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var editor = FindControl(form, "easyConScriptEditor");
        var messages = new List<(string Title, string Message)>();

        SetProperty(editor, "Text", "PRINT \"hello\"");
        SetField(easyCon, "isDeviceConnected", new Func<bool>(() => true));
        SetField(easyCon, "getDeviceFirmwareVersion", new Func<int>(() => 0x45));
        SetField(easyCon, "showEasyConMessage", new Action<string, string>((title, message) => messages.Add((title, message))));

        InvokeClick(FindControl(form, "btnFlash"));

        Assert.That(messages, Is.EqualTo(new[] { (string.Empty, "编译结果为空，无法烧录") }));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConFlashShowsOriginalBoardDataSizeLimitWhenConnected()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var editor = FindControl(form, "easyConScriptEditor");
        var boardType = FindControl(form, "comboBoardType");
        var messages = new List<(string Title, string Message)>();

        SetProperty(editor, "Text", "PRINT \"hello\"");
        SetProperty(boardType, "SelectedIndex", 4);
        SetField(easyCon, "isDeviceConnected", new Func<bool>(() => true));
        SetField(easyCon, "getDeviceFirmwareVersion", new Func<int>(() => 0x45));
        SetField(easyCon, "assembleFirmwareScript", new Func<string, bool, Upstream.EasyConFirmwareAssemblyResult>((_, _) =>
            new Upstream.EasyConFirmwareAssemblyResult(true, Enumerable.Repeat<byte>(0x01, 413).ToArray(), null)));
        SetField(easyCon, "showEasyConMessage", new Action<string, string>((title, message) => messages.Add((title, message))));

        InvokeClick(FindControl(form, "btnFlash"));

        Assert.That(messages, Is.EqualTo(new[] { (string.Empty, "脚本编译后 413 字节，超出 Arduino UNO R3 的 412 字节限制") }));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConFlashShowsOriginalSuccessStatusWhenConnected()
    {
        using var configRestore = PreserveEasyConConfig(new ConfigState { AutoRunAfterFlash = false });
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var editor = FindControl(form, "easyConScriptEditor");
        var status = FindControl(form, "easyConStatusStrip");
        var flashed = new List<byte[]>();

        SetProperty(editor, "Text", "PRINT \"hello\"");
        SetField(easyCon, "isDeviceConnected", new Func<bool>(() => true));
        SetField(easyCon, "getDeviceFirmwareVersion", new Func<int>(() => 0x45));
        SetField(easyCon, "assembleFirmwareScript", new Func<string, bool, Upstream.EasyConFirmwareAssemblyResult>((_, _) =>
            new Upstream.EasyConFirmwareAssemblyResult(true, [0x01, 0x02], null)));
        SetField(easyCon, "flashDevice", new Func<IReadOnlyList<byte>, bool>(bytes =>
        {
            flashed.Add(bytes.ToArray());
            return true;
        }));

        InvokeClick(FindControl(form, "btnFlash"));

        Assert.Multiple(() =>
        {
            Assert.That(flashed.Single(), Is.EqualTo(new byte[] { 0x01, 0x02 }));
            Assert.That(GetProperty<string>(FindToolStripItem(status, "toolStripStatusLabel1"), "Text"), Is.EqualTo("烧录成功"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConFlashShowsOriginalFailureStatusWhenConnected()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var editor = FindControl(form, "easyConScriptEditor");
        var status = FindControl(form, "easyConStatusStrip");

        SetProperty(editor, "Text", "PRINT \"hello\"");
        SetField(easyCon, "isDeviceConnected", new Func<bool>(() => true));
        SetField(easyCon, "getDeviceFirmwareVersion", new Func<int>(() => 0x45));
        SetField(easyCon, "assembleFirmwareScript", new Func<string, bool, Upstream.EasyConFirmwareAssemblyResult>((_, _) =>
            new Upstream.EasyConFirmwareAssemblyResult(true, [0x01, 0x02], null)));
        SetField(easyCon, "flashDevice", new Func<IReadOnlyList<byte>, bool>(_ => false));

        InvokeClick(FindControl(form, "btnFlash"));

        Assert.That(GetProperty<string>(FindToolStripItem(status, "toolStripStatusLabel1"), "Text"), Is.EqualTo("烧录失败"));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConFlashAutoRunsAfterOriginalSuccessfulFlashWhenEnabled()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var editor = FindControl(form, "easyConScriptEditor");
        var autoRun = FindControl(form, "chkAutoRunAfterFlash");
        var status = FindControl(form, "easyConStatusStrip");
        var remoteStartCalls = 0;

        SetProperty(editor, "Text", "PRINT \"hello\"");
        SetProperty(autoRun, "Checked", true);
        SetField(easyCon, "isDeviceConnected", new Func<bool>(() => true));
        SetField(easyCon, "getDeviceFirmwareVersion", new Func<int>(() => 0x45));
        SetField(easyCon, "assembleFirmwareScript", new Func<string, bool, Upstream.EasyConFirmwareAssemblyResult>((_, _) =>
            new Upstream.EasyConFirmwareAssemblyResult(true, [0x01, 0x02], null)));
        SetField(easyCon, "flashDevice", new Func<IReadOnlyList<byte>, bool>(_ => true));
        SetField(easyCon, "remoteStartDevice", new Func<bool>(() =>
        {
            remoteStartCalls++;
            return true;
        }));

        InvokeClick(FindControl(form, "btnFlash"));

        Assert.Multiple(() =>
        {
            Assert.That(remoteStartCalls, Is.EqualTo(1));
            Assert.That(GetProperty<string>(FindToolStripItem(status, "toolStripStatusLabel1"), "Text"), Is.EqualTo("烧录成功，已自动运行"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConRemoteStartShowsOriginalSuccessStatusWhenConnected()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var status = FindControl(form, "easyConStatusStrip");
        var startCalls = 0;

        SetField(easyCon, "isDeviceConnected", new Func<bool>(() => true));
        SetField(easyCon, "remoteStartDevice", new Func<bool>(() =>
        {
            startCalls++;
            return true;
        }));

        InvokeClick(FindControl(form, "btnRemoteStart"));

        Assert.Multiple(() =>
        {
            Assert.That(startCalls, Is.EqualTo(1));
            Assert.That(GetProperty<string>(FindToolStripItem(status, "toolStripStatusLabel1"), "Text"), Is.EqualTo("远程运行已开始"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConRemoteStartShowsOriginalFailureStatusWhenConnected()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var status = FindControl(form, "easyConStatusStrip");

        SetField(easyCon, "isDeviceConnected", new Func<bool>(() => true));
        SetField(easyCon, "remoteStartDevice", new Func<bool>(() => false));

        InvokeClick(FindControl(form, "btnRemoteStart"));

        Assert.That(GetProperty<string>(FindToolStripItem(status, "toolStripStatusLabel1"), "Text"), Is.EqualTo("远程运行失败"));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConRemoteStopShowsOriginalSuccessStatusWhenConnected()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var status = FindControl(form, "easyConStatusStrip");
        var stopCalls = 0;

        SetField(easyCon, "isDeviceConnected", new Func<bool>(() => true));
        SetField(easyCon, "remoteStopDevice", new Func<bool>(() =>
        {
            stopCalls++;
            return true;
        }));

        InvokeClick(FindControl(form, "btnRemoteStop"));

        Assert.Multiple(() =>
        {
            Assert.That(stopCalls, Is.EqualTo(1));
            Assert.That(GetProperty<string>(FindToolStripItem(status, "toolStripStatusLabel1"), "Text"), Is.EqualTo("远程停止成功"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConRemoteStopShowsOriginalFailureStatusWhenConnected()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var status = FindControl(form, "easyConStatusStrip");

        SetField(easyCon, "isDeviceConnected", new Func<bool>(() => true));
        SetField(easyCon, "remoteStopDevice", new Func<bool>(() => false));

        InvokeClick(FindControl(form, "btnRemoteStop"));

        Assert.That(GetProperty<string>(FindToolStripItem(status, "toolStripStatusLabel1"), "Text"), Is.EqualTo("远程停止失败"));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConGuardedDeviceActionsShowOriginalWarnings()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var messages = new List<(string Title, string Message)>();

        SetField(easyCon, "showEasyConMessage", new Action<string, string>((title, message) => messages.Add((title, message))));
        InvokeClick(FindControl(form, "btnManualConnect"));
        InvokeClick(FindControl(form, "btnCaptureToggle"));
        InvokeClick(FindControl(form, "btnRemoteStart"));
        InvokeClick(FindControl(form, "btnRemoteStop"));

        Assert.That(
            messages.Select(item => item.Message),
            Is.EqualTo(new[] { "请先选择或输入串口", "请先选择视频源", "请先连接设备", "请先连接设备" }));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConShowControllerOpensOriginalControllerWhenConnected()
    {
        using var configRestore = PreserveEasyConConfig(new ConfigState { ShowControllerHelp = false });
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var messages = new List<(string Title, string Message)>();
        var openCalls = 0;

        SetField(easyCon, "isDeviceConnected", new Func<bool>(() => true));
        SetField(easyCon, "openVirtualController", new Func<bool>(() =>
        {
            openCalls++;
            return true;
        }));
        SetField(easyCon, "showEasyConMessage", new Action<string, string>((title, message) => messages.Add((title, message))));

        InvokeClick(FindControl(form, "btnShowController"));

        Assert.Multiple(() =>
        {
            Assert.That(openCalls, Is.EqualTo(1));
            Assert.That(messages, Is.Empty);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConShowControllerShowsOriginalOneTimeHelp()
    {
        using var configRestore = PreserveEasyConConfig(new ConfigState { ShowControllerHelp = true });
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var existingForms = Application.OpenForms.Cast<Form>().ToHashSet();
        Form? helpDialog = null;

        SetField(easyCon, "isDeviceConnected", new Func<bool>(() => true));
        SetField(easyCon, "openVirtualController", new Func<bool>(() => true));

        try
        {
            InvokeClick(FindControl(form, "btnShowController"));
            helpDialog = Application.OpenForms
                .Cast<Form>()
                .SingleOrDefault(openForm => !existingForms.Contains(openForm) && openForm.Name == "HelpTxtDialog");
            var helpText = helpDialog is null ? null : FindControl(helpDialog, "textBox1");

            Assert.Multiple(() =>
            {
                Assert.That(helpDialog, Is.Not.Null);
                Assert.That(helpDialog!.Text, Is.EqualTo("关于虚拟手柄"));
                Assert.That(GetProperty<string>(helpText!, "Text"), Does.Contain("鼠标左键：启用/禁用"));
                Assert.That(ConfigManager.LoadConfig().ShowControllerHelp, Is.False);
            });
        }
        finally
        {
            helpDialog?.Close();
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConVirtualControllerUsesOriginalVPadServiceByDefault()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var openController = GetField<Func<bool>>(easyCon, "openVirtualController");

        Assert.Multiple(() =>
        {
            Assert.That(openController.Target, Is.SameAs(easyCon));
            Assert.That(openController.Method.Name, Is.EqualTo("OpenOriginalVirtualController"));
            Assert.That(easyCon, Is.AssignableTo<IControllerAdapter>());
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConRecordStartsWhenDeviceConnectedAndControllerBound()
    {
        using var configRestore = PreserveEasyConConfig(new ConfigState { ShowControllerHelp = false });
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var status = FindControl(form, "easyConStatusStrip");
        var editor = FindControl(form, "easyConScriptEditor");
        var record = FindControl(form, "btnRecord");
        var pause = FindControl(form, "btnRecordPause");
        var messages = new List<(string Title, string Message)>();

        SetField(easyCon, "isDeviceConnected", new Func<bool>(() => true));
        SetField(easyCon, "openVirtualController", new Func<bool>(() => true));
        SetField(easyCon, "showEasyConMessage", new Action<string, string>((title, message) => messages.Add((title, message))));

        InvokeClick(FindControl(form, "btnShowController"));
        InvokeClick(record);

        Assert.Multiple(() =>
        {
            Assert.That(GetProperty<string>(record, "Text"), Is.EqualTo("停止录制"));
            Assert.That(GetProperty<bool>(pause, "Enabled"), Is.True);
            Assert.That(GetProperty<bool>(editor, "ReadOnly"), Is.True);
            Assert.That(GetProperty<string>(FindToolStripItem(status, "toolStripStatusLabel1"), "Text"), Is.EqualTo("开始录制"));
            Assert.That(messages, Is.Empty);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConRecordLifecycleInvokesOriginalRecordActions()
    {
        using var configRestore = PreserveEasyConConfig(new ConfigState { ShowControllerHelp = false });
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var record = FindControl(form, "btnRecord");
        var pause = FindControl(form, "btnRecordPause");
        var startCalls = 0;
        var pauseCalls = 0;
        var stopCalls = 0;

        SetField(easyCon, "isDeviceConnected", new Func<bool>(() => true));
        SetField(easyCon, "openVirtualController", new Func<bool>(() => true));
        SetField(easyCon, "startRecordDevice", new Action(() => startCalls++));
        SetField(easyCon, "pauseRecordDevice", new Action(() => pauseCalls++));
        SetField(easyCon, "stopRecordDevice", new Action(() => stopCalls++));

        InvokeClick(FindControl(form, "btnShowController"));
        InvokeClick(record);
        InvokeClick(pause);
        InvokeClick(pause);
        InvokeClick(record);

        Assert.Multiple(() =>
        {
            Assert.That(startCalls, Is.EqualTo(2));
            Assert.That(pauseCalls, Is.EqualTo(1));
            Assert.That(stopCalls, Is.EqualTo(1));
            Assert.That(GetProperty<string>(record, "Text"), Is.EqualTo("录制脚本"));
            Assert.That(GetProperty<string>(pause, "Text"), Is.EqualTo("暂停录制"));
            Assert.That(GetProperty<bool>(pause, "Enabled"), Is.False);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConRecordDoesNotTreatFailedVirtualControllerOpenAsBound()
    {
        using var configRestore = PreserveEasyConConfig(new ConfigState { ShowControllerHelp = false });
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var record = FindControl(form, "btnRecord");
        var pause = FindControl(form, "btnRecordPause");
        var editor = FindControl(form, "easyConScriptEditor");
        var messages = new List<(string Title, string Message)>();

        SetField(easyCon, "isDeviceConnected", new Func<bool>(() => true));
        SetField(easyCon, "openVirtualController", new Func<bool>(() =>
        {
            messages.Add(("虚拟手柄", "虚拟手柄打开失败"));
            return false;
        }));
        SetField(easyCon, "showEasyConMessage", new Action<string, string>((title, message) => messages.Add((title, message))));

        InvokeClick(FindControl(form, "btnShowController"));
        InvokeClick(record);

        Assert.Multiple(() =>
        {
            Assert.That(
                messages,
                Is.EqualTo(new[]
                {
                    ("虚拟手柄", "虚拟手柄打开失败"),
                    (string.Empty, "请先绑定虚拟手柄"),
                }));
            Assert.That(GetProperty<string>(record, "Text"), Is.EqualTo("录制脚本"));
            Assert.That(GetProperty<bool>(pause, "Enabled"), Is.False);
            Assert.That(GetProperty<bool>(editor, "ReadOnly"), Is.False);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConRecordRequiresOriginalVirtualControllerBinding()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var record = FindControl(form, "btnRecord");
        var pause = FindControl(form, "btnRecordPause");
        var messages = new List<(string Title, string Message)>();

        SetField(easyCon, "isDeviceConnected", new Func<bool>(() => true));
        SetField(easyCon, "showEasyConMessage", new Action<string, string>((title, message) => messages.Add((title, message))));

        InvokeClick(record);

        Assert.Multiple(() =>
        {
            Assert.That(messages, Is.EqualTo(new[] { (string.Empty, "请先绑定虚拟手柄") }));
            Assert.That(GetProperty<string>(record, "Text"), Is.EqualTo("录制脚本"));
            Assert.That(GetProperty<bool>(pause, "Enabled"), Is.False);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConSourceButtonOpensOriginalProjectUrl()
    {
        using var form = new MainForm();
        var easyCon = FindControlByType(form, "EasyConTabControl");
        var openedUrls = new List<string>();

        SetField(easyCon, "openExternalLink", new Action<string>(openedUrls.Add));
        InvokeClick(FindControl(form, "btnSource"));

        Assert.That(openedUrls, Is.EqualTo(new[] { "https://github.com/EasyConNS/EasyCon" }));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EasyConTabDoesNotAddAutoSwshRngLinkagePanelYet()
    {
        using var form = new MainForm();

        var labels = GetDescendantTexts(form);

        Assert.Multiple(() =>
        {
            Assert.That(labels, Does.Not.Contain("自动化联动"));
            Assert.That(labels, Does.Not.Contain("从 owoow 生成脚本"));
            Assert.That(labels, Does.Not.Contain("加入自动化流程"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void AutomationFlowTabKeepsWorkflowDesignPlaceholder()
    {
        using var form = new MainForm();

        var placeholder = FindControl(form, "automationFlowPlaceholder");

        Assert.That(GetProperty<string>(placeholder, "Text"), Does.Contain("自动化流程"));
    }

    private static IDisposable PreserveEasyConConfig(ConfigState config)
    {
        var configPath = AppPaths.ConfigFile;
        var previousConfig = File.Exists(configPath) ? File.ReadAllText(configPath) : null;
        ConfigManager.SaveConfig(config);

        return new RestoreAction(() =>
        {
            if (previousConfig is null)
            {
                File.Delete(configPath);
            }
            else
            {
                File.WriteAllText(configPath, previousConfig);
            }
        });
    }

    private static object FindControl(object root, string name)
    {
        var match = FindControlOrDefault(root, name);
        return match ?? throw new InvalidOperationException($"Control '{name}' was not found.");
    }

    private static object? FindControlOrDefault(object root, string name)
    {
        foreach (var child in (IEnumerable)GetProperty<object>(root, "Controls"))
        {
            if (GetProperty<string>(child, "Name") == name)
            {
                return child;
            }

            var match = FindControlOrDefault(child, name);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static object FindControlByType(object root, string typeName)
    {
        var match = FindControlByTypeOrDefault(root, typeName);
        return match ?? throw new InvalidOperationException($"Control type '{typeName}' was not found.");
    }

    private static object? FindControlByTypeOrDefault(object root, string typeName)
    {
        if (root.GetType().Name == typeName)
        {
            return root;
        }

        foreach (var child in (IEnumerable)GetProperty<object>(root, "Controls"))
        {
            var match = FindControlByTypeOrDefault(child, typeName);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static T GetProperty<T>(object target, string propertyName)
    {
        var property = target.GetType()
            .GetProperties()
            .First(candidate => candidate.Name == propertyName && candidate.GetIndexParameters().Length == 0);
        return (T)property.GetValue(target)!;
    }

    private static IReadOnlyCollection<string> GetDescendantTexts(object root)
    {
        var texts = new List<string>();
        AddTexts(root, texts);
        return texts.Where(text => !string.IsNullOrWhiteSpace(text)).ToArray();
    }

    private static IReadOnlyList<string> GetDataGridViewColumnHeaders(object grid)
    {
        var headers = new List<string>();
        foreach (var column in (IEnumerable)GetProperty<object>(grid, "Columns"))
        {
            headers.Add(GetProperty<string>(column, "HeaderText"));
        }

        return headers;
    }

    private static IReadOnlyList<string> GetTabPageTitles(object tabControl)
    {
        var titles = new List<string>();
        foreach (var page in (IEnumerable)GetProperty<object>(tabControl, "TabPages"))
        {
            titles.Add(GetProperty<string>(page, "Text"));
        }

        return titles;
    }

    private static IReadOnlyList<string> GetComboBoxItemTexts(object comboBox)
    {
        var texts = new List<string>();
        foreach (var item in (IEnumerable)GetProperty<object>(comboBox, "Items"))
        {
            texts.Add(item.ToString() ?? string.Empty);
        }

        return texts;
    }

    private static IReadOnlyList<string> GetToolStripItemTexts(object toolStrip)
    {
        var texts = new List<string>();
        foreach (var item in (IEnumerable)GetProperty<object>(toolStrip, "Items"))
        {
            var text = GetProperty<string>(item, "Text");
            if (!string.IsNullOrWhiteSpace(text))
            {
                texts.Add(text);
            }
        }

        return texts;
    }

    private static object FindToolStripItem(object toolStrip, string name)
    {
        var match = FindToolStripItemOrDefault(GetToolStripSearchItems(toolStrip), name);
        return match ?? throw new InvalidOperationException($"ToolStrip item '{name}' was not found.");
    }

    private static IEnumerable GetToolStripSearchItems(object toolStrip)
    {
        return toolStrip.GetType().GetProperty("Items") is not null
            ? (IEnumerable)GetProperty<object>(toolStrip, "Items")
            : (IEnumerable)GetProperty<object>(toolStrip, "DropDownItems");
    }

    private static object? FindToolStripItemOrDefault(IEnumerable items, string name)
    {
        foreach (var item in items)
        {
            if (GetProperty<string>(item, "Name") == name)
            {
                return item;
            }

            if (item.GetType().Name.Contains("Separator", StringComparison.Ordinal))
            {
                continue;
            }

            if (item.GetType().GetProperty("DropDownItems") is not null)
            {
                var match = FindToolStripItemOrDefault((IEnumerable)GetProperty<object>(item, "DropDownItems"), name);
                if (match is not null)
                {
                    return match;
                }
            }
        }

        return null;
    }

    private static IReadOnlyList<string> GetToolStripDropDownItemTexts(object menuItem)
    {
        var texts = new List<string>();
        foreach (var item in (IEnumerable)GetProperty<object>(menuItem, "DropDownItems"))
        {
            var typeName = item.GetType().Name;
            if (typeName.Contains("Separator", StringComparison.Ordinal))
            {
                continue;
            }

            texts.Add(GetProperty<string>(item, "Text"));
        }

        return texts;
    }

    private static void AddTexts(object root, List<string> texts)
    {
        var text = GetProperty<string>(root, "Text");
        if (!string.IsNullOrWhiteSpace(text))
        {
            texts.Add(text);
        }

        foreach (var child in (IEnumerable)GetProperty<object>(root, "Controls"))
        {
            AddTexts(child, texts);
        }
    }

    private static void SetProperty(object target, string propertyName, object value)
    {
        target.GetType().GetProperty(propertyName)!.SetValue(target, value);
    }

    private static void SetField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(
            fieldName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (field is null)
        {
            throw new InvalidOperationException($"Field '{fieldName}' was not found.");
        }

        field.SetValue(target, value);
    }

    private static T GetField<T>(object target, string fieldName)
    {
        var field = target.GetType().GetField(
            fieldName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (field is null)
        {
            throw new InvalidOperationException($"Field '{fieldName}' was not found.");
        }

        return (T)field.GetValue(target)!;
    }

    private static void InvokeClick(object target)
    {
        target.GetType().GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(target, [EventArgs.Empty]);
    }

    private static void InvokeDropDownOpening(object target)
    {
        target.GetType().GetMethod(
                "ShowDropDown",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null)!
            .Invoke(target, []);
    }

    private static void InvokeDropDown(object target)
    {
        target.GetType().GetMethod("OnDropDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(target, [EventArgs.Empty]);
    }

    private sealed class RestoreAction(Action restore) : IDisposable
    {
        public void Dispose()
        {
            restore();
        }
    }
}
