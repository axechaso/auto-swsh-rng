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
        mainTabs.TabPages.Add(CreateOwoowTab(upstreamSmokeReport.GetRequired("owoow")));
        mainTabs.TabPages.Add(CreateEasyConTab(upstreamSmokeReport.GetRequired("easycon")));
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

    private TabPage CreateOwoowTab(UpstreamSmokeEntry entry)
    {
        const string title = "owoow";
        tabBodies[title] = FormatSmokeEntry(entry);

        var tidInput = new NumericUpDown
        {
            Name = "owoowTidInput",
            Dock = DockStyle.Fill,
            Maximum = ushort.MaxValue,
            Value = 0x1234,
            Hexadecimal = true,
        };
        var sidInput = new NumericUpDown
        {
            Name = "owoowSidInput",
            Dock = DockStyle.Fill,
            Maximum = ushort.MaxValue,
            Value = 0x00FF,
            Hexadecimal = true,
        };
        var resultLabel = new Label
        {
            Name = "owoowResultLabel",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        var calculateButton = new Button
        {
            Name = "owoowCalculateButton",
            Text = "计算",
            Dock = DockStyle.Fill,
        };

        void UpdateResult()
        {
            resultLabel.Text = FormatOwoowResult((uint)tidInput.Value, (uint)sidInput.Value);
        }

        calculateButton.Click += (_, _) => UpdateResult();
        UpdateResult();

        var layout = CreateTwoColumnLayout(5);
        layout.Controls.Add(new Label { Text = "TID", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        layout.Controls.Add(tidInput, 1, 0);
        layout.Controls.Add(new Label { Text = "SID", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        layout.Controls.Add(sidInput, 1, 1);
        layout.Controls.Add(calculateButton, 1, 2);
        layout.Controls.Add(new Label { Text = "Result", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 3);
        layout.Controls.Add(resultLabel, 1, 3);
        layout.Controls.Add(CreateDetailsLabel(entry), 0, 4);
        layout.SetColumnSpan(layout.Controls[^1], 2);

        return CreateControlTab(title, layout);
    }

    private TabPage CreateEasyConTab(UpstreamSmokeEntry entry)
    {
        const string title = "伊机控";
        tabBodies[title] = FormatSmokeEntry(entry);

        var scriptTextBox = new TextBox
        {
            Name = "easyConScriptTextBox",
            AcceptsReturn = true,
            AcceptsTab = true,
            Dock = DockStyle.Fill,
            Font = new Font(FontFamily.GenericMonospace, 10),
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            Text = "PRINT \"hello\"",
            WordWrap = false,
        };
        var runButton = new Button
        {
            Name = "easyConRunButton",
            Text = "运行",
            Dock = DockStyle.Fill,
        };
        var outputTextBox = new TextBox
        {
            Name = "easyConOutputTextBox",
            Dock = DockStyle.Fill,
            Font = new Font(FontFamily.GenericMonospace, 10),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
        };

        void RunScript()
        {
            outputTextBox.Text = FormatEasyConResult(EasyConScriptAdapter.Evaluate(scriptTextBox.Text));
        }

        runButton.Click += (_, _) => RunScript();
        RunScript();

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.Controls.Add(new Label { Text = "Script", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        layout.Controls.Add(scriptTextBox, 0, 1);
        layout.Controls.Add(runButton, 0, 2);
        layout.Controls.Add(outputTextBox, 0, 3);

        var container = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        container.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        container.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        container.Controls.Add(layout, 0, 0);
        container.Controls.Add(CreateDetailsLabel(entry), 0, 1);

        return CreateControlTab(title, container);
    }

    private static TableLayoutPanel CreateTwoColumnLayout(int rowCount)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = rowCount,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < rowCount; row++)
        {
            layout.RowStyles.Add(new RowStyle(row == rowCount - 1 ? SizeType.Percent : SizeType.Absolute, row == rowCount - 1 ? 100 : 36));
        }

        return layout;
    }

    private static Label CreateDetailsLabel(UpstreamSmokeEntry entry)
    {
        return new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text = FormatSmokeEntry(entry),
            TextAlign = ContentAlignment.TopLeft,
        };
    }

    private static string FormatOwoowResult(uint tid, uint sid)
    {
        var shinyValue = OwoowRngAdapter.GetShinyValue(tid, sid);
        return $"0x{tid:X4} ^ 0x{sid:X4} = 0x{shinyValue:X4}";
    }

    private static string FormatEasyConResult(EasyConScriptResult result)
    {
        return "errors = " + result.HasErrors + Environment.NewLine +
            "output = " + string.Join("", result.Printed).Trim() + Environment.NewLine +
            "diagnostics = " + string.Join(Environment.NewLine, result.Diagnostics);
    }

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
