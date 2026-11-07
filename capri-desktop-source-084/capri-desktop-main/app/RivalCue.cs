using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed class RivalCue : Form
    {
        private readonly Bitmap look;
        private readonly string rival, line;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly double born = Environment.TickCount / 1000.0;

        internal RivalCue(string rival, string line, Point near)
        {
            this.rival = rival ?? "outra IA";
            this.line = line ?? "Ei, eu também estou aqui.";
            Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("Capri.Cutin.cutin-look.png");
            if (source != null) using (source) using (Bitmap loaded = new Bitmap(source)) look = new Bitmap(loaded);
            ClientSize = new Size(360, 124);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Rectangle area = Screen.FromPoint(near).WorkingArea;
            int x = near.X + 30, y = near.Y - Height - 14;
            if (x + Width > area.Right) x = near.X - Width - 24;
            Location = new Point(Math.Max(area.Left, Math.Min(area.Right - Width, x)), Math.Max(area.Top, Math.Min(area.Bottom - Height, y)));
            timer.Interval = 80;
            timer.Tick += delegate
            {
                double age = Environment.TickCount / 1000.0 - born;
                if (age >= 4.5) { timer.Stop(); Close(); return; }
                Opacity = age < 3.7 ? 1 : Math.Max(0, (4.5 - age) / 0.8);
            };
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { CreateParams value = base.CreateParams; value.ExStyle |= 0x00080000 | 0x00000080 | 0x08000000 | 0x00000020; return value; }
        }

        internal void ShowCue()
        {
            Show();
            timer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Rounded(new Rectangle(1, 1, Width - 3, Height - 3), 12))
            using (SolidBrush plate = new SolidBrush(Color.FromArgb(242, 14, 14, 30)))
            using (Pen edge = new Pen(Color.FromArgb(240, 255, 42, 112), 2))
            {
                g.FillPath(plate, path);
                g.DrawPath(edge, path);
            }
            if (look != null) g.DrawImage(look, new Rectangle(10, 10, 148, 100));
            using (Font header = Neon.Display(13f))
            using (Font body = Neon.Type(10f))
            using (SolidBrush pink = new SolidBrush(Color.FromArgb(255, 88, 145)))
            using (SolidBrush white = new SolidBrush(Color.FromArgb(236, 239, 255)))
            {
                g.DrawString("COM CIÚMES · " + rival.ToUpperInvariant(), header, pink, new PointF(169, 11));
                Rectangle text = new Rectangle(170, 39, 178, 75);
                TextRenderer.DrawText(g, line, body, text, white.Color, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            }
        }

        private static GraphicsPath Rounded(Rectangle rect, int radius)
        {
            int d = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop();
            timer.Dispose();
            if (look != null) look.Dispose();
            base.OnFormClosed(e);
        }
    }
}
