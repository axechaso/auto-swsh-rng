namespace AutoSwshRng.App.Controls;

public sealed class OwoowTabControl : UserControl
{
    private static readonly string[] ToolMenuItems =
    [
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
    ];

    public OwoowTabControl()
    {
        Dock = DockStyle.Fill;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(CreateToolMenu(), 0, 0);
        root.Controls.Add(CreateWorkArea(), 1, 0);

        Controls.Add(root);
    }

    private static Control CreateToolMenu()
    {
        var menu = new FlowLayoutPanel
        {
            Name = "owoowToolMenu",
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.FromArgb(243, 243, 243),
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(8),
            WrapContents = false,
        };

        menu.Controls.Add(new Label
        {
            Text = "owoow 工具",
            AutoSize = false,
            Width = 160,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft,
        });

        foreach (var item in ToolMenuItems)
        {
            menu.Controls.Add(new Button
            {
                Text = item,
                Width = 160,
                Height = 32,
                FlatStyle = FlatStyle.System,
                TextAlign = ContentAlignment.MiddleLeft,
            });
        }

        return menu;
    }

    private static Control CreateWorkArea()
    {
        var workArea = new TableLayoutPanel
        {
            Name = "owoowOriginalWorkArea",
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(10),
        };
        workArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 390));
        workArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        workArea.Controls.Add(CreateSearchLayout(), 0, 0);
        workArea.Controls.Add(CreateResultsGrid(), 0, 1);
        return workArea;
    }

    private static Control CreateSearchLayout()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 390));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        layout.Controls.Add(CreateConnectionPanel(), 0, 0);
        layout.Controls.Add(CreateEncounterPanel(), 1, 0);
        layout.Controls.Add(CreateRecommendationPanel(), 2, 0);
        layout.Controls.Add(CreateFilterPanel(), 3, 0);
        layout.Controls.Add(CreateToolsPanel(), 4, 0);

        return layout;
    }

    private static Control CreateConnectionPanel()
    {
        var panel = CreateTwoColumnPanel();
        AddTextBoxRow(panel, "Seed[0]:", "0");
        AddTextBoxRow(panel, "Seed[1]:", "0");
        AddSpacer(panel, 12);
        AddTextBoxRow(panel, "Switch IP:", "192.168.0.0");
        AddButtonRow(panel, "连接", "断开");
        AddValueRow(panel, "状态:", "未连接。");
        AddTextBoxRow(panel, "推进数:", string.Empty);
        AddTextBoxRow(panel, "Seed[0]:", string.Empty);
        AddTextBoxRow(panel, "Seed[1]:", string.Empty);
        AddFullWidthButton(panel, "更新种子");
        AddCheckAndValueRow(panel, "闪耀护符?", "TID:", "01337");
        AddCheckAndValueRow(panel, "证章护符?", "SID:", "01390");
        AddComboRow(panel, "游戏:", "剑");
        return panel;
    }

    private static Control CreateEncounterPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var tabs = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
        };
        foreach (var tab in new[] { "定点", "符号", "隐藏", "垂钓" })
        {
            tabs.Controls.Add(new Button
            {
                Text = tab,
                Width = 56,
                Height = 30,
            });
        }

        var encounter = new GroupBox
        {
            Text = "遭遇设置 - 定点",
            Dock = DockStyle.Fill,
        };
        var encounterRows = CreateTwoColumnPanel();
        encounterRows.Padding = new Padding(8, 12, 8, 8);
        AddComboRow(encounterRows, "区域:", "牙牙湖之眼");
        AddComboRow(encounterRows, "天气:", "晴朗");
        AddComboRow(encounterRows, "目标:", "古月鸟");
        encounter.Controls.Add(encounterRows);

        var advanced = CreateTwoColumnPanel();
        AddComboRow(advanced, "同行首位特性:", "无");
        AddCheckAndValueRow(advanced, "考虑关菜单?", "NPC数:", "3");
        AddCheckAndValueRow(advanced, "保持方向?", string.Empty, string.Empty);
        AddTextBoxRow(advanced, "初始推进:", "0");
        AddTextBoxRow(advanced, "+", "5000");

        panel.Controls.Add(tabs, 0, 0);
        panel.Controls.Add(encounter, 0, 1);
        panel.Controls.Add(advanced, 0, 2);
        panel.SetRowSpan(advanced, 3);
        panel.Controls.Add(CreateButton("搜索!"), 0, 5);
        return panel;
    }

    private static Control CreateRecommendationPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 220));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var recommendation = new GroupBox
        {
            Text = "宝可梦图鉴“现在推荐”",
            Dock = DockStyle.Fill,
        };
        var recommendationRows = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(8, 14, 8, 8),
            WrapContents = false,
        };
        for (var i = 0; i < 4; i++)
        {
            recommendationRows.Controls.Add(CreateComboBox("(无)", 245));
        }
        recommendationRows.Controls.Add(CreateButton("刷新", 245));
        recommendation.Controls.Add(recommendationRows);

        var advanced = new GroupBox
        {
            Text = "高级设置",
            Dock = DockStyle.Fill,
        };
        var advancedRows = CreateTwoColumnPanel();
        advancedRows.Padding = new Padding(8, 14, 8, 8);
        AddCheckAndValueRow(advancedRows, "飞翔?", "区域读取:", "0");
        AddCheckAndValueRow(advancedRows, "下雨/雷雨?", "雨滴计数:", "0");
        AddFullWidthButton(advancedRows, "计算雨滴计数");
        advanced.Controls.Add(advancedRows);

        panel.Controls.Add(recommendation, 0, 0);
        panel.Controls.Add(advanced, 0, 1);
        return panel;
    }

    private static Control CreateFilterPanel()
    {
        var panel = CreateTwoColumnPanel();
        foreach (var label in new[] { "HP:", "攻击:", "防御:", "特攻:", "特防:", "速度:" })
        {
            AddTextBoxRow(panel, label, "0  ~  31     0   31");
        }
        AddComboRow(panel, "异色:", "忽略");
        AddComboRow(panel, "证章:", "忽略");
        AddComboRow(panel, "气场:", "忽略");
        AddComboRow(panel, "身高:", "忽略");
        AddCheckAndValueRow(panel, "稀有 EC?", "启用筛选?", "是");
        AddCheckAndValueRow(panel, "播放提示音?", "聚焦窗口?", "否");
        return panel;
    }

    private static Control CreateToolsPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 170));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 215));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var cfw = new GroupBox
        {
            Text = "CFW 工具",
            Dock = DockStyle.Fill,
        };
        var cfwRows = CreateTwoColumnPanel();
        cfwRows.Padding = new Padding(8, 14, 8, 8);
        AddTextBoxRow(cfwRows, "跳过:", "100");
        AddButtonRow(cfwRows, "Days+", "Days-");
        AddButtonRow(cfwRows, "Adv.", "NTP");
        AddButtonRow(cfwRows, "Turbo", "连发控制");
        AddButtonRow(cfwRows, "重置到种子", "设置");
        cfw.Controls.Add(cfwRows);

        var retail = new GroupBox
        {
            Text = "实机工具",
            Dock = DockStyle.Fill,
        };
        var retailRows = CreateTwoColumnPanel();
        retailRows.Padding = new Padding(8, 14, 8, 8);
        AddFullWidthButton(retailRows, "实机种子搜索");
        AddTextBoxRow(retailRows, "初始:", "0");
        AddTextBoxRow(retailRows, "+", "99999");
        AddTextBoxRow(retailRows, "动画:", string.Empty);
        AddTextBoxRow(retailRows, "推进:", string.Empty);
        AddFullWidthButton(retailRows, "更新种子");
        retail.Controls.Add(retailRows);

        panel.Controls.Add(cfw, 0, 0);
        panel.Controls.Add(retail, 0, 1);
        return panel;
    }

    private static Control CreateResultsGrid()
    {
        var grid = new DataGridView
        {
            Name = "owoowResultsGrid",
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            RowHeadersVisible = false,
        };

        foreach (var column in new[]
        {
            "推进数", "跳跃", "步数", "动画", "宝可梦", "异色", "气场", "等级", "特性", "性格", "性别",
            "HP", "攻击", "防御", "特攻", "特防", "速度", "证章", "EC", "PID", "身高",
        })
        {
            grid.Columns.Add(column, column);
        }

        return grid;
    }

    private static TableLayoutPanel CreateTwoColumnPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoScroll = true,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return panel;
    }

    private static void AddTextBoxRow(TableLayoutPanel panel, string label, string value)
    {
        AddRow(panel, CreateLabel(label), new TextBox { Text = value, Dock = DockStyle.Fill });
    }

    private static void AddComboRow(TableLayoutPanel panel, string label, string value)
    {
        AddRow(panel, CreateLabel(label), CreateComboBox(value));
    }

    private static void AddValueRow(TableLayoutPanel panel, string label, string value)
    {
        AddRow(panel, CreateLabel(label), CreateLabel(value));
    }

    private static void AddCheckAndValueRow(TableLayoutPanel panel, string label, string valueLabel, string value)
    {
        var right = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
        };
        right.Controls.Add(new CheckBox { Width = 24, Height = 24 });
        if (!string.IsNullOrEmpty(valueLabel))
        {
            right.Controls.Add(CreateLabel(valueLabel, 90));
        }
        if (!string.IsNullOrEmpty(value))
        {
            right.Controls.Add(new TextBox { Text = value, Width = 70 });
        }
        AddRow(panel, CreateLabel(label), right);
    }

    private static void AddButtonRow(TableLayoutPanel panel, string first, string second)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        row.Controls.Add(CreateButton(first), 0, 0);
        row.Controls.Add(CreateButton(second), 1, 0);
        AddFullWidthControl(panel, row);
    }

    private static void AddFullWidthButton(TableLayoutPanel panel, string text)
    {
        AddFullWidthControl(panel, CreateButton(text));
    }

    private static void AddSpacer(TableLayoutPanel panel, int height)
    {
        var spacer = new Panel { Height = height, Dock = DockStyle.Top };
        AddFullWidthControl(panel, spacer);
    }

    private static void AddRow(TableLayoutPanel panel, Control left, Control right)
    {
        var row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        panel.Controls.Add(left, 0, row);
        panel.Controls.Add(right, 1, row);
    }

    private static void AddFullWidthControl(TableLayoutPanel panel, Control control)
    {
        var row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, Math.Max(32, control.Height)));
        panel.Controls.Add(control, 0, row);
        panel.SetColumnSpan(control, 2);
    }

    private static Label CreateLabel(string text, int width = 0)
    {
        return new Label
        {
            Text = text,
            AutoSize = false,
            Width = width > 0 ? width : 105,
            Dock = width > 0 ? DockStyle.None : DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        };
    }

    private static Button CreateButton(string text, int width = 0)
    {
        return new Button
        {
            Text = text,
            Width = width > 0 ? width : 90,
            Dock = width > 0 ? DockStyle.None : DockStyle.Fill,
            FlatStyle = FlatStyle.System,
        };
    }

    private static ComboBox CreateComboBox(string text, int width = 0)
    {
        var comboBox = new ComboBox
        {
            Text = text,
            Width = width > 0 ? width : 120,
            Dock = width > 0 ? DockStyle.None : DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        comboBox.Items.Add(text);
        comboBox.SelectedIndex = 0;
        return comboBox;
    }
}
