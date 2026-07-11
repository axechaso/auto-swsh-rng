using AutoSwshRng.App.Controls;
using System.Windows.Forms;

namespace AutoSwshRng.App.Tests;

public class OwoowTabControlTests
{
    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowMainUsesPinnedHorizontalToolMenu()
    {
        using var control = new OwoowTabControl();

        var menu = FindControl<MenuStrip>(control, "MS_SubWindows");

        Assert.Multiple(() =>
        {
            Assert.That(menu.Dock, Is.EqualTo(DockStyle.Top));
            Assert.That(
                menu.Items.Cast<ToolStripItem>().Select(item => item.Text),
                Is.EqualTo(new[]
                {
                    "Profiles",
                    "Encounter Lookup",
                    "Spread Finder",
                    "Loto-ID",
                    "Cram-o-matic",
                    "Watt Trader",
                    "Digging Pa",
                    "Digging Bro (Skill)",
                    "Wailord Respawn",
                    "Xoroshiro Tools",
                }));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowMainMatchesPinnedSeedConnectionAndProfileDefaults()
    {
        using var control = new OwoowTabControl();

        Assert.Multiple(() =>
        {
            Assert.That(FindControl<TextBox>(control, "TB_Seed0").Text, Is.EqualTo("0123456789ABCDEF"));
            Assert.That(FindControl<TextBox>(control, "TB_Seed1").Text, Is.EqualTo("0123456789ABCDEF"));
            Assert.That(FindControl<TextBox>(control, "TB_SwitchIP").Text, Is.EqualTo("123.123.123.123"));
            Assert.That(FindControl<Button>(control, "B_Connect").Text, Is.EqualTo("Connect"));
            Assert.That(FindControl<Button>(control, "B_Disconnect").Enabled, Is.False);
            Assert.That(FindControl<TextBox>(control, "TB_TID").Text, Is.EqualTo("12345"));
            Assert.That(FindControl<TextBox>(control, "TB_SID").Text, Is.EqualTo("54321"));
            Assert.That(FindControl<ComboBox>(control, "CB_Game").Items.Cast<object>().Select(value => value.ToString()),
                Is.EqualTo(new[] { "Sword", "Shield" }));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowMainKeepsPinnedEncounterTabsAndRetailDefaults()
    {
        using var control = new OwoowTabControl();

        Assert.Multiple(() =>
        {
            Assert.That(
                FindControl<TabControl>(control, "TC_EncounterType").TabPages.Cast<TabPage>().Select(page => page.Text),
                Is.EqualTo(new[] { "Static", "Symbol", "Hidden", "Fishing" }));
            Assert.That(FindControl<TextBox>(control, "TB_RetailInitial").Text, Is.EqualTo("0"));
            Assert.That(FindControl<TextBox>(control, "TB_RetailRange").Text, Is.EqualTo("99999"));
            Assert.That(FindControl<Button>(control, "B_RetailSeedFinder").Text, Is.EqualTo("Retail Seed Finder"));
            Assert.That(FindControl<Panel>(control, "owoowScrollHost").AutoScroll, Is.True);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowMainResultGridMatchesPinnedColumnOrder()
    {
        using var control = new OwoowTabControl();

        var headers = FindControl<DataGridView>(control, "DGV_Results")
            .Columns
            .Cast<DataGridViewColumn>()
            .Select(column => column.HeaderText);

        Assert.That(headers, Is.EqualTo(new[]
        {
            "Advances", "Jump", "Step", "Animation", "Species", "Shiny", "Brilliant", "Level",
            "Ability", "Nature", "Gender", "HP", "Atk", "Def", "SpA", "SpD", "Spe", "Mark",
            "EC", "PID", "Height", "Item", "Egg Move", "Seed0", "Seed1",
        }));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowMainRemovesUnsupportedControllerEntrypointsAndClosesGap()
    {
        using var control = new OwoowTabControl();

        var cfw = FindControl<GroupBox>(control, "GB_SwitchControls");

        Assert.Multiple(() =>
        {
            Assert.That(FindControlOrDefault(control, "B_Turbo"), Is.Null);
            Assert.That(FindControlOrDefault(control, "B_TurboSettings"), Is.Null);
            Assert.That(FindControlOrDefault(control, "B_SeedSearch"), Is.Null);
            Assert.That(FindControlOrDefault(control, "B_SeedSearch_Settings"), Is.Null);
            Assert.That(
                cfw.Controls.Cast<Control>().OfType<Button>().Select(button => button.Text),
                Is.EquivalentTo(new[] { "Cancel", "Days+", "Days-", "Adv.", "NTP" }));
            Assert.That(cfw.Height, Is.LessThanOrEqualTo(110));
        });
    }

    private static T FindControl<T>(Control root, string name)
        where T : Control
    {
        return FindControlOrDefault(root, name) as T
            ?? throw new InvalidOperationException($"Control '{name}' was not found as {typeof(T).Name}.");
    }

    private static Control? FindControlOrDefault(Control root, string name)
    {
        if (root.Name == name)
        {
            return root;
        }

        foreach (Control child in root.Controls)
        {
            var match = FindControlOrDefault(child, name);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }
}
