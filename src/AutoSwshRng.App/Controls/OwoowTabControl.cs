using AutoSwshRng.Core.Connection;
using AutoSwshRng.Core.Common;
using AutoSwshRng.Core.Encounters;
using AutoSwshRng.Core.Profiles;
using AutoSwshRng.Core.Rng;
using System.Globalization;

namespace AutoSwshRng.App.Controls;

public sealed class OwoowTabControl : UserControl
{
    private const int CanvasWidth = 1278;
    private const int CanvasHeight = 676;
    private static readonly int[] ResultColumnWidths =
    [
        83, 61, 55, 88, 71, 61, 72, 59, 66, 68, 70, 48, 50, 50, 53, 53, 51,
        59, 46, 50, 68, 56, 85, 63, 63,
    ];
    private static readonly IReadOnlyDictionary<string, string> SpecialToolPrefixes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["LotoID"] = "LotoID",
            ["Cramomatic"] = "Cramomatic",
            ["WattTrader"] = "WattTrader",
            ["DiggingPa"] = "DiggingPa",
            ["SkillBro"] = "SkillBro",
            ["WailordRespawn"] = "Wailord",
        };
    private readonly OwoowUiServices services;
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly Action<string, string> showMessage;
    private readonly Action<Form, bool> presentToolWindow;
    private readonly Action resultTone;
    private readonly Action resultFocus;
    private readonly OwoowToolWindowFactory toolWindowFactory;
    private readonly Dictionary<string, Form> openToolWindows = new(StringComparer.Ordinal);
    private CancellationTokenSource? searchCancellation;
    private CancellationTokenSource? skipCancellation;
    private IReadOnlyList<DexRecommendationOption> dexRecommendationOptions = [];
    private PokemonSnapshot? cachedWildPokemon;
    private AnimationSequenceResult? retailSequence;
    private ulong retailInitial;
    private bool updatingCatalog;
    private bool updatingRetailPattern;
    private bool resourcesDisposed;

    public OwoowTabControl(
        OwoowUiServices? services = null,
        Action<string, string>? messageSink = null,
        Action<Form, bool>? toolWindowPresenter = null,
        Action? resultTone = null,
        Action? resultFocus = null)
    {
        this.services = services ?? OwoowUiServices.CreateDefault();
        showMessage = messageSink ?? ((title, message) =>
            MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Error));
        presentToolWindow = toolWindowPresenter ?? PresentToolWindow;
        this.resultTone = resultTone ?? System.Media.SystemSounds.Asterisk.Play;
        this.resultFocus = resultFocus ?? (() =>
        {
            var form = FindForm();
            if (form is not null)
            {
                form.Activate();
            }
        });
        toolWindowFactory = new OwoowToolWindowFactory(this.services, showMessage);

        SuspendLayout();
        Dock = DockStyle.Fill;
        Font = new Font("Segoe UI", 9F);
        ForeColor = AppVisualTheme.Ink;
        BackColor = AppVisualTheme.Workspace;

        var scrollHost = new Panel
        {
            Name = "owoowScrollHost",
            Dock = DockStyle.Fill,
            AutoScroll = true,
            AutoScrollMinSize = new Size(CanvasWidth, CanvasHeight),
            BackColor = AppVisualTheme.Workspace,
        };

        var canvas = new Panel
        {
            Name = "owoowMainCanvas",
            Location = Point.Empty,
            Size = new Size(CanvasWidth, CanvasHeight),
            MinimumSize = new Size(CanvasWidth, CanvasHeight),
            BackColor = AppVisualTheme.Workspace,
        };
        canvas.Controls.Add(CreateResultsGrid());
        canvas.Controls.Add(CreateSeedControlsContainer());
        canvas.Controls.Add(CreateSubWindowsMenu());

        scrollHost.Controls.Add(canvas);
        void ResizeCanvasToViewport()
        {
            canvas.Size = new Size(
                Math.Max(canvas.MinimumSize.Width, scrollHost.ClientSize.Width),
                Math.Max(canvas.MinimumSize.Height, scrollHost.ClientSize.Height));
            var seedControls = canvas.Controls["GB_SeedControlsContainer"];
            if (seedControls is not null)
            {
                seedControls.Left = Math.Max(0, (canvas.ClientSize.Width - seedControls.Width) / 2);
            }
        }
        scrollHost.ClientSizeChanged += (_, _) => ResizeCanvasToViewport();
        scrollHost.HandleCreated += (_, _) => ResizeCanvasToViewport();
        Controls.Add(scrollHost);
        AppVisualTheme.ApplyOwoowTheme(this);

        WireConnectionActions();
        WireEncounterActions();
        WireCatalogActions();
        WireConnectedUtilityActions();
        WireRetailActions();
        WireToolWindowActions();
        this.services.Connection.StatusChanged += ConnectionStatusChanged;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(false);
    }

    internal OwoowUiServices Services => services;

    protected override void Dispose(bool disposing)
    {
        if (disposing && !resourcesDisposed)
        {
            resourcesDisposed = true;
            services.Connection.StatusChanged -= ConnectionStatusChanged;
            searchCancellation?.Cancel();
            searchCancellation?.Dispose();
            skipCancellation?.Cancel();
            skipCancellation?.Dispose();
            lifetimeCancellation.Cancel();
            lifetimeCancellation.Dispose();
            foreach (var form in openToolWindows.Values.ToArray())
            {
                form.Dispose();
            }
            openToolWindows.Clear();
        }

        base.Dispose(disposing);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        if (Controls.Find("DGV_Results", true).FirstOrDefault() is DataGridView grid)
        {
            ApplyResultColumnScale(grid);
        }
    }

    private void WireConnectionActions()
    {
        FindRequiredControl<Button>("B_Connect").Click += async (_, _) => await ConnectAsync();
        FindRequiredControl<Button>("B_Disconnect").Click += async (_, _) => await DisconnectAsync();
        FindRequiredControl<Button>("B_CopyToInitial").Click += (_, _) => CopyCurrentStateToInitial();
    }

    private void WireEncounterActions()
    {
        foreach (var kind in Enum.GetValues<EncounterKind>())
        {
            var kindName = kind.ToString();
            FindRequiredControl<Button>($"B_{kindName}_Search").Click += async (_, _) =>
                await SearchEncountersAsync(kind);
            FindRequiredControl<Button>($"B_{kindName}_MenuClose").Click += async (_, _) =>
                await CalculateMenuCloseAsync(kind);

            var menuClose = FindRequiredControl<CheckBox>($"CB_{kindName}_MenuClose");
            menuClose.CheckedChanged += (_, _) =>
            {
                FindRequiredControl<Button>($"B_{kindName}_MenuClose").Enabled = menuClose.Checked;
                FindRequiredControl<CheckBox>($"CB_{kindName}_MenuClose_Direction").Enabled = menuClose.Checked;
                FindRequiredControl<TextBox>($"TB_{kindName}_NPCs").Enabled = menuClose.Checked;
                FindRequiredControl<Label>($"L_{kindName}_NPCs").Enabled = menuClose.Checked;
                SynchronizeSpecialToolOption("MenuClose", menuClose.Checked);
            };
            var holdDirection = FindRequiredControl<CheckBox>($"CB_{kindName}_MenuClose_Direction");
            holdDirection.CheckedChanged += (_, _) =>
                SynchronizeSpecialToolOption("MenuClose_Direction", holdDirection.Checked);
        }

        foreach (var stat in new[] { "HP", "Atk", "Def", "SpA", "SpD", "Spe" })
        {
            var minimum = FindRequiredControl<NumericUpDown>($"NUD_{stat}_Min");
            var maximum = FindRequiredControl<NumericUpDown>($"NUD_{stat}_Max");
            FindRequiredControl<Button>($"B_{stat}_Min").Click += (_, _) => minimum.Value = 0;
            FindRequiredControl<Button>($"B_{stat}_Max").Click += (_, _) => maximum.Value = 31;
        }

        FindRequiredControl<Button>("B_CalculateRain").Click += async (_, _) => await CalculateRainAsync();
    }

    private async Task CalculateMenuCloseAsync(EncounterKind kind)
    {
        var kindName = kind.ToString();
        var button = FindRequiredControl<Button>($"B_{kindName}_MenuClose");
        button.Enabled = false;
        try
        {
            var request = new MenuCloseCalibrationRequest(
                ReadInitialState(),
                ParseUInt32(FindRequiredControl<TextBox>($"TB_{kindName}_NPCs").Text, "NPCs"),
                FindRequiredControl<CheckBox>($"CB_{kindName}_MenuClose_Direction").Checked,
                ParseWeather(FindRequiredControl<ComboBox>($"CB_{kindName}_Weather").Text));
            var result = await services.Calibration.CalculateMenuCloseAsync(request, lifetimeCancellation.Token);
            FindRequiredControl<TextBox>("TB_Status").Text = $"Menu close: {result.Advances} advances.";
        }
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            FindRequiredControl<TextBox>("TB_Status").Text = exception.Message;
            showMessage("Menu-close calibration failed", exception.Message);
        }
        finally
        {
            button.Enabled = FindRequiredControl<CheckBox>($"CB_{kindName}_MenuClose").Checked;
        }
    }

    private async Task CalculateRainAsync()
    {
        var button = FindRequiredControl<Button>("B_CalculateRain");
        button.Enabled = false;
        try
        {
            var request = new RainCalibrationRequest(
                ReadInitialState(),
                (uint)FindRequiredControl<NumericUpDown>("NUD_RainTick").Value);
            var result = await services.Calibration.CalculateRainAsync(request, lifetimeCancellation.Token);
            FindRequiredControl<TextBox>("TB_Status").Text = $"Rain: {result.Advances} advances.";
        }
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            FindRequiredControl<TextBox>("TB_Status").Text = exception.Message;
            showMessage("Rain calibration failed", exception.Message);
        }
        finally
        {
            button.Enabled = true;
        }
    }

    private RngState ReadInitialState() => new(
        ParseHexUInt64(FindRequiredControl<TextBox>("TB_Seed0").Text, "Seed[0]"),
        ParseHexUInt64(FindRequiredControl<TextBox>("TB_Seed1").Text, "Seed[1]"));

    private static Weather ParseWeather(string value) => value switch
    {
        "Overcast" => Weather.Overcast,
        "Raining" => Weather.Raining,
        "Thunderstorm" => Weather.Thunderstorm,
        "Intense Sun" or "IntenseSun" => Weather.IntenseSun,
        "Snowing" => Weather.Snowing,
        "Snowstorm" => Weather.Snowstorm,
        "Sandstorm" => Weather.Sandstorm,
        "Heavy Fog" or "HeavyFog" => Weather.HeavyFog,
        _ => Weather.Normal,
    };

    private void WireCatalogActions()
    {
        Load += async (_, _) => await InitializeCatalogAsync();
        FindRequiredControl<ComboBox>("CB_Game").SelectedIndexChanged += async (_, _) =>
            await LoadCatalogForCurrentTabAsync();
        FindRequiredControl<TabControl>("TC_EncounterType").SelectedIndexChanged += async (_, _) =>
            await LoadCatalogForCurrentTabAsync();

        foreach (var kind in Enum.GetValues<EncounterKind>())
        {
            var kindName = kind.ToString();
            FindRequiredControl<ComboBox>($"CB_{kindName}_Area").SelectedIndexChanged += async (_, _) =>
                await LoadWeatherAndSpeciesAsync(kind);
            FindRequiredControl<ComboBox>($"CB_{kindName}_Weather").SelectedIndexChanged += async (_, _) =>
                await LoadSpeciesAsync(kind);
        }
    }

    private void WireConnectedUtilityActions()
    {
        FindRequiredControl<Button>("B_RefreshDexRec").Click += async (_, _) => await RefreshDexRecommendationsAsync();
        FindRequiredControl<Button>("B_ReadEncounter").Click += async (_, _) => await ReadWildEncounterAsync();
        FindRequiredControl<Button>("B_CopyToFilter").Click += (_, _) => CopyWildEncounterToFilter();
        FindRequiredControl<Button>("B_SkipForward").Click += async (_, _) => await SkipDaysAsync(forward: true);
        FindRequiredControl<Button>("B_SkipBack").Click += async (_, _) => await SkipDaysAsync(forward: false);
        FindRequiredControl<Button>("B_CancelSkip").Click += (_, _) => skipCancellation?.Cancel();
        FindRequiredControl<Button>("B_SkipAdvance").Click += async (_, _) => await AdvanceConnectedStateAsync();
        FindRequiredControl<Button>("B_NTP").Click += async (_, _) => await ResetNetworkTimeAsync();
    }

    private void WireRetailActions()
    {
        FindRequiredControl<Button>("B_RetailSeedFinder").Click += (_, _) =>
            OpenToolWindow(toolWindowFactory.CreateRetailSeedFinder(ApplyRetailState), modal: true);
        FindRequiredControl<Button>("B_GenerateRetailPattern").Click += async (_, _) =>
            await GenerateRetailPatternAsync();
        FindRequiredControl<Button>("B_RetailUpdateSeeds").Click += async (_, _) =>
            await UpdateRetailSeedsAsync();
        FindRequiredControl<TextBox>("TB_Animations").TextChanged += async (_, _) =>
            await ReidentifyRetailSeedAsync();
    }

    private void WireToolWindowActions()
    {
        var menu = FindRequiredControl<MenuStrip>("MS_SubWindows");
        FindMenuItem(menu, "TSMI_Profiles").Click += (_, _) =>
            OpenToolWindow(toolWindowFactory.CreateProfiles(ApplyProfile), modal: true);
        FindMenuItem(menu, "TSMI_EncounterLookup").Click += (_, _) =>
            OpenToolWindow(toolWindowFactory.CreateEncounterLookup(GetSelectedGame()), modal: false);
        FindMenuItem(menu, "TSMI_SpreadFinder").Click += (_, _) =>
            OpenToolWindow(toolWindowFactory.CreateSpreadFinder(), modal: false);
        FindMenuItem(menu, "TSMI_LotoID").Click += (_, _) =>
            OpenSpecialToolWindow(SpecialToolKind.LotoId);
        FindMenuItem(menu, "TSMI_Cramomatic").Click += (_, _) =>
            OpenSpecialToolWindow(SpecialToolKind.CramOMatic);
        FindMenuItem(menu, "TSMI_WattTrader").Click += (_, _) =>
            OpenSpecialToolWindow(SpecialToolKind.WattTrader);
        FindMenuItem(menu, "TSMI_DiggingPa").Click += (_, _) =>
            OpenSpecialToolWindow(SpecialToolKind.DiggingPa);
        FindMenuItem(menu, "TMSI_SkillBro").Click += (_, _) =>
            OpenSpecialToolWindow(SpecialToolKind.DiggingBro);
        FindMenuItem(menu, "TSMI_WailordRespawn").Click += (_, _) =>
            OpenSpecialToolWindow(SpecialToolKind.WailordRespawn);
        FindMenuItem(menu, "TSMI_XoroshiroTools").Click += (_, _) =>
            OpenToolWindow(toolWindowFactory.CreateXoroshiroTools(ReadInitialState(), ApplyInitialState), modal: false);
    }

    private void OpenSpecialToolWindow(SpecialToolKind kind)
    {
        try
        {
            OpenToolWindow(
                toolWindowFactory.CreateSpecialTool(
                    kind,
                    ReadInitialState(),
                    GetSelectedGame(),
                    CreateSpecialToolContext()),
                modal: false);
        }
        catch (Exception exception)
        {
            showMessage($"{kind} failed", exception.Message);
        }
    }

    private OwoowSpecialToolContext CreateSpecialToolContext()
    {
        var tabs = FindRequiredControl<TabControl>("TC_EncounterType");
        var kind = (EncounterKind)Math.Clamp(tabs.SelectedIndex, 0, Enum.GetValues<EncounterKind>().Length - 1);
        var prefix = kind.ToString();
        var weather = FindRequiredControl<ComboBox>($"CB_{prefix}_Weather").Text;
        if (weather is "None" or "")
        {
            weather = "All Weather";
        }

        return new OwoowSpecialToolContext(
            FindRequiredControl<TextBox>("TB_CurrentAdvances").Text.Replace(",", string.Empty, StringComparison.Ordinal),
            FindRequiredControl<TextBox>($"TB_{prefix}_NPCs").Text,
            FindRequiredControl<CheckBox>($"CB_{prefix}_MenuClose").Checked,
            FindRequiredControl<CheckBox>($"CB_{prefix}_MenuClose_Direction").Checked,
            weather,
            state => ApplySpecialToolState(prefix, state));
    }

    private void ApplySpecialToolState(string prefix, OwoowSpecialToolState state)
    {
        FindRequiredControl<CheckBox>($"CB_{prefix}_MenuClose").Checked = state.MenuClose;
        FindRequiredControl<CheckBox>($"CB_{prefix}_MenuClose_Direction").Checked = state.HoldDirection;
        FindRequiredControl<TextBox>($"TB_{prefix}_NPCs").Text = state.NonPlayerCharacters;
    }

    private void ApplyProfile(RngProfile profile)
    {
        FindRequiredControl<ComboBox>("CB_Game").SelectedIndex = (int)profile.Game;
        FindRequiredControl<TextBox>("TB_TID").Text = profile.TrainerId.ToString("D5", CultureInfo.InvariantCulture);
        FindRequiredControl<TextBox>("TB_SID").Text = profile.SecretId.ToString("D5", CultureInfo.InvariantCulture);
        FindRequiredControl<CheckBox>("CB_ShinyCharm").Checked = profile.HasShinyCharm;
        FindRequiredControl<CheckBox>("CB_MarkCharm").Checked = profile.HasMarkCharm;
    }

    private void ApplyInitialState(RngState state)
    {
        var seed0 = state.Seed0.ToString("X16", CultureInfo.InvariantCulture);
        var seed1 = state.Seed1.ToString("X16", CultureInfo.InvariantCulture);
        FindRequiredControl<TextBox>("TB_Seed0").Text = seed0;
        FindRequiredControl<TextBox>("TB_Seed1").Text = seed1;
        SynchronizeSpecialToolSeeds(seed0, seed1);
    }

    private void ApplyRetailState(RngState state)
    {
        ApplyInitialState(state);
        FindRequiredControl<TextBox>("TB_CurrentS0").Text =
            state.Seed0.ToString("X16", CultureInfo.InvariantCulture);
        FindRequiredControl<TextBox>("TB_CurrentS1").Text =
            state.Seed1.ToString("X16", CultureInfo.InvariantCulture);
    }

    private void OpenToolWindow(Form form, bool modal)
    {
        if (!modal)
        {
            if (openToolWindows.TryGetValue(form.Name, out var existing))
            {
                if (!existing.IsDisposed)
                {
                    form.Dispose();
                    existing.Focus();
                    return;
                }
                openToolWindows.Remove(form.Name);
            }

            openToolWindows[form.Name] = form;
            form.FormClosed += (_, _) => openToolWindows.Remove(form.Name);
        }
        presentToolWindow(form, modal);
    }

    private void PresentToolWindow(Form form, bool modal)
    {
        var owner = FindForm();
        if (modal)
        {
            try
            {
                if (owner is null)
                {
                    form.ShowDialog();
                }
                else
                {
                    form.ShowDialog(owner);
                }
            }
            finally
            {
                form.Dispose();
            }

            return;
        }

        if (owner is null)
        {
            form.Show();
        }
        else
        {
            form.Show(owner);
        }
    }

    private static ToolStripMenuItem FindMenuItem(MenuStrip menu, string name)
    {
        return menu.Items.Cast<ToolStripItem>()
            .OfType<ToolStripMenuItem>()
            .Single(item => item.Name == name);
    }

    private async Task UpdateRetailSeedsAsync()
    {
        FindRequiredControl<TextBox>("TB_CurrentAdvances").Text = "0";
        CopyCurrentStateToInitial();
        await GenerateRetailPatternAsync();
        updatingRetailPattern = true;
        try
        {
            FindRequiredControl<TextBox>("TB_Animations").Clear();
        }
        finally
        {
            updatingRetailPattern = false;
        }

        FindRequiredControl<TextBox>("TB_Animations").Focus();
    }

    private async Task GenerateRetailPatternAsync()
    {
        var button = FindRequiredControl<Button>("B_GenerateRetailPattern");
        button.Enabled = false;
        try
        {
            var seed0 = ParseHexUInt64(FindRequiredControl<TextBox>("TB_Seed0").Text, "Seed[0]");
            var seed1 = ParseHexUInt64(FindRequiredControl<TextBox>("TB_Seed1").Text, "Seed[1]");
            retailInitial = ParseUInt64(FindRequiredControl<TextBox>("TB_RetailInitial").Text, "Retail initial");
            var countValue = ParseUInt64(FindRequiredControl<TextBox>("TB_RetailRange").Text, "Retail range");
            if (countValue is 0 or > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException("Retail range", "Retail range must be between 1 and 2147483647.");
            }

            retailSequence = await services.RetailSeeds.GenerateAnimationSequenceAsync(
                new AnimationSequenceRequest(new RngState(seed0, seed1), retailInitial, (int)countValue),
                lifetimeCancellation.Token);
            FindRequiredControl<TextBox>("TB_RetailAdvances").Text = "Need more inputs";
            FindRequiredControl<TextBox>("TB_Status").Text = "Retail pattern generated.";
        }
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            retailSequence = null;
            FindRequiredControl<TextBox>("TB_RetailAdvances").Text = exception.Message;
            FindRequiredControl<TextBox>("TB_Status").Text = exception.Message;
            showMessage("Retail pattern failed", exception.Message);
        }
        finally
        {
            button.Enabled = true;
        }
    }

    private async Task ReidentifyRetailSeedAsync()
    {
        if (updatingRetailPattern)
        {
            return;
        }

        var pattern = FindRequiredControl<TextBox>("TB_Animations").Text.Trim();
        if (retailSequence is null)
        {
            FindRequiredControl<TextBox>("TB_RetailAdvances").Text = "Generate First ↑";
            return;
        }

        if (pattern.Length <= 5)
        {
            FindRequiredControl<TextBox>("TB_RetailAdvances").Text = "Need more inputs";
            return;
        }

        try
        {
            var result = await services.RetailSeeds.ReidentifyAsync(
                new ReidentifySeedRequest(retailSequence.Observations, retailSequence.InitialState, pattern),
                lifetimeCancellation.Token);
            if (result.Hits == 1)
            {
                var advances = checked((ulong)result.Advances + retailInitial);
                FindRequiredControl<TextBox>("TB_RetailAdvances").Text = advances.ToString(CultureInfo.InvariantCulture);
                FindRequiredControl<TextBox>("TB_CurrentAdvances").Text = advances.ToString(CultureInfo.InvariantCulture);
                UpdateCurrentState(result.State);
            }
            else if (result.Hits == 0)
            {
                FindRequiredControl<TextBox>("TB_RetailAdvances").Text = "No results";
            }
            else
            {
                FindRequiredControl<TextBox>("TB_RetailAdvances").Text = $"{result.Hits} results";
            }
        }
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            FindRequiredControl<TextBox>("TB_RetailAdvances").Text = exception.Message;
            showMessage("Retail re-identification failed", exception.Message);
        }
    }

    private async Task RefreshDexRecommendationsAsync()
    {
        var button = FindRequiredControl<Button>("B_RefreshDexRec");
        button.Enabled = false;
        try
        {
            var snapshot = await services.Connection.ReadDexRecommendationAsync(
                (ModifierKeys & Keys.Shift) == Keys.Shift,
                lifetimeCancellation.Token);
            for (var index = 0; index < snapshot.SpeciesIds.Count; index++)
            {
                var speciesId = unchecked((short)snapshot.SpeciesIds[index]);
                var option = dexRecommendationOptions.FirstOrDefault(value => value.SpeciesId == speciesId)
                    ?? new DexRecommendationOption(
                        snapshot.SpeciesIds[index].ToString(CultureInfo.InvariantCulture),
                        speciesId);
                ReplaceComboItems(
                    FindRequiredControl<ComboBox>($"CB_DexRec{index + 1}"),
                    [option]);
            }

            FindRequiredControl<TextBox>("TB_Status").Text = "Pokédex recommendations updated.";
        }
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            FindRequiredControl<TextBox>("TB_Status").Text = exception.Message;
            showMessage("Pokédex recommendation failed", exception.Message);
        }
        finally
        {
            button.Enabled = services.Connection.Status.State == ConnectionState.Connected;
        }
    }

    private async Task ReadWildEncounterAsync()
    {
        var button = FindRequiredControl<Button>("B_ReadEncounter");
        button.Enabled = false;
        try
        {
            var pokemon = await services.Connection.ReadWildPokemonAsync(lifetimeCancellation.Token);
            cachedWildPokemon = pokemon;
            FindRequiredControl<TextBox>("TB_Wild").Text = string.Join(
                Environment.NewLine,
                pokemon.SpeciesName,
                $"EC: {pokemon.EncryptionConstant:X8}",
                $"PID: {pokemon.PersonalityId:X8}",
                $"{pokemon.Nature} Nature",
                $"Ability: {pokemon.AbilityId}",
                $"IVs: {pokemon.IndividualValues.HP}/{pokemon.IndividualValues.Attack}/{pokemon.IndividualValues.Defense}/{pokemon.IndividualValues.SpecialAttack}/{pokemon.IndividualValues.SpecialDefense}/{pokemon.IndividualValues.Speed}",
                $"Height: {pokemon.Height}",
                string.IsNullOrWhiteSpace(pokemon.Mark) ? string.Empty : $"Mark: {pokemon.Mark}");
            FindRequiredControl<Button>("B_CopyToFilter").Enabled = true;
            FindRequiredControl<TextBox>("TB_Status").Text = "Encounter read.";
        }
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            cachedWildPokemon = null;
            FindRequiredControl<TextBox>("TB_Wild").Clear();
            FindRequiredControl<Button>("B_CopyToFilter").Enabled = false;
            FindRequiredControl<TextBox>("TB_Status").Text = exception.Message;
            showMessage("Read encounter failed", exception.Message);
        }
        finally
        {
            button.Enabled = services.Connection.Status.State == ConnectionState.Connected;
        }
    }

    private void CopyWildEncounterToFilter()
    {
        if (cachedWildPokemon is null)
        {
            return;
        }

        var values = cachedWildPokemon.IndividualValues.ToArray();
        var stats = new[] { "HP", "Atk", "Def", "SpA", "SpD", "Spe" };
        for (var index = 0; index < stats.Length; index++)
        {
            FindRequiredControl<NumericUpDown>($"NUD_{stats[index]}_Min").Value = values[index];
            FindRequiredControl<NumericUpDown>($"NUD_{stats[index]}_Max").Value = values[index];
        }
    }

    private async Task SkipDaysAsync(bool forward)
    {
        if (skipCancellation is not null)
        {
            return;
        }

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCancellation.Token);
        skipCancellation = cancellation;
        SetCfwBusy(true);
        try
        {
            var count = ParseUInt32(FindRequiredControl<TextBox>("TB_Skips").Text, "Days");
            for (var index = 0U; index < count; index++)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (forward)
                {
                    await services.Connection.SkipDayAsync(cancellation.Token);
                }
                else
                {
                    await services.Connection.SkipDayBackAsync(cancellation.Token);
                }

                FindRequiredControl<TextBox>("TB_AdvancesIncrease").Text = (index + 1).ToString(CultureInfo.InvariantCulture);
            }

            await RefreshConnectedStateAsync(cancellation.Token);
            FindRequiredControl<TextBox>("TB_Status").Text = forward ? "Days advanced." : "Days moved back.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            FindRequiredControl<TextBox>("TB_Status").Text = "Date skipping cancelled.";
        }
        catch (Exception exception)
        {
            FindRequiredControl<TextBox>("TB_Status").Text = exception.Message;
            showMessage("Date skipping failed", exception.Message);
        }
        finally
        {
            skipCancellation = null;
            SetCfwBusy(false);
        }
    }

    private async Task AdvanceConnectedStateAsync()
    {
        var button = FindRequiredControl<Button>("B_SkipAdvance");
        button.Enabled = false;
        try
        {
            var amount = ParseUInt64(FindRequiredControl<TextBox>("TB_Skips").Text, "Advances");
            var state = await services.Connection.ReadRngStateAsync(lifetimeCancellation.Token);
            var result = await services.Xoroshiro.CalculateAsync(
                new XoroshiroRequest(state, XoroshiroOperation.Next, amount),
                lifetimeCancellation.Token);
            await services.Connection.WriteRngStateAsync(result.State, lifetimeCancellation.Token);
            UpdateCurrentState(result.State);
            FindRequiredControl<TextBox>("TB_AdvancesIncrease").Text = amount.ToString(CultureInfo.InvariantCulture);
            FindRequiredControl<TextBox>("TB_Status").Text = "RNG state advanced.";
        }
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            FindRequiredControl<TextBox>("TB_Status").Text = exception.Message;
            showMessage("Advance failed", exception.Message);
        }
        finally
        {
            button.Enabled = services.Connection.Status.State == ConnectionState.Connected;
        }
    }

    private async Task ResetNetworkTimeAsync()
    {
        var button = FindRequiredControl<Button>("B_NTP");
        button.Enabled = false;
        try
        {
            await services.Connection.ResetNetworkTimeAsync(lifetimeCancellation.Token);
            FindRequiredControl<TextBox>("TB_Status").Text = "Network time reset.";
        }
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            FindRequiredControl<TextBox>("TB_Status").Text = exception.Message;
            showMessage("NTP reset failed", exception.Message);
        }
        finally
        {
            button.Enabled = services.Connection.Status.State == ConnectionState.Connected;
        }
    }

    private async Task RefreshConnectedStateAsync(CancellationToken cancellationToken)
    {
        var state = await services.Connection.ReadRngStateAsync(cancellationToken);
        UpdateCurrentState(state);
    }

    private void UpdateCurrentState(RngState state)
    {
        FindRequiredControl<TextBox>("TB_CurrentS0").Text = state.Seed0.ToString("X16", CultureInfo.InvariantCulture);
        FindRequiredControl<TextBox>("TB_CurrentS1").Text = state.Seed1.ToString("X16", CultureInfo.InvariantCulture);
    }

    private void SetCfwBusy(bool busy)
    {
        var connected = services.Connection.Status.State == ConnectionState.Connected;
        FindRequiredControl<TextBox>("TB_Skips").Enabled = connected && !busy;
        FindRequiredControl<Button>("B_SkipForward").Enabled = connected && !busy;
        FindRequiredControl<Button>("B_SkipBack").Enabled = connected && !busy;
        FindRequiredControl<Button>("B_SkipAdvance").Enabled = connected && !busy;
        FindRequiredControl<Button>("B_NTP").Enabled = connected && !busy;
        FindRequiredControl<Button>("B_CancelSkip").Enabled = busy;
    }

    private async Task InitializeCatalogAsync()
    {
        try
        {
            dexRecommendationOptions = await services.EncounterCatalog.GetDexRecommendationOptionsAsync(
                includeNone: true,
                lifetimeCancellation.Token);
            foreach (var name in new[] { "CB_DexRec1", "CB_DexRec2", "CB_DexRec3", "CB_DexRec4" })
            {
                ReplaceComboItems(FindRequiredControl<ComboBox>(name), dexRecommendationOptions);
            }

            await LoadCatalogForCurrentTabAsync();
        }
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            FindRequiredControl<TextBox>("TB_Status").Text = exception.Message;
            showMessage("Encounter catalog failed", exception.Message);
        }
    }

    private async Task LoadCatalogForCurrentTabAsync()
    {
        if (updatingCatalog || lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }

        updatingCatalog = true;
        try
        {
            var kind = GetSelectedEncounterKind();
            var game = GetSelectedGame();
            var areas = await services.EncounterCatalog.GetAreasAsync(game, kind, lifetimeCancellation.Token);
            ReplaceComboItems(FindRequiredControl<ComboBox>($"CB_{kind}_Area"), areas);
            await LoadWeatherAndSpeciesCoreAsync(game, kind);
        }
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            FindRequiredControl<TextBox>("TB_Status").Text = exception.Message;
            showMessage("Encounter catalog failed", exception.Message);
        }
        finally
        {
            updatingCatalog = false;
        }
    }

    private async Task LoadWeatherAndSpeciesAsync(EncounterKind kind)
    {
        if (updatingCatalog || kind != GetSelectedEncounterKind() || lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }

        updatingCatalog = true;
        try
        {
            await LoadWeatherAndSpeciesCoreAsync(GetSelectedGame(), kind);
        }
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            FindRequiredControl<TextBox>("TB_Status").Text = exception.Message;
            showMessage("Encounter catalog failed", exception.Message);
        }
        finally
        {
            updatingCatalog = false;
        }
    }

    private async Task LoadWeatherAndSpeciesCoreAsync(GameVersion game, EncounterKind kind)
    {
        var area = FindRequiredControl<ComboBox>($"CB_{kind}_Area").Text;
        if (string.IsNullOrWhiteSpace(area))
        {
            return;
        }

        var weather = await services.EncounterCatalog.GetWeatherAsync(
            game,
            kind,
            area,
            lifetimeCancellation.Token);
        ReplaceComboItems(FindRequiredControl<ComboBox>($"CB_{kind}_Weather"), weather);
        await LoadSpeciesCoreAsync(game, kind, area);
    }

    private async Task LoadSpeciesAsync(EncounterKind kind)
    {
        if (updatingCatalog || kind != GetSelectedEncounterKind() || lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }

        updatingCatalog = true;
        try
        {
            var area = FindRequiredControl<ComboBox>($"CB_{kind}_Area").Text;
            await LoadSpeciesCoreAsync(GetSelectedGame(), kind, area);
        }
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            FindRequiredControl<TextBox>("TB_Status").Text = exception.Message;
            showMessage("Encounter catalog failed", exception.Message);
        }
        finally
        {
            updatingCatalog = false;
        }
    }

    private async Task LoadSpeciesCoreAsync(GameVersion game, EncounterKind kind, string area)
    {
        var weather = FindRequiredControl<ComboBox>($"CB_{kind}_Weather").Text;
        if (string.IsNullOrWhiteSpace(area) || string.IsNullOrWhiteSpace(weather))
        {
            return;
        }

        var species = await services.EncounterCatalog.GetSpeciesAsync(
            game,
            kind,
            area,
            weather,
            lifetimeCancellation.Token);
        ReplaceComboItems(FindRequiredControl<ComboBox>($"CB_{kind}_Species"), species);
    }

    private static void ReplaceComboItems<T>(ComboBox comboBox, IReadOnlyList<T> values)
    {
        comboBox.BeginUpdate();
        try
        {
            comboBox.Items.Clear();
            comboBox.Items.AddRange(values.Cast<object>().ToArray());
            comboBox.SelectedIndex = comboBox.Items.Count > 0 ? 0 : -1;
        }
        finally
        {
            comboBox.EndUpdate();
        }
    }

    private EncounterKind GetSelectedEncounterKind()
    {
        var selectedIndex = FindRequiredControl<TabControl>("TC_EncounterType").SelectedIndex;
        return selectedIndex is >= 0 and <= 3 ? (EncounterKind)selectedIndex : EncounterKind.Static;
    }

    private GameVersion GetSelectedGame() =>
        FindRequiredControl<ComboBox>("CB_Game").SelectedIndex == 1
            ? GameVersion.Shield
            : GameVersion.Sword;

    private async Task SearchEncountersAsync(EncounterKind kind)
    {
        var button = FindRequiredControl<Button>($"B_{kind}_Search");
        if (searchCancellation is not null)
        {
            searchCancellation.Cancel();
            return;
        }

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCancellation.Token);
        searchCancellation = cancellation;
        button.Text = "Cancel";
        try
        {
            var request = BuildSearchRequest(kind);
            var progress = new Progress<OperationProgress>(value =>
                FindRequiredControl<TextBox>("TB_Status").Text = value.Message);
            var results = await services.Encounters.SearchAsync(request, progress, cancellation.Token);
            BindEncounterResults(results);
            FindRequiredControl<TextBox>("TB_Status").Text = $"Found {results.Count} result(s).";
            if (results.Count > 0 && FindRequiredControl<CheckBox>("CB_PlayTone").Checked)
            {
                resultTone();
            }
            if (results.Count > 0 && FindRequiredControl<CheckBox>("CB_FocusWindow").Checked)
            {
                resultFocus();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            FindRequiredControl<TextBox>("TB_Status").Text = "Search cancelled.";
        }
        catch (Exception exception)
        {
            FindRequiredControl<TextBox>("TB_Status").Text = exception.Message;
            showMessage("Search failed", exception.Message);
        }
        finally
        {
            searchCancellation = null;
            button.Text = "Search!";
        }
    }

    private OverworldSearchRequest BuildSearchRequest(EncounterKind kind)
    {
        var kindName = kind.ToString();
        var seed0 = ParseHexUInt64(FindRequiredControl<TextBox>("TB_Seed0").Text, "Seed[0]");
        var seed1 = ParseHexUInt64(FindRequiredControl<TextBox>("TB_Seed1").Text, "Seed[1]");
        var startAdvance = ParseUInt64(FindRequiredControl<TextBox>($"TB_{kindName}_Initial").Text, "Initial Adv.");
        var advanceCount = ParseUInt64(FindRequiredControl<TextBox>($"TB_{kindName}_Advances").Text, "Advances");
        var endAdvance = checked(startAdvance + advanceCount);
        var game = FindRequiredControl<ComboBox>("CB_Game").SelectedIndex == 1
            ? GameVersion.Shield
            : GameVersion.Sword;
        var area = RequiredSelection(FindRequiredControl<ComboBox>($"CB_{kindName}_Area"), "Area");
        var weather = RequiredSelection(FindRequiredControl<ComboBox>($"CB_{kindName}_Weather"), "Weather");
        var species = RequiredSelection(FindRequiredControl<ComboBox>($"CB_{kindName}_Species"), "Target");
        var leadAbility = FindRequiredControl<ComboBox>($"CB_{kindName}_LeadAbility").Text;
        var context = new EncounterCatalogRequest(game, kind, area, weather, leadAbility);
        var profile = new RngProfile(
            "Current",
            game,
            ParseUInt16(FindRequiredControl<TextBox>("TB_TID").Text, "TID"),
            ParseUInt16(FindRequiredControl<TextBox>("TB_SID").Text, "SID"),
            FindRequiredControl<CheckBox>("CB_ShinyCharm").Checked,
            FindRequiredControl<CheckBox>("CB_MarkCharm").Checked);
        var filtersEnabled = FindRequiredControl<CheckBox>("CB_EnableFilters").Checked;
        var filter = new EncounterFilter(
            targetSpecies: species,
            shiny: ParseShinyFilter(FindRequiredControl<ComboBox>("CB_Filter_Shiny").Text),
            aura: ParseAuraFilter(FindRequiredControl<ComboBox>("CB_Filter_Aura").Text),
            mark: ParseMarkFilter(FindRequiredControl<ComboBox>("CB_Filter_Mark").Text),
            height: ParseHeightFilter(FindRequiredControl<ComboBox>("CB_Filter_Height").Text),
            individualValues: CreateIndividualValueConstraints(),
            rareEncryptionConstant: FindRequiredControl<CheckBox>("CB_RareEC").Checked);
        var menuClose = FindRequiredControl<CheckBox>($"CB_{kindName}_MenuClose").Checked;
        var considerFlying = FindRequiredControl<CheckBox>("CB_ConsiderFlying").Checked;
        var considerRain = FindRequiredControl<CheckBox>("CB_ConsiderRain").Checked;
        var rainTicks = (uint)FindRequiredControl<NumericUpDown>("NUD_RainTick").Value;
        var environment = new OverworldEnvironmentSettings(
            menuClose,
            menuClose ? ParseUInt32(FindRequiredControl<TextBox>($"TB_{kindName}_NPCs").Text, "NPCs") : 0,
            menuClose && FindRequiredControl<CheckBox>($"CB_{kindName}_MenuClose_Direction").Checked,
            considerFlying,
            (uint)FindRequiredControl<NumericUpDown>("NUD_AreaLoad").Value,
            (uint)FindRequiredControl<NumericUpDown>("NUD_FlyNPCs").Value,
            considerRain,
            considerFlying && considerRain ? rainTicks : 0,
            !considerFlying && considerRain ? rainTicks : 0);
        var knockouts = kind is EncounterKind.Symbol or EncounterKind.Fishing
            ? checked((int)ParseUInt32(FindRequiredControl<TextBox>($"TB_{kindName}_KOs").Text, "KOs"))
            : 0;
        var maximumStep = kind == EncounterKind.Hidden
            && int.TryParse(FindRequiredControl<ComboBox>("CB_Hidden_MaxStep").Text, out var parsedStep)
                ? parsedStep
                : 0;

        return new OverworldSearchRequest(
            new RngState(seed0, seed1),
            startAdvance,
            endAdvance,
            context,
            profile,
            filter,
            environment,
            knockouts,
            maximumStep,
            Enumerable.Range(1, 4).Select(ReadDexRecommendationSlot),
            filtersEnabled);
    }

    private short ReadDexRecommendationSlot(int index)
    {
        var comboBox = FindRequiredControl<ComboBox>($"CB_DexRec{index}");
        if (comboBox.SelectedItem is DexRecommendationOption option)
        {
            return option.SpeciesId;
        }

        var known = dexRecommendationOptions.FirstOrDefault(value =>
            string.Equals(value.DisplayName, comboBox.Text, StringComparison.Ordinal));
        if (known is not null)
        {
            return known.SpeciesId;
        }

        return ushort.TryParse(comboBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var speciesId)
            ? unchecked((short)speciesId)
            : (short)0;
    }

    private IReadOnlyList<IndividualValueConstraint> CreateIndividualValueConstraints()
    {
        return new[] { "HP", "Atk", "Def", "SpA", "SpD", "Spe" }
            .Select(stat => new IndividualValueConstraint(
                IndividualValueMatch.Range,
                (int)FindRequiredControl<NumericUpDown>($"NUD_{stat}_Min").Value,
                (int)FindRequiredControl<NumericUpDown>($"NUD_{stat}_Max").Value))
            .ToArray();
    }

    private void BindEncounterResults(IReadOnlyList<OverworldEncounterResult> results)
    {
        var grid = FindRequiredControl<DataGridView>("DGV_Results");
        grid.Rows.Clear();
        foreach (var result in results)
        {
            grid.Rows.Add(
                result.Advance.ToString(CultureInfo.InvariantCulture),
                result.Jump.ToString(CultureInfo.InvariantCulture),
                result.Step.ToString(CultureInfo.InvariantCulture),
                result.Animation.ToString(),
                result.Species,
                result.Shiny,
                result.BrilliantAura ? "Yes" : "No",
                result.Level.ToString(CultureInfo.InvariantCulture),
                result.Ability,
                result.Nature,
                result.Gender.ToString(),
                result.IndividualValues.HP.ToString(CultureInfo.InvariantCulture),
                result.IndividualValues.Attack.ToString(CultureInfo.InvariantCulture),
                result.IndividualValues.Defense.ToString(CultureInfo.InvariantCulture),
                result.IndividualValues.SpecialAttack.ToString(CultureInfo.InvariantCulture),
                result.IndividualValues.SpecialDefense.ToString(CultureInfo.InvariantCulture),
                result.IndividualValues.Speed.ToString(CultureInfo.InvariantCulture),
                result.Mark,
                result.EncryptionConstant.ToString("X8", CultureInfo.InvariantCulture),
                result.PersonalityId.ToString("X8", CultureInfo.InvariantCulture),
                $"{result.HeightDescription} ({result.Height})",
                result.Item,
                result.EggMove,
                result.State.Seed0.ToString("X16", CultureInfo.InvariantCulture),
                result.State.Seed1.ToString("X16", CultureInfo.InvariantCulture));
        }
    }

    private static ShinyFilter ParseShinyFilter(string value) => value switch
    {
        "Either" => ShinyFilter.Either,
        "Star" => ShinyFilter.Star,
        "Square" => ShinyFilter.Square,
        "None" => ShinyFilter.None,
        _ => ShinyFilter.Any,
    };

    private static AuraFilter ParseAuraFilter(string value) => value switch
    {
        "Brilliant" => AuraFilter.Brilliant,
        "None" => AuraFilter.None,
        _ => AuraFilter.Any,
    };

    private static MarkFilter ParseMarkFilter(string value) => value switch
    {
        "None" => new MarkFilter(MarkFilterMode.None),
        "Any" => new MarkFilter(MarkFilterMode.Any),
        _ => MarkFilter.Ignore,
    };

    private static HeightFilter ParseHeightFilter(string value) => value switch
    {
        "XXXS" => HeightFilter.XXXS,
        "XXS" => HeightFilter.XXS,
        "XS" => HeightFilter.XS,
        "S" => HeightFilter.Small,
        "M" => HeightFilter.Medium,
        "L" => HeightFilter.Large,
        "XL" => HeightFilter.XL,
        "XXL" => HeightFilter.XXL,
        "XXXL" => HeightFilter.XXXL,
        "XXXS or XXXL" => HeightFilter.MinOrMax,
        _ => HeightFilter.Any,
    };

    private static string RequiredSelection(ComboBox comboBox, string fieldName)
    {
        var value = comboBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(value) || value is "None" or "(None)")
        {
            throw new ArgumentException($"{fieldName} is required.");
        }

        return value;
    }

    private static ulong ParseHexUInt64(string value, string fieldName)
    {
        if (!ulong.TryParse(value.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result))
        {
            throw new ArgumentException($"{fieldName} must be a hexadecimal value.");
        }

        return result;
    }

    private static ulong ParseUInt64(string value, string fieldName)
    {
        if (!ulong.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var result))
        {
            throw new ArgumentException($"{fieldName} must be a non-negative integer.");
        }

        return result;
    }

    private static uint ParseUInt32(string value, string fieldName)
    {
        if (!uint.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var result))
        {
            throw new ArgumentException($"{fieldName} must be a non-negative integer.");
        }

        return result;
    }

    private static ushort ParseUInt16(string value, string fieldName)
    {
        if (!ushort.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var result))
        {
            throw new ArgumentException($"{fieldName} must be between 0 and 65535.");
        }

        return result;
    }

    private async Task ConnectAsync()
    {
        var connectButton = FindRequiredControl<Button>("B_Connect");
        connectButton.Enabled = false;
        FindRequiredControl<TextBox>("TB_Status").Text = "Connecting...";

        try
        {
            var host = FindRequiredControl<TextBox>("TB_SwitchIP").Text.Trim();
            var settings = new OwoowConnectionSettings(ConnectionProtocol.Wifi, host, 6000);
            var status = await services.Connection.ConnectAsync(settings, lifetimeCancellation.Token);
            if (status.State != ConnectionState.Connected)
            {
                throw new InvalidOperationException(status.Message);
            }

            var state = await services.Connection.ReadRngStateAsync(lifetimeCancellation.Token);
            var trainer = await services.Connection.ReadTrainerAsync(lifetimeCancellation.Token);

            FindRequiredControl<TextBox>("TB_CurrentS0").Text = state.Seed0.ToString("X16");
            FindRequiredControl<TextBox>("TB_CurrentS1").Text = state.Seed1.ToString("X16");
            FindRequiredControl<TextBox>("TB_TID").Text = trainer.TrainerId.ToString("D5");
            FindRequiredControl<TextBox>("TB_SID").Text = trainer.SecretId.ToString("D5");
            FindRequiredControl<CheckBox>("CB_ShinyCharm").Checked = trainer.HasShinyCharm;
            FindRequiredControl<CheckBox>("CB_MarkCharm").Checked = trainer.HasMarkCharm;
            FindRequiredControl<TextBox>("TB_Status").Text = status.Message;
            SetConnectedState(true);
        }
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
        {
            SetConnectedState(false);
        }
        catch (Exception exception)
        {
            FindRequiredControl<TextBox>("TB_Status").Text = exception.Message;
            SetConnectedState(false);
            showMessage("Connection failed", exception.Message);
        }
    }

    private async Task DisconnectAsync()
    {
        var disconnectButton = FindRequiredControl<Button>("B_Disconnect");
        disconnectButton.Enabled = false;
        try
        {
            await services.Connection.DisconnectAsync(lifetimeCancellation.Token);
            FindRequiredControl<TextBox>("TB_Status").Text = services.Connection.Status.Message;
        }
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            FindRequiredControl<TextBox>("TB_Status").Text = exception.Message;
            showMessage("Disconnect failed", exception.Message);
        }
        finally
        {
            SetConnectedState(false);
        }
    }

    private void CopyCurrentStateToInitial()
    {
        var seed0 = FindRequiredControl<TextBox>("TB_CurrentS0").Text;
        var seed1 = FindRequiredControl<TextBox>("TB_CurrentS1").Text;
        FindRequiredControl<TextBox>("TB_Seed0").Text = seed0;
        FindRequiredControl<TextBox>("TB_Seed1").Text = seed1;
        SynchronizeSpecialToolSeeds(seed0, seed1);
    }

    private void SynchronizeSpecialToolSeeds(string seed0, string seed1)
    {
        foreach (var (windowName, _) in SpecialToolPrefixes)
        {
            if (!openToolWindows.TryGetValue(windowName, out var form) || form.IsDisposed)
            {
                continue;
            }

            SetFormText(form, "TB_Seed0", seed0);
            SetFormText(form, "TB_Seed1", seed1);
        }
    }

    private void SynchronizeSpecialToolOption(string optionName, bool value)
    {
        foreach (var (windowName, prefix) in SpecialToolPrefixes)
        {
            if (!openToolWindows.TryGetValue(windowName, out var form) || form.IsDisposed)
            {
                continue;
            }

            var matches = form.Controls.Find($"CB_{prefix}_{optionName}", true);
            if (matches.Length > 0 && matches[0] is CheckBox checkBox)
            {
                checkBox.Checked = value;
            }
        }
    }

    private static void SetFormText(Form form, string controlName, string value)
    {
        var matches = form.Controls.Find(controlName, true);
        if (matches.Length > 0 && matches[0] is TextBox textBox)
        {
            textBox.Text = value;
        }
    }

    private void ConnectionStatusChanged(object? sender, ConnectionStatusChangedEventArgs eventArgs)
    {
        RunOnUi(() =>
        {
            FindRequiredControl<TextBox>("TB_Status").Text = eventArgs.Status.Message;
            if (eventArgs.Status.State is ConnectionState.Connected or ConnectionState.Disconnected)
            {
                SetConnectedState(eventArgs.Status.State == ConnectionState.Connected);
            }
        });
    }

    private void SetConnectedState(bool connected)
    {
        FindRequiredControl<Button>("B_Connect").Enabled = !connected;
        FindRequiredControl<Button>("B_Disconnect").Enabled = connected;
        FindRequiredControl<Button>("B_CopyToInitial").Enabled = connected;
        FindRequiredControl<Button>("B_ReadEncounter").Enabled = connected;
        FindRequiredControl<Button>("B_RefreshDexRec").Enabled = connected;
        FindRequiredControl<TextBox>("TB_Skips").Enabled = connected;
        FindRequiredControl<Button>("B_SkipForward").Enabled = connected;
        FindRequiredControl<Button>("B_SkipBack").Enabled = connected;
        FindRequiredControl<Button>("B_SkipAdvance").Enabled = connected;
        FindRequiredControl<Button>("B_NTP").Enabled = connected;
        if (!connected)
        {
            FindRequiredControl<Button>("B_CancelSkip").Enabled = false;
        }
    }

    private void RunOnUi(Action action)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(action);
            return;
        }

        action();
    }

    private T FindRequiredControl<T>(string name)
        where T : Control
    {
        var matches = Controls.Find(name, true);
        if (matches.Length == 0 || matches[0] is not T control)
        {
            throw new InvalidOperationException($"Control '{name}' was not found as {typeof(T).Name}.");
        }

        return control;
    }

    private static MenuStrip CreateSubWindowsMenu()
    {
        var menu = new MenuStrip
        {
            Name = "MS_SubWindows",
            Dock = DockStyle.Top,
            BackColor = AppVisualTheme.Surface,
            ForeColor = AppVisualTheme.Ink,
            Font = AppVisualTheme.UiSemiboldFont,
            Renderer = AppVisualTheme.CreateLightToolStripRenderer(),
            AutoSize = true,
        };

        foreach (var (name, text) in new[]
        {
            ("TSMI_Profiles", "Profiles"),
            ("TSMI_EncounterLookup", "Encounter Lookup"),
            ("TSMI_SpreadFinder", "Spread Finder"),
            ("TSMI_LotoID", "Loto-ID"),
            ("TSMI_Cramomatic", "Cram-o-matic"),
            ("TSMI_WattTrader", "Watt Trader"),
            ("TSMI_DiggingPa", "Digging Pa"),
            ("TMSI_SkillBro", "Digging Bro (Skill)"),
            ("TSMI_WailordRespawn", "Wailord Respawn"),
            ("TSMI_XoroshiroTools", "Xoroshiro Tools"),
        })
        {
            menu.Items.Add(new ToolStripMenuItem(text)
            {
                Name = name,
                ForeColor = AppVisualTheme.Ink,
            });
        }

        return menu;
    }

    private static GroupBox CreateSeedControlsContainer()
    {
        var container = new GroupBox
        {
            Name = "GB_SeedControlsContainer",
            Location = new Point(0, 24),
            Size = new Size(1266, 321),
            TabStop = false,
        };

        container.Controls.Add(CreateSeedGroup());
        container.Controls.Add(CreateConnectionGroup());
        container.Controls.Add(CreateProfileGroup());
        container.Controls.Add(CreateEncounterTabs());
        container.Controls.Add(CreateEncounterSettingsPanel());
        container.Controls.Add(CreateFilterGroup());
        container.Controls.Add(CreateWildViewGroup());
        container.Controls.Add(CreateCfwGroup());
        container.Controls.Add(CreateRetailGroup());
        return container;
    }

    private static GroupBox CreateSeedGroup()
    {
        var group = CreateGroup("GB_Seed", string.Empty, 0, 0, 212, 60);
        group.Controls.Add(CreateLabel("L_Seed0", "Seed[0]:", 6, 15, 58));
        group.Controls.Add(CreateTextBox("TB_Seed0", "0123456789ABCDEF", 68, 12, 138));
        group.Controls.Add(CreateLabel("L_Seed1", "Seed[1]:", 6, 38, 58));
        group.Controls.Add(CreateTextBox("TB_Seed1", "0123456789ABCDEF", 68, 35, 138));
        return group;
    }

    private static GroupBox CreateConnectionGroup()
    {
        var group = CreateGroup("GB_Connection", string.Empty, 0, 49, 212, 184);
        group.Controls.Add(CreateLabel("L_SwitchIP", "Switch IP:", 6, 17, 64));
        group.Controls.Add(CreateTextBox("TB_SwitchIP", "123.123.123.123", 74, 14, 132));
        group.Controls.Add(CreateButton("B_Connect", "Connect", 6, 42, 98));
        group.Controls.Add(CreateButton("B_Disconnect", "Disconnect", 108, 42, 98, enabled: false));
        group.Controls.Add(CreateLabel("L_Status", "Status:", 6, 72, 64));
        group.Controls.Add(CreateTextBox("TB_Status", "Disconnected.", 74, 69, 132, readOnly: true));
        group.Controls.Add(CreateLabel("L_CurrentS0", "Seed[0]:", 6, 98, 64));
        group.Controls.Add(CreateTextBox("TB_CurrentS0", "0123456789ABCDEF", 74, 95, 132, readOnly: true));
        group.Controls.Add(CreateLabel("L_CurrentS1", "Seed[1]:", 6, 122, 64));
        group.Controls.Add(CreateTextBox("TB_CurrentS1", "0123456789ABCDEF", 74, 119, 132, readOnly: true));
        group.Controls.Add(CreateLabel("L_CurrentAdvances", "Advances:", 6, 148, 64));
        group.Controls.Add(CreateTextBox("TB_CurrentAdvances", "0", 74, 145, 84, readOnly: true));
        group.Controls.Add(CreateTextBox("TB_AdvancesIncrease", "0", 160, 145, 46, readOnly: true));
        return group;
    }

    private static GroupBox CreateProfileGroup()
    {
        var group = CreateGroup("GB_SAVInfo", string.Empty, 0, 223, 212, 98);
        group.Controls.Add(CreateLabel("L_Game", "Game:", 6, 18, 42));
        group.Controls.Add(CreateComboBox("CB_Game", ["Sword", "Shield"], 50, 15, 72, selectedIndex: 0));
        group.Controls.Add(CreateLabel("L_TID", "TID:", 126, 18, 30));
        group.Controls.Add(CreateTextBox("TB_TID", "12345", 156, 15, 50));
        group.Controls.Add(CreateLabel("L_SID", "SID:", 126, 44, 30));
        group.Controls.Add(CreateTextBox("TB_SID", "54321", 156, 41, 50));
        group.Controls.Add(CreateCheckBox("CB_ShinyCharm", "Shiny Charm?", 6, 44, 112));
        group.Controls.Add(CreateCheckBox("CB_MarkCharm", "Mark Charm?", 6, 68, 112));
        group.Controls.Add(CreateButton("B_CopyToInitial", "Copy", 126, 67, 80, enabled: false));
        return group;
    }

    private static TabControl CreateEncounterTabs()
    {
        var tabs = new TabControl
        {
            Name = "TC_EncounterType",
            Location = new Point(211, 11),
            Size = new Size(269, 310),
        };

        tabs.TabPages.Add(CreateEncounterPage("Static", 3, includeKnockouts: false, includeMaximumStep: false));
        tabs.TabPages.Add(CreateEncounterPage("Symbol", 3, includeKnockouts: true, includeMaximumStep: false));
        tabs.TabPages.Add(CreateEncounterPage("Hidden", 3, includeKnockouts: false, includeMaximumStep: true));
        tabs.TabPages.Add(CreateEncounterPage("Fishing", 3, includeKnockouts: true, includeMaximumStep: false));
        return tabs;
    }

    private static TabPage CreateEncounterPage(
        string kind,
        int defaultNonPlayerCharacters,
        bool includeKnockouts,
        bool includeMaximumStep)
    {
        var page = new TabPage(kind)
        {
            Name = $"TP_{kind}",
            UseVisualStyleBackColor = true,
        };

        var settings = CreateGroup(
            $"GB_{kind}_EncounterSettings",
            $"Encounter Settings - {kind}",
            6,
            2,
            249,
            102);
        settings.Controls.Add(CreateLabel($"L_{kind}_Area", "Area:", 8, 22, 56));
        settings.Controls.Add(CreateComboBox($"CB_{kind}_Area", ["None"], 70, 19, 173, selectedIndex: 0));
        settings.Controls.Add(CreateLabel($"L_{kind}_Weather", "Weather:", 8, 47, 56));
        settings.Controls.Add(CreateComboBox($"CB_{kind}_Weather", ["None"], 70, 44, 173, selectedIndex: 0));
        settings.Controls.Add(CreateLabel($"L_{kind}_Species", "Target:", 8, 72, 56));
        settings.Controls.Add(CreateComboBox($"CB_{kind}_Species", ["None"], 70, 69, 173, selectedIndex: 0));
        page.Controls.Add(settings);

        page.Controls.Add(CreateLabel($"L_{kind}_LeadAbility", "Lead Ability:", 4, 109, 76));
        page.Controls.Add(CreateComboBox(
            $"CB_{kind}_LeadAbility",
            ["None", "Compound Eyes", "Super Luck", "Synchronize", "Cute Charm", "Magnet Pull", "Lightning Rod", "Static", "Flash Fire", "Storm Drain", "Harvest"],
            82,
            106,
            173,
            selectedIndex: 0));

        var optionTop = 134;
        if (includeKnockouts)
        {
            page.Controls.Add(CreateLabel($"L_{kind}_KOs", "KOs:", 4, optionTop + 3, 76));
            page.Controls.Add(CreateTextBox($"TB_{kind}_KOs", "500", 82, optionTop, 173));
            optionTop += 25;
        }

        if (includeMaximumStep)
        {
            page.Controls.Add(CreateLabel($"L_{kind}_MaxStep", "Max Step:", 4, optionTop + 3, 76));
            page.Controls.Add(CreateComboBox($"CB_{kind}_MaxStep", ["None", "1", "2", "3", "4", "5"], 82, optionTop, 173, 0));
            optionTop += 25;
        }

        var menuClose = CreateCheckBox($"CB_{kind}_MenuClose", "Consider Menu Close?", 6, optionTop, 148);
        page.Controls.Add(menuClose);
        page.Controls.Add(CreateButton($"B_{kind}_MenuClose", "Calibrate NPCs", 155, optionTop - 2, 100, enabled: false));
        optionTop += 24;
        page.Controls.Add(CreateCheckBox($"CB_{kind}_MenuClose_Direction", "Holding Direction?", 6, optionTop, 150, enabled: false));
        page.Controls.Add(CreateLabel($"L_{kind}_NPCs", "NPCs:", 170, optionTop + 2, 44));
        page.Controls.Add(CreateTextBox($"TB_{kind}_NPCs", defaultNonPlayerCharacters.ToString(), 216, optionTop, 39, enabled: false));

        page.Controls.Add(CreateLabel($"L_{kind}_Initial", "Initial Adv.", 6, 208, 72));
        page.Controls.Add(CreateTextBox($"TB_{kind}_Initial", "0", 82, 205, 173));
        page.Controls.Add(CreateLabel($"L_{kind}_Advances", "+", 61, 232, 15));
        page.Controls.Add(CreateTextBox($"TB_{kind}_Advances", "5000", 82, 229, 173));
        page.Controls.Add(CreateButton($"B_{kind}_Search", "Search!", 3, 255, 255));
        return page;
    }

    private static Panel CreateEncounterSettingsPanel()
    {
        var panel = new Panel
        {
            Name = "P_EncounterSettings",
            Location = new Point(482, 9),
            Size = new Size(181, 308),
        };

        var recommendations = CreateGroup("GB_DexRec", "Pokédex Recommendation", 0, 0, 181, 150);
        for (var index = 1; index <= 4; index++)
        {
            recommendations.Controls.Add(CreateComboBox($"CB_DexRec{index}", ["(None)"], 8, 20 + ((index - 1) * 25), 165, 0));
        }
        recommendations.Controls.Add(CreateButton("B_RefreshDexRec", "Refresh", 8, 121, 165, enabled: false));

        var advanced = CreateGroup("GB_Advanced", "Advanced Settings", 0, 148, 181, 160);
        advanced.Controls.Add(CreateCheckBox("CB_ConsiderFlying", "Consider Flying?", 8, 20, 150));
        advanced.Controls.Add(CreateLabel("L_AreaLoad", "Area Load:", 8, 49, 80));
        advanced.Controls.Add(CreateNumeric("NUD_AreaLoad", 90, 46, 82, 0, decimal.MaxValue));
        advanced.Controls.Add(CreateLabel("L_FlyNPCs", "NPCs:", 8, 76, 80));
        advanced.Controls.Add(CreateNumeric("NUD_FlyNPCs", 90, 73, 82, 0, decimal.MaxValue));
        advanced.Controls.Add(CreateCheckBox("CB_ConsiderRain", "Raining/Thunderstorm?", 8, 100, 165));
        advanced.Controls.Add(CreateNumeric("NUD_RainTick", 8, 126, 70, 0, decimal.MaxValue));
        advanced.Controls.Add(CreateButton("B_CalculateRain", "Calculate Rain", 82, 124, 90));

        panel.Controls.Add(recommendations);
        panel.Controls.Add(advanced);
        return panel;
    }

    private static GroupBox CreateFilterGroup()
    {
        var group = CreateGroup("GB_Filters", "Filters", 663, 0, 199, 321);
        var stats = new[]
        {
            ("HP", "HP"),
            ("Atk", "Atk"),
            ("Def", "Def"),
            ("SpA", "SpA"),
            ("SpD", "SpD"),
            ("Spe", "Spe"),
        };

        for (var index = 0; index < stats.Length; index++)
        {
            var (key, text) = stats[index];
            var top = 20 + (index * 26);
            group.Controls.Add(CreateLabel($"L_{key}", $"{text}:", 6, top + 3, 34));
            group.Controls.Add(CreateNumeric($"NUD_{key}_Min", 40, top, 46, 0, 31));
            group.Controls.Add(CreateLabel($"L_{key}Spacer", "-", 88, top + 3, 12));
            group.Controls.Add(CreateNumeric($"NUD_{key}_Max", 101, top, 46, 31, 31));
            group.Controls.Add(CreateButton($"B_{key}_Min", "0", 150, top, 20));
            group.Controls.Add(CreateButton($"B_{key}_Max", "31", 172, top, 20));
        }

        group.Controls.Add(CreateLabel("L_Filter_Shiny", "Shiny:", 6, 179, 55));
        group.Controls.Add(CreateComboBox("CB_Filter_Shiny", ["Ignore", "Either", "Star", "Square", "None"], 64, 176, 128, 0));
        group.Controls.Add(CreateLabel("L_Filter_Mark", "Mark:", 6, 205, 55));
        group.Controls.Add(CreateComboBox("CB_Filter_Mark", ["Ignore", "None", "Any"], 64, 202, 128, 0));
        group.Controls.Add(CreateLabel("L_Filter_Aura", "Aura:", 6, 231, 55));
        group.Controls.Add(CreateComboBox("CB_Filter_Aura", ["Ignore", "Brilliant", "None"], 64, 228, 128, 0));
        group.Controls.Add(CreateLabel("L_Filter_Height", "Height:", 6, 257, 55));
        group.Controls.Add(CreateComboBox("CB_Filter_Height", ["Ignore", "XXXS", "XXS", "XS", "S", "M", "L", "XL", "XXL", "XXXL", "XXXS or XXXL"], 64, 254, 128, 0));
        group.Controls.Add(CreateCheckBox("CB_RareEC", "Rare EC?", 6, 284, 82));
        group.Controls.Add(CreateCheckBox("CB_EnableFilters", "Filters Enabled?", 92, 284, 101, isChecked: true));
        group.Controls.Add(CreateCheckBox("CB_PlayTone", "Play Tone?", 6, 302, 82));
        group.Controls.Add(CreateCheckBox("CB_FocusWindow", "Focus Window?", 92, 302, 101));
        return group;
    }

    private static GroupBox CreateWildViewGroup()
    {
        var group = CreateGroup("GB_WildView", string.Empty, 861, 0, 194, 321);
        var pokemonSprite = new PictureBox
        {
            Name = "PB_PokemonSprite",
            Location = new Point(64, 203),
            Size = new Size(64, 64),
            SizeMode = PictureBoxSizeMode.CenterImage,
            BackColor = AppVisualTheme.AccentSoft,
            AccessibleName = "宝可梦图像占位",
        };
        var markSprite = new PictureBox
        {
            Name = "PB_MarkSprite",
            Location = new Point(127, 219),
            Size = new Size(48, 48),
            SizeMode = PictureBoxSizeMode.CenterImage,
            BackColor = AppVisualTheme.SurfaceMuted,
            AccessibleName = "证章图像占位",
        };
        ConfigureSpritePlaceholder(pokemonSprite, "PKM");
        ConfigureSpritePlaceholder(markSprite, "MARK");
        group.Controls.Add(pokemonSprite);
        group.Controls.Add(markSprite);
        group.Controls.Add(new TextBox
        {
            Name = "TB_Wild",
            Location = new Point(6, 17),
            Size = new Size(181, 186),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Text = "No encounter loaded." + Environment.NewLine +
                "Connect, then read the current encounter.",
        });
        group.Controls.Add(CreateButton("B_ReadEncounter", "Read Encounter", 4, 267, 183, enabled: false));
        group.Controls.Add(CreateButton("B_CopyToFilter", "Copy to Filter", 4, 294, 183, enabled: false));
        return group;
    }

    private static GroupBox CreateCfwGroup()
    {
        var group = CreateGroup("GB_SwitchControls", "CFW Tools", 1054, 9, 212, 105);
        group.Controls.Add(CreateTextBox("TB_Skips", "100", 6, 20, 62, enabled: false));
        group.Controls.Add(CreateButton("B_CancelSkip", "Cancel", 144, 20, 62, enabled: false));
        group.Controls.Add(CreateButton("B_SkipForward", "Days+", 6, 50, 98, enabled: false));
        group.Controls.Add(CreateButton("B_SkipBack", "Days-", 108, 50, 98, enabled: false));
        group.Controls.Add(CreateButton("B_SkipAdvance", "Adv.", 6, 78, 98, enabled: false));
        group.Controls.Add(CreateButton("B_NTP", "NTP", 108, 78, 98, enabled: false));
        return group;
    }

    private static GroupBox CreateRetailGroup()
    {
        var group = CreateGroup("GB_Retail", "Retail Tools", 1054, 116, 212, 201);
        group.Controls.Add(CreateButton("B_RetailSeedFinder", "Retail Seed Finder", 6, 20, 200));
        group.Controls.Add(CreateLabel("L_RetailInitial", "Initial:", 6, 54, 64));
        group.Controls.Add(CreateTextBox("TB_RetailInitial", "0", 74, 51, 132));
        group.Controls.Add(CreateLabel("L_RetailRange", "+", 6, 80, 64));
        group.Controls.Add(CreateTextBox("TB_RetailRange", "99999", 74, 77, 58));
        group.Controls.Add(CreateButton("B_GenerateRetailPattern", "Generate", 136, 76, 70));
        group.Controls.Add(CreateLabel("L_Animations", "Anim.:", 6, 106, 64));
        group.Controls.Add(CreateTextBox("TB_Animations", string.Empty, 74, 103, 132));
        group.Controls.Add(CreateLabel("L_RetailAdvances", "Adv.:", 6, 132, 64));
        group.Controls.Add(CreateTextBox("TB_RetailAdvances", string.Empty, 74, 129, 132, readOnly: true));
        group.Controls.Add(CreateButton("B_RetailUpdateSeeds", "Update Seeds", 6, 158, 200));
        return group;
    }

    private static DataGridView CreateResultsGrid()
    {
        var grid = new DataGridView
        {
            Name = "DGV_Results",
            Location = new Point(10, 352),
            Size = new Size(1256, 312),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BackgroundColor = AppVisualTheme.Surface,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            GridColor = AppVisualTheme.Border,
            EnableHeadersVisualStyles = false,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
            ColumnHeadersHeight = 30,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = AppVisualTheme.ShellRaised,
                ForeColor = Color.White,
                Font = AppVisualTheme.UiSemiboldFont,
                SelectionBackColor = AppVisualTheme.ShellRaised,
                SelectionForeColor = Color.White,
                Padding = new Padding(4, 0, 4, 0),
            },
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = AppVisualTheme.Surface,
                ForeColor = AppVisualTheme.Ink,
                SelectionBackColor = AppVisualTheme.AccentSoft,
                SelectionForeColor = AppVisualTheme.Ink,
                Padding = new Padding(4, 0, 4, 0),
            },
            AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = AppVisualTheme.Workspace,
                ForeColor = AppVisualTheme.Ink,
                SelectionBackColor = AppVisualTheme.AccentSoft,
                SelectionForeColor = AppVisualTheme.Ink,
            },
        };

        var headers = new[]
        {
            "Advances", "Jump", "Step", "Animation", "Species", "Shiny", "Brilliant", "Level",
            "Ability", "Nature", "Gender", "HP", "Atk", "Def", "SpA", "SpD", "Spe", "Mark",
            "EC", "PID", "Height", "Item", "Egg Move", "Seed0", "Seed1",
        };
        for (var index = 0; index < headers.Length; index++)
        {
            var header = headers[index];
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = $"Column_{header.Replace(" ", string.Empty)}",
                HeaderText = header,
                SortMode = DataGridViewColumnSortMode.Automatic,
                Width = ResultColumnWidths[index],
            });
        }

        grid.RowTemplate.Height = 27;
        grid.HandleCreated += (_, _) => ApplyResultColumnScale(grid);
        grid.Paint += (_, e) => DrawEmptyResultsState(grid, e.Graphics);
        grid.RowsAdded += (_, _) => grid.Invalidate();
        grid.RowsRemoved += (_, _) => grid.Invalidate();

        return grid;
    }

    private static void ApplyResultColumnScale(DataGridView grid)
    {
        var scale = grid.DeviceDpi / 96F;
        for (var index = 0; index < grid.Columns.Count && index < ResultColumnWidths.Length; index++)
        {
            grid.Columns[index].Width = Math.Max(
                grid.Columns[index].MinimumWidth,
                (int)Math.Round(ResultColumnWidths[index] * scale));
        }
    }

    private static void DrawEmptyResultsState(DataGridView grid, Graphics graphics)
    {
        if (grid.Rows.Count > 0)
        {
            return;
        }

        var content = new Rectangle(
            0,
            grid.ColumnHeadersHeight,
            grid.ClientSize.Width,
            Math.Max(0, grid.ClientSize.Height - grid.ColumnHeadersHeight));
        if (content.Width <= 0 || content.Height <= 0)
        {
            return;
        }

        var title = new Rectangle(content.Left, content.Top + (content.Height / 2) - 24, content.Width, 24);
        var description = new Rectangle(content.Left, title.Bottom + 2, content.Width, 22);
        TextRenderer.DrawText(
            graphics,
            "No results yet",
            AppVisualTheme.UiSemiboldFont,
            title,
            AppVisualTheme.Ink,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(
            graphics,
            "Configure a search above to populate this table.",
            SystemFonts.MessageBoxFont,
            description,
            AppVisualTheme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private static void ConfigureSpritePlaceholder(PictureBox pictureBox, string label)
    {
        pictureBox.Paint += (_, e) =>
        {
            if (pictureBox.Image is not null)
            {
                return;
            }

            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var diameter = Math.Min(pictureBox.ClientSize.Width, pictureBox.ClientSize.Height) - 12;
            var circle = new Rectangle(
                (pictureBox.ClientSize.Width - diameter) / 2,
                (pictureBox.ClientSize.Height - diameter) / 2,
                diameter,
                diameter);
            using var border = new Pen(AppVisualTheme.Accent, 1.5F);
            using var font = new Font("Segoe UI Semibold", pictureBox.Width >= 60 ? 7F : 6F);
            e.Graphics.DrawEllipse(border, circle);
            TextRenderer.DrawText(
                e.Graphics,
                label,
                font,
                pictureBox.ClientRectangle,
                AppVisualTheme.Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        };
    }

    private static GroupBox CreateGroup(string name, string text, int left, int top, int width, int height)
    {
        return new GroupBox
        {
            Name = name,
            Text = text,
            Location = new Point(left, top),
            Size = new Size(width, height),
            TabStop = false,
        };
    }

    private static Label CreateLabel(string name, string text, int left, int top, int width)
    {
        return new Label
        {
            Name = name,
            Text = text,
            AutoSize = false,
            Location = new Point(left, top),
            Size = new Size(width, 20),
            TextAlign = ContentAlignment.MiddleLeft,
        };
    }

    private static TextBox CreateTextBox(
        string name,
        string text,
        int left,
        int top,
        int width,
        bool enabled = true,
        bool readOnly = false)
    {
        return new TextBox
        {
            Name = name,
            Text = text,
            Location = new Point(left, top),
            Size = new Size(width, 23),
            Enabled = enabled,
            ReadOnly = readOnly,
            Font = new Font("Consolas", 9F),
        };
    }

    private static Button CreateButton(
        string name,
        string text,
        int left,
        int top,
        int width,
        bool enabled = true)
    {
        return new Button
        {
            Name = name,
            Text = text,
            Location = new Point(left, top),
            Size = new Size(width, 25),
            Enabled = enabled,
            UseVisualStyleBackColor = true,
        };
    }

    private static ComboBox CreateComboBox(
        string name,
        IEnumerable<string> values,
        int left,
        int top,
        int width,
        int selectedIndex)
    {
        var comboBox = new ComboBox
        {
            Name = name,
            Location = new Point(left, top),
            Size = new Size(width, 23),
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        comboBox.Items.AddRange(values.Cast<object>().ToArray());
        comboBox.SelectedIndex = selectedIndex;
        return comboBox;
    }

    private static CheckBox CreateCheckBox(
        string name,
        string text,
        int left,
        int top,
        int width,
        bool enabled = true,
        bool isChecked = false)
    {
        return new CheckBox
        {
            Name = name,
            Text = text,
            Location = new Point(left, top),
            Size = new Size(width, 22),
            Enabled = enabled,
            Checked = isChecked,
            UseVisualStyleBackColor = true,
        };
    }

    private static NumericUpDown CreateNumeric(
        string name,
        int left,
        int top,
        int width,
        decimal value,
        decimal maximum)
    {
        return new NumericUpDown
        {
            Name = name,
            Location = new Point(left, top),
            Size = new Size(width, 23),
            Minimum = 0,
            Maximum = maximum,
            Value = value,
        };
    }
}
