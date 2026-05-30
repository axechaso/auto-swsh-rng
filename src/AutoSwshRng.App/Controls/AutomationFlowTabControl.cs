namespace AutoSwshRng.App.Controls;

public sealed class AutomationFlowTabControl : UserControl
{
    public AutomationFlowTabControl()
    {
        Dock = DockStyle.Fill;

        var placeholder = new Label
        {
            Name = "automationFlowPlaceholder",
            Dock = DockStyle.Fill,
            Text = "自动化流程将在 owoow 与伊机控界面稳定后单独设计。",
            TextAlign = ContentAlignment.MiddleCenter,
        };

        Controls.Add(placeholder);
    }
}
