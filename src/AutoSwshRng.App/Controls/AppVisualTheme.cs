using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace AutoSwshRng.App.Controls;

internal static class AppVisualTheme
{
    internal static readonly Color Shell = Color.FromArgb(15, 27, 42);
    internal static readonly Color ShellRaised = Color.FromArgb(26, 44, 62);
    internal static readonly Color ShellMuted = Color.FromArgb(154, 170, 184);
    internal static readonly Color Workspace = Color.FromArgb(241, 245, 247);
    internal static readonly Color Surface = Color.White;
    internal static readonly Color SurfaceMuted = Color.FromArgb(234, 240, 243);
    internal static readonly Color Border = Color.FromArgb(202, 214, 222);
    internal static readonly Color Ink = Color.FromArgb(28, 42, 54);
    internal static readonly Color Muted = Color.FromArgb(96, 111, 123);
    internal static readonly Color Accent = Color.FromArgb(16, 135, 124);
    internal static readonly Color AccentHover = Color.FromArgb(12, 113, 104);
    internal static readonly Color AccentSoft = Color.FromArgb(222, 243, 239);
    internal static readonly Color DisabledText = Color.FromArgb(145, 157, 166);

    internal static readonly Font HeaderTitleFont = new("Segoe UI Semibold", 10F);
    internal static readonly Font HeaderSubtitleFont = new("Microsoft YaHei UI", 8F);
    internal static readonly Font NavigationFont = new("Microsoft YaHei UI", 9F);
    internal static readonly Font UiSemiboldFont = new("Segoe UI Semibold", 9F);

    internal static Icon CreateApplicationIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            using var background = new SolidBrush(Shell);
            using var accent = new SolidBrush(Color.FromArgb(51, 201, 181));
            using var bright = new SolidBrush(Color.White);
            using var line = new Pen(Color.FromArgb(125, 236, 219), 2.2F)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            using var path = CreateRoundedRectangle(new RectangleF(1, 1, 30, 30), 7F);
            graphics.FillPath(background, path);
            graphics.DrawLine(line, 9, 10, 22, 9);
            graphics.DrawLine(line, 22, 9, 16, 22);
            graphics.DrawLine(line, 16, 22, 9, 10);
            graphics.FillEllipse(bright, 6.5F, 7.5F, 5F, 5F);
            graphics.FillEllipse(accent, 19F, 6F, 6F, 6F);
            graphics.FillEllipse(bright, 13F, 19F, 6F, 6F);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            _ = DestroyIcon(handle);
        }
    }

    internal static ToolStripRenderer CreateLightToolStripRenderer() =>
        new ToolStripProfessionalRenderer(new LightWorkbenchColorTable())
        {
            RoundedEdges = false,
        };

    internal static ToolStripRenderer CreateDarkToolStripRenderer() =>
        new ToolStripProfessionalRenderer(new DarkWorkbenchColorTable())
        {
            RoundedEdges = false,
        };

    internal static void ApplyOwoowTheme(Control root)
    {
        foreach (Control child in root.Controls)
        {
            switch (child)
            {
                case GroupBox group:
                    group.BackColor = Surface;
                    group.ForeColor = Ink;
                    break;

                case Button button:
                    StyleButton(button, IsPrimaryOwoowAction(button.Name));
                    break;

                case TextBox textBox:
                    textBox.BorderStyle = BorderStyle.FixedSingle;
                    textBox.BackColor = textBox.ReadOnly ? SurfaceMuted : Surface;
                    textBox.ForeColor = textBox.Enabled ? Ink : DisabledText;
                    textBox.EnabledChanged += (_, _) =>
                        textBox.ForeColor = textBox.Enabled ? Ink : DisabledText;
                    break;

                case ComboBox comboBox:
                    comboBox.FlatStyle = FlatStyle.Flat;
                    comboBox.BackColor = Surface;
                    comboBox.ForeColor = Ink;
                    break;

                case NumericUpDown numeric:
                    numeric.BorderStyle = BorderStyle.FixedSingle;
                    numeric.BackColor = Surface;
                    numeric.ForeColor = Ink;
                    break;

                case CheckBox checkBox:
                    checkBox.BackColor = Color.Transparent;
                    checkBox.ForeColor = Ink;
                    break;

                case TabControl tabs:
                    foreach (TabPage page in tabs.TabPages)
                    {
                        page.UseVisualStyleBackColor = false;
                        page.BackColor = Surface;
                        page.ForeColor = Ink;
                    }
                    break;

                case Panel panel:
                    panel.BackColor = panel.Name is "owoowScrollHost" or "owoowMainCanvas"
                        ? Workspace
                        : Surface;
                    break;
            }

            if (child is not DataGridView)
            {
                ApplyOwoowTheme(child);
            }
        }
    }

    internal static void StyleButton(Button button, bool primary)
    {
        button.UseVisualStyleBackColor = false;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = Border;
        button.Cursor = Cursors.Hand;
        button.EnabledChanged += (_, _) => ApplyButtonState(button, primary, hot: false);
        button.MouseEnter += (_, _) => ApplyButtonState(button, primary, hot: true);
        button.MouseLeave += (_, _) => ApplyButtonState(button, primary, hot: false);
        ApplyButtonState(button, primary, hot: false);
    }

    internal static GraphicsPath CreateRoundedRectangle(RectangleF rectangle, float radius)
    {
        var diameter = radius * 2F;
        var path = new GraphicsPath();
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void ApplyButtonState(Button button, bool primary, bool hot)
    {
        if (!button.Enabled)
        {
            button.BackColor = SurfaceMuted;
            button.ForeColor = DisabledText;
            button.Cursor = Cursors.Default;
            return;
        }

        button.Cursor = Cursors.Hand;
        if (primary)
        {
            button.BackColor = hot ? AccentHover : Accent;
            button.ForeColor = Color.White;
            return;
        }

        button.BackColor = hot ? AccentSoft : Surface;
        button.ForeColor = hot ? AccentHover : Ink;
    }

    private static bool IsPrimaryOwoowAction(string name) =>
        name is "B_Connect" or "B_RetailSeedFinder" ||
        name.EndsWith("_Search", StringComparison.Ordinal);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    private sealed class LightWorkbenchColorTable : ProfessionalColorTable
    {
        public override Color ToolStripGradientBegin => Surface;
        public override Color ToolStripGradientMiddle => Surface;
        public override Color ToolStripGradientEnd => Surface;
        public override Color ToolStripBorder => Border;
        public override Color MenuItemSelected => AccentSoft;
        public override Color MenuItemBorder => Accent;
        public override Color MenuItemPressedGradientBegin => AccentSoft;
        public override Color MenuItemPressedGradientMiddle => AccentSoft;
        public override Color MenuItemPressedGradientEnd => AccentSoft;
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Workspace;
        public override Color ImageMarginGradientMiddle => Workspace;
        public override Color ImageMarginGradientEnd => Workspace;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Surface;
    }

    private sealed class DarkWorkbenchColorTable : ProfessionalColorTable
    {
        public override Color StatusStripGradientBegin => Shell;
        public override Color StatusStripGradientEnd => Shell;
        public override Color ToolStripGradientBegin => Shell;
        public override Color ToolStripGradientMiddle => Shell;
        public override Color ToolStripGradientEnd => Shell;
        public override Color ToolStripBorder => ShellRaised;
        public override Color SeparatorDark => ShellRaised;
        public override Color SeparatorLight => Shell;
    }
}

