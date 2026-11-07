using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Capri's old, small desktop form. It is a separate transparent window so the full-size Desktop companion
    // keeps all of its existing movement and animation state while she is small.
    internal sealed class MiniAvatar : Form
    {
        private readonly Dictionary<string, Bitmap> art = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private readonly Timer timer = new Timer();
        private readonly Random random = new Random();
        private readonly Action<Point> returnFull;
        private readonly Action openMainframe;
        private readonly Action toggleWander;
        private readonly Func<bool> wanderEnabled;
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly ToolStripMenuItem wanderItem;
        private Ledge seat;
        private int seatOffset;
        private Point pressAt, windowAt, lastClick;
        private bool pressed, dragged, laying, lookingLeft, lookingRight;
        private int clicks;
        private double lastClickAt, blinkAt, blinkUntil, talkUntil, annoyedUntil, glitchUntil, nextGlitchAt, nextSeatCheck;
        private long began = Environment.TickCount;

        internal MiniAvatar(int companionSize, Point floor, Action<Point> returnFull, Action openMainframe, Action toggleWander, Func<bool> wanderEnabled)
        {
            this.returnFull = returnFull;
            this.openMainframe = openMainframe;
            this.toggleWander = toggleWander;
            this.wanderEnabled = wanderEnabled;
            int side = Math.Max(48, Math.Min(220, (int)Math.Round(96 * companionSize / 240.0)));
            ClientSize = new Size(side, side);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Text = "Capri mini";
            AccessibleName = "Capri mini companion";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;

            foreach (string name in new[] { "idle", "blink", "talk", "talk2", "annoyed", "annoyed_talk", "look_left", "look_right", "glitch" })
            {
                Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Capri.Mini." + name + ".png");
                if (stream == null) continue;
                using (stream)
                using (Bitmap loaded = new Bitmap(stream)) art[name] = new Bitmap(loaded);
            }
            if (!art.ContainsKey("idle")) throw new FileNotFoundException("Capri's mini sprites are missing.");

            menu.Renderer = new Mainframe.MenuLook();
            menu.ForeColor = Mainframe.MenuLook.Ink;
            menu.BackColor = Mainframe.MenuLook.Panel;
            menu.Font = Neon.Type(10f);
            menu.ShowImageMargin = false;
            menu.Items.Add(new ToolStripMenuItem("Abrir painel da Capri", null, delegate { this.openMainframe(); }));
            wanderItem = new ToolStripMenuItem("Passear pela tela", null, delegate { this.toggleWander(); });
            wanderItem.Checked = this.wanderEnabled();
            menu.Items.Add(wanderItem);
            menu.Items.Add(new ToolStripMenuItem("Voltar ao tamanho normal", null, delegate { GoFull(); }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Sair da Capri", null, delegate { Application.Exit(); }));
            menu.Opening += delegate { wanderItem.Checked = this.wanderEnabled(); };
            menu.Closed += delegate { Computer.MenuOpen = false; };
            menu.Opened += delegate { Computer.MenuOpen = true; };
            Location = new Point(floor.X - side / 2, floor.Y - (int)(side * 0.92));
            blinkAt = Now + 2 + random.NextDouble() * 3;
            timer.Interval = 80;
            timer.Tick += delegate { TickMini(); };
            timer.Start();
        }

        private double Now { get { return (Environment.TickCount - began) / 1000.0; } }

        private void TickMini()
        {
            double now = Now;
            Point cursor = Cursor.Position;
            int dx = cursor.X - (Left + Width / 2);
            lookingLeft = Math.Abs(dx) >= Width * 1.2 && dx < 0;
            lookingRight = Math.Abs(dx) >= Width * 1.2 && dx > 0;
            if (now >= blinkAt)
            {
                blinkUntil = now + 0.14;
                blinkAt = now + (random.NextDouble() < 0.15 ? 0.4 : 2.5 + random.NextDouble() * 3.5);
            }
            if (now >= nextGlitchAt)
            {
                nextGlitchAt = now + 1.5;
                if (random.NextDouble() < 0.1) glitchUntil = now + 0.12;
            }
            if (!pressed && now >= nextSeatCheck)
            {
                nextSeatCheck = now + 0.8;
                SettleOnNearbyEdge();
            }
            Invalidate();
        }

        private void SettleOnNearbyEdge()
        {
            if (seat != null && seat.Window != IntPtr.Zero && FollowWindowSeat()) return;
            List<IntPtr> skip = new List<IntPtr>();
            if (IsHandleCreated) skip.Add(Handle);
            try
            {
                Point floor = new Point(Left + Width / 2, Top + (int)(Height * 0.92));
                Ledge ledge = Ledges.Below(Ledges.Find(skip, new Rectangle[0]), floor, 30, 220, 36, Math.Max(48, Height / 2));
                if (ledge == null) { seat = null; laying = false; return; }
                int x = Math.Max(ledge.Left + Width / 2, Math.Min(floor.X, ledge.Right - Width / 2));
                Location = new Point(x - Width / 2, ledge.Y - (int)(Height * 0.92));
                seat = ledge.Window != IntPtr.Zero || ledge.Floor ? ledge : null;
                seatOffset = ledge.Window != IntPtr.Zero ? x - ledge.Bounds.Left : 0;
                laying = true;
            }
            catch (Exception) { laying = false; }
        }

        private bool FollowWindowSeat()
        {
            Rectangle bounds;
            if (!Ledges.Showing(seat.Window, out bounds) || !Ledges.Seatable(seat.Window, bounds))
            {
                DropToTaskbar();
                return true;
            }
            int x = Math.Max(bounds.Left + Width / 2, Math.Min(bounds.Right - Width / 2, bounds.Left + seatOffset));
            Ledge moved = new Ledge { Window = seat.Window, Bounds = bounds, Left = bounds.Left, Right = bounds.Right, Y = bounds.Top };
            List<IntPtr> skip = new List<IntPtr>();
            if (IsHandleCreated) skip.Add(Handle);
            if (Ledges.Covered(moved, x, skip))
            {
                DropToTaskbar();
                return true;
            }
            seat.Bounds = bounds;
            seat.Left = bounds.Left;
            seat.Right = bounds.Right;
            seat.Y = bounds.Top;
            Location = new Point(x - Width / 2, bounds.Top - (int)(Height * 0.92));
            laying = true;
            return true;
        }

        private void DropToTaskbar()
        {
            Screen screen = Screen.FromPoint(new Point(Left + Width / 2, Top + Height / 2));
            Rectangle area = screen.WorkingArea;
            int x = Math.Max(area.Left + Width / 2, Math.Min(Left + Width / 2, area.Right - Width / 2));
            Location = new Point(x - Width / 2, area.Bottom - (int)(Height * 0.92));
            seat = null;
            laying = true;
        }

        private Bitmap Picture(string name)
        {
            Bitmap result;
            if (art.TryGetValue(name, out result)) return result;
            if (name == "talk2" || name == "annoyed_talk") return Picture("talk");
            if (name == "look_left" || name == "look_right" || name == "blink" || name == "glitch") return Picture("idle");
            return Picture("idle");
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            double now = Now;
            string state;
            if (now < glitchUntil && art.ContainsKey("glitch")) state = "glitch";
            else if (now < annoyedUntil) state = now < talkUntil ? "annoyed_talk" : "annoyed";
            else if (now < talkUntil) state = ((int)(now * 7) % 3 == 1 && art.ContainsKey("talk2")) ? "talk2" : "talk";
            else if (now < blinkUntil) state = "blink";
            else if (lookingLeft) state = "look_left";
            else if (lookingRight) state = "look_right";
            else state = "idle";

            Bitmap picture = Picture(state);
            float unit = Width / 100f;
            float bob = laying ? 0 : (float)Math.Sin(now * 1.9) * 2.5f * unit;
            float breathe = (float)Math.Sin(now * 1.9 + 0.6);
            float size = 84 * unit;
            float w = size * (1 - 0.025f * breathe), h = size * (1 + 0.03f * breathe);
            RectangleF rect = new RectangleF(Width / 2f - w / 2f, Height * 0.93f - 3 * unit - h + bob, w, h);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            if (now < glitchUntil)
            {
                using (ImageAttributesOpacity cyan = new ImageAttributesOpacity(0.38f))
                    e.Graphics.DrawImage(picture, Rectangle.Round(new RectangleF(rect.X - 3 * unit, rect.Y, rect.Width, rect.Height)), 0, 0, picture.Width, picture.Height, GraphicsUnit.Pixel, cyan.Attributes);
                using (ImageAttributesOpacity pink = new ImageAttributesOpacity(0.38f))
                    e.Graphics.DrawImage(picture, Rectangle.Round(new RectangleF(rect.X + 3 * unit, rect.Y, rect.Width, rect.Height)), 0, 0, picture.Width, picture.Height, GraphicsUnit.Pixel, pink.Attributes);
            }
            e.Graphics.DrawImage(picture, rect);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right) { menu.Show(Cursor.Position); return; }
            if (e.Button != MouseButtons.Left) return;
            pressed = true;
            dragged = false;
            pressAt = Cursor.Position;
            windowAt = Location;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!pressed) return;
            Point cursor = Cursor.Position;
            int dx = cursor.X - pressAt.X, dy = cursor.Y - pressAt.Y;
            if (dx * dx + dy * dy > 16) { dragged = true; laying = false; seat = null; }
            if (dragged) Location = new Point(windowAt.X + dx, windowAt.Y + dy);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || !pressed) return;
            pressed = false;
            Capture = false;
            if (dragged) { nextSeatCheck = 0; return; }
            double now = Now;
            if (now - lastClickAt <= SystemInformation.DoubleClickTime / 1000.0 && Math.Abs(Cursor.Position.X - lastClick.X) < SystemInformation.DoubleClickSize.Width && Math.Abs(Cursor.Position.Y - lastClick.Y) < SystemInformation.DoubleClickSize.Height)
            {
                GoFull();
                return;
            }
            lastClickAt = now;
            lastClick = Cursor.Position;
            if (++clicks >= 3) { clicks = 0; annoyedUntil = now + 2.5; }
            else { talkUntil = now + 1.5; openMainframe(); }
        }

        private void GoFull()
        {
            timer.Stop();
            Point floor = new Point(Left + Width / 2, Top + (int)(Height * 0.92));
            returnFull(floor);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop();
            timer.Dispose();
            menu.Dispose();
            foreach (Bitmap picture in art.Values) picture.Dispose();
            base.OnFormClosed(e);
        }

        private sealed class ImageAttributesOpacity : IDisposable
        {
            internal readonly System.Drawing.Imaging.ImageAttributes Attributes = new System.Drawing.Imaging.ImageAttributes();
            internal ImageAttributesOpacity(float alpha)
            {
                System.Drawing.Imaging.ColorMatrix matrix = new System.Drawing.Imaging.ColorMatrix();
                matrix.Matrix00 = matrix.Matrix11 = matrix.Matrix22 = matrix.Matrix33 = matrix.Matrix44 = 1;
                matrix.Matrix33 = alpha;
                Attributes.SetColorMatrix(matrix);
            }
            public void Dispose() { Attributes.Dispose(); }
        }
    }
}
