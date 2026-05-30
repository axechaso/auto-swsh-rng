using AutoSwshRng.Core;
using AutoSwshRng.App.Controls;

namespace AutoSwshRng.App;

public sealed class MainForm : Form
{
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
        mainTabs.TabPages.Add(CreateControlTab("伊机控", new EasyConTabControl()));
        mainTabs.TabPages.Add(CreateTab("自动化流程", "后续自动化流程将在 UI 需求确定后接入。"));

        Controls.Add(mainTabs);
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

    private TabPage CreateTab(string title, string body)
    {
        var page = new TabPage(title)
        {
            Padding = new Padding(16),
        };
        page.Controls.Add(new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text = body,
            TextAlign = ContentAlignment.TopLeft,
        });
        return page;
    }
}
