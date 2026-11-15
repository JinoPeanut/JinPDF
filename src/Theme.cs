using System.Drawing;
using System.Drawing.Drawing2D;
using System;
using System.Windows.Forms;

static class Theme
{
    public static readonly Color Ink = Color.FromArgb(24, 36, 57);
    public static readonly Color Muted = Color.FromArgb(78, 94, 118);
    public static readonly Color Surface = Color.FromArgb(242, 246, 251);
    public static readonly Color Accent = Color.FromArgb(37, 95, 173);
    public static readonly Color Tint = Color.FromArgb(222, 236, 252);
    public static readonly Color Line = Color.FromArgb(213, 223, 235);

    public static Label Text(string value, int size = 10, bool bold = false)
    {
        return new Label { Text = value, ForeColor = Ink, AutoSize = true,
            Font = new Font("Malgun Gothic", size, bold ? FontStyle.Bold : FontStyle.Regular) };
    }

    public static Button Button(string value, bool primary = false)
    {
        var button = new SoftButton(primary) { Text = value, AutoSize = true, Padding = new Padding(12, 0, 12, 0),
            MinimumSize = new Size(primary ? 100 : 42, 35), Height = 35, FlatStyle = FlatStyle.Flat,
            ForeColor = Ink,
            UseVisualStyleBackColor = false, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 8, 0) };
        if (primary) button.Font = new Font("Malgun Gothic", 10, FontStyle.Bold);
        return button;
    }

    sealed class SoftButton : Button
    {
        readonly bool primary;
        bool hover;
        bool pressed;

        public SoftButton(bool primary)
        {
            this.primary = primary;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.Opaque | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var backdrop = new SolidBrush(Parent == null ? BackColor : Parent.BackColor))
                e.Graphics.FillRectangle(backdrop, e.ClipRectangle);
            var fill = Color.FromArgb(250, 252, 255);
            var edge = Line;
            var text = Ink;
            if (primary)
            {
                fill = Color.FromArgb(205, 227, 251);
                edge = Color.FromArgb(155, 188, 228);
            }
            if (!Enabled) { fill = Color.FromArgb(234, 240, 248); edge = Line; text = Muted; }
            else if (pressed) { fill = Color.FromArgb(184, 211, 244); edge = Accent; }
            else if (hover) { fill = primary ? Color.FromArgb(190, 219, 249) : Tint; edge = Accent; }
            if (SystemInformation.HighContrast)
            {
                fill = Enabled && (primary || pressed || hover) ? SystemColors.Highlight : SystemColors.Control;
                edge = SystemColors.WindowText;
                text = Enabled ? (fill == SystemColors.Highlight ? SystemColors.HighlightText : SystemColors.ControlText) : SystemColors.GrayText;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var shape = Round(new RectangleF(1, 1, Width - 3, Height - 3), 9))
            using (var background = new SolidBrush(fill))
            using (var outline = new Pen(edge))
            {
                e.Graphics.FillPath(background, shape);
                e.Graphics.DrawPath(outline, shape);
            }
            if (Enabled && Focused && ShowFocusCues)
                using (var shape = Round(new RectangleF(4, 4, Width - 9, Height - 9), 6))
                using (var focus = new Pen(SystemInformation.HighContrast ? text : Accent) { DashStyle = DashStyle.Dot })
                    e.Graphics.DrawPath(focus, shape);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, text, TextFormatFlags.SingleLine |
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        static GraphicsPath Round(RectangleF bounds, float radius)
        {
            var path = new GraphicsPath();
            float diameter = radius * 2;
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnMouseEnter(System.EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(System.EventArgs e) { base.OnMouseLeave(e); hover = false; pressed = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); pressed = e.Button == MouseButtons.Left; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            if (pressed && !Capture)
            {
                pressed = false;
                Invalidate();
            }
            base.OnMouseCaptureChanged(e);
        }
    }

    public static FlowLayoutPanel Row()
    {
        return new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false,
            BackColor = Color.White, FlowDirection = FlowDirection.LeftToRight };
    }

    public static void DrawBorder(Graphics graphics, Rectangle bounds)
    {
        using (var pen = new Pen(Line)) graphics.DrawRectangle(pen, bounds.Left, bounds.Top, bounds.Width - 1, bounds.Height - 1);
    }

    public static void DrawFileIcon(Graphics graphics, Rectangle bounds, string extension)
    {
        Color color;
        string type;
        switch (extension.ToLowerInvariant())
        {
            case ".pdf": color = Color.FromArgb(184, 50, 52); type = "PDF"; break;
            case ".xls":
            case ".xlsx": color = Color.FromArgb(28, 120, 91); type = "X"; break;
            case ".ppt":
            case ".pptx": color = Color.FromArgb(172, 76, 45); type = "P"; break;
            case "add": color = Accent; type = "+"; break;
            default: color = Accent; type = "image"; break;
        }

        var icon = new Rectangle(bounds.Left + 2, bounds.Top + 2, bounds.Width - 5, bounds.Height - 5);
        int fold = Math.Max(5, icon.Width / 3);
        var outline = new[] { new Point(icon.Left, icon.Top), new Point(icon.Right - fold, icon.Top),
            new Point(icon.Right, icon.Top + fold), new Point(icon.Right, icon.Bottom),
            new Point(icon.Left, icon.Bottom), new Point(icon.Left, icon.Top) };
        var previous = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        try
        {
            using (var pen = new Pen(color, 1.3f) { LineJoin = LineJoin.Round })
            {
                graphics.DrawLines(pen, outline);
                graphics.DrawLines(pen, new[] { outline[1], new Point(outline[1].X, outline[2].Y), outline[2] });
                if (type == "+")
                {
                    int x = icon.Left + icon.Width / 2;
                    int y = icon.Top + icon.Height * 2 / 3;
                    graphics.DrawLine(pen, x - 4, y, x + 4, y);
                    graphics.DrawLine(pen, x, y - 4, x, y + 4);
                }
                else if (type == "image")
                    graphics.DrawLines(pen, new[] { new Point(icon.Left + 3, icon.Bottom - 4), new Point(icon.Left + 7, icon.Bottom - 8),
                        new Point(icon.Left + 10, icon.Bottom - 5), new Point(icon.Right - 2, icon.Bottom - 7) });
            }
            if (type != "+" && type != "image")
            {
                using (var font = new Font("Segoe UI", type.Length == 1 ? 7 : 5, FontStyle.Bold))
                {
                    var label = Rectangle.FromLTRB(icon.Left, icon.Top + fold - 1, icon.Right, icon.Bottom);
                    TextRenderer.DrawText(graphics, type, font, label, color,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }
        }
        finally { graphics.SmoothingMode = previous; }
    }

    public static void Fit(Graphics graphics, Image image, RectangleF bounds)
    {
        float scale = System.Math.Min(bounds.Width / image.Width, bounds.Height / image.Height);
        float width = image.Width * scale;
        float height = image.Height * scale;
        graphics.DrawImage(image, bounds.X + (bounds.Width - width) / 2,
            bounds.Y + (bounds.Height - height) / 2, width, height);
    }
}
