using System.Drawing.Drawing2D;

namespace AutoSwshRng.App.Controls;

public sealed class AutomationFlowTabControl : UserControl
{
    public AutomationFlowTabControl()
    {
        Dock = DockStyle.Fill;
        BackColor = AppVisualTheme.Workspace;

        var placeholder = new AutomationPlaceholderSurface
        {
            Name = "automationFlowPlaceholder",
            Dock = DockStyle.Fill,
            Text = "自动化流程将在 owoow 与伊机控界面稳定后单独设计。",
        };

        Controls.Add(placeholder);
    }
}

internal sealed class AutomationPlaceholderSurface : Control
{
    internal AutomationPlaceholderSurface()
    {
        DoubleBuffered = true;
        BackColor = AppVisualTheme.Workspace;
        ForeColor = AppVisualTheme.Ink;
        AccessibleName = "自动化流程规划中";
        AccessibleDescription = "该模块将在基础工具界面稳定后单独设计。";
        AccessibleRole = AccessibleRole.Pane;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var scale = DeviceDpi / 96F;
        var cardWidth = Math.Min(ClientSize.Width - (48F * scale), 680F * scale);
        var cardHeight = Math.Min(ClientSize.Height - (48F * scale), 312F * scale);
        if (cardWidth <= 0 || cardHeight <= 0)
        {
            return;
        }

        var card = new RectangleF(
            (ClientSize.Width - cardWidth) / 2F,
            (ClientSize.Height - cardHeight) / 2F,
            cardWidth,
            cardHeight);
        using var shadow = new SolidBrush(Color.FromArgb(18, 15, 27, 42));
        using var surface = new SolidBrush(AppVisualTheme.Surface);
        using var border = new Pen(AppVisualTheme.Border, Math.Max(1F, scale));
        using var accent = new SolidBrush(AppVisualTheme.Accent);
        using var accentSoft = new SolidBrush(AppVisualTheme.AccentSoft);
        using var ink = new SolidBrush(AppVisualTheme.Ink);
        using var muted = new SolidBrush(AppVisualTheme.Muted);
        using var titleFont = new Font("Microsoft YaHei UI", 20F * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        using var bodyFont = new Font("Microsoft YaHei UI", 10F * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        using var metaFont = new Font("Microsoft YaHei UI", 9F * scale, FontStyle.Regular, GraphicsUnit.Pixel);

        var shadowCard = card;
        shadowCard.Offset(0, 5F * scale);
        using (var shadowPath = AppVisualTheme.CreateRoundedRectangle(shadowCard, 14F * scale))
        {
            e.Graphics.FillPath(shadow, shadowPath);
        }
        using (var cardPath = AppVisualTheme.CreateRoundedRectangle(card, 14F * scale))
        {
            e.Graphics.FillPath(surface, cardPath);
            e.Graphics.DrawPath(border, cardPath);
        }

        var accentBar = new RectangleF(card.Left, card.Top, 7F * scale, card.Height);
        using (var accentPath = AppVisualTheme.CreateRoundedRectangle(accentBar, 3.5F * scale))
        {
            e.Graphics.FillPath(accent, accentPath);
        }

        var iconBounds = new RectangleF(
            card.Left + (40F * scale),
            card.Top + (42F * scale),
            56F * scale,
            56F * scale);
        e.Graphics.FillEllipse(accentSoft, iconBounds);
        DrawFlowMark(e.Graphics, iconBounds, scale);

        var textLeft = iconBounds.Right + (28F * scale);
        e.Graphics.DrawString("自动化流程", titleFont, ink, textLeft, card.Top + (42F * scale));
        e.Graphics.DrawString(
            Text,
            bodyFont,
            muted,
            new RectangleF(textLeft, card.Top + (82F * scale), card.Right - textLeft - (38F * scale), 48F * scale));

        var dividerY = card.Top + (154F * scale);
        e.Graphics.DrawLine(border, card.Left + (40F * scale), dividerY, card.Right - (40F * scale), dividerY);

        var statusBounds = new RectangleF(
            card.Left + (40F * scale),
            dividerY + (30F * scale),
            116F * scale,
            32F * scale);
        using (var statusPath = AppVisualTheme.CreateRoundedRectangle(statusBounds, 8F * scale))
        {
            e.Graphics.FillPath(accentSoft, statusPath);
        }
        using var statusFormat = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        e.Graphics.DrawString("独立设计中", metaFont, accent, statusBounds, statusFormat);
        e.Graphics.DrawString(
            "当前优先完善 owoow 与伊机控的稳定性和基础体验",
            metaFont,
            muted,
            statusBounds.Right + (18F * scale),
            statusBounds.Top + (7F * scale));
    }

    private static void DrawFlowMark(Graphics graphics, RectangleF bounds, float scale)
    {
        using var line = new Pen(AppVisualTheme.Accent, 2F * scale)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        using var node = new SolidBrush(AppVisualTheme.Accent);
        var first = new PointF(bounds.Left + (15F * scale), bounds.Top + (18F * scale));
        var second = new PointF(bounds.Left + (39F * scale), bounds.Top + (16F * scale));
        var third = new PointF(bounds.Left + (29F * scale), bounds.Top + (39F * scale));
        graphics.DrawLine(line, first, second);
        graphics.DrawLine(line, second, third);
        graphics.DrawLine(line, third, first);
        var nodeSize = 7F * scale;
        foreach (var point in new[] { first, second, third })
        {
            graphics.FillEllipse(node, point.X - (nodeSize / 2F), point.Y - (nodeSize / 2F), nodeSize, nodeSize);
        }
    }
}
