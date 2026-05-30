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
            Is.EqualTo(new[] { "概览", "owoow", "伊机控", "自动化流程" }));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void UpstreamTabsShowAdapterSmokeResults()
    {
        using var form = new MainForm();

        Assert.Multiple(() =>
        {
            Assert.That(form.TabBodies["owoow"], Does.Contain("Square"));
            Assert.That(form.TabBodies["伊机控"], Does.Contain("hello"));
            Assert.That(form.TabBodies["概览"], Does.Contain("EasyCon: OK"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowTabCalculatesShinyValue()
    {
        using var form = new MainForm();

        SetProperty(FindControl(form, "owoowTidInput"), "Value", 1m);
        SetProperty(FindControl(form, "owoowSidInput"), "Value", 2m);
        InvokeClick(FindControl(form, "owoowCalculateButton"));

        Assert.That(GetProperty<string>(FindControl(form, "owoowResultLabel"), "Text"), Does.Contain("0x0003"));
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
