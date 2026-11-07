using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using System.Diagnostics;

namespace CapriDesktop
{
    // Frame-by-frame port of the old Capri fullscreen entrance. It runs above the existing C# companion,
    // then contracts onto her normal landing point; the Desktop character and its motion remain untouched.
    internal sealed class LegacyIntro : Form
    {
        private const double Frame = 0.033, Fast = 0.025, Blend = 0.4, FadeIn = 0.12, FadeOut = 0.28;
        private const int ShrinkFrom = 24;
        private const float EndX = 530f / 640f, EndY = 370f / 480f, EndHeight = 170f / 480f;
        private readonly List<Bitmap> frames = new List<Bitmap>();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly Stopwatch clock = new Stopwatch();
        private readonly Action finished;
        private readonly Rectangle screen;
        private readonly Point targetCenter;
        private readonly int characterSize;
        private readonly float fullScale;
        private readonly PointF endFull;
        private double last;
        private bool ended;

        internal LegacyIntro(Rectangle screen, Point targetCenter, int characterSize, Action finished)
        {
            this.screen = screen;
            this.targetCenter = targetCenter;
            this.characterSize = characterSize;
            this.finished = finished;
            for (int i = 1; i <= 43; i++)
            {
                string name = "Capri.Intro." + i.ToString() + ".png";
                Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
                if (stream == null) continue;
                using (stream)
                using (Bitmap loaded = new Bitmap(stream)) frames.Add(new Bitmap(loaded));
            }
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Bounds = screen;
            fullScale = Math.Max(screen.Width / 640f, screen.Height / 480f);
            float fullWidth = 640 * fullScale, fullHeight = 480 * fullScale;
            endFull = new PointF((screen.Width - fullWidth) / 2 + EndX * fullWidth, (screen.Height - fullHeight) / 2 + EndY * fullHeight);
            timer.Interval = 16;
            timer.Tick += TickIntro;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams value = base.CreateParams;
                value.ExStyle |= 0x00080000 | 0x00000080 | 0x08000000 | 0x00000020;
                return value;
            }
        }

        internal void Start()
        {
            if (frames.Count == 0) { Finish(); return; }
            clock.Start();
            last = clock.Elapsed.TotalSeconds;
            Show();
            BringToFront();
            timer.Start();
        }

        private double StartOf(int index) { return Math.Min(index, ShrinkFrom) * Frame + Math.Max(0, index - ShrinkFrom) * Fast; }
        private double Length { get { return StartOf(frames.Count); } }
        private static double Smooth(double value) { value = Math.Max(0, Math.Min(1, value)); return value * value * (3 - 2 * value); }

        private void TickIntro(object sender, EventArgs e)
        {
            double now = clock.Elapsed.TotalSeconds;
            if (now >= Length + FadeOut) { Finish(); return; }
            if (now - last > 0.12) last = now - 0.05;
            Invalidate();
        }

        private void FrameAt(double time, out int index, out double mix)
        {
            index = 0;
            for (int i = frames.Count - 1; i >= 0; i--) if (time >= StartOf(i)) { index = i; break; }
            if (index >= frames.Count - 1) { mix = 0; return; }
            double duration = StartOf(index + 1) - StartOf(index);
            mix = Smooth(((time - StartOf(index)) / duration - (1 - Blend)) / Blend);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (frames.Count == 0) return;
            double time = clock.Elapsed.TotalSeconds;
            int index;
            double mix;
            FrameAt(Math.Min(time, Length), out index, out mix);
            double shrinkStart = StartOf(ShrinkFrom);
            double z = time < Length ? Smooth((time - shrinkStart) / Math.Max(0.001, Length - shrinkStart)) : 1;
            double endScale = 0.8 * characterSize / (EndHeight * 480);
            double scale = fullScale * Math.Pow(endScale / fullScale, z);
            float alpha = (float)Smooth(time / FadeIn);
            if (time > Length) { double fade = Smooth((time - Length) / FadeOut); scale *= 1 - 0.55 * fade; alpha *= (float)(1 - fade); }
            PointF end = new PointF(endFull.X + (targetCenter.X - screen.Left - endFull.X) * (float)z,
                                    endFull.Y + (targetCenter.Y - screen.Top - endFull.Y) * (float)z);
            float width = 640 * (float)scale, height = 480 * (float)scale;
            RectangleF target = new RectangleF(end.X - EndX * width, end.Y - EndY * height, width, height);
            e.Graphics.InterpolationMode = z > 0 ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
            Draw(e.Graphics, frames[index], target, alpha * (float)(1 - mix));
            if (mix > 0 && index + 1 < frames.Count) Draw(e.Graphics, frames[index + 1], target, alpha * (float)mix);
        }

        private static void Draw(Graphics graphics, Bitmap image, RectangleF target, float alpha)
        {
            if (alpha <= 0) return;
            using (ImageAttributes attributes = new ImageAttributes())
            {
                ColorMatrix matrix = new ColorMatrix();
                matrix.Matrix00 = matrix.Matrix11 = matrix.Matrix22 = matrix.Matrix44 = 1;
                matrix.Matrix33 = Math.Max(0, Math.Min(1, alpha));
                attributes.SetColorMatrix(matrix);
                graphics.DrawImage(image, Rectangle.Round(target), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
            }
        }

        private void Finish()
        {
            if (ended) return;
            ended = true;
            timer.Stop();
            Hide();
            Action after = finished;
            Close();
            if (after != null) after();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Dispose();
            foreach (Bitmap frame in frames) frame.Dispose();
            base.OnFormClosed(e);
        }
    }
}
