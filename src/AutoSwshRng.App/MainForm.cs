using AutoSwshRng.Core;
using AutoSwshRng.Upstream;

namespace AutoSwshRng.App;

public sealed class MainForm : Form
{
    private readonly Dictionary<string, string> tabBodies = [];
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

        var upstreamSmokeReport = UpstreamSmokeReport.Create();
        mainTabs.TabPages.Add(CreateTab(
            "概览",
            ProjectInfo.Description + Environment.NewLine + upstreamSmokeReport.ToDisplayText()));
        mainTabs.TabPages.Add(CreateTab("owoow", FormatSmokeEntry(upstreamSmokeReport.GetRequired("owoow"))));
        mainTabs.TabPages.Add(CreateTab("伊机控", FormatSmokeEntry(upstreamSmokeReport.GetRequired("easycon"))));
        mainTabs.TabPages.Add(CreateTab("自动化流程", "后续自动化流程将在 UI 需求确定后接入。"));

        Controls.Add(mainTabs);
    }

    public IReadOnlyList<string> TabTitles => mainTabs.TabPages.Cast<TabPage>().Select(page => page.Text).ToArray();

    public IReadOnlyDictionary<string, string> TabBodies => tabBodies;

    private static string FormatSmokeEntry(UpstreamSmokeEntry entry)
    {
        return string.Join(
            Environment.NewLine,
            new[] { $"{entry.DisplayName}: {(entry.Passed ? "OK" : "FAIL")} - {entry.Summary}" }
                .Concat(entry.Details));
    }

    private TabPage CreateTab(string title, string body)
    {
        tabBodies[title] = body;
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
