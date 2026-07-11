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
                Is.EqualTo(new[] { "(None)", "Pikachu" }));
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
                SpecialTools);
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

        public Task<RngApplicationSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Settings);

        public Task SaveAsync(RngApplicationSettings settings, CancellationToken cancellationToken = default)
        {
            Settings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class StubEncounterCatalogService : IEncounterCatalogService
    {
        public List<(GameVersion Game, EncounterKind Kind)> AreaRequests { get; } = [];

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

        public Task<IReadOnlyList<EncounterLookupResult>> LookupAsync(GameVersion game, string species, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EncounterLookupResult>>([]);

        public Task<IReadOnlyList<string>> GetDexRecommendationOptionsAsync(bool includeNone = true, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(["(None)", "Pikachu"]);
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
        public AnimationSequenceRequest? LastAnimationRequest { get; private set; }
        public ReidentifySeedRequest? LastReidentifyRequest { get; private set; }
        public ReidentifySeedResult ReidentifyResult { get; set; } = new(1, 0, new RngState(1, 2));

        public Task<RngState> FindExactAsync(RetailSeedRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RngState(1, 2));

        public Task<IReadOnlyList<RngState>> FindRangeAsync(RetailRangeSeedRequest request, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RngState>>([]);

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
        public XoroshiroRequest? LastRequest { get; private set; }
        public RngState ResultState { get; set; } = new(1, 2);

        public Task<XoroshiroResult> CalculateAsync(XoroshiroRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new XoroshiroResult(ResultState, request.Amount, null, true));
        }

        public Task<FixedSeedResult> GenerateFixedAsync(FixedSeedRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FixedSeedResult(1, 2, new RngIndividualValues([1, 2, 3, 4, 5, 6]), 7));
    }

    private sealed class StubSpreadFinderService : ISpreadFinderService
    {
        public Task<IReadOnlyList<SpreadSearchResult>> SearchAsync(SpreadSearchRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SpreadSearchResult>>([]);
    }

    private sealed class StubSpecialToolService : ISpecialRngToolService
    {
        public Task<IReadOnlyList<SpecialToolFrame>> SearchAsync(
            SpecialToolSearchRequest request,
            IProgress<OperationProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SpecialToolFrame>>([]);
    }
}
