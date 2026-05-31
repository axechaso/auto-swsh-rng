using AutoSwshRng.App;
using System.Collections;
using System.Drawing;

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
    public void EasyConTabRestoresOriginalMenuAndPanels()
    {
        using var form = new MainForm();

        var menu = FindControl(form, "easyConOriginalMenu");
        var menuLabels = GetDescendantTexts(menu);

        Assert.Multiple(() =>
        {
            Assert.That(menuLabels, Is.SupersetOf(new[] { "文件", "编辑", "脚本", "搜图", "设置", "蓝牙", "ESP32", "画图", "帮助" }));
            Assert.That(FindControl(form, "easyConScriptEditor"), Is.Not.Null);
            Assert.That(FindControl(form, "easyConLogBox"), Is.Not.Null);
            Assert.That(FindControl(form, "easyConSerialPanel"), Is.Not.Null);
            Assert.That(FindControl(form, "easyConCapturePanel"), Is.Not.Null);
            Assert.That(FindControl(form, "easyConRecordPanel"), Is.Not.Null);
            Assert.That(FindControl(form, "easyConControllerPanel"), Is.Not.Null);
            Assert.That(FindControl(form, "easyConFirmwarePanel"), Is.Not.Null);
            Assert.That(GetDescendantTexts(form), Does.Contain("串口状态: 未连接"));
            Assert.That(GetDescendantTexts(form), Does.Contain("采集状态: 未开启"));
        });
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

    private static void InvokeClick(object target)
    {
        target.GetType().GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(target, [EventArgs.Empty]);
    }
}
