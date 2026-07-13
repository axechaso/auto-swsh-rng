using AutoSwshRng.Core;
using AutoSwshRng.App.Controls;

namespace AutoSwshRng.App;

public sealed class MainForm : Form
{
    private readonly Icon applicationIcon;
    private readonly EasyConTabControl easyConTab = new();
    private readonly OwoowTabControl owoowTab = new();
    private readonly List<RadioButton> moduleNavigationButtons = [];
    private bool dpiScaleInitialized;
    private bool visualResourcesDisposed;
    private readonly ToolStripStatusLabel currentModuleStatusLabel = new()
    {
        Name = "autoSwshCurrentModuleStatusLabel",
    };

    private readonly TabControl mainTabs = new()
    {
        Name = "autoSwshMainTabs",
        Dock = DockStyle.Fill,
        Appearance = TabAppearance.FlatButtons,
        ItemSize = new Size(0, 1),
        SizeMode = TabSizeMode.Fixed,
        Padding = Point.Empty,
        TabStop = false,
    };

    public MainForm()
    {
        Font = new Font("Microsoft YaHei UI", 9F);
        applicationIcon = AppVisualTheme.CreateApplicationIcon();
        Icon = applicationIcon;
        BackColor = AppVisualTheme.Workspace;
        MinimumSize = new Size(1040, 680);
        ClientSize = new Size(1310, 760);
        Text = $"{ProjectInfo.Name} - {ProjectInfo.Description}";
        StartPosition = FormStartPosition.CenterScreen;

        mainTabs.TabPages.Add(CreateControlTab("owoow", owoowTab, edgeToEdge: true));
        mainTabs.TabPages.Add(CreateControlTab("伊机控", easyConTab, edgeToEdge: true));
        mainTabs.TabPages.Add(CreateControlTab("自动化流程", new AutomationFlowTabControl()));
        mainTabs.SelectedIndexChanged += (_, _) =>
        {
            UpdateCurrentModuleStatus();
            UpdateModuleNavigation();
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = AppVisualTheme.Workspace,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        root.Controls.Add(CreateHeader(), 0, 0);
        root.Controls.Add(mainTabs, 0, 1);
        root.Controls.Add(CreateStatusStrip(), 0, 2);

        Controls.Add(root);
        FormClosing += MainFormClosing;
        UpdateCurrentModuleStatus();
        UpdateModuleNavigation();
    }

    public IReadOnlyList<string> TabTitles => mainTabs.TabPages.Cast<TabPage>().Select(page => page.Text).ToArray();

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (dpiScaleInitialized)
        {
            return;
        }

        var initialWorkingArea = Screen.FromHandle(Handle).WorkingArea;
        var owoowPage = (TabPage)owoowTab.Parent!;
        owoowPage.SuspendLayout();
        owoowPage.Controls.Remove(owoowTab);
        try
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            dpiScaleInitialized = true;
        }
        finally
        {
            owoowPage.Controls.Add(owoowTab);
            owoowPage.ResumeLayout(true);
        }
        FitInitialWindow(initialWorkingArea);
        PerformLayout();
        mainTabs.Parent?.PerformLayout();
        mainTabs.PerformLayout();
        foreach (TabPage page in mainTabs.TabPages)
        {
            page.PerformLayout();
        }
    }

    private void FitInitialWindow(Rectangle workingArea)
    {
        var bounds = CalculateInitialBounds(Size, workingArea);
        MinimumSize = new Size(
            Math.Min(MinimumSize.Width, bounds.Width),
            Math.Min(MinimumSize.Height, bounds.Height));
        SetBounds(bounds.Left, bounds.Top, bounds.Width, bounds.Height, BoundsSpecified.All);
    }

    private static Rectangle CalculateInitialBounds(Size desiredSize, Rectangle workingArea)
    {
        const int edgeInset = 12;
        var horizontalInset = Math.Min(edgeInset, Math.Max(0, (workingArea.Width - 1) / 2));
        var verticalInset = Math.Min(edgeInset, Math.Max(0, (workingArea.Height - 1) / 2));
        var available = Rectangle.FromLTRB(
            workingArea.Left + horizontalInset,
            workingArea.Top + verticalInset,
            workingArea.Right - horizontalInset,
            workingArea.Bottom - verticalInset);
        var width = Math.Max(1, Math.Min(desiredSize.Width, available.Width));
        var height = Math.Max(1, Math.Min(desiredSize.Height, available.Height));
        return new Rectangle(
            available.Left + Math.Max(0, (available.Width - width) / 2),
            available.Top + Math.Max(0, (available.Height - height) / 2),
            width,
            height);
    }

    private static TabPage CreateControlTab(string title, Control control, bool edgeToEdge = false)
    {
        var page = new TabPage(title)
        {
            Padding = edgeToEdge ? Padding.Empty : new Padding(16),
            BackColor = AppVisualTheme.Workspace,
            ForeColor = AppVisualTheme.Ink,
            UseVisualStyleBackColor = false,
        };
        page.Controls.Add(control);
        return page;
    }

    private Control CreateHeader()
    {
        var header = new TableLayoutPanel
        {
            Name = "autoSwshHeader",
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = AppVisualTheme.Shell,
            Margin = Padding.Empty,
            Padding = new Padding(16, 0, 12, 0),
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 244));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var brand = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = AppVisualTheme.Shell,
            Margin = Padding.Empty,
        };
        brand.Controls.Add(new BrandMarkControl
        {
            Location = new Point(0, 5),
        });
        brand.Controls.Add(new Label
        {
            Name = "autoSwshBrandTitle",
            Text = "AUTO · SWSH RNG",
            AutoSize = false,
            Location = new Point(44, 2),
            Size = new Size(190, 20),
            Font = AppVisualTheme.HeaderTitleFont,
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft,
        });
        brand.Controls.Add(new Label
        {
            Name = "autoSwshBrandSubtitle",
            Text = "剑盾乱数工作台",
            AutoSize = false,
            Location = new Point(44, 22),
            Size = new Size(190, 17),
            Font = AppVisualTheme.HeaderSubtitleFont,
            ForeColor = AppVisualTheme.ShellMuted,
            TextAlign = ContentAlignment.MiddleLeft,
        });

        var navigation = new FlowLayoutPanel
        {
            Name = "autoSwshModuleNavigation",
            Dock = DockStyle.Fill,
            BackColor = AppVisualTheme.Shell,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 5, 0, 0),
            Margin = Padding.Empty,
        };
        navigation.Controls.Add(CreateModuleNavigationButton("autoSwshNavOwoow", "owoow", 0));
        navigation.Controls.Add(CreateModuleNavigationButton("autoSwshNavEasyCon", "伊机控", 1));
        navigation.Controls.Add(CreateModuleNavigationButton("autoSwshNavAutomation", "自动化流程", 2));

        var readiness = new Label
        {
            Name = "autoSwshReadyStatus",
            Dock = DockStyle.Fill,
            Text = "●  LOCAL",
            Font = AppVisualTheme.HeaderSubtitleFont,
            ForeColor = Color.FromArgb(83, 220, 199),
            TextAlign = ContentAlignment.MiddleRight,
            Margin = Padding.Empty,
        };

        header.Controls.Add(brand, 0, 0);
        header.Controls.Add(navigation, 1, 0);
        header.Controls.Add(readiness, 2, 0);
        return header;
    }

    private RadioButton CreateModuleNavigationButton(string name, string text, int tabIndex)
    {
        var button = new RadioButton
        {
            Name = name,
            Text = text,
            Appearance = Appearance.Button,
            Size = new Size(118, 34),
            Margin = new Padding(0, 0, 6, 0),
            Padding = new Padding(8, 0, 8, 0),
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
            Font = AppVisualTheme.NavigationFont,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand,
            AccessibleRole = AccessibleRole.PageTab,
            AccessibleDescription = $"切换到{text}模块",
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.CheckedBackColor = AppVisualTheme.ShellRaised;
        button.FlatAppearance.MouseOverBackColor = AppVisualTheme.ShellRaised;
        button.FlatAppearance.MouseDownBackColor = AppVisualTheme.AccentHover;
        button.Click += (_, _) => SelectModule(tabIndex, focusNavigation: false);
        button.KeyDown += (_, e) => NavigateModulesFromKeyboard(tabIndex, e);
        moduleNavigationButtons.Add(button);
        return button;
    }

    private void NavigateModulesFromKeyboard(int currentIndex, KeyEventArgs e)
    {
        var targetIndex = e.KeyCode switch
        {
            Keys.Left or Keys.Up => (currentIndex - 1 + moduleNavigationButtons.Count) % moduleNavigationButtons.Count,
            Keys.Right or Keys.Down => (currentIndex + 1) % moduleNavigationButtons.Count,
            Keys.Home => 0,
            Keys.End => moduleNavigationButtons.Count - 1,
            _ => -1,
        };
        if (targetIndex < 0)
        {
            return;
        }

        SelectModule(targetIndex, focusNavigation: true);
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private void SelectModule(int tabIndex, bool focusNavigation)
    {
        if (tabIndex < 0 || tabIndex >= mainTabs.TabPages.Count)
        {
            return;
        }

        mainTabs.SelectedIndex = tabIndex;
        if (focusNavigation)
        {
            moduleNavigationButtons[tabIndex].Focus();
        }
    }

    private StatusStrip CreateStatusStrip()
    {
        var status = new StatusStrip
        {
            Name = "autoSwshStatusStrip",
            SizingGrip = false,
            Dock = DockStyle.Fill,
            BackColor = AppVisualTheme.Shell,
            ForeColor = AppVisualTheme.ShellMuted,
            Font = AppVisualTheme.HeaderSubtitleFont,
            Padding = new Padding(12, 0, 12, 0),
            Renderer = AppVisualTheme.CreateDarkToolStripRenderer(),
        };
        status.Items.Add(new ToolStripStatusLabel(ProjectInfo.Name)
        {
            Name = "autoSwshProjectStatusLabel",
            ForeColor = Color.FromArgb(83, 220, 199),
        });
        status.Items.Add(new ToolStripStatusLabel(ProjectInfo.Description)
        {
            Name = "autoSwshDescriptionStatusLabel",
            Spring = true,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = AppVisualTheme.ShellMuted,
        });
        currentModuleStatusLabel.ForeColor = Color.White;
        status.Items.Add(currentModuleStatusLabel);
        return status;
    }

    private void UpdateCurrentModuleStatus()
    {
        var selectedTitle = mainTabs.SelectedIndex >= 0
            ? mainTabs.TabPages[mainTabs.SelectedIndex].Text
            : mainTabs.TabPages.Count > 0
                ? mainTabs.TabPages[0].Text
                : "未选择";
        currentModuleStatusLabel.Text = $"当前：{selectedTitle}";
    }

    private void UpdateModuleNavigation()
    {
        var selectedIndex = mainTabs.SelectedIndex >= 0
            ? mainTabs.SelectedIndex
            : mainTabs.TabPages.Count > 0
                ? 0
                : -1;
        for (var index = 0; index < moduleNavigationButtons.Count; index++)
        {
            var button = moduleNavigationButtons[index];
            var selected = index == selectedIndex;
            button.Checked = selected;
            button.BackColor = selected ? AppVisualTheme.ShellRaised : AppVisualTheme.Shell;
            button.ForeColor = selected ? Color.White : AppVisualTheme.ShellMuted;
            button.AccessibleDescription = selected
                ? $"当前模块：{button.Text}"
                : $"切换到{button.Text}模块";
        }
    }

    private void MainFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!easyConTab.CloseForParentForm())
        {
            e.Cancel = true;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !visualResourcesDisposed)
        {
            visualResourcesDisposed = true;
            Icon = null;
            applicationIcon.Dispose();
        }

        base.Dispose(disposing);
    }

}
