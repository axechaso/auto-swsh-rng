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

        mainTabs.TabPages.Add(CreateTab("概览", ProjectInfo.Description));
        mainTabs.TabPages.Add(CreateTab(
            "owoow",
            $"已引用 {OwoowUpstreamInfo.AssemblyName} ({OwoowUpstreamInfo.GameEnumTypeName})\r\n" +
            $"Smoke: shiny value 0x1234 ^ 0x00FF = 0x{OwoowRngAdapter.GetShinyValue(0x1234, 0x00FF):X4}\r\n" +
            $"Smoke: shiny xor 0 = {OwoowRngAdapter.GetShinyType(0)}"));
        var easyConScriptResult = EasyConScriptAdapter.Evaluate("PRINT \"hello\"");
        mainTabs.TabPages.Add(CreateTab(
            "伊机控",
            $"已引用 {EasyConUpstreamInfo.ScriptAssemblyName} ({EasyConUpstreamInfo.GamePadKeyTypeName})\r\n" +
            $"已引用 {EasyConUpstreamInfo.DeviceAssemblyName} ({EasyConUpstreamInfo.DirectionKeyTypeName})\r\n" +
            $"Smoke: script errors = {easyConScriptResult.HasErrors}\r\n" +
            $"Smoke: script output = {string.Join("", easyConScriptResult.Printed)}"));
        mainTabs.TabPages.Add(CreateTab("自动化流程", "后续自动化流程将在 UI 需求确定后接入。"));

        Controls.Add(mainTabs);
    }

    public IReadOnlyList<string> TabTitles => mainTabs.TabPages.Cast<TabPage>().Select(page => page.Text).ToArray();

    public IReadOnlyDictionary<string, string> TabBodies => tabBodies;

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
