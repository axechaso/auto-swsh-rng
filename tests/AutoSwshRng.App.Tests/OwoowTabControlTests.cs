using AutoSwshRng.App.Controls;
using AutoSwshRng.Core.Common;
using AutoSwshRng.Core.Connection;
using AutoSwshRng.Core.Encounters;
using AutoSwshRng.Core.Profiles;
using AutoSwshRng.Core.Rng;
using AutoSwshRng.Core.SpreadFinder;
using System.Drawing;
using System.Windows.Forms;

namespace AutoSwshRng.App.Tests;

public class OwoowTabControlTests
{
    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowConnectUsesServiceAndLoadsCurrentSnapshot()
    {
        var doubles = new OwoowServiceDoubles();
        using var control = new OwoowTabControl(doubles.Services);
        FindControl<TextBox>(control, "TB_SwitchIP").Text = "10.0.0.5";

        FindControl<Button>(control, "B_Connect").PerformClick();

        Assert.Multiple(() =>
        {
            Assert.That(doubles.Connection.LastConnectionSettings, Is.EqualTo(
                new OwoowConnectionSettings(ConnectionProtocol.Wifi, "10.0.0.5", 6000)));
            Assert.That(FindControl<TextBox>(control, "TB_Status").Text, Is.EqualTo("Connected."));
            Assert.That(FindControl<TextBox>(control, "TB_CurrentS0").Text, Is.EqualTo("1111222233334444"));
            Assert.That(FindControl<TextBox>(control, "TB_CurrentS1").Text, Is.EqualTo("AAAABBBBCCCCDDDD"));
            Assert.That(FindControl<TextBox>(control, "TB_TID").Text, Is.EqualTo("01337"));
            Assert.That(FindControl<TextBox>(control, "TB_SID").Text, Is.EqualTo("01390"));
            Assert.That(FindControl<Button>(control, "B_Connect").Enabled, Is.False);
            Assert.That(FindControl<Button>(control, "B_Disconnect").Enabled, Is.True);
            Assert.That(FindControl<Button>(control, "B_ReadEncounter").Enabled, Is.True);
            Assert.That(FindControl<Button>(control, "B_SkipForward").Enabled, Is.True);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowConnectShowsReadableServiceFailureAndRestoresControls()
    {
        var doubles = new OwoowServiceDoubles();
        doubles.Connection.ConnectException = new InvalidOperationException("Switch unavailable");
        var messages = new List<(string Title, string Message)>();
        using var control = new OwoowTabControl(
            doubles.Services,
            (title, message) => messages.Add((title, message)));

        FindControl<Button>(control, "B_Connect").PerformClick();

        Assert.Multiple(() =>
        {
            Assert.That(messages, Is.EqualTo(new[] { ("Connection failed", "Switch unavailable") }));
            Assert.That(FindControl<TextBox>(control, "TB_Status").Text, Is.EqualTo("Switch unavailable"));
            Assert.That(FindControl<Button>(control, "B_Connect").Enabled, Is.True);
            Assert.That(FindControl<Button>(control, "B_Disconnect").Enabled, Is.False);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowStaticSearchBuildsCoreRequestAndBindsPinnedResultColumns()
    {
        var doubles = new OwoowServiceDoubles();
        doubles.Encounters.Results =
        [
            new OverworldEncounterResult(
                12,
                3,
                2,
                'P',
                "Pikachu",
                "Square",
                true,
                50,
                "Static",
                "Jolly",
                PokemonGender.Female,
                new RngIndividualValues([31, 30, 29, 28, 27, 26]),
                "Rare",
                0x12345678,
                0x87654321,
                200,
                "XL",
                "Light Ball",
                "Volt Tackle",
                new RngState(0x1111222233334444, 0xAAAABBBBCCCCDDDD)),
        ];
        var messages = new List<(string Title, string Message)>();
        var control = new OwoowTabControl(
            doubles.Services,
            (title, message) => messages.Add((title, message)));
        using var host = new Form { ClientSize = new Size(1300, 720) };
        host.Controls.Add(control);
        host.Show();
        Application.DoEvents();
        SelectOnly(FindControl<ComboBox>(control, "CB_Static_Area"), "Rolling Fields");
        SelectOnly(FindControl<ComboBox>(control, "CB_Static_Weather"), "Normal Weather");
        SelectOnly(FindControl<ComboBox>(control, "CB_Static_Species"), "Pikachu");
        FindControl<TextBox>(control, "TB_Static_Initial").Text = "10";
        FindControl<TextBox>(control, "TB_Static_Advances").Text = "5";

        var searchButton = FindControl<Button>(control, "B_Static_Search");
        Assert.That(searchButton.Visible, Is.True, "Static search button must be visible before click.");
        Assert.That(searchButton.CanSelect, Is.True, "Static search button must be selectable before click.");
        searchButton.PerformClick();

        var request = doubles.Encounters.LastRequest;
        Assert.That(messages, Is.Empty, $"Unexpected UI error: {string.Join(" | ", messages)}");
        Assert.That(request, Is.Not.Null);
        var row = FindControl<DataGridView>(control, "DGV_Results").Rows.Cast<DataGridViewRow>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(request!.Context.Kind, Is.EqualTo(EncounterKind.Static));
            Assert.That(request.Context.Game, Is.EqualTo(GameVersion.Sword));
            Assert.That(request.Context.Area, Is.EqualTo("Rolling Fields"));
            Assert.That(request.Context.Weather, Is.EqualTo("Normal Weather"));
            Assert.That(request.Filter.TargetSpecies, Is.EqualTo("Pikachu"));
            Assert.That(request.StartAdvance, Is.EqualTo(10));
            Assert.That(request.EndAdvance, Is.EqualTo(15));
            Assert.That(row.Cells[0].Value, Is.EqualTo("12"));
            Assert.That(row.Cells[4].Value, Is.EqualTo("Pikachu"));
            Assert.That(row.Cells[11].Value, Is.EqualTo("31"));
            Assert.That(row.Cells[18].Value, Is.EqualTo("12345678"));
            Assert.That(row.Cells[23].Value, Is.EqualTo("1111222233334444"));
            Assert.That(row.Cells[24].Value, Is.EqualTo("AAAABBBBCCCCDDDD"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowCatalogLoadsCurrentEncounterSelectionsFromCoreService()
    {
        var doubles = new OwoowServiceDoubles();
        var control = new OwoowTabControl(doubles.Services, (_, _) => { });
        using var host = new Form { ClientSize = new Size(1300, 720) };
        host.Controls.Add(control);

        host.Show();
        Application.DoEvents();

        Assert.Multiple(() =>
        {
            Assert.That(doubles.Catalog.AreaRequests, Does.Contain((GameVersion.Sword, EncounterKind.Static)));
            Assert.That(FindControl<ComboBox>(control, "CB_Static_Area").Text, Is.EqualTo("Rolling Fields"));
            Assert.That(FindControl<ComboBox>(control, "CB_Static_Weather").Text, Is.EqualTo("Normal Weather"));
            Assert.That(FindControl<ComboBox>(control, "CB_Static_Species").Text, Is.EqualTo("Pikachu"));
            Assert.That(FindControl<ComboBox>(control, "CB_DexRec1").Items.Cast<object>().Select(item => item.ToString()),
                Is.EqualTo(new[] { "(None)", "Pikachu", "Eevee", "Grookey", "Scorbunny" }));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowSearchHonorsFilterToggleToneAndFocusOptions()
    {
        var doubles = new OwoowServiceDoubles();
        doubles.Encounters.Results =
        [
            new OverworldEncounterResult(
                1, 0, 0, 'P', "Pikachu", "None", false, 5, "Static", "Hardy",
                PokemonGender.Male, new RngIndividualValues([1, 1, 1, 1, 1, 1]),
                string.Empty, 1, 2, 128, "M", string.Empty, string.Empty, new RngState(3, 4)),
        ];
        var tones = 0;
        var focuses = 0;
        var messages = new List<(string Title, string Message)>();
        var control = new OwoowTabControl(
            doubles.Services,
            (title, message) => messages.Add((title, message)),
            resultTone: () => tones++,
            resultFocus: () => focuses++);
        using var host = new Form { ClientSize = new Size(1300, 720) };
        host.Controls.Add(control);
        host.Show();
        Application.DoEvents();
        SelectOnly(FindControl<ComboBox>(control, "CB_Static_Area"), "Rolling Fields");
        SelectOnly(FindControl<ComboBox>(control, "CB_Static_Weather"), "Normal Weather");
        SelectOnly(FindControl<ComboBox>(control, "CB_Static_Species"), "Pikachu");
        FindControl<CheckBox>(control, "CB_EnableFilters").Checked = false;
        FindControl<CheckBox>(control, "CB_PlayTone").Checked = true;
        FindControl<CheckBox>(control, "CB_FocusWindow").Checked = true;

        FindControl<Button>(control, "B_Static_Search").PerformClick();

        Assert.That(doubles.Encounters.LastRequest, Is.Not.Null,
            $"Unexpected UI error: {string.Join(" | ", messages)}");
        Assert.Multiple(() =>
        {
            Assert.That(doubles.Encounters.LastRequest?.Filter.TargetSpecies, Is.EqualTo("Pikachu"));
            Assert.That(doubles.Encounters.LastRequest?.FiltersEnabled, Is.False);
            Assert.That(tones, Is.EqualTo(1));
            Assert.That(focuses, Is.EqualTo(1));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowSearchMapsDexRecommendationSlotsAndRainStage()
    {
        var doubles = new OwoowServiceDoubles();
        using var control = new OwoowTabControl(doubles.Services, (_, _) => { });
        using var host = new Form { ClientSize = new Size(1300, 720) };
        host.Controls.Add(control);
        host.Show();
        Application.DoEvents();
        SelectOnly(FindControl<ComboBox>(control, "CB_Static_Area"), "Rolling Fields");
        SelectOnly(FindControl<ComboBox>(control, "CB_Static_Weather"), "Normal Weather");
        SelectOnly(FindControl<ComboBox>(control, "CB_Static_Species"), "Pikachu");
        FindControl<ComboBox>(control, "CB_DexRec1").SelectedIndex = 1;
        FindControl<ComboBox>(control, "CB_DexRec2").SelectedIndex = 2;
        FindControl<ComboBox>(control, "CB_DexRec3").SelectedIndex = 3;
        FindControl<ComboBox>(control, "CB_DexRec4").SelectedIndex = 4;
        FindControl<CheckBox>(control, "CB_ConsiderRain").Checked = true;
        FindControl<NumericUpDown>(control, "NUD_RainTick").Value = 5;

        FindControl<Button>(control, "B_Static_Search").PerformClick();
        var rainOnly = doubles.Encounters.LastRequest
            ?? throw new InvalidOperationException("Rain-only search request was not captured.");

        FindControl<CheckBox>(control, "CB_ConsiderFlying").Checked = true;
        FindControl<Button>(control, "B_Static_Search").PerformClick();
        var flyingAndRain = doubles.Encounters.LastRequest
            ?? throw new InvalidOperationException("Flying-and-rain search request was not captured.");

        Assert.Multiple(() =>
        {
            Assert.That(rainOnly.DexRecommendationSlots, Is.EqualTo(new short[] { 25, 133, 810, 813 }));
            Assert.That(rainOnly.Environment.RainTicksDuringAreaLoad, Is.Zero);
            Assert.That(rainOnly.Environment.RainTicksBeforeEncounter, Is.EqualTo(5));
            Assert.That(flyingAndRain.Environment.RainTicksDuringAreaLoad, Is.EqualTo(5));
            Assert.That(flyingAndRain.Environment.RainTicksBeforeEncounter, Is.Zero);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowConnectedUtilityButtonsInvokeMatchingServices()
    {
        var doubles = new OwoowServiceDoubles();
        doubles.Xoroshiro.ResultState = new RngState(0x2222333344445555, 0xBBBBCCCCDDDDEEEE);
        var control = new OwoowTabControl(doubles.Services, (_, _) => { });
        using var host = new Form { ClientSize = new Size(1300, 720) };
        host.Controls.Add(control);
        host.Show();
        Application.DoEvents();
        FindControl<Button>(control, "B_Connect").PerformClick();
        FindControl<TextBox>(control, "TB_Skips").Text = "2";

        FindControl<Button>(control, "B_RefreshDexRec").PerformClick();
        FindControl<Button>(control, "B_ReadEncounter").PerformClick();
        FindControl<Button>(control, "B_SkipForward").PerformClick();
        FindControl<Button>(control, "B_SkipBack").PerformClick();
        FindControl<Button>(control, "B_SkipAdvance").PerformClick();
        FindControl<Button>(control, "B_NTP").PerformClick();

        Assert.Multiple(() =>
        {
            Assert.That(doubles.Connection.DexRecommendationReads, Is.EqualTo(1));
            Assert.That(FindControl<ComboBox>(control, "CB_DexRec1").Text, Is.EqualTo("1"));
            Assert.That(doubles.Connection.WildPokemonReads, Is.EqualTo(1));
            Assert.That(FindControl<TextBox>(control, "TB_Wild").Text, Does.Contain("Bulbasaur"));
            Assert.That(FindControl<Button>(control, "B_CopyToFilter").Enabled, Is.True);
            Assert.That(doubles.Connection.SkipDayCalls, Is.EqualTo(2));
            Assert.That(doubles.Connection.SkipDayBackCalls, Is.EqualTo(2));
            Assert.That(doubles.Xoroshiro.LastRequest?.Operation, Is.EqualTo(XoroshiroOperation.Next));
            Assert.That(doubles.Xoroshiro.LastRequest?.Amount, Is.EqualTo(2));
            Assert.That(doubles.Connection.WrittenStates, Does.Contain(doubles.Xoroshiro.ResultState));
            Assert.That(doubles.Connection.ResetNetworkTimeCalls, Is.EqualTo(1));
            Assert.That(FindControl<TextBox>(control, "TB_CurrentS0").Text, Is.EqualTo("2222333344445555"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowInvalidDayCountShowsReadableErrorWithoutCallingConnection()
    {
        var doubles = new OwoowServiceDoubles();
        var messages = new List<(string Title, string Message)>();
        using var control = new OwoowTabControl(
            doubles.Services,
            (title, message) => messages.Add((title, message)));
        using var host = new Form { ClientSize = new Size(1300, 720) };
        host.Controls.Add(control);
        host.Show();
        Application.DoEvents();
        FindControl<Button>(control, "B_Connect").PerformClick();
        FindControl<TextBox>(control, "TB_Skips").Text = "not-a-day-count";

        FindControl<Button>(control, "B_SkipForward").PerformClick();
        Application.DoEvents();

        Assert.Multiple(() =>
        {
            Assert.That(doubles.Connection.SkipDayCalls, Is.Zero);
            Assert.That(messages, Is.EqualTo(new[]
            {
                ("Date skipping failed", "Days must be a non-negative integer."),
            }));
            Assert.That(FindControl<TextBox>(control, "TB_Status").Text,
                Is.EqualTo("Days must be a non-negative integer."));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowRetailUpdateGeneratesPatternAndReidentifiesObservedAnimations()
    {
        var doubles = new OwoowServiceDoubles();
        doubles.RetailSeeds.ReidentifyResult = new ReidentifySeedResult(
            1,
            7,
            new RngState(0x3333444455556666, 0xCCCCDDDDEEEEFFFF));
        var control = new OwoowTabControl(doubles.Services, (_, _) => { });
        using var host = new Form { ClientSize = new Size(1300, 720) };
        host.Controls.Add(control);
        host.Show();
        Application.DoEvents();
        FindControl<Button>(control, "B_Connect").PerformClick();
        FindControl<TextBox>(control, "TB_RetailInitial").Text = "0";
        FindControl<TextBox>(control, "TB_RetailRange").Text = "7";

        FindControl<Button>(control, "B_RetailUpdateSeeds").PerformClick();
        FindControl<TextBox>(control, "TB_Animations").Text = "010101";

        Assert.Multiple(() =>
        {
            Assert.That(doubles.RetailSeeds.LastAnimationRequest?.State, Is.EqualTo(doubles.Connection.CurrentState));
            Assert.That(doubles.RetailSeeds.LastAnimationRequest?.Count, Is.EqualTo(7));
            Assert.That(doubles.RetailSeeds.LastReidentifyRequest?.Pattern, Is.EqualTo("010101"));
            Assert.That(FindControl<TextBox>(control, "TB_RetailAdvances").Text, Is.EqualTo("7"));
            Assert.That(FindControl<TextBox>(control, "TB_CurrentS0").Text, Is.EqualTo("3333444455556666"));
            Assert.That(FindControl<TextBox>(control, "TB_CurrentS1").Text, Is.EqualTo("CCCCDDDDEEEEFFFF"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowCalibrationButtonsUseCoreService()
    {
        var doubles = new OwoowServiceDoubles();
        doubles.Calibration.Result = new CalibrationResult(42, new RngState(9, 10));
        var control = new OwoowTabControl(doubles.Services, (_, _) => { });
        using var host = new Form { ClientSize = new Size(1300, 720) };
        host.Controls.Add(control);
        host.Show();
        Application.DoEvents();
        FindControl<CheckBox>(control, "CB_Static_MenuClose").Checked = true;
        FindControl<NumericUpDown>(control, "NUD_RainTick").Value = 5;

        FindControl<Button>(control, "B_Static_MenuClose").PerformClick();
        FindControl<Button>(control, "B_CalculateRain").PerformClick();

        Assert.Multiple(() =>
        {
            Assert.That(doubles.Calibration.LastMenuCloseRequest?.NonPlayerCharacters, Is.EqualTo(3));
            Assert.That(doubles.Calibration.LastMenuCloseRequest?.Weather, Is.EqualTo(Weather.Normal));
            Assert.That(doubles.Calibration.LastRainRequest?.Ticks, Is.EqualTo(5));
            Assert.That(FindControl<TextBox>(control, "TB_Status").Text, Is.EqualTo("Rain: 42 advances."));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowMenuOpensEverySupportedPinnedToolWindow()
    {
        var doubles = new OwoowServiceDoubles();
        var opened = new List<(Form Form, bool Modal)>();
        using var control = new OwoowTabControl(
            doubles.Services,
            (_, _) => { },
            (form, modal) => opened.Add((form, modal)));
        var menu = FindControl<MenuStrip>(control, "MS_SubWindows");
        var itemNames = new[]
        {
            "TSMI_Profiles",
            "TSMI_EncounterLookup",
            "TSMI_SpreadFinder",
            "TSMI_LotoID",
            "TSMI_Cramomatic",
            "TSMI_WattTrader",
            "TSMI_DiggingPa",
            "TMSI_SkillBro",
            "TSMI_WailordRespawn",
            "TSMI_XoroshiroTools",
        };

        foreach (var itemName in itemNames)
        {
            FindMenuItem(menu, itemName).PerformClick();
        }

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(opened.Select(entry => entry.Form.AutoScaleMode),
                    Is.All.EqualTo(AutoScaleMode.Font));
                Assert.That(opened.Select(entry => entry.Form.AutoScaleDimensions),
                    Is.All.EqualTo(new SizeF(7F, 15F)));
            });
            Assert.That(opened.Select(entry => entry.Form.Text), Is.EqualTo(new[]
            {
                "Profile Manager",
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
            Assert.That(opened.Select(entry => entry.Modal), Is.EqualTo(new[]
            {
                true, false, false, false, false, false, false, false, false, false,
            }));
            _ = FindControl<ListBox>(opened[0].Form, "LB_ProfileList");
            _ = FindControl<ComboBox>(opened[1].Form, "CB_Species");
            _ = FindControl<TextBox>(opened[2].Form, "TB_Single");
            _ = FindControl<Button>(opened[3].Form, "B_LotoID_Search");
            _ = FindControl<ComboBox>(opened[4].Form, "CB_Item4");
            _ = FindControl<TextBox>(opened[5].Form, "TB_SlotMax");
            _ = FindControl<TextBox>(opened[6].Form, "TB_Target");
            _ = FindControl<NumericUpDown>(opened[7].Form, "NUD_MinTotal");
            _ = FindControl<Button>(opened[8].Form, "B_Wailord_Search");
            _ = FindControl<ComboBox>(opened[9].Form, "CB_Operation");
            foreach (var entry in opened.Skip(3).Take(6))
            {
                var settings = FindControl<GroupBox>(entry.Form, "GB_SearchSettings");
                var grid = FindControl<DataGridView>(entry.Form, "DGV_Results");
                Assert.That(grid.Left, Is.GreaterThan(settings.Right), entry.Form.Text);
                Assert.That(settings.Text, Is.Empty, entry.Form.Text);
            }
            Assert.That(opened.Skip(3).Take(4).Append(opened[8])
                .All(entry => entry.Form.ClientSize == new Size(800, 450)), Is.True);
            Assert.That(opened[7].Form.ClientSize, Is.EqualTo(new Size(1121, 798)));
            Assert.That(
                opened.SelectMany(entry => DescendantTexts(entry.Form))
                    .Any(text => text.Contains("unimplemented", StringComparison.OrdinalIgnoreCase)),
                Is.False);
        }
        finally
        {
            foreach (var entry in opened)
            {
                entry.Form.Dispose();
            }
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowProfileManagerLoadsSavesAndAppliesSelectedProfile()
    {
        var doubles = new OwoowServiceDoubles();
        doubles.Profiles.Settings = new RngApplicationSettings(
            "Main",
            [new RngProfile("Main", GameVersion.Shield, 123, 456, true, false)]);
        Form? profileForm = null;
        using var control = new OwoowTabControl(
            doubles.Services,
            (_, _) => { },
            (form, _) => profileForm = form);
        FindMenuItem(FindControl<MenuStrip>(control, "MS_SubWindows"), "TSMI_Profiles").PerformClick();
        using var dialog = profileForm ?? throw new InvalidOperationException("Profile dialog was not opened.");
        dialog.Show();
        Application.DoEvents();

        Assert.That(FindControl<ListBox>(dialog, "LB_ProfileList").Items.Cast<object>().Select(item => item.ToString()),
            Is.EqualTo(new[] { "Main" }));
        FindControl<TextBox>(dialog, "TB_Name").Text = "Second";
        FindControl<ComboBox>(dialog, "CB_Game").SelectedIndex = 0;
        FindControl<TextBox>(dialog, "TB_TID").Text = "00111";
        FindControl<TextBox>(dialog, "TB_SID").Text = "00222";
        FindControl<CheckBox>(dialog, "CB_ShinyCharm").Checked = false;
        FindControl<CheckBox>(dialog, "CB_MarkCharm").Checked = true;
        FindControl<Button>(dialog, "B_Add").PerformClick();
        FindControl<ListBox>(dialog, "LB_ProfileList").SelectedItem = "Second";
        FindControl<Button>(dialog, "B_Select").PerformClick();

        Assert.Multiple(() =>
        {
            Assert.That(doubles.Profiles.SaveCalls, Is.EqualTo(2));
            Assert.That(doubles.Profiles.Settings.ActiveProfileName, Is.EqualTo("Second"));
            Assert.That(doubles.Profiles.Settings.Profiles.Select(profile => profile.Name), Is.EqualTo(new[] { "Main", "Second" }));
            Assert.That(FindControl<ComboBox>(control, "CB_Game").SelectedIndex, Is.EqualTo(0));
            Assert.That(FindControl<TextBox>(control, "TB_TID").Text, Is.EqualTo("00111"));
            Assert.That(FindControl<TextBox>(control, "TB_SID").Text, Is.EqualTo("00222"));
            Assert.That(FindControl<CheckBox>(control, "CB_MarkCharm").Checked, Is.True);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowEncounterLookupLoadsOptionsAndBindsServiceResults()
    {
        var doubles = new OwoowServiceDoubles();
        doubles.Catalog.LookupResults =
        [
            new EncounterLookupResult(
                "Pikachu", 10, 20, 15, 12, 18, 10, false, false, false, 0, 0,
                "Normal Weather", "Rolling Fields", "Symbol"),
        ];
        Form? lookupForm = null;
        using var control = new OwoowTabControl(
            doubles.Services,
            (_, _) => { },
            (form, _) => lookupForm = form);
        FindMenuItem(FindControl<MenuStrip>(control, "MS_SubWindows"), "TSMI_EncounterLookup").PerformClick();
        using var dialog = lookupForm ?? throw new InvalidOperationException("Lookup dialog was not opened.");
        dialog.Show();
        Application.DoEvents();

        var species = FindControl<ComboBox>(dialog, "CB_Species");
        species.SelectedItem = species.Items.Cast<object>().Single(item => item.ToString() == "Pikachu");
        Application.DoEvents();

        var row = FindControl<DataGridView>(dialog, "DGV_Results").Rows.Cast<DataGridViewRow>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(doubles.Catalog.LastLookup, Is.EqualTo((GameVersion.Sword, "Pikachu")));
            Assert.That(row.Cells[0].Value, Is.EqualTo("Pikachu"));
            Assert.That(row.Cells[1].Value, Is.EqualTo("Symbol"));
            Assert.That(row.Cells[2].Value, Is.EqualTo("Rolling Fields"));
            Assert.That(row.Cells[4].Value, Is.EqualTo("10"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowSpreadFinderUsesSeedAndEntireSpaceServiceScopes()
    {
        var doubles = new OwoowServiceDoubles();
        doubles.SpreadFinder.Results =
        [
            new SpreadSearchResult(
                0xDEADC0DE,
                0x12345678,
                new IndividualValues(31, 30, 29, 28, 27, 26),
                200,
                SpreadScale.XL),
        ];
        Form? spreadForm = null;
        using var control = new OwoowTabControl(
            doubles.Services,
            (_, _) => { },
            (form, _) => spreadForm = form);
        FindMenuItem(FindControl<MenuStrip>(control, "MS_SubWindows"), "TSMI_SpreadFinder").PerformClick();
        using var dialog = spreadForm ?? throw new InvalidOperationException("Spread Finder dialog was not opened.");
        dialog.Show();
        Application.DoEvents();
        FindControl<TextBox>(dialog, "TB_Single").Text = "DEADC0DE";

        FindControl<Button>(dialog, "B_GenerateSingle").PerformClick();
        var singleRequest = doubles.SpreadFinder.Requests.Single();
        doubles.SpreadFinder.Requests.Clear();
        FindControl<Button>(dialog, "B_Search").PerformClick();
        var fullRequest = doubles.SpreadFinder.Requests.Single();

        var row = FindControl<DataGridView>(dialog, "DGV_Results").Rows.Cast<DataGridViewRow>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(singleRequest.Scope, Is.TypeOf<SpreadSearchScope.Seeds>());
            Assert.That(((SpreadSearchScope.Seeds)singleRequest.Scope).Values, Is.EqualTo(new[] { 0xDEADC0DEU }));
            Assert.That(fullRequest.Scope, Is.TypeOf<SpreadSearchScope.EntireSpace>());
            Assert.That(((SpreadSearchScope.EntireSpace)fullRequest.Scope).PartitionCount, Is.EqualTo(1));
            Assert.That(row.Cells[0].Value, Is.EqualTo("DEADC0DE"));
            Assert.That(row.Cells[2].Value, Is.EqualTo("31"));
            Assert.That(row.Cells[8].Value, Does.Contain("XL"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowSpecialToolWindowsMapEverySupportedServiceRequest()
    {
        var doubles = new OwoowServiceDoubles();
        var opened = new List<Form>();
        using var control = new OwoowTabControl(
            doubles.Services,
            (_, _) => { },
            (form, _) => opened.Add(form));
        var menu = FindControl<MenuStrip>(control, "MS_SubWindows");
        foreach (var itemName in new[]
        {
            "TSMI_LotoID", "TSMI_Cramomatic", "TSMI_WattTrader", "TSMI_DiggingPa",
            "TMSI_SkillBro", "TSMI_WailordRespawn",
        })
        {
            FindMenuItem(menu, itemName).PerformClick();
        }

        var buttonNames = new[]
        {
            "B_LotoID_Search", "B_Cramomatic_Search", "B_WattTrader_Search", "B_DiggingPa_Search",
            "B_SkillBro_Search", "B_Wailord_Search",
        };
        try
        {
            for (var index = 0; index < opened.Count; index++)
            {
                opened[index].Show();
                Application.DoEvents();
                FindControl<Button>(opened[index], buttonNames[index]).PerformClick();
            }

            var requests = doubles.SpecialTools.Requests;
            Assert.Multiple(() =>
            {
                Assert.That(requests.Select(request => request.Kind), Is.EqualTo(new[]
                {
                    SpecialToolKind.LotoId,
                    SpecialToolKind.CramOMatic,
                    SpecialToolKind.WattTrader,
                    SpecialToolKind.DiggingPa,
                    SpecialToolKind.DiggingBro,
                    SpecialToolKind.WailordRespawn,
                }));
                Assert.That(requests[0].LotoPrize, Is.EqualTo(LotoPrizeFilter.MasterBall));
                Assert.That(requests[1].CramInputs, Is.All.EqualTo(CramInputItem.BlackApricorn));
                Assert.That(requests[1].CramPrize, Is.EqualTo(CramPrizeFilter.SportBall));
                Assert.That(requests[2].WattTraderSlotMinimum, Is.EqualTo(0));
                Assert.That(requests[2].WattTraderSlotMaximum, Is.EqualTo(999));
                Assert.That(requests[3].DiggingPaMinimumWatts, Is.EqualTo(500000));
                Assert.That(requests[4].DiggingBroMinimumTotal, Is.EqualTo(5));
                Assert.That(requests[4].DiggingBroMinimumRewards.Count, Is.EqualTo(21));
                Assert.That(requests[5].Success, Is.EqualTo(SuccessFilter.Yes));
                Assert.That(requests, Is.All.Matches<SpecialToolSearchRequest>(request => !request.MenuClose.Enabled));
                Assert.That(opened.Select(form => FindControl<DataGridView>(form, "DGV_Results").Rows.Count),
                    Is.All.EqualTo(1));
            });
        }
        finally
        {
            foreach (var form in opened)
            {
                form.Dispose();
            }
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowLotoIdListLoadsEditsPersistsAndFeedsSearch()
    {
        var doubles = new OwoowServiceDoubles();
        doubles.LotoIds.LoadedIds = ["654321"];
        Form? opened = null;
        using var control = new OwoowTabControl(
            doubles.Services,
            (_, _) => { },
            (form, _) => opened = form);
        FindMenuItem(FindControl<MenuStrip>(control, "MS_SubWindows"), "TSMI_LotoID").PerformClick();
        using var dialog = opened ?? throw new InvalidOperationException("Loto-ID dialog was not opened.");
        dialog.Show();
        Application.DoEvents();
        Assert.That(FindControl<Label>(dialog, "L_LoadedIDs").Text, Is.EqualTo("Loaded IDs: 1"));

        FindControl<Button>(dialog, "B_IDList").PerformClick();
        Application.DoEvents();
        var idDialog = Application.OpenForms.Cast<Form>().Single(form => form.Name == "IDList");
        FindControl<TextBox>(idDialog, "TB_ID").Text = "222";
        FindControl<Button>(idDialog, "B_Add").PerformClick();
        idDialog.Close();
        Application.DoEvents();
        FindControl<Button>(dialog, "B_LotoID_Search").PerformClick();

        Assert.Multiple(() =>
        {
            Assert.That(doubles.LotoIds.SavedIds, Is.EqualTo(new[] { "000222", "654321" }));
            Assert.That(doubles.SpecialTools.Requests.Single().LotoIds, Is.EqualTo(new[] { "000222", "654321" }));
            Assert.That(FindControl<Label>(dialog, "L_LoadedIDs").Text, Is.EqualTo("Loaded IDs: 2"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowLotoIdListWaitsForLoadAndDoesNotAddBlankId()
    {
        var doubles = new OwoowServiceDoubles();
        doubles.LotoIds.PendingLoad = new TaskCompletionSource<IReadOnlyList<string>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Form? opened = null;
        using var control = new OwoowTabControl(
            doubles.Services,
            (_, _) => { },
            (form, _) => opened = form);
        FindMenuItem(FindControl<MenuStrip>(control, "MS_SubWindows"), "TSMI_LotoID").PerformClick();
        using var dialog = opened ?? throw new InvalidOperationException("Loto-ID dialog was not opened.");
        dialog.Show();
        Application.DoEvents();
        var idListButton = FindControl<Button>(dialog, "B_IDList");
        var searchButton = FindControl<Button>(dialog, "B_LotoID_Search");

        Assert.Multiple(() =>
        {
            Assert.That(idListButton.Enabled, Is.False);
            Assert.That(searchButton.Enabled, Is.False);
        });

        doubles.LotoIds.PendingLoad.SetResult(["654321"]);
        PumpMessagesUntil(() => idListButton.Enabled && searchButton.Enabled);
        idListButton.PerformClick();
        Application.DoEvents();
        var idDialog = Application.OpenForms.Cast<Form>().Single(form => form.Name == "IDList");
        FindControl<TextBox>(idDialog, "TB_ID").Clear();
        FindControl<Button>(idDialog, "B_Add").PerformClick();

        Assert.That(FindControl<ListBox>(idDialog, "LB_IDs").Items.Cast<string>(),
            Is.EqualTo(new[] { "654321" }));
        idDialog.Close();
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowLotoIdSavesSnapshotsSeriallyAcrossQuickReopen()
    {
        var doubles = new OwoowServiceDoubles();
        var firstSave = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondSave = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        doubles.LotoIds.SaveCompletions.Enqueue(firstSave);
        doubles.LotoIds.SaveCompletions.Enqueue(secondSave);
        Form? opened = null;
        using var control = new OwoowTabControl(
            doubles.Services,
            (_, _) => { },
            (form, _) => opened = form);
        FindMenuItem(FindControl<MenuStrip>(control, "MS_SubWindows"), "TSMI_LotoID").PerformClick();
        using var dialog = opened ?? throw new InvalidOperationException("Loto-ID dialog was not opened.");
        dialog.Show();
        Application.DoEvents();
        var idListButton = FindControl<Button>(dialog, "B_IDList");

        idListButton.PerformClick();
        Application.DoEvents();
        var firstDialog = Application.OpenForms.Cast<Form>().Single(form => form.Name == "IDList");
        FindControl<TextBox>(firstDialog, "TB_ID").Text = "111";
        FindControl<Button>(firstDialog, "B_Add").PerformClick();
        firstDialog.Close();

        idListButton.PerformClick();
        Application.DoEvents();
        var secondDialog = Application.OpenForms.Cast<Form>().Single(form => form.Name == "IDList");
        FindControl<TextBox>(secondDialog, "TB_ID").Text = "222";
        FindControl<Button>(secondDialog, "B_Add").PerformClick();
        secondDialog.Close();

        Assert.That(doubles.LotoIds.SaveSnapshots, Has.Count.EqualTo(1));
        firstSave.SetResult(null);
        PumpMessagesUntil(() => doubles.LotoIds.SaveSnapshots.Count == 2);

        Assert.That(doubles.LotoIds.SaveSnapshots, Is.EqualTo(new[]
        {
            new[] { "000111" },
            new[] { "000111", "000222" },
        }));
        secondSave.SetResult(null);
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowClosingSpecialToolCancelsPendingSearchWithoutShowingFailure()
    {
        var doubles = new OwoowServiceDoubles();
        doubles.SpecialTools.WaitForCancellation = true;
        var messages = new List<(string Title, string Message)>();
        Form? opened = null;
        using var control = new OwoowTabControl(
            doubles.Services,
            (title, message) => messages.Add((title, message)),
            (form, _) => opened = form);
        FindMenuItem(FindControl<MenuStrip>(control, "MS_SubWindows"), "TSMI_WattTrader").PerformClick();
        using var dialog = opened ?? throw new InvalidOperationException("Watt Trader dialog was not opened.");
        dialog.Show();
        Application.DoEvents();

        FindControl<Button>(dialog, "B_WattTrader_Search").PerformClick();
        dialog.Close();
        PumpMessagesUntil(() => doubles.SpecialTools.LastCancellationToken.IsCancellationRequested);

        Assert.Multiple(() =>
        {
            Assert.That(doubles.SpecialTools.LastCancellationToken.IsCancellationRequested, Is.True);
            Assert.That(messages, Is.Empty);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowWattTraderTargetSelectsPinnedSlotRange()
    {
        var doubles = new OwoowServiceDoubles();
        Form? opened = null;
        using var control = new OwoowTabControl(
            doubles.Services,
            (_, _) => { },
            (form, _) => opened = form);
        FindMenuItem(FindControl<MenuStrip>(control, "MS_SubWindows"), "TSMI_WattTrader").PerformClick();
        using var dialog = opened ?? throw new InvalidOperationException("Watt Trader dialog was not opened.");
        dialog.Show();
        Application.DoEvents();
        var target = FindControl<ComboBox>(dialog, "CB_Target");

        target.SelectedItem = "Beast or Dream Ball";
        FindControl<Button>(dialog, "B_WattTrader_Search").PerformClick();

        Assert.Multiple(() =>
        {
            Assert.That(target.Items, Has.Count.EqualTo(52));
            Assert.That(FindControl<TextBox>(dialog, "TB_SlotMin").Text, Is.EqualTo("827"));
            Assert.That(FindControl<TextBox>(dialog, "TB_SlotMax").Text, Is.EqualTo("828"));
            Assert.That(doubles.SpecialTools.Requests.Single().WattTraderSlotMinimum, Is.EqualTo(827));
            Assert.That(doubles.SpecialTools.Requests.Single().WattTraderSlotMaximum, Is.EqualTo(828));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowSpecialToolInheritsAndReturnsSelectedEncounterContext()
    {
        var doubles = new OwoowServiceDoubles();
        Form? opened = null;
        using var control = new OwoowTabControl(
            doubles.Services,
            (_, _) => { },
            (form, _) => opened = form);
        FindControl<TextBox>(control, "TB_CurrentAdvances").Text = "1,234";
        FindControl<CheckBox>(control, "CB_Static_MenuClose").Checked = true;
        FindControl<CheckBox>(control, "CB_Static_MenuClose_Direction").Checked = true;
        FindControl<TextBox>(control, "TB_Static_NPCs").Text = "7";
        SelectOnly(FindControl<ComboBox>(control, "CB_Static_Weather"), "Raining");

        FindMenuItem(FindControl<MenuStrip>(control, "MS_SubWindows"), "TSMI_WattTrader").PerformClick();
        using var dialog = opened ?? throw new InvalidOperationException("Watt Trader dialog was not opened.");
        dialog.Show();
        Application.DoEvents();

        Assert.Multiple(() =>
        {
            Assert.That(FindControl<TextBox>(dialog, "TB_WattTrader_Initial").Text, Is.EqualTo("1234"));
            Assert.That(FindControl<CheckBox>(dialog, "CB_WattTrader_MenuClose").Checked, Is.True);
            Assert.That(FindControl<CheckBox>(dialog, "CB_WattTrader_MenuClose_Direction").Checked, Is.True);
            Assert.That(FindControl<TextBox>(dialog, "TB_WattTrader_NPCs").Text, Is.EqualTo("7"));
            Assert.That(FindControl<ComboBox>(dialog, "CB_WattTrader_Weather").Text, Is.EqualTo("Raining"));
        });

        FindControl<CheckBox>(dialog, "CB_WattTrader_MenuClose_Direction").Checked = false;
        FindControl<TextBox>(dialog, "TB_WattTrader_NPCs").Text = "9";
        dialog.Close();

        Assert.Multiple(() =>
        {
            Assert.That(FindControl<CheckBox>(control, "CB_Static_MenuClose_Direction").Checked, Is.False);
            Assert.That(FindControl<TextBox>(control, "TB_Static_NPCs").Text, Is.EqualTo("9"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowOpenSpecialToolTracksSeedAndMenuContextChanges()
    {
        var doubles = new OwoowServiceDoubles();
        Form? opened = null;
        using var control = new OwoowTabControl(
            doubles.Services,
            (_, _) => { },
            (form, _) => opened = form);

        FindMenuItem(FindControl<MenuStrip>(control, "MS_SubWindows"), "TSMI_WattTrader").PerformClick();
        using var dialog = opened ?? throw new InvalidOperationException("Watt Trader dialog was not opened.");
        dialog.Show();
        Application.DoEvents();

        FindControl<TextBox>(control, "TB_CurrentS0").Text = "1111111111111111";
        FindControl<TextBox>(control, "TB_CurrentS1").Text = "2222222222222222";
        var copy = FindControl<Button>(control, "B_CopyToInitial");
        copy.Enabled = true;
        copy.PerformClick();
        FindControl<CheckBox>(control, "CB_Static_MenuClose").Checked = true;
        FindControl<CheckBox>(control, "CB_Static_MenuClose_Direction").Checked = true;

        FindControl<Button>(dialog, "B_WattTrader_Search").PerformClick();

        Assert.Multiple(() =>
        {
            Assert.That(FindControl<TextBox>(dialog, "TB_Seed0").Text, Is.EqualTo("1111111111111111"));
            Assert.That(FindControl<TextBox>(dialog, "TB_Seed1").Text, Is.EqualTo("2222222222222222"));
            Assert.That(FindControl<CheckBox>(dialog, "CB_WattTrader_MenuClose").Checked, Is.True);
            Assert.That(FindControl<CheckBox>(dialog, "CB_WattTrader_MenuClose_Direction").Checked, Is.True);
            Assert.That(doubles.SpecialTools.Requests.Single().State,
                Is.EqualTo(new RngState(0x1111111111111111, 0x2222222222222222)));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowNonModalToolMenuKeepsSingleWindowInstance()
    {
        var opened = new List<Form>();
        using var control = new OwoowTabControl(
            toolWindowPresenter: (form, _) => opened.Add(form));
        var item = FindMenuItem(FindControl<MenuStrip>(control, "MS_SubWindows"), "TSMI_WattTrader");

        item.PerformClick();
        item.PerformClick();

        Assert.That(opened, Has.Count.EqualTo(1));
        opened[0].Dispose();
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowXoroshiroWindowMapsAllOperationsAndDisplaysResult()
    {
        var doubles = new OwoowServiceDoubles();
        doubles.Xoroshiro.ResultState = new RngState(0x1234567890ABCDEF, 0xFEDCBA0987654321);
        Form? opened = null;
        using var control = new OwoowTabControl(
            doubles.Services,
            (_, _) => { },
            (form, _) => opened = form);
        FindMenuItem(FindControl<MenuStrip>(control, "MS_SubWindows"), "TSMI_XoroshiroTools").PerformClick();
        using var dialog = opened ?? throw new InvalidOperationException("Xoroshiro Tools dialog was not opened.");
        dialog.Show();
        Application.DoEvents();
        FindControl<TextBox>(dialog, "TB_Seed0").Text = "0123456789ABCDEF";
        FindControl<TextBox>(dialog, "TB_Seed1").Text = "FEDCBA9876543210";
        FindControl<TextBox>(dialog, "TB_N").Text = "2";
        var operation = FindControl<ComboBox>(dialog, "CB_Operation");
        var calculate = FindControl<Button>(dialog, "B_Calculate");

        for (var index = 0; index < 4; index++)
        {
            operation.SelectedIndex = index;
            calculate.PerformClick();
        }

        Assert.Multiple(() =>
        {
            Assert.That(doubles.Xoroshiro.Requests.Select(request => request.Operation), Is.EqualTo(new[]
            {
                XoroshiroOperation.Next,
                XoroshiroOperation.Previous,
                XoroshiroOperation.NextInteger,
                XoroshiroOperation.FindInitial,
            }));
            Assert.That(doubles.Xoroshiro.Requests, Is.All.Matches<XoroshiroRequest>(request => request.Amount == 2));
            Assert.That(FindControl<TextBox>(dialog, "TB_Result_S0").Text, Is.EqualTo("1234567890ABCDEF"));
            Assert.That(FindControl<TextBox>(dialog, "TB_Result_S1").Text, Is.EqualTo("FEDCBA0987654321"));
            Assert.That(FindControl<TextBox>(dialog, "TB_Distance").Text, Is.EqualTo("+2"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowRetailSeedFinderCalculatesExactSeedAndUpdatesMainForm()
    {
        var doubles = new OwoowServiceDoubles();
        doubles.RetailSeeds.ExactResult = new RngState(0x0123456789ABCDEF, 0xFEDCBA9876543210);
        Form? opened = null;
        var control = new OwoowTabControl(
            doubles.Services,
            (_, _) => { },
            (form, _) => opened = form);
        using var host = new Form { ClientSize = new Size(1300, 720) };
        host.Controls.Add(control);
        host.Show();
        Application.DoEvents();
        FindControl<Button>(control, "B_RetailSeedFinder").PerformClick();
        using var dialog = opened ?? throw new InvalidOperationException("Retail Seed Finder dialog was not opened.");
        dialog.Show();
        Application.DoEvents();
        var observations = string.Concat(Enumerable.Repeat("01", 64));

        FindControl<TextBox>(dialog, "TB_InputAnimations").Text = observations;
        FindControl<Button>(dialog, "OKButton").PerformClick();

        Assert.Multiple(() =>
        {
            Assert.That(doubles.RetailSeeds.LastExactRequest?.Observations, Is.EqualTo(observations));
            Assert.That(FindControl<TextBox>(dialog, "TB_Seed0").Text, Is.EqualTo("0123456789ABCDEF"));
            Assert.That(FindControl<TextBox>(dialog, "TB_Seed1").Text, Is.EqualTo("FEDCBA9876543210"));
            Assert.That(FindControl<TextBox>(control, "TB_Seed0").Text, Is.EqualTo("0123456789ABCDEF"));
            Assert.That(FindControl<TextBox>(control, "TB_Seed1").Text, Is.EqualTo("FEDCBA9876543210"));
            Assert.That(FindControl<TextBox>(control, "TB_CurrentS0").Text, Is.EqualTo("0123456789ABCDEF"));
            Assert.That(FindControl<TextBox>(control, "TB_CurrentS1").Text, Is.EqualTo("FEDCBA9876543210"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowRetailSeedFinderUsesRangeServiceInAdvancedMode()
    {
        var doubles = new OwoowServiceDoubles();
        doubles.RetailSeeds.RangeResults = [new RngState(0x1111222233334444, 0xAAAABBBBCCCCDDDD)];
        Form? opened = null;
        using var control = new OwoowTabControl(
            doubles.Services,
            (_, _) => { },
            (form, _) => opened = form);
        FindControl<Button>(control, "B_RetailSeedFinder").PerformClick();
        using var dialog = opened ?? throw new InvalidOperationException("Retail Seed Finder dialog was not opened.");
        dialog.Show();
        Application.DoEvents();
        FindControl<CheckBox>(dialog, "CB_Advanced").Checked = true;
        var observations = string.Concat(Enumerable.Repeat("01", 32));

        FindControl<TextBox>(dialog, "TB_InputAnimations").Text = observations;

        Assert.Multiple(() =>
        {
            Assert.That(doubles.RetailSeeds.LastRangeRequest?.Observations, Is.EqualTo(observations));
            Assert.That(doubles.RetailSeeds.LastRangeRequest?.MinimumAdvance, Is.EqualTo(400));
            Assert.That(doubles.RetailSeeds.LastRangeRequest?.MaximumAdvance, Is.EqualTo(527));
            Assert.That(FindControl<TextBox>(dialog, "TB_Status").Text, Is.EqualTo("Result found!"));
            Assert.That(FindControl<TextBox>(dialog, "TB_Seed0").Text, Is.EqualTo("1111222233334444"));
            Assert.That(FindControl<TextBox>(dialog, "TB_Seed1").Text, Is.EqualTo("AAAABBBBCCCCDDDD"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowMainRestoresPinnedFontScaleContract()
    {
        using var control = new OwoowTabControl();

        Assert.Multiple(() =>
        {
            Assert.That(control.AutoScaleMode, Is.EqualTo(AutoScaleMode.Font));
            Assert.That(control.AutoScaleDimensions, Is.EqualTo(new SizeF(7F, 15F)));
            Assert.That(FindControl<MenuStrip>(control, "MS_SubWindows").AutoSize, Is.True);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowMainCanvasExpandsWithLargeViewport()
    {
        using var control = new OwoowTabControl();
        using var host = new Form { ClientSize = new Size(1600, 900) };
        host.Controls.Add(control);
        host.Show();
        Application.DoEvents();

        var scrollHost = FindControl<Panel>(control, "owoowScrollHost");
        var canvas = FindControl<Panel>(control, "owoowMainCanvas");
        var results = FindControl<DataGridView>(control, "DGV_Results");

        Assert.Multiple(() =>
        {
            Assert.That(canvas.Width, Is.GreaterThanOrEqualTo(scrollHost.ClientSize.Width));
            Assert.That(canvas.Height, Is.GreaterThanOrEqualTo(scrollHost.ClientSize.Height));
            Assert.That(results.Right, Is.LessThanOrEqualTo(canvas.ClientSize.Width - 10));
            Assert.That(results.Bottom, Is.LessThanOrEqualTo(canvas.ClientSize.Height - 12));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void OwoowWildViewMatchesPinnedDesignerGeometry()
    {
        using var control = new OwoowTabControl();

        var pokemonSprite = FindControl<PictureBox>(control, "PB_PokemonSprite");
        var markSprite = FindControl<PictureBox>(control, "PB_MarkSprite");

        Assert.Multiple(() =>
        {
            Assert.That(FindControl<TextBox>(control, "TB_Wild").Bounds, Is.EqualTo(new Rectangle(6, 17, 181, 186)));
            Assert.That(pokemonSprite.Bounds, Is.EqualTo(new Rectangle(64, 203, 64, 64)));
            Assert.That(markSprite.Bounds, Is.EqualTo(new Rectangle(127, 219, 48, 48)));
            Assert.That(FindControl<Button>(control, "B_ReadEncounter").Bounds, Is.EqualTo(new Rectangle(4, 267, 183, 25)));
            Assert.That(FindControl<Button>(control, "B_CopyToFilter").Bounds, Is.EqualTo(new Rectangle(4, 294, 183, 25)));
            Assert.That(pokemonSprite.SizeMode, Is.EqualTo(PictureBoxSizeMode.CenterImage));
            Assert.That(markSprite.SizeMode, Is.EqualTo(PictureBoxSizeMode.CenterImage));
        });
    }

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

    private static void SelectOnly(ComboBox comboBox, string value)
    {
        comboBox.Items.Clear();
        comboBox.Items.Add(value);
        comboBox.SelectedIndex = 0;
    }

    private static void PumpMessagesUntil(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(2);
        while (!condition() && DateTime.UtcNow < timeout)
        {
            Application.DoEvents();
            Thread.Yield();
        }
    }

    private static ToolStripMenuItem FindMenuItem(MenuStrip menu, string name) =>
        menu.Items.Cast<ToolStripItem>().OfType<ToolStripMenuItem>().Single(item => item.Name == name);

    private static IEnumerable<string> DescendantTexts(Control root)
    {
        if (!string.IsNullOrWhiteSpace(root.Text))
        {
            yield return root.Text;
        }

        foreach (Control child in root.Controls)
        {
            foreach (var text in DescendantTexts(child))
            {
                yield return text;
            }
        }
    }

    private sealed class OwoowServiceDoubles
    {
        public OwoowServiceDoubles()
        {
            Services = new OwoowUiServices(
                Connection,
                Profiles,
                Catalog,
                Encounters,
                Calibration,
                RetailSeeds,
                Xoroshiro,
                SpreadFinder,
                SpecialTools,
                LotoIds);
        }

        public StubConnectionService Connection { get; } = new();
        public StubProfileStore Profiles { get; } = new();
        public StubEncounterCatalogService Catalog { get; } = new();
        public StubOverworldEncounterService Encounters { get; } = new();
        public StubCalibrationService Calibration { get; } = new();
        public StubRetailSeedService RetailSeeds { get; } = new();
        public StubXoroshiroService Xoroshiro { get; } = new();
        public StubSpreadFinderService SpreadFinder { get; } = new();
        public StubSpecialToolService SpecialTools { get; } = new();
        public StubLotoIdStore LotoIds { get; } = new();
        public OwoowUiServices Services { get; }
    }

    private sealed class StubConnectionService : IOwoowConnectionService
    {
        public ConnectionStatus Status { get; private set; } = new(ConnectionState.Disconnected, "Disconnected.");
        public OwoowConnectionSettings? LastConnectionSettings { get; private set; }
        public Exception? ConnectException { get; set; }
        public RngState CurrentState { get; set; } = new(0x1111222233334444, 0xAAAABBBBCCCCDDDD);
        public TrainerSnapshot Trainer { get; set; } = new(1337, 1390, true, true);
        public int DexRecommendationReads { get; private set; }
        public int WildPokemonReads { get; private set; }
        public int SkipDayCalls { get; private set; }
        public int SkipDayBackCalls { get; private set; }
        public int ResetNetworkTimeCalls { get; private set; }
        public List<RngState> WrittenStates { get; } = [];
        public PokemonSnapshot WildPokemon { get; set; } = new(
            1,
            "Bulbasaur",
            0,
            5,
            PokemonGender.Male,
            PokemonShinyType.None,
            "Hardy",
            1,
            0,
            0x12345678,
            0x87654321,
            new RngIndividualValues([31, 30, 29, 28, 27, 26]),
            128,
            null,
            [1, 2]);

        public event EventHandler<ConnectionStatusChangedEventArgs>? StatusChanged;

        public Task<ConnectionStatus> ConnectAsync(OwoowConnectionSettings settings, CancellationToken cancellationToken = default)
        {
            LastConnectionSettings = settings;
            if (ConnectException is not null)
            {
                throw ConnectException;
            }

            Status = new ConnectionStatus(ConnectionState.Connected, "Connected.");
            StatusChanged?.Invoke(this, new ConnectionStatusChangedEventArgs(Status));
            return Task.FromResult(Status);
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            Status = new ConnectionStatus(ConnectionState.Disconnected, "Disconnected.");
            StatusChanged?.Invoke(this, new ConnectionStatusChangedEventArgs(Status));
            return Task.CompletedTask;
        }

        public Task<RngState> ReadRngStateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CurrentState);

        public Task WriteRngStateAsync(RngState state, CancellationToken cancellationToken = default)
        {
            CurrentState = state;
            WrittenStates.Add(state);
            return Task.CompletedTask;
        }

        public Task<TrainerSnapshot> ReadTrainerAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Trainer);

        public Task<DexRecommendationSnapshot> ReadDexRecommendationAsync(bool full, CancellationToken cancellationToken = default)
        {
            DexRecommendationReads++;
            return Task.FromResult(new DexRecommendationSnapshot([1, 2, 3, 4], "Test", 1));
        }

        public Task<PokemonSnapshot> ReadWildPokemonAsync(CancellationToken cancellationToken = default)
        {
            WildPokemonReads++;
            return Task.FromResult(WildPokemon);
        }

        public Task<WorldSnapshot> ReadWorldAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorldSnapshot(0, new WorldPosition(0, 0, 0), []));

        public async IAsyncEnumerable<RngStateUpdate> WatchRngStateAsync(
            RngWatchRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task SkipDayAsync(CancellationToken cancellationToken = default)
        {
            SkipDayCalls++;
            return Task.CompletedTask;
        }

        public Task SkipDayBackAsync(CancellationToken cancellationToken = default)
        {
            SkipDayBackCalls++;
            return Task.CompletedTask;
        }

        public Task ResetNetworkTimeAsync(CancellationToken cancellationToken = default)
        {
            ResetNetworkTimeCalls++;
            return Task.CompletedTask;
        }
        public Task<ulong> GetCurrentTimeAsync(CancellationToken cancellationToken = default) => Task.FromResult(0UL);
        public Task SetCurrentTimeAsync(ulong value, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubProfileStore : IProfileStore
    {
        public RngApplicationSettings Settings { get; set; } = new(null, []);
        public int SaveCalls { get; private set; }

        public Task<RngApplicationSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Settings);

        public Task SaveAsync(RngApplicationSettings settings, CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            Settings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class StubEncounterCatalogService : IEncounterCatalogService
    {
        public List<(GameVersion Game, EncounterKind Kind)> AreaRequests { get; } = [];
        public (GameVersion Game, string Species)? LastLookup { get; private set; }
        public IReadOnlyList<EncounterLookupResult> LookupResults { get; set; } = [];

        public Task<IReadOnlyList<string>> GetAreasAsync(GameVersion game, EncounterKind kind, CancellationToken cancellationToken = default)
        {
            AreaRequests.Add((game, kind));
            return Task.FromResult<IReadOnlyList<string>>(["Rolling Fields"]);
        }

        public Task<IReadOnlyList<string>> GetWeatherAsync(GameVersion game, EncounterKind kind, string area, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(["Normal Weather"]);

        public Task<IReadOnlyList<string>> GetSpeciesAsync(GameVersion game, EncounterKind kind, string area, string weather, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(["Pikachu"]);

        public Task<EncounterTableResult> GetTableAsync(EncounterCatalogRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EncounterTableResult([], [], []));

        public Task<IReadOnlyList<EncounterLookupResult>> LookupAsync(GameVersion game, string species, CancellationToken cancellationToken = default)
        {
            LastLookup = (game, species);
            return Task.FromResult(LookupResults);
        }

        public Task<IReadOnlyList<DexRecommendationOption>> GetDexRecommendationOptionsAsync(bool includeNone = true, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DexRecommendationOption>>(
            [
                new("(None)", 0),
                new("Pikachu", 25),
                new("Eevee", 133),
                new("Grookey", 810),
                new("Scorbunny", 813),
            ]);
    }

    private sealed class StubOverworldEncounterService : IOverworldEncounterService
    {
        public OverworldSearchRequest? LastRequest { get; private set; }
        public IReadOnlyList<OverworldEncounterResult> Results { get; set; } = [];

        public Task<IReadOnlyList<OverworldEncounterResult>> SearchAsync(
            OverworldSearchRequest request,
            IProgress<OperationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(Results);
        }
    }

    private sealed class StubCalibrationService : ICalibrationService
    {
        public MenuCloseCalibrationRequest? LastMenuCloseRequest { get; private set; }
        public RainCalibrationRequest? LastRainRequest { get; private set; }
        public CalibrationResult Result { get; set; } = new(0, new RngState(0, 0));

        public Task<CalibrationResult> CalculateMenuCloseAsync(MenuCloseCalibrationRequest request, CancellationToken cancellationToken = default)
        {
            LastMenuCloseRequest = request;
            return Task.FromResult(Result);
        }

        public Task<CalibrationResult> CalculateRainAsync(RainCalibrationRequest request, CancellationToken cancellationToken = default)
        {
            LastRainRequest = request;
            return Task.FromResult(Result);
        }

        public Task<CalibrationResult> CalculateAreaLoadAsync(AreaLoadCalibrationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CalibrationResult(0, request.State));

        public Task<CalibrationResult> CalculateFlyAsync(FlyCalibrationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CalibrationResult(0, request.State));
    }

    private sealed class StubRetailSeedService : IRetailSeedService
    {
        public RetailSeedRequest? LastExactRequest { get; private set; }
        public RetailRangeSeedRequest? LastRangeRequest { get; private set; }
        public AnimationSequenceRequest? LastAnimationRequest { get; private set; }
        public ReidentifySeedRequest? LastReidentifyRequest { get; private set; }
        public RngState ExactResult { get; set; } = new(1, 2);
        public IReadOnlyList<RngState> RangeResults { get; set; } = [];
        public ReidentifySeedResult ReidentifyResult { get; set; } = new(1, 0, new RngState(1, 2));

        public Task<RngState> FindExactAsync(RetailSeedRequest request, CancellationToken cancellationToken = default)
        {
            LastExactRequest = request;
            return Task.FromResult(ExactResult);
        }

        public Task<IReadOnlyList<RngState>> FindRangeAsync(RetailRangeSeedRequest request, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            LastRangeRequest = request;
            return Task.FromResult(RangeResults);
        }

        public Task<AnimationSequenceResult> GenerateAnimationSequenceAsync(AnimationSequenceRequest request, CancellationToken cancellationToken = default)
        {
            LastAnimationRequest = request;
            return Task.FromResult(new AnimationSequenceResult([0, 1, 0, 1, 0, 1, 0], request.State));
        }

        public Task<ReidentifySeedResult> ReidentifyAsync(ReidentifySeedRequest request, CancellationToken cancellationToken = default)
        {
            LastReidentifyRequest = request;
            return Task.FromResult(ReidentifyResult);
        }
    }

    private sealed class StubXoroshiroService : IXoroshiroService
    {
        public List<XoroshiroRequest> Requests { get; } = [];
        public XoroshiroRequest? LastRequest => Requests.LastOrDefault();
        public RngState ResultState { get; set; } = new(1, 2);

        public Task<XoroshiroResult> CalculateAsync(XoroshiroRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new XoroshiroResult(
                ResultState,
                request.Amount,
                request.Operation == XoroshiroOperation.NextInteger ? 42UL : null,
                true));
        }

        public Task<FixedSeedResult> GenerateFixedAsync(FixedSeedRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FixedSeedResult(1, 2, new RngIndividualValues([1, 2, 3, 4, 5, 6]), 7));
    }

    private sealed class StubSpreadFinderService : ISpreadFinderService
    {
        public List<SpreadSearchRequest> Requests { get; } = [];
        public IReadOnlyList<SpreadSearchResult> Results { get; set; } = [];

        public Task<IReadOnlyList<SpreadSearchResult>> SearchAsync(SpreadSearchRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(Results);
        }
    }

    private sealed class StubSpecialToolService : ISpecialRngToolService
    {
        public List<SpecialToolSearchRequest> Requests { get; } = [];
        public bool WaitForCancellation { get; set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<IReadOnlyList<SpecialToolFrame>> SearchAsync(
            SpecialToolSearchRequest request,
            IProgress<OperationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            LastCancellationToken = cancellationToken;
            if (WaitForCancellation)
            {
                return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                    .ContinueWith<IReadOnlyList<SpecialToolFrame>>(
                        _ => [],
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
            }
            return Task.FromResult<IReadOnlyList<SpecialToolFrame>>(
            [
                new SpecialToolFrame(
                    request.Kind,
                    1,
                    2,
                    'P',
                    new RngState(3, 4),
                    identifier: "123456",
                    primaryResult: "Primary",
                    secondaryResult: "Secondary",
                    bonus: true,
                    watts: 500000,
                    total: 5,
                    success: true),
            ]);
        }
    }

    private sealed class StubLotoIdStore : ILotoIdStore
    {
        public IReadOnlyList<string> LoadedIds { get; set; } = [];
        public IReadOnlyList<string>? SavedIds { get; private set; }
        public TaskCompletionSource<IReadOnlyList<string>>? PendingLoad { get; set; }
        public Queue<TaskCompletionSource<object?>> SaveCompletions { get; } = [];
        public List<IReadOnlyList<string>> SaveSnapshots { get; } = [];

        public Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken = default) =>
            PendingLoad?.Task ?? Task.FromResult(LoadedIds);

        public Task SaveAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken = default)
        {
            SavedIds = ids.ToArray();
            SaveSnapshots.Add(SavedIds);
            return SaveCompletions.Count > 0
                ? SaveCompletions.Dequeue().Task
                : Task.CompletedTask;
        }
    }
}
