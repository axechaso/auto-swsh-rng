namespace AutoSwshRng.App.Controls;

public sealed class OwoowTabControl : UserControl
{
    private const int CanvasWidth = 1278;
    private readonly OwoowUiServices services;

    public OwoowTabControl(OwoowUiServices? services = null)
    {
        this.services = services ?? OwoowUiServices.CreateDefault();

        Dock = DockStyle.Fill;
        Font = new Font("Segoe UI", 9F);

        var scrollHost = new Panel
        {
            Name = "owoowScrollHost",
            Dock = DockStyle.Fill,
            AutoScroll = true,
            AutoScrollMinSize = new Size(CanvasWidth, 680),
            BackColor = SystemColors.Control,
        };

        var canvas = new Panel
        {
            Name = "owoowMainCanvas",
            Location = Point.Empty,
            Size = new Size(CanvasWidth, 680),
            MinimumSize = new Size(CanvasWidth, 680),
        };
        canvas.Controls.Add(CreateResultsGrid());
        canvas.Controls.Add(CreateSeedControlsContainer());
        canvas.Controls.Add(CreateSubWindowsMenu());

        scrollHost.Controls.Add(canvas);
        Controls.Add(scrollHost);
    }

    internal OwoowUiServices Services => services;

    private static MenuStrip CreateSubWindowsMenu()
    {
        var menu = new MenuStrip
        {
            Name = "MS_SubWindows",
            Dock = DockStyle.Top,
            BackColor = SystemColors.ButtonFace,
            AutoSize = false,
            Height = 24,
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
            menu.Items.Add(new ToolStripMenuItem(text) { Name = name });
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
        group.Controls.Add(new PictureBox
        {
            Name = "PB_PokemonSprite",
            Location = new Point(8, 16),
            Size = new Size(80, 80),
            SizeMode = PictureBoxSizeMode.Zoom,
        });
        group.Controls.Add(new PictureBox
        {
            Name = "PB_MarkSprite",
            Location = new Point(102, 16),
            Size = new Size(80, 80),
            SizeMode = PictureBoxSizeMode.Zoom,
        });
        group.Controls.Add(new TextBox
        {
            Name = "TB_Wild",
            Location = new Point(8, 100),
            Size = new Size(178, 150),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
        });
        group.Controls.Add(CreateButton("B_ReadEncounter", "Read Encounter", 8, 256, 178, enabled: false));
        group.Controls.Add(CreateButton("B_CopyToFilter", "Copy to Filter", 8, 286, 178, enabled: false));
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
        group.Controls.Add(CreateTextBox("TB_RetailRange", "99999", 74, 77, 72));
        group.Controls.Add(CreateButton("B_GenerateRetailPattern", "Generate", 150, 76, 56));
        group.Controls.Add(CreateLabel("L_Animations", "Animations:", 6, 106, 64));
        group.Controls.Add(CreateTextBox("TB_Animations", string.Empty, 74, 103, 132));
        group.Controls.Add(CreateLabel("L_RetailAdvances", "Advances:", 6, 132, 64));
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
        };

        foreach (var header in new[]
        {
            "Advances", "Jump", "Step", "Animation", "Species", "Shiny", "Brilliant", "Level",
            "Ability", "Nature", "Gender", "HP", "Atk", "Def", "SpA", "SpD", "Spe", "Mark",
            "EC", "PID", "Height", "Item", "Egg Move", "Seed0", "Seed1",
        })
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = $"Column_{header.Replace(" ", string.Empty)}",
                HeaderText = header,
                SortMode = DataGridViewColumnSortMode.Automatic,
            });
        }

        return grid;
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
