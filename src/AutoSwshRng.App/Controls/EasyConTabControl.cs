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
        var menu = new MenuStrip
        {
            Name = "easyConOriginalMenu",
            Dock = DockStyle.Fill,
            Font = new Font("微软雅黑", 9F),
            ImageScalingSize = new Size(20, 20),
        };

        menu.Items.Add(CreateMenuItem("fileMenu", "文件"));
        menu.Items.Add(CreateMenuItem("editMenu", "编辑"));
        var scriptMenu = CreateMenuItem("scriptMenu", "脚本");
        scriptMenu.Visible = false;
        menu.Items.Add(scriptMenu);
        menu.Items.Add(CreateMenuItem("captureMenu", "搜图"));
        menu.Items.Add(CreateMenuItem("helpMenu", "帮助"));

        return menu;
    }

    private static ToolStripMenuItem CreateMenuItem(string name, string text)
    {
        return new ToolStripMenuItem
        {
            Name = name,
            Text = text,
        };
    }

    private static Control CreateMainArea()
    {
        var mainSplit = new SplitContainer
        {
            Name = "mainSplit",
            Width = 916,
            Height = 681,
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(230, 229, 224),
            SplitterWidth = 6,
            Panel2MinSize = 240,
        };
        mainSplit.Panel1.Controls.Add(CreateContentPanel());
        mainSplit.Panel1.Controls.Add(CreatePageSidebar());
        mainSplit.Panel2.Controls.Add(CreateRightPanel());
        return mainSplit;
    }

    private static Control CreatePageSidebar()
    {
        var sideBar = new Panel
        {
            Name = "sideBar",
            Dock = DockStyle.Left,
            Width = 40,
            BackColor = Color.FromArgb(230, 229, 224),
        };
        sideBar.Controls.Add(CreatePageButton("btnPageLog", "📄", Color.FromArgb(235, 234, 229), 10));
        sideBar.Controls.Add(CreatePageButton("btnPageEditor", "📝", Color.FromArgb(230, 229, 224), 50));
        sideBar.Controls.Add(CreatePageButton("btnPageBurn", "🔥", Color.FromArgb(230, 229, 224), 90));
        sideBar.Controls.Add(CreatePageButton("btnPageSettings", "⚙", Color.FromArgb(230, 229, 224), 130));
        return sideBar;
    }

    private static Button CreatePageButton(string name, string text, Color backColor, int top)
    {
        var button = new Button
        {
            Name = name,
            Text = text,
            Width = 36,
            Height = 36,
            Left = 2,
            Top = top,
            BackColor = backColor,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Emoji", 14F),
            ForeColor = Color.FromArgb(38, 37, 30),
            UseVisualStyleBackColor = false,
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private static Control CreateContentPanel()
    {
        var contentPanel = new Panel
        {
            Name = "contentPanel",
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(242, 241, 237),
        };

        contentPanel.Controls.Add(CreateEditorHost());
        contentPanel.Controls.Add(CreateLogPanel());
        contentPanel.Controls.Add(CreateBurnPanel());
        contentPanel.Controls.Add(CreateSettingsPanel());
        contentPanel.Controls.Add(new Label
        {
            Name = "scriptTitleLabel",
            Text = "未命名脚本",
            Dock = DockStyle.Top,
            BackColor = Color.FromArgb(230, 229, 224),
            Font = new Font("微软雅黑", 9F),
            ForeColor = Color.FromArgb(38, 37, 30),
            Height = 24,
            Padding = new Padding(4, 0, 0, 0),
            TextAlign = ContentAlignment.MiddleLeft,
            Visible = false,
        });
        return contentPanel;
    }

    private static Control CreateEditorHost()
    {
        var editorHost = new Panel
        {
            Name = "editorHost",
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9F),
            Visible = false,
        };
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
        editorHost.Controls.Add(editor);
        return editorHost;
    }

    private static Control CreateLogPanel()
    {
        var logPanel = new Panel
        {
            Name = "logPanel",
            Dock = DockStyle.Fill,
            BackColor = SystemColors.Control,
            ForeColor = Color.White,
        };

        var log = new TextBox
        {
            Name = "logTxtBox",
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = Color.FromArgb(64, 64, 64),
            ForeColor = Color.White,
            Font = new Font(FontFamily.GenericMonospace, 9),
            Location = new Point(6, 10),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Size = new Size(590, 644),
            Text = "[EasyCon] 等待设备连接..." + Environment.NewLine +
                "[EasyCon] 脚本编辑器已就绪",
            WordWrap = false,
        };

        var clearLog = new Button
        {
            Name = "clsLogBtn",
            AccessibleName = "清除日志输出",
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            BackColor = Color.Transparent,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(564, 5),
            Size = new Size(30, 30),
            UseVisualStyleBackColor = false,
        };
        clearLog.FlatAppearance.BorderSize = 0;

        logPanel.Controls.Add(clearLog);
        logPanel.Controls.Add(log);
        return logPanel;
    }

    private static Control CreateBurnPanel()
    {
        return new Panel
        {
            Name = "burnPanel",
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(242, 241, 237),
            Visible = false,
        };
    }

    private static Control CreateSettingsPanel()
    {
        return new Panel
        {
            Name = "settingsPanel",
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(242, 241, 237),
            Visible = false,
        };
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
        var group = CreateOriginalGroupBox("grpScriptRun", "脚本运行", 265, 162);
        group.Controls.Add(CreateOriginalButton("runStopBtn", "运行脚本", Color.FromArgb(31, 138, 101), Color.White, 8, 22, 206, 55));
        group.Controls.Add(CreateOriginalButton("formatBtn", "格式化", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 8, 85, 206, 30));
        group.Controls.Add(new Label
        {
            Name = "timerLabel",
            Text = "00:00:00",
            Font = new Font("Consolas", 12F),
            ForeColor = Color.FromArgb(38, 37, 30),
            Location = new Point(8, 123),
            Size = new Size(206, 30),
            TextAlign = ContentAlignment.MiddleCenter,
        });
        return group;
    }

    private static Control CreateSerialPanel()
    {
        var group = CreateOriginalGroupBox("grpDevice", "设备连接", 265, 130);
        group.Controls.Add(new Panel
        {
            Name = "easyConSerialPanel",
            Width = 1,
            Height = 1,
            Visible = false,
        });
        group.Controls.Add(new ComboBox
        {
            Name = "comboComPort",
            Location = new Point(8, 22),
            Size = new Size(206, 28),
        });
        group.Controls.Add(CreateOriginalButton("btnAutoConnect", "自动连接", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 8, 54, 206, 30));
        group.Controls.Add(CreateOriginalButton("btnManualConnect", "手动连接", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 8, 90, 206, 30));
        return group;
    }

    private static Control CreateCapturePanel()
    {
        var group = CreateOriginalGroupBox("grpVideoSource", "视频源", 265, 150);
        group.Controls.Add(new Panel
        {
            Name = "easyConCapturePanel",
            Width = 1,
            Height = 1,
            Visible = false,
        });
        group.Controls.Add(new ComboBox
        {
            Name = "comboVideoSource",
            Location = new Point(8, 22),
            Size = new Size(206, 28),
        });
        group.Controls.Add(CreateOriginalButton("btnCaptureToggle", "连接视频源", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 8, 54, 206, 30));
        group.Controls.Add(CreateOriginalButton("btnOpenCaptureConsole", "搜图控制台", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 8, 90, 206, 30));
        return group;
    }

    private static Control CreateRecordPanel()
    {
        var group = CreateOriginalGroupBox("grpRecord", "录制", 265, 90);
        group.Controls.Add(new Panel
        {
            Name = "easyConRecordPanel",
            Width = 1,
            Height = 1,
            Visible = false,
        });
        group.Controls.Add(CreateOriginalButton("btnRecord", "录制脚本", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 8, 22, 206, 28));
        var pauseButton = CreateOriginalButton("btnRecordPause", "暂停录制", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 8, 54, 206, 28);
        pauseButton.Enabled = false;
        group.Controls.Add(pauseButton);
        return group;
    }

    private static Control CreateControllerPanel()
    {
        var group = CreateOriginalGroupBox("grpController", "手柄", 265, 90);
        group.Controls.Add(new Panel
        {
            Name = "easyConControllerPanel",
            Width = 1,
            Height = 1,
            Visible = false,
        });
        group.Controls.Add(CreateOriginalButton("btnShowController", "虚拟手柄", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 8, 22, 206, 28));
        group.Controls.Add(CreateOriginalButton("btnKeyMapping", "按键映射", Color.FromArgb(235, 234, 229), Color.FromArgb(38, 37, 30), 8, 54, 206, 28));
        return group;
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
        var status = new StatusStrip
        {
            Name = "easyConStatusStrip",
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(230, 229, 224),
            ImageScalingSize = new Size(20, 20),
        };
        status.Items.Add(new ToolStripStatusLabel
        {
            Name = "toolStripStatusLabel1",
            AutoSize = false,
            ForeColor = Color.FromArgb(38, 37, 30),
            Size = new Size(300, 20),
        });
        status.Items.Add(CreateStatusLabel("toolStripStatusLabel2", " | "));
        status.Items.Add(CreateStatusLabel("labelSerialStatus", "单片机未连接"));
        status.Items.Add(CreateStatusLabel("toolStripStatusLabel3", " | "));
        status.Items.Add(CreateStatusLabel("labelCaptureStatus", "采集卡未连接"));
        return status;
    }

    private static ToolStripStatusLabel CreateStatusLabel(string name, string text)
    {
        return new ToolStripStatusLabel
        {
            Name = name,
            Text = text,
            ForeColor = Color.FromArgb(140, 139, 132),
        };
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

    private static GroupBox CreateOriginalGroupBox(string name, string title, int width, int height)
    {
        return new GroupBox
        {
            Name = name,
            Text = title,
            Width = width,
            Height = height,
            ForeColor = Color.FromArgb(38, 37, 30),
            Margin = new Padding(10, 3, 10, 0),
        };
    }

    private static Button CreateOriginalButton(
        string name,
        string text,
        Color backColor,
        Color foreColor,
        int left,
        int top,
        int width,
        int height)
    {
        var button = new Button
        {
            Name = name,
            Text = text,
            BackColor = backColor,
            ForeColor = foreColor,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("微软雅黑", 9F),
            Location = new Point(left, top),
            Size = new Size(width, height),
            UseVisualStyleBackColor = false,
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
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
