using AutoSwshRng.Core.Profiles;
using AutoSwshRng.Core.Rng;
using AutoSwshRng.Core.SpreadFinder;
using System.Globalization;

namespace AutoSwshRng.App.Controls;

internal sealed record OwoowSpecialToolContext(
    string InitialAdvance,
    string NonPlayerCharacters,
    bool MenuClose,
    bool HoldDirection,
    string Weather,
    Action<OwoowSpecialToolState> StateChanged);

internal sealed record OwoowSpecialToolState(
    string NonPlayerCharacters,
    bool MenuClose,
    bool HoldDirection);

internal sealed class OwoowToolWindowFactory
{
    private static readonly string[] CramInputItems =
    [
        "Black Apricorn", "Blue Apricorn", "Green Apricorn", "Pink Apricorn", "Red Apricorn",
        "White Apricorn", "Yellow Apricorn", "Sweet Ingredient",
    ];

    private static readonly string[] WeatherItems =
    [
        "All Weather", "Normal Weather", "Overcast", "Raining", "Thunderstorm", "Intense Sun",
        "Snowing", "Snowstorm", "Sandstorm", "Heavy Fog",
    ];

    private static readonly string[] WattTraderTargets =
    [
        "(None)", "Beast or Dream Ball", "Beast Ball x1", "Dream Ball x1",
        "Bottle Cap x1", "Bottle Cap x3", "Gold Bottle Cap x1", "Red Apricorn x5",
        "Blue Apricorn x5", "Yellow Apricorn x5", "Green Apricorn x5", "White Apricorn x5",
        "Black Apricorn x5", "Pink Apricorn x5", "Red Apricorn x10", "Blue Apricorn x10",
        "Yellow Apricorn x10", "Green Apricorn x10", "White Apricorn x10", "Black Apricorn x10",
        "Pink Apricorn x10", "PP Up x1", "PP Up x2", "PP Max x1", "Rare Candy x1",
        "Rare Candy x5", "Gigantamix x1", "Armorite Ore x1", "Armorite Ore x3",
        "Armorite Ore x8", "Dynite Ore x1", "Dynite Ore x5", "Max Mushrooms x1",
        "Max Elixir x1", "Galarica Twig x3", "Galarica Twig x5", "Strawberry Sweet x1",
        "Love Sweet x1", "Berry Sweet x1", "Clover Sweet x1", "Flower Sweet x1",
        "Star Sweet x1", "Ribbon Sweet x1", "Big Nugget x1", "Lansat Berry x1",
        "Starf Berry x1", "Lucky Egg x1", "Electirizer x1", "Magmarizer x1",
        "Cracked Pot x1", "Chipped Pot x1", "King's Rock x1",
    ];

    private readonly OwoowUiServices services;
    private readonly Action<string, string> showMessage;
    private readonly Dictionary<Form, CancellationTokenSource> windowCancellations = [];
    private readonly SemaphoreSlim lotoSaveGate = new(1, 1);

    public OwoowToolWindowFactory(OwoowUiServices services, Action<string, string> showMessage)
    {
        this.services = services ?? throw new ArgumentNullException(nameof(services));
        this.showMessage = showMessage ?? throw new ArgumentNullException(nameof(showMessage));
    }

    public Form CreateProfiles(Action<RngProfile>? profileSelected = null)
    {
        var form = CreateForm("Profiles", "Profile Manager", 440, 260, fixedBorder: true);
        form.Controls.Add(new ListBox
        {
            Name = "LB_ProfileList",
            Location = new Point(12, 12),
            Size = new Size(170, 198),
        });
        form.Controls.Add(CreateLabel("L_Name", "Name:", 198, 16, 60));
        form.Controls.Add(CreateTextBox("TB_Name", string.Empty, 260, 13, 156));
        form.Controls.Add(CreateLabel("L_Game", "Game:", 198, 48, 60));
        form.Controls.Add(CreateComboBox("CB_Game", ["Sword", "Shield"], 260, 45, 156, 0));
        form.Controls.Add(CreateLabel("L_TID", "TID:", 198, 80, 60));
        form.Controls.Add(CreateTextBox("TB_TID", "12345", 260, 77, 156));
        form.Controls.Add(CreateLabel("L_SID", "SID:", 198, 112, 60));
        form.Controls.Add(CreateTextBox("TB_SID", "54321", 260, 109, 156));
        form.Controls.Add(CreateCheckBox("CB_ShinyCharm", "Shiny Charm?", 198, 141, 110));
        form.Controls.Add(CreateCheckBox("CB_MarkCharm", "Mark Charm?", 310, 141, 106));
        form.Controls.Add(CreateButton("B_Add", "Add", 198, 176, 68));
        form.Controls.Add(CreateButton("B_Remove", "Delete", 272, 176, 68));
        form.Controls.Add(CreateButton("B_Select", "Select", 346, 176, 70));
        WireProfileActions(form, profileSelected);
        return form;
    }

