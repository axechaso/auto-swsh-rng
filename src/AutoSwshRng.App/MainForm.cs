using AutoSwshRng.Core;
using AutoSwshRng.App.Controls;

namespace AutoSwshRng.App;

public sealed class MainForm : Form
{
    private readonly EasyConTabControl easyConTab = new();
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
        Text = $"{ProjectInfo.Name} - {ProjectInfo.Description}";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(960, 640);
        Size = new Size(1120, 720);

        mainTabs.TabPages.Add(CreateControlTab("owoow", new OwoowTabControl()));
        mainTabs.TabPages.Add(CreateControlTab("伊机控", easyConTab));
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

    private static TabPage CreateControlTab(string title, Control control)
    {
        var page = new TabPage(title)
        {
            Padding = new Padding(16),
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
