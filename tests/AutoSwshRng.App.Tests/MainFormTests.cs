using AutoSwshRng.App;
using System.Collections;

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
    public void EasyConTabRunsScriptWithoutSerialDevice()
    {
        using var form = new MainForm();

        SetProperty(FindControl(form, "easyConScriptTextBox"), "Text", "PRINT \"world\"");
        InvokeClick(FindControl(form, "easyConRunButton"));

        var output = GetProperty<string>(FindControl(form, "easyConOutputTextBox"), "Text");
        Assert.Multiple(() =>
        {
            Assert.That(output, Does.Contain("errors = False"));
            Assert.That(output, Does.Contain("world"));
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
