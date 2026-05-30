namespace AutoSwshRng.App.Controls;

public sealed class EasyConTabControl : UserControl
{
    private static readonly string[] MenuItems =
    [
        "文件",
        "编辑",
        "脚本",
        "搜图",
        "设置",
        "蓝牙",
        "ESP32",
        "画图",
        "帮助",
    ];

    public EasyConTabControl()
    {
        Dock = DockStyle.Fill;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.Controls.Add(CreateOriginalMenu(), 0, 0);
        root.Controls.Add(CreateMainArea(), 0, 1);
        root.Controls.Add(CreateStatusStrip(), 0, 2);

        Controls.Add(root);
    }

    private static Control CreateOriginalMenu()
    {
        var menu = new FlowLayoutPanel
        {
            Name = "easyConOriginalMenu",
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(244, 244, 244),
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(8, 3, 0, 0),
            WrapContents = false,
        };

        foreach (var item in MenuItems)
        {
            menu.Controls.Add(new Label
            {
                Text = item,
                AutoSize = false,
                Width = item == "ESP32" ? 55 : 48,
                Height = 22,
                TextAlign = ContentAlignment.MiddleLeft,
            });
        }

        return menu;
    }

    private static Control CreateMainArea()
    {
        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
        };
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        main.Controls.Add(CreateEditorAndLog(), 0, 0);
        main.Controls.Add(CreateRightPanel(), 1, 0);
        return main;
    }

    private static Control CreateEditorAndLog()
    {
        var area = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        area.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        area.RowStyles.Add(new RowStyle(SizeType.Absolute, 160));

        var editor = new TextBox
        {
            Name = "easyConScriptEditor",
            Dock = DockStyle.Fill,
            AcceptsReturn = true,
            AcceptsTab = true,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.FromArgb(212, 212, 212),
            Font = new Font(FontFamily.GenericMonospace, 10),
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            Text = "PRINT \"hello\"" + Environment.NewLine +
                "WAIT 1000" + Environment.NewLine +
                "PRESS A 30" + Environment.NewLine +
                "WAIT 200" + Environment.NewLine +
                "PRESS B 30",
            WordWrap = false,
        };

        var log = new TextBox
        {
            Name = "easyConLogBox",
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(17, 17, 17),
            ForeColor = Color.FromArgb(216, 255, 216),
            Font = new Font(FontFamily.GenericMonospace, 9),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Text = "[EasyCon] 等待设备连接..." + Environment.NewLine +
                "[EasyCon] 脚本编辑器已就绪",
            WordWrap = false,
        };

        area.Controls.Add(editor, 0, 0);
        area.Controls.Add(log, 0, 1);
        return area;
    }

    private static Control CreateRightPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.FromArgb(245, 245, 245),
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(8),
            WrapContents = false,
        };

        panel.Controls.Add(CreateRunPanel());
        panel.Controls.Add(CreateSerialPanel());
        panel.Controls.Add(CreateCapturePanel());
        panel.Controls.Add(CreateRecordPanel());
        panel.Controls.Add(CreateControllerPanel());
        panel.Controls.Add(CreateFirmwarePanel());
        return panel;
    }

    private static Control CreateRunPanel()
    {
        var panel = CreateGroupPanel("脚本", 265, 92);
        AddButtonGrid(panel, "运行", "停止");
        AddButtonGrid(panel, "格式化", "脚本帮助");
        panel.Controls.Add(CreateLabel("计时: 00:00:00", 240));
        return panel;
    }

    private static Control CreateSerialPanel()
    {
        var panel = CreateGroupPanel("串口", 265, 112);
        panel.Name = "easyConSerialPanel";
        AddTextRow(panel, "COM:", "COM3");
        AddButtonGrid(panel, "搜索", "连接");
        return panel;
    }

    private static Control CreateCapturePanel()
    {
        var panel = CreateGroupPanel("采集卡", 265, 112);
        panel.Name = "easyConCapturePanel";
        AddTextRow(panel, "来源:", "未选择");
        AddButtonGrid(panel, "启动", "控制台");
        return panel;
    }

    private static Control CreateRecordPanel()
    {
        var panel = CreateGroupPanel("录制", 265, 78);
        panel.Name = "easyConRecordPanel";
        AddButtonGrid(panel, "录制", "暂停");
        return panel;
    }

    private static Control CreateControllerPanel()
    {
        var panel = CreateGroupPanel("手柄", 265, 78);
        panel.Name = "easyConControllerPanel";
        AddButtonGrid(panel, "显示手柄", "按键映射");
        return panel;
    }

    private static Control CreateFirmwarePanel()
    {
        var panel = CreateGroupPanel("固件", 265, 112);
        panel.Name = "easyConFirmwarePanel";
        AddTextRow(panel, "开发板:", "Leonardo");
        AddButtonGrid(panel, "生成固件", "烧录");
        return panel;
    }

    private static Control CreateStatusStrip()
    {
        var status = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(244, 244, 244),
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(8, 3, 0, 0),
            WrapContents = false,
        };
        status.Controls.Add(CreateLabel("串口状态: 未连接", 160));
        status.Controls.Add(CreateLabel("采集状态: 未开启", 160));
        status.Controls.Add(CreateLabel("日志: 自动保存关闭", 180));
        return status;
    }

    private static FlowLayoutPanel CreateGroupPanel(string title, int width, int height)
    {
        var panel = new FlowLayoutPanel
        {
            Width = width,
            Height = height,
            BorderStyle = BorderStyle.FixedSingle,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(8),
            WrapContents = false,
        };
        panel.Controls.Add(new Label
        {
            Text = title,
            AutoSize = false,
            Width = width - 20,
            Height = 24,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
        });
        return panel;
    }

    private static void AddTextRow(FlowLayoutPanel parent, string label, string value)
    {
        var row = new TableLayoutPanel
        {
            Width = parent.Width - 20,
            Height = 30,
            ColumnCount = 2,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.Controls.Add(CreateLabel(label, 70), 0, 0);
        row.Controls.Add(new TextBox { Text = value, Dock = DockStyle.Fill }, 1, 0);
        parent.Controls.Add(row);
    }

    private static void AddButtonGrid(FlowLayoutPanel parent, string first, string second)
    {
        var row = new TableLayoutPanel
        {
            Width = parent.Width - 20,
            Height = 32,
            ColumnCount = 2,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        row.Controls.Add(CreateButton(first), 0, 0);
        row.Controls.Add(CreateButton(second), 1, 0);
        parent.Controls.Add(row);
    }

    private static Label CreateLabel(string text, int width)
    {
        return new Label
        {
            Text = text,
            AutoSize = false,
            Width = width,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft,
        };
    }

    private static Button CreateButton(string text)
    {
        return new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.System,
        };
    }
}
