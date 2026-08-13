using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WorkbenchHost
{
    // ------------------------------------------------------------------
    // VS Code dark theme palette (Dark+ chrome colors).
    // ------------------------------------------------------------------
    internal static class VSCodeColors
    {
        internal static readonly Color TitleBar = Color.FromArgb(24, 24, 27);      // modern VS Code title bar
        internal static readonly Color TitleBarHover = Color.FromArgb(45, 45, 48);
        internal static readonly Color CloseHover = Color.FromArgb(232, 17, 35);   // #E81123
        internal static readonly Color Window = Color.FromArgb(24, 24, 27);
        internal static readonly Color Editor = Color.FromArgb(30, 30, 30);       // #1E1E1E editor area
        internal static readonly Color Sidebar = Color.FromArgb(24, 24, 27);
        internal static readonly Color ActivityBar = Color.FromArgb(24, 24, 27);
        internal static readonly Color Toolbar = Color.FromArgb(24, 24, 27);
        internal static readonly Color TabInactive = Color.FromArgb(24, 24, 27);
        internal static readonly Color StatusBar = Color.FromArgb(24, 24, 27);
        internal static readonly Color Accent = Color.FromArgb(0, 122, 204);       // #007ACC
        internal static readonly Color AccentDark = Color.FromArgb(14, 99, 156);   // #0E639C buttons
        internal static readonly Color Hover = Color.FromArgb(43, 45, 48);
        internal static readonly Color Selected = Color.FromArgb(55, 58, 65);
        internal static readonly Color Dropdown = Color.FromArgb(31, 31, 35);
        internal static readonly Color DropdownBorder = Color.FromArgb(69, 69, 69);// #454545
        internal static readonly Color Separator = Color.FromArgb(63, 63, 70);     // #3F3F46
        internal static readonly Color Input = Color.FromArgb(35, 35, 40);
        internal static readonly Color Border = Color.FromArgb(43, 43, 48);
        internal static readonly Color Text = Color.FromArgb(204, 204, 204);       // #CCCCCC
        internal static readonly Color TextBright = Color.FromArgb(255, 255, 255); // #FFFFFF
        internal static readonly Color TextMuted = Color.FromArgb(150, 150, 150);  // #969696
        internal static readonly Color ActivityInactive = Color.FromArgb(133, 133, 133); // #858585
    }

    internal sealed class EditorGutter : Control
    {
        private RichTextBox editor;

        internal EditorGutter()
        {
            Dock = DockStyle.Left;
            Width = 52;
            BackColor = VSCodeColors.Editor;
            ForeColor = Color.FromArgb(133, 133, 133);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        internal RichTextBox Editor
        {
            get { return editor; }
            set
            {
                editor = value;
                if (editor == null) return;
                editor.VScroll += delegate { Invalidate(); };
                editor.TextChanged += delegate { Invalidate(); };
                editor.Resize += delegate { Invalidate(); };
                editor.SelectionChanged += delegate { Invalidate(); };
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            if (editor == null || editor.Lines.Length == 0) return;
            int firstChar = editor.GetCharIndexFromPosition(new Point(1, 1));
            int firstLine = Math.Max(0, editor.GetLineFromCharIndex(firstChar));
            int currentLine = Math.Max(0, editor.GetLineFromCharIndex(editor.SelectionStart));
            int bottom = editor.ClientSize.Height;
            for (int line = firstLine; line < editor.Lines.Length; line++)
            {
                int lineStart = editor.GetFirstCharIndexFromLine(line);
                if (lineStart < 0) break;
                Point position = editor.GetPositionFromCharIndex(lineStart);
                if (position.Y > bottom) break;
                Color color = line == currentLine ? VSCodeColors.Text : ForeColor;
                TextRenderer.DrawText(e.Graphics, (line + 1).ToString(), editor.Font,
                    new Rectangle(0, position.Y, Width - 10, Math.Max(18, editor.Font.Height + 3)),
                    color, TextFormatFlags.Right | TextFormatFlags.Top | TextFormatFlags.NoPadding);
            }
        }
    }

    // ------------------------------------------------------------------
    // Professional color table that drives ToolStripProfessionalRenderer
    // so menus, toolbars and context menus follow the VS Code palette.
    // ------------------------------------------------------------------
    internal sealed class VSCodeColorTable : ProfessionalColorTable
    {
        private readonly Color background;

        internal VSCodeColorTable(Color background)
        {
            this.background = background;
        }

        public override Color ToolStripGradientBegin { get { return background; } }
        public override Color ToolStripGradientMiddle { get { return background; } }
        public override Color ToolStripGradientEnd { get { return background; } }
        public override Color ToolStripBorder { get { return background; } }
        public override Color ToolStripDropDownBackground { get { return VSCodeColors.Dropdown; } }
        public override Color ImageMarginGradientBegin { get { return VSCodeColors.Dropdown; } }
        public override Color ImageMarginGradientMiddle { get { return VSCodeColors.Dropdown; } }
        public override Color ImageMarginGradientEnd { get { return VSCodeColors.Dropdown; } }
        public override Color MenuBorder { get { return VSCodeColors.DropdownBorder; } }
        public override Color MenuItemBorder { get { return VSCodeColors.Hover; } }
        public override Color MenuItemSelected { get { return VSCodeColors.Hover; } }
        public override Color MenuItemSelectedGradientBegin { get { return VSCodeColors.Hover; } }
        public override Color MenuItemSelectedGradientEnd { get { return VSCodeColors.Hover; } }
        public override Color MenuItemPressedGradientBegin { get { return VSCodeColors.Dropdown; } }
        public override Color MenuItemPressedGradientMiddle { get { return VSCodeColors.Dropdown; } }
        public override Color MenuItemPressedGradientEnd { get { return VSCodeColors.Dropdown; } }
        public override Color SeparatorDark { get { return VSCodeColors.Separator; } }
        public override Color SeparatorLight { get { return VSCodeColors.Dropdown; } }
        public override Color CheckBackground { get { return VSCodeColors.Dropdown; } }
        public override Color CheckSelectedBackground { get { return VSCodeColors.Hover; } }
        public override Color CheckPressedBackground { get { return VSCodeColors.Selected; } }
        public override Color ButtonSelectedHighlight { get { return VSCodeColors.Hover; } }
        public override Color ButtonSelectedHighlightBorder { get { return Color.Transparent; } }
        public override Color ButtonSelectedGradientBegin { get { return VSCodeColors.Hover; } }
        public override Color ButtonSelectedGradientMiddle { get { return VSCodeColors.Hover; } }
        public override Color ButtonSelectedGradientEnd { get { return VSCodeColors.Hover; } }
        public override Color ButtonPressedGradientBegin { get { return VSCodeColors.Selected; } }
        public override Color ButtonPressedGradientMiddle { get { return VSCodeColors.Selected; } }
        public override Color ButtonPressedGradientEnd { get { return VSCodeColors.Selected; } }
        public override Color ButtonPressedHighlight { get { return VSCodeColors.Selected; } }
        public override Color ButtonPressedHighlightBorder { get { return Color.Transparent; } }
        public override Color ButtonCheckedGradientBegin { get { return VSCodeColors.AccentDark; } }
        public override Color ButtonCheckedGradientMiddle { get { return VSCodeColors.AccentDark; } }
        public override Color ButtonCheckedGradientEnd { get { return VSCodeColors.AccentDark; } }
        public override Color ButtonCheckedHighlight { get { return VSCodeColors.AccentDark; } }
        public override Color ButtonCheckedHighlightBorder { get { return Color.Transparent; } }
    }

    internal sealed class VSCodeToolStripRenderer : ToolStripProfessionalRenderer
    {
        internal VSCodeToolStripRenderer(Color background)
            : base(new VSCodeColorTable(background))
        {
        }
    }

    // ------------------------------------------------------------------
    // Dark tab control: paints the whole tab strip with the VS Code
    // side-bar color; individual tabs are owner-drawn by the form.
    // ------------------------------------------------------------------
    internal sealed class DarkTabControl : TabControl
    {
        internal DarkTabControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(VSCodeColors.Sidebar);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(VSCodeColors.Sidebar);
            for (int i = 0; i < TabCount; i++)
            {
                DrawItemState state = i == SelectedIndex ? DrawItemState.Selected : DrawItemState.Default;
                OnDrawItem(new DrawItemEventArgs(e.Graphics, Font, GetTabRect(i), i, state));
            }
        }
    }

    // ------------------------------------------------------------------
    // Flat title bar window buttons (minimize / maximize / close).
    // ------------------------------------------------------------------
    internal sealed class TitleBarButton : Control
    {
        internal enum Kind
        {
            Minimize,
            Maximize,
            Restore,
            Close
        }

        private bool hovered;
        private Kind kind;

        internal TitleBarButton(Kind kind)
        {
            this.kind = kind;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(46, 34);
            TabStop = false;
            Cursor = Cursors.Default;
        }

        internal event EventHandler Clicked;

        internal Kind ButtonKind
        {
            get { return kind; }
            set
            {
                if (kind != value)
                {
                    kind = value;
                    Invalidate();
                }
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hovered = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && Clicked != null) Clicked(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color back = VSCodeColors.TitleBar;
            if (hovered) back = kind == Kind.Close ? VSCodeColors.CloseHover : VSCodeColors.TitleBarHover;
            using (SolidBrush brush = new SolidBrush(back))
                e.Graphics.FillRectangle(brush, ClientRectangle);

            Color stroke = hovered ? Color.White : Color.FromArgb(210, 210, 210);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int cx = Width / 2;
            int cy = Height / 2;
            using (Pen pen = new Pen(stroke, 1.2f))
            {
                switch (kind)
                {
                    case Kind.Minimize:
                        e.Graphics.DrawLine(pen, cx - 7, cy, cx + 7, cy);
                        break;
                    case Kind.Maximize:
                        e.Graphics.DrawRectangle(pen, cx - 7, cy - 6, 14, 12);
                        break;
                    case Kind.Restore:
                        e.Graphics.DrawRectangle(pen, cx - 7, cy - 5, 10, 8);
                        e.Graphics.DrawRectangle(pen, cx - 3, cy - 8, 10, 8);
                        break;
                    case Kind.Close:
                        e.Graphics.DrawLine(pen, cx - 6, cy - 6, cx + 6, cy + 6);
                        e.Graphics.DrawLine(pen, cx + 6, cy - 6, cx - 6, cy + 6);
                        break;
                }
            }
        }
    }

    // ------------------------------------------------------------------
    // Small vector icon helpers drawn with GDI+ (no image assets).
    // ------------------------------------------------------------------
    internal static class IconPainter
    {
        internal static void DrawFolder(Graphics g, Rectangle r, Color color)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float x = r.X;
            float y = r.Y;
            float w = r.Width;
            float h = r.Height;
            using (SolidBrush b = new SolidBrush(color))
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddPolygon(new PointF[]
                {
                    new PointF(x + w * 0.04f, y + h * 0.30f),
                    new PointF(x + w * 0.26f, y + h * 0.30f),
                    new PointF(x + w * 0.34f, y + h * 0.44f),
                    new PointF(x + w * 0.94f, y + h * 0.44f),
                    new PointF(x + w * 0.94f, y + h * 0.84f),
                    new PointF(x + w * 0.04f, y + h * 0.84f)
                });
                p.CloseFigure();
                g.FillPath(b, p);
            }
        }

        internal static void DrawSearch(Graphics g, Rectangle r, Color color)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(color, 2f))
            {
                g.DrawEllipse(pen, r.X + r.Width * 0.14f, r.Y + r.Height * 0.12f, r.Width * 0.52f, r.Height * 0.52f);
                g.DrawLine(pen, r.X + r.Width * 0.56f, r.Y + r.Height * 0.56f, r.X + r.Width * 0.86f, r.Y + r.Height * 0.86f);
            }
        }

        internal static void DrawBranch(Graphics g, Rectangle r, Color color)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(color, 1.8F))
            {
                g.DrawLine(pen, r.Left + 6, r.Top + 5, r.Left + 6, r.Bottom - 5);
                g.DrawLine(pen, r.Left + 6, r.Top + 11, r.Right - 6, r.Top + 11);
                g.DrawLine(pen, r.Right - 6, r.Top + 11, r.Right - 6, r.Bottom - 5);
                g.DrawEllipse(pen, r.Left + 3, r.Top + 1, 6, 6);
                g.DrawEllipse(pen, r.Left + 3, r.Bottom - 8, 6, 6);
                g.DrawEllipse(pen, r.Right - 9, r.Bottom - 8, 6, 6);
            }
        }

        internal static void DrawPlay(Graphics g, Rectangle r, Color color)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(color, 1.6F)) g.DrawEllipse(pen, r.Left + 1, r.Top + 1, r.Width - 5, r.Height - 5);
            using (SolidBrush brush = new SolidBrush(color))
                g.FillPolygon(brush, new Point[] { new Point(r.Left + 9, r.Top + 6), new Point(r.Right - 5, r.Top + r.Height / 2), new Point(r.Left + 9, r.Bottom - 7) });
        }

        internal static void DrawExtensions(Graphics g, Rectangle r, Color color)
        {
            using (Pen pen = new Pen(color, 1.5F))
            {
                int s = 8;
                g.DrawRectangle(pen, r.Left + 2, r.Top + 2, s, s);
                g.DrawRectangle(pen, r.Left + 13, r.Top + 2, s, s);
                g.DrawRectangle(pen, r.Left + 2, r.Top + 13, s, s);
                g.DrawRectangle(pen, r.Left + 13, r.Top + 13, s, s);
            }
        }

        internal static void DrawAccount(Graphics g, Rectangle r, Color color)
        {
            using (Pen pen = new Pen(color, 1.6F))
            {
                g.DrawEllipse(pen, r.Left + 7, r.Top + 2, 10, 10);
                g.DrawArc(pen, r.Left + 3, r.Top + 12, 18, 12, 190, 160);
            }
        }

        internal static void DrawGear(Graphics g, Rectangle r, Color color)
        {
            using (Pen pen = new Pen(color, 1.8F))
            {
                g.DrawEllipse(pen, r.Left + 4, r.Top + 4, 16, 16);
                g.DrawEllipse(pen, r.Left + 9, r.Top + 9, 6, 6);
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    eLine(g, pen, r, a, 9, 12);
                }
            }
        }

        private static void eLine(Graphics g, Pen pen, Rectangle r, double angle, double inner, double outer)
        {
            float cx = r.Left + r.Width / 2F;
            float cy = r.Top + r.Height / 2F;
            g.DrawLine(pen, cx + (float)Math.Cos(angle) * (float)inner, cy + (float)Math.Sin(angle) * (float)inner,
                cx + (float)Math.Cos(angle) * (float)outer, cy + (float)Math.Sin(angle) * (float)outer);
        }

        internal static void DrawClose(Graphics g, Rectangle r, Color color)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(color, 1.2f))
            {
                g.DrawLine(pen, r.Left + 4, r.Top + 4, r.Right - 5, r.Bottom - 5);
                g.DrawLine(pen, r.Right - 5, r.Top + 4, r.Left + 4, r.Bottom - 5);
            }
        }

        internal static void DrawFile(Graphics g, Rectangle r, Color color)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(color, 1.2F))
            {
                int fold = Math.Max(3, r.Width / 3);
                Point[] outline =
                {
                    new Point(r.Left + 1, r.Top),
                    new Point(r.Right - fold, r.Top),
                    new Point(r.Right, r.Top + fold),
                    new Point(r.Right, r.Bottom),
                    new Point(r.Left + 1, r.Bottom),
                    new Point(r.Left + 1, r.Top)
                };
                g.DrawLines(pen, outline);
                g.DrawLine(pen, r.Right - fold, r.Top, r.Right - fold, r.Top + fold);
                g.DrawLine(pen, r.Right - fold, r.Top + fold, r.Right, r.Top + fold);
            }
        }

        internal static Color FileColor(string extension)
        {
            switch ((extension ?? String.Empty).ToLowerInvariant())
            {
                case ".cs": return Color.FromArgb(86, 156, 214);
                case ".go": return Color.FromArgb(78, 201, 176);
                case ".json": return Color.FromArgb(220, 220, 170);
                case ".md": return Color.FromArgb(117, 190, 255);
                case ".png":
                case ".jpg":
                case ".ico": return Color.FromArgb(197, 134, 192);
                case ".cmd":
                case ".ps1": return Color.FromArgb(106, 153, 85);
                default: return VSCodeColors.TextMuted;
            }
        }
    }
}
