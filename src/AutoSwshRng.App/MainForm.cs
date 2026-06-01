using AutoSwshRng.Core;
using AutoSwshRng.App.Controls;

namespace AutoSwshRng.App;

public sealed class MainForm : Form
{
    private readonly EasyConTabControl easyConTab = new();

    private readonly TabControl mainTabs = new()
    {
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

        Controls.Add(mainTabs);
        FormClosing += MainFormClosing;
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

    private void MainFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!easyConTab.CloseForParentForm())
        {
            e.Cancel = true;
        }
    }

}
