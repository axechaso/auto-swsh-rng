using AutoSwshRng.App;

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
        });
    }
}
