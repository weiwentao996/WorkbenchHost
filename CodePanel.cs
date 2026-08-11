using System;
using System.Drawing;
using System.Windows.Forms;

namespace WorkbenchHost
{
    internal sealed class CodePanel : Panel
    {
        internal RichTextBox SourceEditor { get; set; }
        internal Font CodeFont { get; set; }

        internal CodePanel()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(31, 31, 31);
            ResizeRedraw = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (SourceEditor == null || CodeFont == null) return;

            e.Graphics.Clear(BackColor);
            string[] lines = SourceEditor.Lines;
            float lineHeight = CodeFont.GetHeight(e.Graphics) + 3;
            int visibleLines = (int)Math.Ceiling(ClientSize.Height / lineHeight);
            using (Brush brush = new SolidBrush(Color.FromArgb(204, 204, 204)))
            {
                for (int i = 0; i < Math.Min(lines.Length, visibleLines); i++)
                {
                    e.Graphics.DrawString(lines[i], CodeFont, brush, 12, 8 + i * lineHeight);
                }
            }
        }
    }
}
