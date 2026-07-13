using AutoSwshRng.Core;
using AutoSwshRng.App.Controls;

namespace AutoSwshRng.App;

public sealed class MainForm : Form
{
    private readonly EasyConTabControl easyConTab = new();
    private bool dpiScaleInitialized;
    private readonly ToolStripStatusLabel currentModuleStatusLabel = new()
    {
        Name = "autoSwshCurrentModuleStatusLabel",
    };

    private readonly TabControl mainTabs = new()
    {
        Name = "autoSwshMainTabs",
        Dock = DockStyle.Fill,
    };

    public MainForm()
    {
        Font = new Font("Microsoft YaHei UI", 9F);
        MinimumSize = new Size(1040, 680);
        ClientSize = new Size(1310, 760);
        Text = $"{ProjectInfo.Name} - {ProjectInfo.Description}";
        StartPosition = FormStartPosition.CenterScreen;

        mainTabs.TabPages.Add(CreateControlTab("owoow", new OwoowTabControl(), edgeToEdge: true));
        mainTabs.TabPages.Add(CreateControlTab("伊机控", easyConTab, edgeToEdge: true));
        mainTabs.TabPages.Add(CreateControlTab("自动化流程", new AutomationFlowTabControl()));
        mainTabs.SelectedIndexChanged += (_, _) => UpdateCurrentModuleStatus();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        root.Controls.Add(mainTabs, 0, 0);
        root.Controls.Add(CreateStatusStrip(), 0, 1);

        Controls.Add(root);
        FormClosing += MainFormClosing;
        UpdateCurrentModuleStatus();
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
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        dpiScaleInitialized = true;
        CenterInitialWindow(initialWorkingArea);
    }

    private void CenterInitialWindow(Rectangle workingArea)
    {
        var left = workingArea.Left + Math.Max(0, (workingArea.Width - Width) / 2);
        var top = workingArea.Top + Math.Max(0, (workingArea.Height - Height) / 2);
        SetBounds(left, top, 0, 0, BoundsSpecified.Location);
    }

    private static TabPage CreateControlTab(string title, Control control, bool edgeToEdge = false)
    {
        var page = new TabPage(title)
        {
            Padding = edgeToEdge ? Padding.Empty : new Padding(16),
        };
        page.Controls.Add(control);
        return page;
    }

    private StatusStrip CreateStatusStrip()
    {
        var status = new StatusStrip
        {
            Name = "autoSwshStatusStrip",
            SizingGrip = false,
        };
        status.Items.Add(new ToolStripStatusLabel(ProjectInfo.Name)
        {
            Name = "autoSwshProjectStatusLabel",
        });
        status.Items.Add(new ToolStripStatusLabel(ProjectInfo.Description)
        {
            Name = "autoSwshDescriptionStatusLabel",
            Spring = true,
            TextAlign = ContentAlignment.MiddleLeft,
        });
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

    private void MainFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!easyConTab.CloseForParentForm())
        {
            e.Cancel = true;
        }
    }

}