    private void WireProfileActions(Form form, Action<RngProfile>? profileSelected)
    {
        var cancellationToken = GetWindowCancellationToken(form);
        var list = FindRequired<ListBox>(form, "LB_ProfileList");
        RngApplicationSettings settings = new(null, []);

        void ShowProfile(RngProfile profile)
        {
            FindRequired<TextBox>(form, "TB_Name").Text = profile.Name;
            FindRequired<ComboBox>(form, "CB_Game").SelectedIndex = (int)profile.Game;
            FindRequired<TextBox>(form, "TB_TID").Text = profile.TrainerId.ToString("D5");
            FindRequired<TextBox>(form, "TB_SID").Text = profile.SecretId.ToString("D5");
            FindRequired<CheckBox>(form, "CB_ShinyCharm").Checked = profile.HasShinyCharm;
            FindRequired<CheckBox>(form, "CB_MarkCharm").Checked = profile.HasMarkCharm;
        }

        void RefreshList(string? selectedName)
        {
            list.Items.Clear();
            list.Items.AddRange(settings.Profiles.Select(profile => profile.Name).Cast<object>().ToArray());
            if (selectedName is not null && list.Items.Contains(selectedName))
            {
                list.SelectedItem = selectedName;
            }
            else if (list.Items.Count > 0)
            {
                list.SelectedIndex = 0;
            }
        }

        RngProfile ReadEditorProfile()
        {
            return new RngProfile(
                FindRequired<TextBox>(form, "TB_Name").Text,
                FindRequired<ComboBox>(form, "CB_Game").SelectedIndex == 1 ? GameVersion.Shield : GameVersion.Sword,
                int.Parse(FindRequired<TextBox>(form, "TB_TID").Text),
                int.Parse(FindRequired<TextBox>(form, "TB_SID").Text),
                FindRequired<CheckBox>(form, "CB_ShinyCharm").Checked,
                FindRequired<CheckBox>(form, "CB_MarkCharm").Checked);
        }

        form.Load += async (_, _) =>
        {
            try
            {
                settings = await services.Profiles.LoadAsync(cancellationToken);
                if (CanUpdate(form, cancellationToken))
                {
                    RefreshList(settings.ActiveProfileName);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception) when (!CanUpdate(form, cancellationToken))
            {
            }
            catch (Exception exception)
            {
                showMessage("Profile load failed", exception.Message);
            }
        };
        list.SelectedIndexChanged += (_, _) =>
        {
            var selectedName = list.SelectedItem?.ToString();
            var profile = settings.Profiles.FirstOrDefault(candidate => candidate.Name == selectedName);
            if (profile is not null)
            {
                ShowProfile(profile);
            }
        };
        FindRequired<Button>(form, "B_Add").Click += async (_, _) =>
        {
            try
            {
                var profile = ReadEditorProfile();
                var profiles = settings.Profiles.Where(candidate => candidate.Name != profile.Name).Append(profile).ToArray();
                settings = new RngApplicationSettings(settings.ActiveProfileName, profiles, settings.MaxSearchTasksPowerOfTwo);
                await services.Profiles.SaveAsync(settings, cancellationToken);
                if (CanUpdate(form, cancellationToken))
                {
                    RefreshList(profile.Name);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception) when (!CanUpdate(form, cancellationToken))
            {
            }
            catch (Exception exception)
            {
                showMessage("Profile save failed", exception.Message);
            }
        };
        FindRequired<Button>(form, "B_Remove").Click += async (_, _) =>
        {
            var selectedName = list.SelectedItem?.ToString();
            if (selectedName is null)
            {
                return;
            }

            try
            {
                var profiles = settings.Profiles.Where(profile => profile.Name != selectedName).ToArray();
                var activeName = settings.ActiveProfileName == selectedName ? null : settings.ActiveProfileName;
                settings = new RngApplicationSettings(activeName, profiles, settings.MaxSearchTasksPowerOfTwo);
                await services.Profiles.SaveAsync(settings, cancellationToken);
                if (CanUpdate(form, cancellationToken))
                {
                    RefreshList(activeName);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception) when (!CanUpdate(form, cancellationToken))
            {
            }
            catch (Exception exception)
            {
                showMessage("Profile delete failed", exception.Message);
            }
        };
        FindRequired<Button>(form, "B_Select").Click += async (_, _) =>
        {
            var selectedName = list.SelectedItem?.ToString();
            var profile = settings.Profiles.FirstOrDefault(candidate => candidate.Name == selectedName);
            if (profile is null)
            {
                return;
            }

            try
            {
                settings = new RngApplicationSettings(profile.Name, settings.Profiles, settings.MaxSearchTasksPowerOfTwo);
                await services.Profiles.SaveAsync(settings, cancellationToken);
                if (!CanUpdate(form, cancellationToken))
                {
                    return;
                }
                profileSelected?.Invoke(profile);
                form.DialogResult = DialogResult.OK;
                form.Close();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception) when (!CanUpdate(form, cancellationToken))
            {
            }
            catch (Exception exception)
            {
                showMessage("Profile selection failed", exception.Message);
            }
        };
    }

    public Form CreateEncounterLookup(GameVersion game)
    {
        var form = CreateForm("EncounterLookup", "Encounter Lookup", 920, 540);
        form.Controls.Add(CreateLabel("L_Game", "Game:", 12, 14, 52));
        form.Controls.Add(CreateComboBox("CB_Game", ["Sword", "Shield"], 66, 11, 120, (int)game));
        form.Controls.Add(CreateLabel("L_Species", "Species:", 202, 14, 60));
        form.Controls.Add(CreateComboBox("CB_Species", ["None"], 264, 11, 220, 0));
        form.Controls.Add(CreateGrid(
            "DGV_Results",
            [
                "Species", "Encounter Type", "Area", "Weather", "Encounter Rate", "Slot Min", "Slot Max",
                "Level", "Min Level", "Max Level", "Shiny Locked", "Gender Locked", "Ability Locked",
                "Ability", "Guaranteed IVs",
            ],
            new Rectangle(12, 48, 880, 440)));
        WireEncounterLookupActions(form);
        return form;
    }

    private void WireEncounterLookupActions(Form form)
    {
        var cancellationToken = GetWindowCancellationToken(form);
        var game = FindRequired<ComboBox>(form, "CB_Game");
        var species = FindRequired<ComboBox>(form, "CB_Species");

        async Task LookupAsync()
        {
            var selectedSpecies = species.Text;
            if (string.IsNullOrWhiteSpace(selectedSpecies) || selectedSpecies is "None" or "(None)")
            {
                FindRequired<DataGridView>(form, "DGV_Results").Rows.Clear();
                return;
            }

            try
            {
                var results = await services.EncounterCatalog.LookupAsync(
                    game.SelectedIndex == 1 ? GameVersion.Shield : GameVersion.Sword,
                    selectedSpecies,
                    cancellationToken);
                if (!CanUpdate(form, cancellationToken))
                {
                    return;
                }
                var grid = FindRequired<DataGridView>(form, "DGV_Results");
                grid.Rows.Clear();
                foreach (var result in results)
                {
                    grid.Rows.Add(
                        result.Species,
                        result.EncounterKind,
                        result.Area,
                        result.Weather,
                        result.EncounterRate.ToString(),
                        result.SlotMinimum.ToString(),
                        result.SlotMaximum.ToString(),
                        result.Level.ToString(),
                        result.MinimumLevel.ToString(),
                        result.MaximumLevel.ToString(),
                        result.IsShinyLocked.ToString(),
                        result.IsGenderLocked.ToString(),
                        result.IsAbilityLocked.ToString(),
                        result.LockedAbility.ToString(),
                        result.GuaranteedIndividualValues.ToString());
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception) when (!CanUpdate(form, cancellationToken))
            {
            }
            catch (Exception exception)
            {
                showMessage("Encounter lookup failed", exception.Message);
            }
        }

        form.Load += async (_, _) =>
        {
            try
            {
                var options = await services.EncounterCatalog.GetDexRecommendationOptionsAsync(
                    includeNone: true,
                    cancellationToken);
                if (CanUpdate(form, cancellationToken))
                {
                    ReplaceItems(species, options);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception) when (!CanUpdate(form, cancellationToken))
            {
            }
            catch (Exception exception)
            {
                showMessage("Encounter lookup failed", exception.Message);
            }
        };
        species.SelectedIndexChanged += async (_, _) => await LookupAsync();
        game.SelectedIndexChanged += async (_, _) => await LookupAsync();
    }

    public Form CreateSpreadFinder()
    {
        var form = CreateForm("SpreadFinder", "Spread Finder", 900, 560);
        var filters = new GroupBox
        {
            Name = "GB_Filters",
            Text = "Filters",
            Location = new Point(12, 12),
            Size = new Size(330, 330),
        };
        var stats = new[] { "HP", "Atk", "Def", "SpA", "SpD", "Spe" };
        for (var index = 0; index < stats.Length; index++)
        {
            var stat = stats[index];
            var top = 22 + (index * 32);
            filters.Controls.Add(CreateLabel($"L_{stat}", $"{stat}:", 8, top + 3, 42));
            filters.Controls.Add(CreateNumeric($"NUD_{stat}_Min", 52, top, 58, 0, 31));
            filters.Controls.Add(CreateLabel($"L_{stat}Spacer", "-", 114, top + 3, 12));
            filters.Controls.Add(CreateNumeric($"NUD_{stat}_Max", 130, top, 58, 31, 31));
            filters.Controls.Add(CreateButton($"B_{stat}_Min", "0", 194, top, 54));
            filters.Controls.Add(CreateButton($"B_{stat}_Max", "31", 254, top, 54));
        }

        filters.Controls.Add(CreateLabel("L_GuaranteedIVs", "Guaranteed IVs:", 8, 218, 104));
        filters.Controls.Add(CreateNumeric("NUD_GuaranteedIVs", 116, 215, 72, 0, 6));
        filters.Controls.Add(CreateCheckBox("CB_RareEC", "Rare EC?", 194, 215, 100));
        filters.Controls.Add(CreateLabel("L_Filter_Height", "Height:", 8, 251, 60));
        filters.Controls.Add(CreateComboBox(
            "CB_Filter_Height",
            ["Ignore", "XXXS", "XXS", "XS", "S", "M", "L", "XL", "XXL", "XXXL", "XXXS or XXXL"],
            72,
            248,
            236,
            0));
        form.Controls.Add(filters);

        form.Controls.Add(CreateLabel("L_Target", "Target:", 360, 18, 60));
        form.Controls.Add(CreateTextBox("TB_Single", "DEADC0DE", 424, 15, 160));
        form.Controls.Add(CreateButton("B_GenerateSingle", "Generate Single", 594, 14, 128));
        form.Controls.Add(new RadioButton
        {
            Name = "RB_Seed",
            Text = "Seed",
            Location = new Point(734, 16),
            Size = new Size(66, 24),
            Checked = true,
        });
        form.Controls.Add(CreateLabel("L_Tasks", "Search Tasks:", 360, 50, 90));
        form.Controls.Add(CreateComboBox("CB_Tasks", ["1", "2", "4", "8", "16", "32", "64", "128"], 454, 47, 100, 0));
        form.Controls.Add(CreateButton("B_Search", "Search!", 564, 46, 300));
        form.Controls.Add(CreateGrid(
            "DGV_Results",
            ["Seed", "EC", "HP", "Atk", "Def", "SpA", "SpD", "Spe", "Height"],
            new Rectangle(360, 84, 504, 414)));
        WireSpreadFinderActions(form);
        return form;
    }

    private void WireSpreadFinderActions(Form form)
    {
        var cancellationToken = GetWindowCancellationToken(form);
        foreach (var stat in new[] { "HP", "Atk", "Def", "SpA", "SpD", "Spe" })
        {
            var minimum = FindRequired<NumericUpDown>(form, $"NUD_{stat}_Min");
            var maximum = FindRequired<NumericUpDown>(form, $"NUD_{stat}_Max");
            FindRequired<Button>(form, $"B_{stat}_Min").Click += (_, _) =>
            {
                minimum.Value = 0;
                maximum.Value = 0;
            };
            FindRequired<Button>(form, $"B_{stat}_Max").Click += (_, _) =>
            {
                minimum.Value = 31;
                maximum.Value = 31;
            };
        }

        FindRequired<Button>(form, "B_GenerateSingle").Click += async (_, _) =>
        {
            try
            {
                var seedText = FindRequired<TextBox>(form, "TB_Single").Text.Trim();
                if (!uint.TryParse(seedText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var seed))
                {
                    throw new ArgumentException("Target must be an eight-digit hexadecimal seed.");
                }

                await SearchSpreadsAsync(form, new SpreadSearchScope.Seeds([seed]), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception) when (!CanUpdate(form, cancellationToken))
            {
            }
            catch (Exception exception)
            {
                showMessage("Spread Finder failed", exception.Message);
            }
        };
        FindRequired<Button>(form, "B_Search").Click += async (_, _) =>
        {
            try
            {
                var partitions = int.Parse(FindRequired<ComboBox>(form, "CB_Tasks").Text, CultureInfo.InvariantCulture);
                await SearchSpreadsAsync(form, new SpreadSearchScope.EntireSpace(partitions), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception) when (!CanUpdate(form, cancellationToken))
            {
            }
            catch (Exception exception)
            {
                showMessage("Spread Finder failed", exception.Message);
            }
        };
    }

    private async Task SearchSpreadsAsync(
        Form form,
        SpreadSearchScope scope,
        CancellationToken cancellationToken)
    {
        var request = new SpreadSearchRequest(
            scope,
            new[] { "HP", "Atk", "Def", "SpA", "SpD", "Spe" }
                .Select(stat => new IndividualValueRange(
                    (byte)FindRequired<NumericUpDown>(form, $"NUD_{stat}_Min").Value,
                    (byte)FindRequired<NumericUpDown>(form, $"NUD_{stat}_Max").Value)),
            (int)FindRequired<NumericUpDown>(form, "NUD_GuaranteedIVs").Value,
            FindRequired<CheckBox>(form, "CB_RareEC").Checked,
            ParseSpreadScale(FindRequired<ComboBox>(form, "CB_Filter_Height").Text));
        var results = await services.SpreadFinder.SearchAsync(request, cancellationToken);
        if (!CanUpdate(form, cancellationToken))
        {
            return;
        }
        var grid = FindRequired<DataGridView>(form, "DGV_Results");
        grid.Rows.Clear();
        foreach (var result in results)
        {
            grid.Rows.Add(
                result.Seed.ToString("X8", CultureInfo.InvariantCulture),
                result.EncryptionConstant.ToString("X8", CultureInfo.InvariantCulture),
                result.IndividualValues.HP.ToString(CultureInfo.InvariantCulture),
                result.IndividualValues.Attack.ToString(CultureInfo.InvariantCulture),
                result.IndividualValues.Defense.ToString(CultureInfo.InvariantCulture),
                result.IndividualValues.SpecialAttack.ToString(CultureInfo.InvariantCulture),
                result.IndividualValues.SpecialDefense.ToString(CultureInfo.InvariantCulture),
                result.IndividualValues.Speed.ToString(CultureInfo.InvariantCulture),
                $"{result.Scale} ({result.Height})");
        }
    }

    private static SpreadScale ParseSpreadScale(string value) => value switch
    {
        "XXXS" => SpreadScale.XXXS,
        "XXS" => SpreadScale.XXS,
        "XS" => SpreadScale.XS,
        "S" => SpreadScale.Small,
        "M" => SpreadScale.Medium,
        "L" => SpreadScale.Large,
        "XL" => SpreadScale.XL,
        "XXL" => SpreadScale.XXL,
        "XXXL" => SpreadScale.XXXL,
        "XXXS or XXXL" => SpreadScale.MinOrMax,
        _ => SpreadScale.Any,
    };

    public Form CreateSpecialTool(
        SpecialToolKind kind,
        RngState state,
        GameVersion game,
        OwoowSpecialToolContext? context = null)
    {
        var form = kind switch
        {
            SpecialToolKind.LotoId => CreateLotoId(state),
            SpecialToolKind.CramOMatic => CreateCramOMatic(state),
            SpecialToolKind.WattTrader => CreateWattTrader(state),
            SpecialToolKind.DiggingPa => CreateDiggingPa(state),
            SpecialToolKind.DiggingBro => CreateDiggingBro(state, game),
            SpecialToolKind.WailordRespawn => CreateWailord(state),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        if (kind == SpecialToolKind.LotoId)
        {
            WireLotoIdListActions(form);
        }
        WireSpecialToolActions(form, kind, game);
        if (context is not null)
        {
            ApplySpecialToolContext(form, kind, context);
        }
        return form;
    }

    private static void ApplySpecialToolContext(
        Form form,
        SpecialToolKind kind,
        OwoowSpecialToolContext context)
    {
        var prefix = GetSpecialToolPrefix(kind);
        FindRequired<TextBox>(form, $"TB_{prefix}_Initial").Text = context.InitialAdvance;
        var menuClose = FindRequired<CheckBox>(form, $"CB_{prefix}_MenuClose");
        var holdDirection = FindRequired<CheckBox>(form, $"CB_{prefix}_MenuClose_Direction");
        var nonPlayerCharacters = FindRequired<TextBox>(form, $"TB_{prefix}_NPCs");
        nonPlayerCharacters.Text = context.NonPlayerCharacters;
        menuClose.Checked = context.MenuClose;
        holdDirection.Checked = context.HoldDirection;

        var weatherControls = form.Controls.Find($"CB_{prefix}_Weather", true);
        if (weatherControls.Length > 0 && weatherControls[0] is ComboBox weather)
        {
            var index = weather.Items.IndexOf(context.Weather);
            if (index >= 0)
            {
                weather.SelectedIndex = index;
            }
        }

        void NotifyStateChanged() => context.StateChanged(new OwoowSpecialToolState(
            nonPlayerCharacters.Text,
            menuClose.Checked,
            holdDirection.Checked));

        menuClose.CheckedChanged += (_, _) => NotifyStateChanged();
        holdDirection.CheckedChanged += (_, _) => NotifyStateChanged();
        form.FormClosing += (_, _) => NotifyStateChanged();
    }

    private void WireLotoIdListActions(Form form)
    {
        var state = new LotoIdEditorState();
        form.Tag = state;
        var cancellationToken = GetWindowCancellationToken(form);
        var idListButton = FindRequired<Button>(form, "B_IDList");
        var searchButton = FindRequired<Button>(form, "B_LotoID_Search");
        idListButton.Enabled = false;
        searchButton.Enabled = false;

        void RefreshCount() => FindRequired<Label>(form, "L_LoadedIDs").Text = $"Loaded IDs: {state.Ids.Count}";

        form.Load += async (_, _) =>
        {
            try
            {
                var loaded = await services.LotoIds.LoadAsync(cancellationToken);
                if (!CanUpdate(form, cancellationToken))
                {
                    return;
                }
                state.Ids.Clear();
                state.Ids.AddRange(loaded);
                RefreshCount();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception) when (!CanUpdate(form, cancellationToken))
            {
            }
            catch (Exception exception)
            {
                showMessage("Loto-ID list load failed", exception.Message);
            }
            finally
            {
                if (CanUpdate(form, cancellationToken))
                {
                    idListButton.Enabled = true;
                    searchButton.Enabled = true;
                }
            }
        };
        idListButton.Click += (_, _) =>
        {
            if (state.Dialog is { IsDisposed: false })
            {
                state.Dialog.Focus();
                return;
            }

            state.Dialog = CreateLotoIdList(state, RefreshCount);
            if (form.Visible)
            {
                state.Dialog.Show(form);
            }
            else
            {
                state.Dialog.Show();
            }
        };
    }

    private Form CreateLotoIdList(LotoIdEditorState state, Action refreshParent)
    {
        var dialog = CreateForm("IDList", "ID List", 215, 153, fixedBorder: true);
        var list = new ListBox
        {
            Name = "LB_IDs",
            Location = new Point(8, 8),
            Size = new Size(120, 139),
        };
        dialog.Controls.Add(list);
        dialog.Controls.Add(CreateLabel("L_ID", "ID:", 134, 8, 27));
        var id = CreateTextBox("TB_ID", "123456", 161, 8, 48);
        id.MaxLength = 6;
        dialog.Controls.Add(id);
        dialog.Controls.Add(CreateButton("B_Add", "Add", 134, 32, 75));
        dialog.Controls.Add(CreateButton("B_Remove", "Remove", 134, 59, 75));

        void RefreshList(string? selected = null)
        {
            list.Items.Clear();
            list.Items.AddRange(state.Ids.Cast<object>().ToArray());
            if (selected is not null && list.Items.Contains(selected))
            {
                list.SelectedItem = selected;
            }
        }

        RefreshList();
        list.SelectedIndexChanged += (_, _) =>
        {
            if (list.SelectedItem is string selected)
            {
                id.Text = selected;
            }
        };
        FindRequired<Button>(dialog, "B_Add").Click += (_, _) =>
        {
            var input = id.Text.Trim();
            if (input.Length == 0)
            {
                return;
            }
            var value = input.PadLeft(6, '0');
            if (value.Length != 6 || value.Any(character => !char.IsAsciiDigit(character)))
            {
                showMessage("Loto-ID list", "Each ID must contain at most six decimal digits.");
                return;
            }
            if (!state.Ids.Contains(value, StringComparer.Ordinal))
            {
                state.Ids.Add(value);
            }
            RefreshList(value);
        };
        FindRequired<Button>(dialog, "B_Remove").Click += (_, _) =>
        {
            var value = id.Text.PadLeft(6, '0');
            state.Ids.Remove(value);
            RefreshList();
        };
        dialog.FormClosed += (_, _) =>
        {
            state.Ids.Sort(StringComparer.Ordinal);
            refreshParent();
            state.Dialog = null;
            _ = SaveLotoIdsAsync(state.Ids.ToArray());
        };
        return dialog;
    }

    private async Task SaveLotoIdsAsync(IReadOnlyList<string> snapshot)
    {
        await lotoSaveGate.WaitAsync();
        try
        {
            await services.LotoIds.SaveAsync(snapshot);
        }
        catch (Exception exception)
        {
            showMessage("Loto-ID list save failed", exception.Message);
        }
        finally
        {
            lotoSaveGate.Release();
        }
    }

    private void WireSpecialToolActions(Form form, SpecialToolKind kind, GameVersion game)
    {
        var cancellationToken = GetWindowCancellationToken(form);
        var prefix = GetSpecialToolPrefix(kind);
        var menuClose = FindRequired<CheckBox>(form, $"CB_{prefix}_MenuClose");
        var holdDirection = FindRequired<CheckBox>(form, $"CB_{prefix}_MenuClose_Direction");
        var nonPlayerCharacters = FindRequired<TextBox>(form, $"TB_{prefix}_NPCs");
        var nonPlayerCharacterLabel = FindRequired<Label>(form, $"L_{prefix}_NPCs");
        menuClose.CheckedChanged += (_, _) =>
        {
            holdDirection.Enabled = menuClose.Checked;
            nonPlayerCharacters.Enabled = menuClose.Checked;
            nonPlayerCharacterLabel.Enabled = menuClose.Checked;
        };

        FindRequired<Button>(form, $"B_{prefix}_Search").Click += async (sender, _) =>
        {
            var button = (Button)sender!;
            try
            {
                button.Enabled = false;
                var request = CreateSpecialToolRequest(form, kind, prefix, game);
                var results = await services.SpecialTools.SearchAsync(
                    request,
                    cancellationToken: cancellationToken);
                if (!CanUpdate(form, cancellationToken))
                {
                    return;
                }
                BindSpecialToolResults(FindRequired<DataGridView>(form, "DGV_Results"), kind, results, form);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception) when (!CanUpdate(form, cancellationToken))
            {
            }
            catch (Exception exception)
            {
                showMessage($"{form.Text} search failed", exception.Message);
            }
            finally
            {
                if (CanUpdate(form, cancellationToken))
                {
                    button.Enabled = true;
                }
            }
        };
    }

    private static string GetSpecialToolPrefix(SpecialToolKind kind) => kind switch
    {
        SpecialToolKind.LotoId => "LotoID",
        SpecialToolKind.CramOMatic => "Cramomatic",
        SpecialToolKind.WattTrader => "WattTrader",
        SpecialToolKind.DiggingPa => "DiggingPa",
        SpecialToolKind.DiggingBro => "SkillBro",
        SpecialToolKind.WailordRespawn => "Wailord",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static SpecialToolSearchRequest CreateSpecialToolRequest(
        Form form,
        SpecialToolKind kind,
        string prefix,
        GameVersion mainGame)
    {
        NormalizeSearchInputs(form, prefix);
        var start = ParseUnsigned(FindRequired<TextBox>(form, $"TB_{prefix}_Initial").Text, "Initial Adv.");
        var advances = ParseUnsigned(FindRequired<TextBox>(form, $"TB_{prefix}_Advances").Text, "Advances");
        var end = checked(start + advances);
        var menuEnabled = FindRequired<CheckBox>(form, $"CB_{prefix}_MenuClose").Checked;
        var menu = menuEnabled
            ? new SpecialToolMenuClose(
                true,
                ParseUInt32(FindRequired<TextBox>(form, $"TB_{prefix}_NPCs").Text, "NPCs"),
                FindRequired<CheckBox>(form, $"CB_{prefix}_MenuClose_Direction").Checked,
                ParseWeather(form, prefix))
            : SpecialToolMenuClose.None;

        var game = kind == SpecialToolKind.DiggingBro
            ? ParseGame(FindRequired<ComboBox>(form, "CB_SkillBro_Game"))
            : mainGame;
        var rewards = kind == SpecialToolKind.DiggingBro
            ? Enum.GetValues<DiggingBroReward>().ToDictionary(
                reward => reward,
                reward => checked((byte)FindRequired<NumericUpDown>(form, $"NUD_{reward}").Value))
            : null;
        var cramInputs = kind == SpecialToolKind.CramOMatic
            ? Enumerable.Range(1, 4)
                .Select(index => ParseCramInput(FindRequired<ComboBox>(form, $"CB_Item{index}")))
                .ToArray()
            : null;

        return new SpecialToolSearchRequest(
            kind,
            ReadState(form),
            start,
            end,
            game,
            lotoIds: kind == SpecialToolKind.LotoId && form.Tag is LotoIdEditorState lotoState
                ? lotoState.Ids
                : null,
            lotoPrize: kind == SpecialToolKind.LotoId
                ? ParseLotoPrize(FindRequired<ComboBox>(form, "CB_Target"))
                : LotoPrizeFilter.Any,
            success: kind == SpecialToolKind.WailordRespawn
                ? ParseSuccess(FindRequired<ComboBox>(form, "CB_Target"))
                : SuccessFilter.Any,
            cramInputs: cramInputs,
            cramPrize: kind == SpecialToolKind.CramOMatic
                ? ParseCramPrize(FindRequired<ComboBox>(form, "CB_Target"))
                : CramPrizeFilter.Any,
            bonusOnly: kind == SpecialToolKind.CramOMatic
                && FindRequired<CheckBox>(form, "CB_BonusOnly").Checked,
            wattTraderSlotMinimum: kind == SpecialToolKind.WattTrader
                ? ParseUInt32(FindRequired<TextBox>(form, "TB_SlotMin").Text, "Minimum slot")
                : 0,
            wattTraderSlotMaximum: kind == SpecialToolKind.WattTrader
                ? ParseUInt32(FindRequired<TextBox>(form, "TB_SlotMax").Text, "Maximum slot")
                : 999,
            diggingPaMinimumWatts: kind == SpecialToolKind.DiggingPa
                ? ParseUnsigned(FindRequired<TextBox>(form, "TB_Target").Text, "Minimum watts")
                : 0,
            diggingBroMinimumTotal: kind == SpecialToolKind.DiggingBro
                ? decimal.ToInt32(FindRequired<NumericUpDown>(form, "NUD_MinTotal").Value)
                : 0,
            diggingBroMinimumRewards: rewards,
            menuClose: menu);
    }

    private static void NormalizeSearchInputs(Form form, string prefix)
    {
        var initial = FindRequired<TextBox>(form, $"TB_{prefix}_Initial");
        var advances = FindRequired<TextBox>(form, $"TB_{prefix}_Advances");
        var seed0 = FindRequired<TextBox>(form, "TB_Seed0");
        var seed1 = FindRequired<TextBox>(form, "TB_Seed1");
        if (string.IsNullOrWhiteSpace(initial.Text))
        {
            initial.Text = "0";
        }
        if (string.IsNullOrWhiteSpace(advances.Text) || advances.Text == "0")
        {
            advances.Text = "1";
        }
        if (string.IsNullOrWhiteSpace(seed0.Text))
        {
            seed0.Text = "0";
        }
        if (string.IsNullOrWhiteSpace(seed1.Text))
        {
            seed1.Text = "0";
        }
        if (seed0.Text == "0" && seed1.Text == "0")
        {
            seed0.Text = "1337";
            seed1.Text = "1390";
        }
        seed0.Text = seed0.Text.PadLeft(16, '0');
        seed1.Text = seed1.Text.PadLeft(16, '0');
    }

    private static void BindSpecialToolResults(
        DataGridView grid,
        SpecialToolKind kind,
        IReadOnlyList<SpecialToolFrame> results,
        Form form)
    {
        grid.Rows.Clear();
        foreach (var result in results)
        {
            var values = new List<object>
            {
                result.Advance.ToString("N0", CultureInfo.InvariantCulture),
                $"+{result.Jump}",
                result.Animation.ToString(),
            };
            switch (kind)
            {
                case SpecialToolKind.LotoId:
                    values.Add(result.Identifier ?? string.Empty);
                    values.Add(result.PrimaryResult ?? string.Empty);
                    break;
                case SpecialToolKind.CramOMatic:
                    values.Add(result.PrimaryResult ?? string.Empty);
                    values.Add(result.Bonus ?? false);
                    break;
                case SpecialToolKind.WattTrader:
                    values.Add(result.PrimaryResult ?? string.Empty);
                    values.Add(result.SecondaryResult ?? string.Empty);
                    break;
                case SpecialToolKind.DiggingPa:
                    values.Add(result.PrimaryResult ?? string.Empty);
                    values.Add(result.SecondaryResult ?? string.Empty);
                    break;
                case SpecialToolKind.DiggingBro:
                    values.Add(result.Total ?? 0);
                    values.AddRange(Enum.GetValues<DiggingBroReward>()
                        .Select(reward => (object)result.Rewards.GetValueOrDefault(reward)));
                    break;
                case SpecialToolKind.WailordRespawn:
                    values.Add(result.Success switch { true => "Y", false => "N", null => string.Empty });
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
            values.Add(result.State.Seed0.ToString("X16", CultureInfo.InvariantCulture));
            values.Add(result.State.Seed1.ToString("X16", CultureInfo.InvariantCulture));
            var rowIndex = grid.Rows.Add(values.ToArray());
            FormatSpecialToolRow(grid.Rows[rowIndex], kind, result, form);
        }
    }

    private static void FormatSpecialToolRow(
        DataGridViewRow row,
        SpecialToolKind kind,
        SpecialToolFrame result,
        Form form)
    {
        row.DefaultCellStyle.BackColor = row.Index % 2 == 0 ? Color.White : Color.WhiteSmoke;
        switch (kind)
        {
            case SpecialToolKind.LotoId:
                var target = FindRequired<ComboBox>(form, "CB_Target").Text;
                if (target != "All" && result.PrimaryResult == target)
                {
                    row.DefaultCellStyle.BackColor = Color.PapayaWhip;
                }
                else if (result.PrimaryResult == "Master Ball")
                {
                    row.DefaultCellStyle.BackColor = Color.LightCyan;
                }
                break;
            case SpecialToolKind.CramOMatic:
                if (result.PrimaryResult is "Safari Ball" or "Sport Ball")
                {
                    row.DefaultCellStyle.BackColor = Color.PapayaWhip;
                }
                else if (result.Bonus == true)
                {
                    row.DefaultCellStyle.BackColor = Color.LightCyan;
                }
                break;
            case SpecialToolKind.WattTrader:
                if (result.PrimaryResult is "Beast Ball x1 (827)" or "Dream Ball x1 (828)")
                {
                    row.DefaultCellStyle.BackColor = Color.PapayaWhip;
                }
                break;
            case SpecialToolKind.DiggingPa:
                if (result.Watts >= 1_000_000)
                {
                    row.DefaultCellStyle.BackColor = Color.LightCyan;
                }
                else if (result.Watts >= 700_000)
                {
                    row.DefaultCellStyle.BackColor = Color.PapayaWhip;
                }
                break;
            case SpecialToolKind.DiggingBro:
                if (result.Total >= 10)
                {
                    row.DefaultCellStyle.BackColor = Color.LightCyan;
                }
                else if (result.Total >= 7)
                {
                    row.DefaultCellStyle.BackColor = Color.PapayaWhip;
                }
                var rewardValues = Enum.GetValues<DiggingBroReward>();
                for (var index = 0; index < rewardValues.Length; index++)
                {
                    if (result.Rewards.GetValueOrDefault(rewardValues[index]) > 0)
                    {
                        row.Cells[4 + index].Style.Font = new Font(
                            row.DataGridView?.Font ?? SystemFonts.DefaultFont,
                            FontStyle.Bold);
                    }
                }
                break;
            case SpecialToolKind.WailordRespawn:
                if (result.Success == true)
                {
                    row.DefaultCellStyle.BackColor = Color.PapayaWhip;
                }
                break;
        }
    }

    private static RngState ReadState(Control root) => new(
        ParseHexUInt64(FindRequired<TextBox>(root, "TB_Seed0").Text, "Seed[0]"),
        ParseHexUInt64(FindRequired<TextBox>(root, "TB_Seed1").Text, "Seed[1]"));

    private static ulong ParseHexUInt64(string text, string fieldName) =>
        ulong.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new FormatException($"{fieldName} must be a hexadecimal value.");

    private static ulong ParseUnsigned(string text, string fieldName) =>
        ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new FormatException($"{fieldName} must be a non-negative whole number.");

    private static ulong ParseFlexibleUnsigned(string text, string fieldName)
    {
        var trimmed = text.Trim();
        var isHex = trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        var digits = isHex ? trimmed[2..] : trimmed;
        return ulong.TryParse(
            digits,
            isHex ? NumberStyles.AllowHexSpecifier : NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : throw new FormatException($"{fieldName} must be a positive decimal or 0x-prefixed hexadecimal value.");
    }

    private static uint ParseUInt32(string text, string fieldName) =>
        uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new FormatException($"{fieldName} must be a non-negative whole number no greater than {uint.MaxValue}.");

    private static int ParseInt32(string text, string fieldName) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new FormatException($"{fieldName} must be a non-negative whole number no greater than {int.MaxValue}.");

    private static GameVersion ParseGame(ComboBox comboBox) =>
        comboBox.SelectedIndex == 1 ? GameVersion.Shield : GameVersion.Sword;

    private static Weather ParseWeather(Form form, string prefix)
    {
        var controls = form.Controls.Find($"CB_{prefix}_Weather", true);
        if (controls.Length == 0 || controls[0] is not ComboBox comboBox)
        {
            return Weather.Normal;
        }
        if (comboBox.SelectedIndex <= 0)
        {
            return Weather.Any;
        }
        return (Weather)(comboBox.SelectedIndex - 1);
    }

    private static CramInputItem ParseCramInput(ComboBox comboBox) =>
        comboBox.SelectedIndex < 0 ? CramInputItem.BlackApricorn : (CramInputItem)comboBox.SelectedIndex;

    private static LotoPrizeFilter ParseLotoPrize(ComboBox comboBox) =>
        comboBox.SelectedIndex < 0 ? LotoPrizeFilter.Any : (LotoPrizeFilter)comboBox.SelectedIndex;

    private static CramPrizeFilter ParseCramPrize(ComboBox comboBox) =>
        comboBox.SelectedIndex < 0 ? CramPrizeFilter.Any : (CramPrizeFilter)comboBox.SelectedIndex;

    private static SuccessFilter ParseSuccess(ComboBox comboBox) =>
        comboBox.SelectedIndex switch
        {
            0 => SuccessFilter.Yes,
            1 => SuccessFilter.No,
            _ => SuccessFilter.Any,
        };

    private static (uint Minimum, uint Maximum) GetWattTraderSlotRange(string item) => item switch
    {
        "Bottle Cap x1" => (0, 49),
        "Bottle Cap x3" => (50, 59),
        "Gold Bottle Cap x1" => (60, 62),
        "Red Apricorn x5" => (63, 79),
        "Blue Apricorn x5" => (80, 96),
        "Yellow Apricorn x5" => (97, 113),
        "Green Apricorn x5" => (114, 130),
        "White Apricorn x5" => (131, 147),
        "Black Apricorn x5" => (148, 164),
        "Pink Apricorn x5" => (165, 181),
        "Red Apricorn x10" => (182, 191),
        "Blue Apricorn x10" => (192, 201),
        "Yellow Apricorn x10" => (202, 211),
        "Green Apricorn x10" => (212, 221),
        "White Apricorn x10" => (222, 231),
        "Black Apricorn x10" => (232, 241),
        "Pink Apricorn x10" => (242, 251),
        "PP Up x1" => (252, 301),
        "PP Up x2" => (302, 326),
        "PP Max x1" => (327, 331),
        "Rare Candy x1" => (332, 381),
        "Rare Candy x5" => (382, 421),
        "Gigantamix x1" => (422, 471),
        "Armorite Ore x1" => (472, 531),
        "Armorite Ore x3" => (532, 551),
        "Armorite Ore x8" => (552, 556),
        "Dynite Ore x1" => (557, 576),
        "Dynite Ore x5" => (577, 581),
        "Max Mushrooms x1" => (582, 601),
        "Max Elixir x1" => (602, 641),
        "Galarica Twig x3" => (642, 681),
        "Galarica Twig x5" => (682, 691),
        "Strawberry Sweet x1" => (692, 706),
        "Love Sweet x1" => (707, 721),
        "Berry Sweet x1" => (722, 736),
        "Clover Sweet x1" => (737, 751),
        "Flower Sweet x1" => (752, 766),
        "Star Sweet x1" => (767, 781),
        "Ribbon Sweet x1" => (782, 796),
        "Big Nugget x1" => (797, 826),
        "Beast Ball x1" => (827, 827),
        "Dream Ball x1" => (828, 828),
        "Lansat Berry x1" => (829, 858),
        "Starf Berry x1" => (859, 868),
        "Lucky Egg x1" => (869, 878),
        "Electirizer x1" => (879, 908),
        "Magmarizer x1" => (909, 938),
        "Cracked Pot x1" => (939, 968),
        "Chipped Pot x1" => (969, 979),
        "King's Rock x1" => (980, 999),
        "Beast or Dream Ball" => (827, 828),
        _ => (0, 999),
    };

    public Form CreateXoroshiroTools(RngState state, Action<RngState>? stateSelected = null)
    {
        var form = CreateForm("XoroshiroTools", "Xoroshiro Tools", 217, 243, fixedBorder: true);
        form.Controls.Add(CreateLabel("L_Seed0", "State[0]:", 25, 7, 60));
        form.Controls.Add(CreateTextBox("TB_Seed0", state.Seed0.ToString("X16"), 89, 7, 118));
        form.Controls.Add(CreateLabel("L_Seed1", "State[1]:", 25, 31, 60));
        form.Controls.Add(CreateTextBox("TB_Seed1", state.Seed1.ToString("X16"), 89, 31, 118));
        form.Controls.Add(CreateLabel("L_Operation", "Operation:", 12, 55, 70));
        form.Controls.Add(CreateComboBox(
            "CB_Operation",
            ["Advance 𝑛", "Backwards 𝑛", "NextInt(𝑛)", "Find Initial"],
            89,
            55,
            118,
            0));
        form.Controls.Add(CreateLabel("L_N", "𝑛:", 58, 80, 26));
        form.Controls.Add(CreateTextBox("TB_N", string.Empty, 89, 80, 118));
        form.Controls.Add(CreateButton("B_Calculate", "Calculate", 12, 108, 195));
        form.Controls.Add(CreateLabel("L_Result_S0", "State[0]:", 25, 139, 60));
        form.Controls.Add(CreateTextBox("TB_Result_S0", string.Empty, 89, 139, 118, readOnly: true));
        form.Controls.Add(CreateLabel("L_Result_S1", "State[1]:", 25, 163, 60));
        form.Controls.Add(CreateTextBox("TB_Result_S1", string.Empty, 89, 163, 118, readOnly: true));
        form.Controls.Add(CreateLabel("L_Value", "Value:", 37, 187, 50));
        form.Controls.Add(CreateTextBox("TB_Value", string.Empty, 89, 187, 118, readOnly: true));
        form.Controls.Add(CreateLabel("L_Distance", "Advances:", 14, 211, 70));
        form.Controls.Add(CreateTextBox("TB_Distance", string.Empty, 89, 211, 118, readOnly: true));
        WireXoroshiroActions(form, stateSelected);
        return form;
    }

    private void WireXoroshiroActions(Form form, Action<RngState>? stateSelected)
    {
        var cancellationToken = GetWindowCancellationToken(form);
        ulong? lastValue = null;
        var valueAsHex = false;
        var valueBox = FindRequired<TextBox>(form, "TB_Value");

        void ShowValue()
        {
            valueBox.Text = lastValue is null
                ? string.Empty
                : valueAsHex
                    ? $"0x{lastValue:X}"
                    : lastValue.Value.ToString("N0", CultureInfo.InvariantCulture);
        }

        valueBox.Click += (_, _) =>
        {
            valueAsHex = !valueAsHex;
            ShowValue();
        };
        EventHandler selectState = (_, _) =>
        {
            try
            {
                var state = new RngState(
                    ParseHexUInt64(FindRequired<TextBox>(form, "TB_Result_S0").Text, "Result State[0]"),
                    ParseHexUInt64(FindRequired<TextBox>(form, "TB_Result_S1").Text, "Result State[1]"));
                if ((Control.ModifierKeys & Keys.Shift) != 0)
                {
                    Clipboard.SetText($"0x{state.Seed0:X16}, 0x{state.Seed1:X16}");
                }
                else
                {
                    stateSelected?.Invoke(state);
                }
            }
            catch (Exception exception)
            {
                showMessage("Xoroshiro result selection failed", exception.Message);
            }
        };
        FindRequired<TextBox>(form, "TB_Result_S0").Click += selectState;
        FindRequired<TextBox>(form, "TB_Result_S1").Click += selectState;
        FindRequired<Button>(form, "B_Calculate").Click += async (sender, _) =>
        {
            var button = (Button)sender!;
            try
            {
                button.Enabled = false;
                var operationBox = FindRequired<ComboBox>(form, "CB_Operation");
                var operation = operationBox.SelectedIndex is >= 0 and <= 3
                    ? (XoroshiroOperation)operationBox.SelectedIndex
                    : XoroshiroOperation.Next;
                NormalizeXoroshiroSeeds(form);
                var amountBox = FindRequired<TextBox>(form, "TB_N");
                var amount = string.IsNullOrWhiteSpace(amountBox.Text)
                    ? 1UL
                    : ParseFlexibleUnsigned(amountBox.Text, "n");
                if (amount == 0)
                {
                    amount = 1;
                }
                if (operation == XoroshiroOperation.NextInteger && amount > uint.MaxValue)
                {
                    amount = uint.MaxValue;
                }
                var result = await services.Xoroshiro.CalculateAsync(
                    new XoroshiroRequest(ReadState(form), operation, amount),
                    cancellationToken);
                if (!CanUpdate(form, cancellationToken))
                {
                    return;
                }
                FindRequired<TextBox>(form, "TB_Result_S0").Text =
                    result.State.Seed0.ToString("X16", CultureInfo.InvariantCulture);
                FindRequired<TextBox>(form, "TB_Result_S1").Text =
                    result.State.Seed1.ToString("X16", CultureInfo.InvariantCulture);
                var sign = result.Distance == 0
                    ? string.Empty
                    : operation == XoroshiroOperation.Previous ? "-" : "+";
                FindRequired<TextBox>(form, "TB_Distance").Text =
                    sign + result.Distance.ToString("N0", CultureInfo.InvariantCulture);
                lastValue = result.Value;
                valueAsHex = false;
                ShowValue();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception) when (!CanUpdate(form, cancellationToken))
            {
            }
            catch (Exception exception)
            {
                showMessage("Xoroshiro calculation failed", exception.Message);
            }
            finally
            {
                if (CanUpdate(form, cancellationToken))
                {
                    button.Enabled = true;
                }
            }
        };
    }

    private static void NormalizeXoroshiroSeeds(Form form)
    {
        var seed0 = FindRequired<TextBox>(form, "TB_Seed0");
        var seed1 = FindRequired<TextBox>(form, "TB_Seed1");
        if (string.IsNullOrWhiteSpace(seed0.Text))
        {
            seed0.Text = "0";
        }
        if (string.IsNullOrWhiteSpace(seed1.Text))
        {
            seed1.Text = "0";
        }
        if (seed0.Text == "0" && seed1.Text == "0")
        {
            seed0.Text = "1337";
            seed1.Text = "1390";
        }
        seed0.Text = seed0.Text.PadLeft(16, '0');
        seed1.Text = seed1.Text.PadLeft(16, '0');
    }

    public Form CreateRetailSeedFinder(Action<RngState>? stateSelected = null)
    {
        var form = CreateForm("RetailSeedFinder", "Retail Seed Finder", 919, 164);
        form.Controls.Add(CreateLabel("L_Seed0", "Seed[0]:", 13, 12, 55));
        form.Controls.Add(CreateTextBox("TB_Seed0", string.Empty, 70, 12, 118, readOnly: true));
        form.Controls.Add(CreateLabel("L_Seed1", "Seed[1]:", 13, 36, 55));
        form.Controls.Add(CreateTextBox("TB_Seed1", string.Empty, 70, 36, 118, readOnly: true));
        form.Controls.Add(CreateCheckBox("CB_Advanced", "Advanced Mode", 612, 5, 113));
        form.Controls.Add(CreateLabel("L_Min", "Min Adv.:", 734, 6, 62));
        form.Controls.Add(CreateTextBox("TB_Min", "400", 798, 6, 118, enabled: false));
        form.Controls.Add(CreateLabel("L_Max", "Max Adv.:", 732, 30, 64));
        form.Controls.Add(CreateTextBox("TB_Max", "527", 798, 30, 118, enabled: false));
        form.Controls.Add(CreateButton("B_CalcSeed", "Calculate Seed", 612, 51, 113, enabled: false));
        form.Controls.Add(CreateLabel("L_Status", "Status:", 750, 54, 46));
        form.Controls.Add(CreateTextBox("TB_Status", string.Empty, 798, 54, 118, readOnly: true));
        form.Controls.Add(CreateLabel("L_InputAnimations", "Animations:", 13, 59, 80));
        var input = CreateTextBox("TB_InputAnimations", string.Empty, 13, 79, 903);
        input.MaxLength = 128;
        form.Controls.Add(input);
        form.Controls.Add(CreateButton("B_Physical", "(0) Physical", 13, 103, 88));
        form.Controls.Add(CreateButton("B_Special", "(1) Special", 100, 103, 88));
        form.Controls.Add(CreateLabel("L_CompletedInputs", "Completed Animations: 0 / 128", 745, 105, 171));
        var okButton = CreateButton("OKButton", "Update Main Form", 13, 130, 175);
        okButton.DialogResult = DialogResult.OK;
        form.AcceptButton = okButton;
        form.Controls.Add(okButton);
        WireRetailSeedFinderActions(form, stateSelected);
        return form;
    }

    private void WireRetailSeedFinderActions(Form form, Action<RngState>? stateSelected)
    {
        var cancellationToken = GetWindowCancellationToken(form);
        var input = FindRequired<TextBox>(form, "TB_InputAnimations");
        var advanced = FindRequired<CheckBox>(form, "CB_Advanced");
        var minimum = FindRequired<TextBox>(form, "TB_Min");
        var maximum = FindRequired<TextBox>(form, "TB_Max");
        var calculate = FindRequired<Button>(form, "B_CalcSeed");
        var status = FindRequired<TextBox>(form, "TB_Status");
        var seed0 = FindRequired<TextBox>(form, "TB_Seed0");
        var seed1 = FindRequired<TextBox>(form, "TB_Seed1");
        var calculationVersion = 0;

        void ClearResult()
        {
            seed0.Clear();
            seed1.Clear();
        }

        void ShowState(RngState state)
        {
            seed0.Text = state.Seed0.ToString("X16", CultureInfo.InvariantCulture);
            seed1.Text = state.Seed1.ToString("X16", CultureInfo.InvariantCulture);
        }

        void ShowRangeResults(IReadOnlyList<RngState> results)
        {
            switch (results.Count)
            {
                case 0:
                    status.Text = "No seeds found.";
                    ClearResult();
                    break;
                case 1:
                    status.Text = "Result found!";
                    ShowState(results[0]);
                    break;
                default:
                    status.Text = $"~{Math.Floor(Math.Log2(results.Count))} more inputs";
                    ClearResult();
                    break;
            }
        }

        async Task CalculateAsync(bool forceRange)
        {
            var version = ++calculationVersion;
            var observations = input.Text;
            FindRequired<Label>(form, "L_CompletedInputs").Text =
                $"Completed Motions: {observations.Length} / 128";
            if (observations.Any(value => value is not ('0' or '1')))
            {
                status.Text = "Animations must contain only 0 and 1.";
                ClearResult();
                return;
            }

            try
            {
                if (!advanced.Checked)
                {
                    calculate.Enabled = false;
                    status.Clear();
                    if (observations.Length != 128)
                    {
                        ClearResult();
                        return;
                    }

                    var state = await services.RetailSeeds.FindExactAsync(
                        new RetailSeedRequest(observations),
                        cancellationToken);
                    if (version == calculationVersion && CanUpdate(form, cancellationToken))
                    {
                        ShowState(state);
                    }
                    return;
                }

                if (observations.Length < 64)
                {
                    calculate.Enabled = false;
                    status.Clear();
                    ClearResult();
                    return;
                }

                var min = ParseInt32(minimum.Text, "Minimum advance");
                var max = ParseInt32(maximum.Text, "Maximum advance");
                var requiresButton = max - min > 150;
                calculate.Enabled = requiresButton;
                if (requiresButton && !forceRange)
                {
                    status.Clear();
                    ClearResult();
                    return;
                }

                status.Text = "Calculating...";
                var isLongCalculation = requiresButton && forceRange;
                if (isLongCalculation)
                {
                    input.Enabled = false;
                    FindRequired<Button>(form, "B_Physical").Enabled = false;
                    FindRequired<Button>(form, "B_Special").Enabled = false;
                    calculate.Enabled = false;
                }
                IReadOnlyList<RngState> results;
                try
                {
                    results = await services.RetailSeeds.FindRangeAsync(
                        new RetailRangeSeedRequest(observations, min, max),
                        cancellationToken: cancellationToken);
                }
                finally
                {
                    if (isLongCalculation && CanUpdate(form, cancellationToken))
                    {
                        input.Enabled = true;
                        FindRequired<Button>(form, "B_Physical").Enabled = true;
                        FindRequired<Button>(form, "B_Special").Enabled = true;
                        calculate.Enabled = true;
                    }
                }
                if (version == calculationVersion && CanUpdate(form, cancellationToken))
                {
                    ShowRangeResults(results);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception) when (!CanUpdate(form, cancellationToken))
            {
            }
            catch (Exception exception)
            {
                if (version == calculationVersion)
                {
                    status.Text = exception.Message;
                    ClearResult();
                }
                showMessage("Retail seed search failed", exception.Message);
            }
        }

        FindRequired<Button>(form, "B_Physical").Click += (_, _) =>
        {
            if (input.TextLength < input.MaxLength)
            {
                input.AppendText("0");
            }
        };
        FindRequired<Button>(form, "B_Special").Click += (_, _) =>
        {
            if (input.TextLength < input.MaxLength)
            {
                input.AppendText("1");
            }
        };
        input.KeyPress += (_, eventArgs) =>
        {
            if (!char.IsControl(eventArgs.KeyChar) && eventArgs.KeyChar is not ('0' or '1'))
            {
                eventArgs.Handled = true;
            }
        };
        KeyPressEventHandler decimalOnly = (_, eventArgs) =>
        {
            if (!char.IsControl(eventArgs.KeyChar) && !char.IsAsciiDigit(eventArgs.KeyChar))
            {
                eventArgs.Handled = true;
            }
        };
        minimum.KeyPress += decimalOnly;
        maximum.KeyPress += decimalOnly;
        input.TextChanged += async (_, _) => await CalculateAsync(forceRange: false);
        advanced.CheckedChanged += async (_, _) =>
        {
            minimum.Enabled = advanced.Checked;
            maximum.Enabled = advanced.Checked;
            await CalculateAsync(forceRange: false);
        };
        minimum.Leave += async (_, _) =>
        {
            try
            {
                var min = ParseInt32(minimum.Text, "Minimum advance");
                var max = ParseInt32(maximum.Text, "Maximum advance");
                if (min > max)
                {
                    showMessage("Retail seed range", "Minimum advances cannot be greater than maximum advances.");
                    maximum.Text = minimum.Text;
                }
                await CalculateAsync(forceRange: false);
            }
            catch (Exception exception)
            {
                status.Text = exception.Message;
                showMessage("Retail seed range", exception.Message);
            }
        };
        maximum.Leave += async (_, _) =>
        {
            try
            {
                var min = ParseInt32(minimum.Text, "Minimum advance");
                var max = ParseInt32(maximum.Text, "Maximum advance");
                if (max < min)
                {
                    showMessage("Retail seed range", "Maximum advances cannot be less than minimum advances.");
                    minimum.Text = maximum.Text;
                }
                await CalculateAsync(forceRange: false);
            }
            catch (Exception exception)
            {
                status.Text = exception.Message;
                showMessage("Retail seed range", exception.Message);
            }
        };
        calculate.Click += async (_, _) => await CalculateAsync(forceRange: true);
        FindRequired<Button>(form, "OKButton").Click += (_, _) =>
        {
            try
            {
                var state = ReadState(form);
                stateSelected?.Invoke(state);
            }
            catch (Exception exception)
            {
                form.DialogResult = DialogResult.None;
                showMessage("Retail seed selection failed", exception.Message);
            }
        };
    }

    private Form CreateLotoId(RngState state)
    {
        var form = CreateSpecialToolForm("LotoID", "Loto-ID", state, "LotoID", 3,
            ["Advances", "Jump", "Animation", "ID", "Prize", "Seed0", "Seed1"],
            settingsHeight: 209,
            menuTop: 131);
        var settings = FindRequired<GroupBox>(form, "GB_SearchSettings");
        settings.Controls.Add(CreateLabel("L_Target", "Target:", 40, 60, 46));
        settings.Controls.Add(CreateComboBox("CB_Target", ["Master Ball", "Rare Candy", "PP Max", "PP Up", "Moomoo Milk", "All"], 88, 60, 118, 0));
        settings.Controls.Add(CreateButton("B_IDList", "ID List", 6, 85, 200));
        settings.Controls.Add(CreateLabel("L_LoadedIDs", "Loaded IDs: 0", 129, 111, 77));
        return form;
    }

    private Form CreateCramOMatic(RngState state)
    {
        var form = CreateSpecialToolForm("Cramomatic", "Cram-o-matic", state, "Cramomatic", 22,
            ["Advances", "Jump", "Animation", "Prize", "Bonus", "Seed0", "Seed1"],
            settingsHeight: 289,
            menuTop: 214);
        var settings = FindRequired<GroupBox>(form, "GB_SearchSettings");
        settings.Controls.Add(CreateLabel("L_Target", "Target:", 40, 60, 46));
        settings.Controls.Add(CreateComboBox("CB_Target", ["Sport Ball", "Safari Ball", "Apricorn Ball", "Shop Ball", "Star Sweet", "Ribbon Sweet", "Strawberry Sweet", "Any"], 88, 60, 118, 0));
        settings.Controls.Add(CreateCheckBox("CB_BonusOnly", "Bonus Only?", 114, 85, 92));
        for (var index = 1; index <= 4; index++)
        {
            var top = 110 + ((index - 1) * 25);
            settings.Controls.Add(CreateLabel($"L_Item{index}", $"Item {index}:", 39, top, 47));
            settings.Controls.Add(CreateComboBox(
                $"CB_Item{index}",
                CramInputItems,
                88,
                top,
                118,
                0));
        }
        var grid = FindRequired<DataGridView>(form, "DGV_Results");
        var bonusColumn = new DataGridViewCheckBoxColumn
        {
            Name = grid.Columns[4].Name,
            HeaderText = grid.Columns[4].HeaderText,
            ReadOnly = true,
        };
        grid.Columns.RemoveAt(4);
        grid.Columns.Insert(4, bonusColumn);
        return form;
    }

    private Form CreateWattTrader(RngState state)
    {
        var form = CreateSpecialToolForm("WattTrader", "Watt Trader", state, "WattTrader", 4,
            ["Advances", "Jump", "Animation", "Highlight", "Regular", "Seed0", "Seed1"],
            settingsHeight: 215,
            menuTop: 138);
        var settings = FindRequired<GroupBox>(form, "GB_SearchSettings");
        settings.Controls.Add(CreateLabel("L_Target", "Target:", 12, 60, 46));
        var target = CreateComboBox("CB_Target", WattTraderTargets, 60, 60, 146, 0);
        settings.Controls.Add(target);
        settings.Controls.Add(CreateLabel("L_SlotRange", "Slot Range:", 12, 85, 80));
        settings.Controls.Add(CreateTextBox("TB_SlotMin", "0", 136, 85, 37));
        settings.Controls.Add(CreateLabel("L_Slot_Spacer", "-", 165, 85, 14));
        settings.Controls.Add(CreateTextBox("TB_SlotMax", "999", 179, 85, 27));
        settings.Controls.Add(CreateLabel("L_WattTrader_Weather", "Weather:", 12, 113, 74));
        settings.Controls.Add(CreateComboBox("CB_WattTrader_Weather", WeatherItems, 88, 113, 118, 0));
        target.SelectedIndexChanged += (_, _) =>
        {
            var (minimum, maximum) = GetWattTraderSlotRange(target.Text);
            FindRequired<TextBox>(form, "TB_SlotMin").Text = minimum.ToString(CultureInfo.InvariantCulture);
            FindRequired<TextBox>(form, "TB_SlotMax").Text = maximum.ToString(CultureInfo.InvariantCulture);
        };
        return form;
    }

    private Form CreateDiggingPa(RngState state)
    {
        var form = CreateSpecialToolForm("DiggingPa", "Digging Pa", state, "DiggingPa", 4,
            ["Advances", "Jump", "Animation", "Actual", "Reported", "Seed0", "Seed1"],
            settingsHeight: 194,
            menuTop: 117);
        var settings = FindRequired<GroupBox>(form, "GB_SearchSettings");
        settings.Controls.Add(CreateLabel("L_Target", "Min. Watts:", 12, 64, 74));
        settings.Controls.Add(CreateTextBox("TB_Target", "500000", 88, 64, 118));
        settings.Controls.Add(CreateLabel("L_DiggingPa_Weather", "Weather:", 12, 92, 74));
        settings.Controls.Add(CreateComboBox("CB_DiggingPa_Weather", WeatherItems, 88, 92, 118, 0));
        return form;
    }

    private Form CreateDiggingBro(RngState state, GameVersion game)
    {
        var form = CreateSpecialToolForm("SkillBro", "Digging Bro (Skill)", state, "SkillBro", 4,
            ["Advances", "Jump", "Animation", "Total", "Gold Bottle Cap", "Bottle Cap", "Normal Gem", "Sticky Barb", "Light Clay", "Lagging Tail", "Iron Ball", "Metal Coat", "Ice Stone", "Dawn Stone", "Dusk Stone", "Shiny Stone", "Moon Stone", "Sun Stone", "Fossilized Fish", "Fossilized Drake", "Fossilized Dino", "Fossilized Bird", "Wishing Piece", "Comet Shard", "Rare Bone", "Seed0", "Seed1"],
            settingsHeight: 734,
            menuTop: 110,
            formWidth: 1121,
            formHeight: 798);
        var settings = FindRequired<GroupBox>(form, "GB_SearchSettings");
        settings.Controls.Add(CreateLabel("L_SkillBro_Weather", "Weather:", 12, 60, 74));
        settings.Controls.Add(CreateComboBox("CB_SkillBro_Weather", WeatherItems, 88, 60, 118, 0));
        settings.Controls.Add(CreateLabel("L_SkillBro_Game", "Game:", 12, 85, 74));
        settings.Controls.Add(CreateComboBox("CB_SkillBro_Game", ["Sword", "Shield"], 88, 85, 118, (int)game));
        settings.Controls.Add(CreateLabel("L_MinTotal", "Min Total Rewards:", 12, 182, 156));
        settings.Controls.Add(CreateNumeric("NUD_MinTotal", 171, 182, 35, 5, 100));

        var rewards = Enum.GetValues<DiggingBroReward>();
        for (var index = 0; index < rewards.Length; index++)
        {
            var reward = rewards[index];
            var top = 207 + (index * 25);
            var name = reward.ToString();
            var label = CreateLabel($"L_{name}", SplitPascalCase(name) + ":", 12, top, 156);
            var value = CreateNumeric($"NUD_{name}", 171, top, 35, 0, 100);
            label.Click += (_, _) => value.Value = 0;
            settings.Controls.Add(label);
            settings.Controls.Add(value);
        }
        return form;
    }

    private Form CreateWailord(RngState state)
    {
        var form = CreateSpecialToolForm("WailordRespawn", "Wailord Respawn", state, "Wailord", 3,
            ["Advances", "Jump", "Animation", "Respawn", "Seed0", "Seed1"],
            settingsHeight: 166,
            menuTop: 89);
        var settings = FindRequired<GroupBox>(form, "GB_SearchSettings");
        settings.Controls.Add(CreateLabel("L_Target", "Target:", 40, 60, 46));
        settings.Controls.Add(CreateComboBox("CB_Target", ["Success", "Fail", "All"], 88, 60, 118, 0));
        return form;
    }

    private Form CreateSpecialToolForm(
        string name,
        string title,
        RngState state,
        string prefix,
        int defaultNonPlayerCharacters,
        IReadOnlyList<string> columns,
        int settingsHeight,
        int menuTop,
        int formWidth = 800,
        int formHeight = 450)
    {
        var form = CreateForm(name, title, formWidth, formHeight);
        var seed = new GroupBox
        {
            Name = "GB_Seed",
            Text = string.Empty,
            Location = new Point(0, 2),
            Size = new Size(212, 60),
        };
        seed.Controls.Add(CreateLabel("L_Seed0", "Seed[0]:", 33, 9, 53));
        seed.Controls.Add(CreateTextBox("TB_Seed0", state.Seed0.ToString("X16"), 88, 9, 118));
        seed.Controls.Add(CreateLabel("L_Seed1", "Seed[1]:", 33, 33, 53));
        seed.Controls.Add(CreateTextBox("TB_Seed1", state.Seed1.ToString("X16"), 88, 33, 118));
        form.Controls.Add(seed);

        var settings = new GroupBox
        {
            Name = "GB_SearchSettings",
            Text = string.Empty,
            Location = new Point(0, 53),
            Size = new Size(212, settingsHeight),
        };
        settings.Controls.Add(CreateLabel($"L_{prefix}_Initial", "Initial Adv.", 12, 13, 74));
        settings.Controls.Add(CreateTextBox($"TB_{prefix}_Initial", "0", 88, 13, 118));
        settings.Controls.Add(CreateLabel($"L_{prefix}_Plus", "+", 67, 36, 19));
        settings.Controls.Add(CreateTextBox($"TB_{prefix}_Advances", "5000", 88, 36, 118));
        settings.Controls.Add(CreateCheckBox($"CB_{prefix}_MenuClose", "Consider Menu Close?", 62, menuTop, 144));
        settings.Controls.Add(CreateCheckBox($"CB_{prefix}_MenuClose_Direction", "Holding Direction?", 0, menuTop + 22, 125, enabled: false));
        var nonPlayerCharacterLabel = CreateLabel($"L_{prefix}_NPCs", "NPCs:", 126, menuTop + 22, 45);
        nonPlayerCharacterLabel.Enabled = false;
        settings.Controls.Add(nonPlayerCharacterLabel);
        settings.Controls.Add(CreateTextBox($"TB_{prefix}_NPCs", defaultNonPlayerCharacters.ToString(), 171, menuTop + 21, 35, enabled: false));
        settings.Controls.Add(CreateButton($"B_{prefix}_Search", "Search!", 6, menuTop + 45, 200));
        form.Controls.Add(settings);
        form.Controls.Add(CreateGrid("DGV_Results", columns, new Rectangle(218, 11, formWidth - 230, formHeight - 23)));
        return form;
    }

    private static Form CreateForm(string name, string title, int width, int height, bool fixedBorder = false)
    {
        return new DeferredAutoScaleForm
        {
            Name = name,
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(width, height),
            AutoScroll = true,
            FormBorderStyle = fixedBorder ? FormBorderStyle.FixedSingle : FormBorderStyle.Sizable,
            MaximizeBox = !fixedBorder,
            Font = new Font("Segoe UI", 9F),
        };
    }

    private sealed class DeferredAutoScaleForm : Form
    {
        private bool scaleInitialized;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (scaleInitialized)
            {
                return;
            }

            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            scaleInitialized = true;
        }
    }

    private static DataGridView CreateGrid(string name, IReadOnlyList<string> columns, Rectangle bounds)
    {
        var grid = new DataGridView
        {
            Name = name,
            Location = bounds.Location,
            Size = bounds.Size,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
        };
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.WhiteSmoke;
        foreach (var column in columns)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = $"Column_{column.Replace(" ", string.Empty)}",
                HeaderText = column,
            });
        }
        return grid;
    }

    private CancellationToken GetWindowCancellationToken(Form form)
    {
        if (windowCancellations.TryGetValue(form, out var existing))
        {
            return existing.Token;
        }

        var source = new CancellationTokenSource();
        windowCancellations.Add(form, source);
        void CancelOperations(object? _, EventArgs __)
        {
            if (!windowCancellations.Remove(form, out var current))
            {
                return;
            }

            current.Cancel();
            current.Dispose();
        }

        form.FormClosed += CancelOperations;
        form.Disposed += CancelOperations;
        return source.Token;
    }

    private static bool CanUpdate(Form form, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested && !form.IsDisposed && !form.Disposing;

    private static T FindRequired<T>(Control root, string name)
        where T : Control
    {
        var matches = root.Controls.Find(name, true);
        return matches.Length > 0 && matches[0] is T control
            ? control
            : throw new InvalidOperationException($"Control '{name}' was not found as {typeof(T).Name}.");
    }

    private static void ReplaceItems<T>(ComboBox comboBox, IReadOnlyList<T> items)
    {
        comboBox.Items.Clear();
        comboBox.Items.AddRange(items.Cast<object>().ToArray());
        comboBox.SelectedIndex = comboBox.Items.Count > 0 ? 0 : -1;
    }

    private static Label CreateLabel(string name, string text, int left, int top, int width) => new()
    {
        Name = name,
        Text = text,
        Location = new Point(left, top),
        Size = new Size(width, 22),
        TextAlign = ContentAlignment.MiddleLeft,
    };

    private static TextBox CreateTextBox(
        string name,
        string text,
        int left,
        int top,
        int width,
        bool enabled = true,
        bool readOnly = false) => new()
    {
        Name = name,
        Text = text,
        Location = new Point(left, top),
        Size = new Size(width, 23),
        Enabled = enabled,
        ReadOnly = readOnly,
        Font = new Font("Consolas", 9F),
    };

    private static Button CreateButton(
        string name,
        string text,
        int left,
        int top,
        int width,
        bool enabled = true) => new()
    {
        Name = name,
        Text = text,
        Location = new Point(left, top),
        Size = new Size(width, 25),
        Enabled = enabled,
        UseVisualStyleBackColor = true,
    };

    private static CheckBox CreateCheckBox(
        string name,
        string text,
        int left,
        int top,
        int width,
        bool enabled = true) => new()
    {
        Name = name,
        Text = text,
        Location = new Point(left, top),
        Size = new Size(width, 24),
        Enabled = enabled,
        UseVisualStyleBackColor = true,
    };

    private static ComboBox CreateComboBox(
        string name,
        IEnumerable<string> items,
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
            DropDownStyle = ComboBoxStyle.DropDown,
        };
        comboBox.Items.AddRange(items.Cast<object>().ToArray());
        comboBox.SelectedIndex = selectedIndex >= 0 && selectedIndex < comboBox.Items.Count ? selectedIndex : -1;
        comboBox.Leave += (_, _) =>
        {
            var previousIndex = comboBox.SelectedIndex;
            var match = comboBox.Items.Cast<object>()
                .Select((item, index) => new { Text = item.ToString(), Index = index })
                .FirstOrDefault(item => string.Equals(
                    item.Text,
                    comboBox.Text,
                    StringComparison.CurrentCultureIgnoreCase));
            comboBox.SelectedIndex = match?.Index ?? Math.Max(previousIndex, 0);
        };
        return comboBox;
    }

    private static NumericUpDown CreateNumeric(
        string name,
        int left,
        int top,
        int width,
        decimal value,
        decimal maximum) => new()
    {
        Name = name,
        Location = new Point(left, top),
        Size = new Size(width, 23),
        Minimum = 0,
        Maximum = maximum,
        Value = value,
    };

    private static string SplitPascalCase(string value) =>
        string.Concat(value.Select((character, index) =>
            index > 0 && char.IsUpper(character) ? $" {character}" : character.ToString()));

    private sealed class LotoIdEditorState
    {
        public List<string> Ids { get; } = [];
        public Form? Dialog { get; set; }
    }
}