internal sealed class BrandMarkControl : Control
{
    internal BrandMarkControl()
    {
        DoubleBuffered = true;
        AccessibleName = "Auto SWSH RNG 标志";
        AccessibleRole = AccessibleRole.Graphic;
        BackColor = AppVisualTheme.Shell;
        Size = new Size(34, 34);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using var background = new SolidBrush(AppVisualTheme.ShellRaised);
        using var accent = new SolidBrush(Color.FromArgb(57, 211, 190));
        using var bright = new SolidBrush(Color.White);
        using var line = new Pen(Color.FromArgb(116, 232, 216), 2F)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        using var path = AppVisualTheme.CreateRoundedRectangle(new RectangleF(1, 1, Width - 2, Height - 2), 8F);
        e.Graphics.FillPath(background, path);

        var scaleX = Width / 34F;
        var scaleY = Height / 34F;
        PointF Point(float x, float y) => new(x * scaleX, y * scaleY);
        e.Graphics.DrawLine(line, Point(10, 11), Point(24, 10));
        e.Graphics.DrawLine(line, Point(24, 10), Point(17, 24));
        e.Graphics.DrawLine(line, Point(17, 24), Point(10, 11));
        e.Graphics.FillEllipse(bright, 7F * scaleX, 8F * scaleY, 6F * scaleX, 6F * scaleY);
        e.Graphics.FillEllipse(accent, 21F * scaleX, 7F * scaleY, 6F * scaleX, 6F * scaleY);
        e.Graphics.FillEllipse(bright, 14F * scaleX, 21F * scaleY, 6F * scaleX, 6F * scaleY);
    }
}
