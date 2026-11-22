using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Her task manager, opened from her right-click menu: what she is using and what the PC is using, side by side, so
    // "é o código ou o PC?" has a straight answer. It only reads the machine, the way Windows does, and changes nothing.
    internal sealed class SysPanel : Form
    {
        // Her own side, which only the companion knows: mods, how fast her voice is. Filled in by Capri.
        internal static Func<List<KeyValuePair<string, string>>> Notes;
        // Where her folder is, for errors.txt.
        internal static string DataFolder;

        private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");
        private static SysPanel open;

        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly bool live;
        private PcUse pc;
        private long workSet, peakSet;
        private double herShare, pcCpu, pcMem;
        private int threads, errors;
        private TimeSpan awake;
        private TimeSpan lastCpu;
        private DateTime lastAt;
        private string verdict = "Lendo o sistema...";
        private string blame = "";
        private bool sampling, copied, copiedAsk;
        private Rectangle copyBox, copyAskBox, closeBox;
        private Point dragFrom, windowFrom;
        private bool dragging;
        private readonly List<string> recentShort = new List<string>();
        private readonly List<string> recentFull = new List<string>();
        private sealed class ProposalHit { internal Rectangle Box; internal string Id; internal string Action; }
        private readonly List<IntelProposal> pendingIdeas = new List<IntelProposal>();
        private readonly List<IntelProposal> doneIdeas = new List<IntelProposal>();
        private readonly List<ProposalHit> proposalHits = new List<ProposalHit>();
        private bool intelBusy;
        private string intelStatus = "";
        private Rectangle allIntelBox;

        internal SysPanel(bool live)
        {
            this.live = live;
            Text = "Capri sistema";
            AccessibleName = "Capri system panel";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            KeyPreview = true;
            BackColor = Mainframe.MenuLook.Panel;
            Font = Neon.Type(10f);
            AutoScaleMode = AutoScaleMode.None;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            float s = DeviceDpi / 96f;
            ClientSize = new Size((int)(430 * s), (int)(900 * s));
            timer.Interval = 2000;
            timer.Tick += delegate { Sample(); };
            if (live)
            {
                timer.Start();
                Sample();
            }
            KeyDown += delegate(object sender, KeyEventArgs key) { if (key.KeyCode == Keys.Escape) Close(); };
            MouseDown += delegate(object sender, MouseEventArgs click)
            {
                if (click.Button != MouseButtons.Left) return;
                if (copyBox.Contains(click.Location)) { Copy(false); return; }
                if (copyAskBox.Contains(click.Location)) { Copy(true); return; }
                if (allIntelBox.Contains(click.Location)) { ShowAllIntel(); return; }
                if (!intelBusy)
                {
                    for (int i = 0; i < proposalHits.Count; i++)
                    {
                        ProposalHit hit = proposalHits[i];
                        if (hit != null && hit.Box.Contains(click.Location)) { RunIntelAction(hit.Id, hit.Action); return; }
                    }
                }
                if (closeBox.Contains(click.Location)) { Close(); return; }
                dragging = true;
                dragFrom = Cursor.Position;
                windowFrom = Location;
            };
            MouseMove += delegate(object sender, MouseEventArgs move)
            {
                if (!dragging) return;
                Point at = Cursor.Position;
                Location = new Point(windowFrom.X + at.X - dragFrom.X, windowFrom.Y + at.Y - dragFrom.Y);
            };
            MouseUp += delegate { dragging = false; };
            SizeChanged += delegate { Neon.Clip(this, 14 * s, 5 * s); };
            FormClosed += delegate { if (open == this) open = null; };
        }

        // The real screen scaling is only known once the window exists, so the panel is sized again there.
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            float s = DeviceDpi / 96f;
            Size want = new Size((int)(430 * s), (int)(900 * s));
            if (ClientSize != want) ClientSize = want;
            Neon.Clip(this, 14 * s, 5 * s);
        }

        // One panel at a time: opening it again puts it away, the way a toggle should.
        internal static void Popup(IWin32Window owner)
        {
            if (open != null && !open.IsDisposed)
            {
                open.Close();
                return;
            }
            open = new SysPanel(true);
            Point at = Cursor.Position;
            try
            {
                Screen screen = Screen.FromPoint(at);
                Rectangle area = screen.WorkingArea;
                at = new Point(at.X - open.Width / 2, at.Y - open.Height - 12);
                at.X = Math.Max(area.Left + 8, Math.Min(area.Right - open.Width - 8, at.X));
                at.Y = Math.Max(area.Top + 8, Math.Min(area.Bottom - open.Height - 8, at.Y));
            }
            catch (Exception) { }
            open.Location = at;
            // The companion may be hidden while they use the tray, and a window owned by a hidden window would never appear.
            Form form = owner as Form;
            if (form != null && form.Visible && form.WindowState != FormWindowState.Minimized) open.Show(form);
            else open.Show();
        }

        internal static void CloseOpen()
        {
            if (open != null && !open.IsDisposed) open.Close();
            open = null;
        }

        // Reads everything once, off the UI thread, because the PC sample spends half a second counting.
        private void Sample()
        {
            if (sampling) return;
            sampling = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                PcUse read = null;
                try { read = Usage.Read(); } catch (Exception) { }
                long mem = 0, peak = 0;
                int thr = 0;
                TimeSpan up = TimeSpan.Zero;
                double share = 0;
                try
                {
                    using (Process me = Process.GetCurrentProcess())
                    {
                        mem = me.WorkingSet64;
                        peak = me.PeakWorkingSet64;
                        thr = me.Threads.Count;
                        try { up = DateTime.Now - me.StartTime; } catch (Exception) { up = TimeSpan.FromMilliseconds(Environment.TickCount); }
                        TimeSpan now = me.TotalProcessorTime;
                        DateTime at = DateTime.Now;
                        if (lastAt != DateTime.MinValue)
                        {
                            double spent = Math.Max(0, (now - lastCpu).TotalMilliseconds);
                            double wall = Math.Max(1, (at - lastAt).TotalMilliseconds) * Environment.ProcessorCount;
                            share = Math.Max(0, Math.Min(100, spent / wall * 100));
                        }
                        lastCpu = now;
                        lastAt = at;
                    }
                }
                catch (Exception) { }
                List<KeyValuePair<string, string>> rows = null;
                try { if (Notes != null) rows = Notes(); } catch (Exception) { }
                List<string> shortErr = new List<string>(), fullErr = new List<string>();
                int bad = CollectErrors(shortErr, fullErr);
                IntelProposal[] all = null;
                try { all = SystemIntel.ListProposals(); } catch (Exception) { }
                List<IntelProposal> pend = new List<IntelProposal>(), done = new List<IntelProposal>();
                if (all != null) foreach (IntelProposal p in all) { if (p != null && p.State == "proposed") pend.Add(p); else if (p != null) done.Add(p); }
                if (pend.Count > 3) pend.RemoveRange(3, pend.Count - 3);
                if (done.Count > 3) done.RemoveRange(3, done.Count - 3);
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        Apply(read, mem, peak, thr, up, share, rows, bad, shortErr, fullErr, pend, done);
                        sampling = false;
                        Invalidate();
                    });
                }
                catch (Exception) { sampling = false; }
            });
        }

        // The same read the panel's first frame does, without waiting for the timer: her checks call this.
        internal void SampleNow()
        {
            PcUse read = null;
            try { read = Usage.Read(); } catch (Exception) { }
            long mem = 0, peak = 0;
            int thr = 0;
            TimeSpan up = TimeSpan.Zero;
            try
            {
                using (Process me = Process.GetCurrentProcess())
                {
                    mem = me.WorkingSet64;
                    peak = me.PeakWorkingSet64;
                    thr = me.Threads.Count;
                    try { up = DateTime.Now - me.StartTime; } catch (Exception) { up = TimeSpan.Zero; }
                }
            }
            catch (Exception) { }
            List<KeyValuePair<string, string>> rows = null;
            try { if (Notes != null) rows = Notes(); } catch (Exception) { }
            List<string> shortErr = new List<string>(), fullErr = new List<string>();
            int bad = CollectErrors(shortErr, fullErr);
            IntelProposal[] all = null;
            try { all = SystemIntel.ListProposals(); } catch (Exception) { }
            List<IntelProposal> pend = new List<IntelProposal>(), done = new List<IntelProposal>();
            if (all != null) foreach (IntelProposal p in all) { if (p != null && p.State == "proposed") pend.Add(p); else if (p != null) done.Add(p); }
            if (pend.Count > 3) pend.RemoveRange(3, pend.Count - 3);
            if (done.Count > 3) done.RemoveRange(3, done.Count - 3);
            Apply(read, mem, peak, thr, up, 0, rows, bad, shortErr, fullErr, pend, done);
        }

        private void Apply(PcUse read, long mem, long peak, int thr, TimeSpan up, double share,
            List<KeyValuePair<string, string>> rows, int bad, List<string> shortErr, List<string> fullErr,
            List<IntelProposal> pend, List<IntelProposal> done)
        {
            pc = read;
            workSet = mem;
            peakSet = peak;
            threads = thr;
            awake = up;
            herShare = share;
            errors = bad;
            hers = rows ?? new List<KeyValuePair<string, string>>();
            recentShort.Clear();
            recentFull.Clear();
            if (shortErr != null) recentShort.AddRange(shortErr);
            if (fullErr != null) recentFull.AddRange(fullErr);
            pendingIdeas.Clear();
            doneIdeas.Clear();
            if (pend != null) pendingIdeas.AddRange(pend);
            if (done != null) doneIdeas.AddRange(done);
            pcCpu = pc == null ? 0 : pc.Cpu;
            pcMem = pc == null || pc.MemoryTotal <= 0 ? 0 : 100.0 * pc.MemoryUsed / pc.MemoryTotal;
            ReadVerdict();
        }

        private List<KeyValuePair<string, string>> hers = new List<KeyValuePair<string, string>>();

        // "É o código ou o PC?", said in one line, with the numbers that led there.
        private void ReadVerdict()
        {
            if (pc == null)
            {
                blame = "";
                verdict = "Ainda não consegui ler o sistema.";
                return;
            }
            double freeGb = pc.MemoryTotal <= 0 ? 99 : (pc.MemoryTotal - pc.MemoryUsed) / 1073741824.0;
            bool pcHeavy = pcCpu >= 85 || freeGb < 1.2;
            bool herHeavy = herShare >= 25 || workSet >= 1200L * 1024 * 1024;
            if (pcHeavy && !herHeavy)
            {
                blame = "PC";
                verdict = "O PC está no limite agora: " + (int)Math.Round(pcCpu) + "% de processador e "
                    + Usage.Size(Math.Max(0, pc.MemoryTotal - pc.MemoryUsed)) + " de memória livre. A Capri usa só "
                    + (int)Math.Round(herShare) + "%. Então é o PC, não ela.";
            }
            else if (herHeavy)
            {
                blame = "CAPRI";
                verdict = "A Capri está pesando: " + Size(workSet) + " de memória e " + (int)Math.Round(herShare)
                    + "% do processador. Mods abertos ou uma conversa longa costumam ser a causa, não o PC ("
                    + (int)Math.Round(pcCpu) + "%).";
            }
            else
            {
                blame = "OK";
                verdict = "Tudo tranquilo: a Capri usa " + Size(workSet) + " e " + (int)Math.Round(herShare)
                    + "% do processador, e o PC está em " + (int)Math.Round(pcCpu) + "%. Se algo travar, não é falta de máquina.";
            }
            if (errors > 0)
                verdict += " " + errors + (errors == 1 ? " erro leve" : " erros leves") + " nas últimas 24 h.";
        }

        // Only what likely needs attention in her code or behaviour: normal timing, routine device notes, voice interruptions,
        // external/intentional closes, slow-but-successful voice and configuration/model messages are not faults to fix in code.
        // Fills recent lists and returns the total in 24 h.
        private int CollectErrors(List<string> shortOut, List<string> fullOut)
        {
            try
            {
                if (shortOut == null) shortOut = new List<string>();
                if (fullOut == null) fullOut = new List<string>();
                if (string.IsNullOrEmpty(DataFolder)) return 0;
                string log = Path.Combine(DataFolder, "errors.txt");
                if (!File.Exists(log)) return 0;
                string[] lines = File.ReadAllLines(log);
                DateTime since = DateTime.Now.AddHours(-24);
                int bad = 0;
                for (int i = Math.Max(0, lines.Length - 4000); i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (line.Length < 20 || line.IndexOf("voice delivery", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (line.IndexOf("WaveIn opened", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (line.IndexOf("| voice interruption |", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (line.IndexOf("| closed |", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (line.IndexOf("Pick another model in Capri Mainframe", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (line.IndexOf("escolha um modelo que consiga", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (line.IndexOf("HTTP 404 model", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (line.IndexOf("Não encontrado para a conta", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if ((line.IndexOf("| free voice |", StringComparison.OrdinalIgnoreCase) >= 0
                        || line.IndexOf("| HD voice |", StringComparison.OrdinalIgnoreCase) >= 0
                        || line.IndexOf("| ElevenLabs voice |", StringComparison.OrdinalIgnoreCase) >= 0)
                        && line.IndexOf(" slow,", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (line.IndexOf("| voice cut |", StringComparison.OrdinalIgnoreCase) >= 0
                        && line.IndexOf("OnFormClosed", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    DateTime when;
                    if (!DateTime.TryParseExact(line.Substring(0, 19), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out when)) continue;
                    if (when < since) continue;
                    bad++;
                    string text = line.Trim();
                    string area = "", msg = text;
                    string[] parts = text.Split(new[] { " | " }, StringSplitOptions.None);
                    if (parts.Length >= 4) { area = parts[1].Trim(); msg = parts[parts.Length - 1].Trim(); }
                    else if (parts.Length == 3) { area = parts[1].Trim(); msg = parts[2].Trim(); }
                    if (msg.Length == 0) msg = text;
                    string brief = (area.Length > 0 ? area + ": " : "") + msg;
                    if (brief.Length > 90) brief = brief.Substring(0, 87) + "...";
                    shortOut.Add("[" + when.ToString("HH:mm") + "] " + brief);
                    fullOut.Add(text);
                }
                int keep = 5;
                if (shortOut.Count > keep)
                {
                    shortOut.RemoveRange(0, shortOut.Count - keep);
                    fullOut.RemoveRange(0, fullOut.Count - keep);
                }
                return bad;
            }
            catch (Exception) { return 0; }
        }

        private void Copy(bool askOnly)
        {
            try
            {
                string text = askOnly ? FixRequest() : Report();
                Clipboard.SetText(text);
                if (askOnly) copiedAsk = true; else copied = true;
                Invalidate();
                System.Windows.Forms.Timer back = new System.Windows.Forms.Timer();
                back.Interval = 1500;
                back.Tick += delegate { back.Stop(); back.Dispose(); copied = false; copiedAsk = false; if (!IsDisposed) Invalidate(); };
                back.Start();
            }
            catch (Exception) { }
        }

        private void RunIntelAction(string id, string action)
        {
            if (intelBusy || string.IsNullOrEmpty(id)) return;
            intelBusy = true;
            intelStatus = action == "revert" ? "Desfazendo..." : "Aplicando e testando...";
            Invalidate();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string result = "";
                try
                {
                    if (action == "revert") result = SystemIntel.RevertFromUser(id);
                    else result = SystemIntel.ApplyFromUser(id);
                }
                catch (Exception e) { result = "Não deu: " + e.Message; }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        intelBusy = false;
                        intelStatus = ShortStatus(result);
                        Sample();
                        Invalidate();
                    });
                }
                catch (Exception) { }
            });
        }

        private static string ShortStatus(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace("\r", " ").Replace("\n", " ").Trim();
            if (text.Length > 90) text = text.Substring(0, 87) + "...";
            return text;
        }

        // A ready-to-paste request for Capri: the errors in plain text and a clear ask to fix them with her own tools.
        internal string FixRequest()
        {
            StringBuilder said = new StringBuilder();
            said.Append("Olha estes erros do seu sistema e me ajuda a corrigir. ");
            said.Append("Explique a causa em linguagem simples, proponha uma mudança pequena e segura, e peça minha autorização antes de aplicar.\n");
            if (recentFull.Count == 0)
            {
                said.Append("\nNão apareceu nenhum erro nas últimas 24 h. Mesmo assim, veja se tem algo lento ou estranho para melhorar.");
            }
            else
            {
                said.Append("\nErros recentes (").Append(errors).Append(" nas últimas 24 h, mostrando os mais recentes):\n");
                foreach (string line in recentFull) said.Append("- ").Append(line).Append('\n');
            }
            said.Append("\nLeitura: ").Append(verdict).Append('\n');
            return said.ToString();
        }

        private static string Size(long bytes)
        {
            double mb = bytes / 1048576.0;
            if (mb >= 1024) return (mb / 1024).ToString("0.0", Br) + " GB";
            return Math.Round(mb).ToString("0", Br) + " MB";
        }

        private static string Span(TimeSpan span)
        {
            if (span.TotalMinutes < 1) return "menos de 1 min";
            if (span.TotalHours < 1) return ((int)span.TotalMinutes) + " min";
            return ((int)span.TotalHours) + " h " + span.Minutes.ToString("00", Br) + " min";
        }

        // The whole panel as plain text, which is also what Copiar puts on the clipboard and what the checks read.
        internal string Report()
        {
            string version = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            StringBuilder said = new StringBuilder();
            said.Append("CAPRI ").Append(version).Append(" — SISTEMA\n");
            said.Append("ELA AGORA\n");
            said.Append("  Memória: ").Append(Size(workSet)).Append(" (pico ").Append(Size(peakSet)).Append(")\n");
            said.Append("  Processador: ").Append((int)Math.Round(herShare)).Append("% do PC\n");
            said.Append("  Acordada: ").Append(Span(awake)).Append('\n');
            said.Append("  Linhas de execução: ").Append(threads).Append('\n');
            foreach (KeyValuePair<string, string> row in hers) said.Append("  ").Append(row.Key).Append(": ").Append(row.Value).Append('\n');
            said.Append("  Erros (24 h): ").Append(errors).Append('\n');
            said.Append("SEU PC\n");
            if (pc == null) said.Append("  Ainda lendo...\n");
            else
            {
                said.Append("  Processador: ").Append((int)Math.Round(pcCpu)).Append("%\n");
                said.Append("  Memória: ").Append(Usage.Size(pc.MemoryUsed)).Append(" de ").Append(Usage.Size(pc.MemoryTotal))
                    .Append(" (").Append((int)Math.Round(pcMem)).Append("%)\n");
                foreach (string drive in pc.Drives) said.Append("  Disco: ").Append(drive).Append('\n');
                if (!string.IsNullOrEmpty(pc.Battery)) said.Append("  Bateria: ").Append(pc.Battery).Append('\n');
            }
            List<AppUse> heavy = Heaviest();
            if (heavy.Count > 0)
            {
                said.Append("MAIS PESADO AGORA\n");
                foreach (AppUse app in heavy)
                    said.Append("  ").Append(app.App).Append(": ").Append((int)Math.Round(app.Cpu))
                        .Append("% de processador, ").Append(Usage.Size(app.Memory)).Append('\n');
            }
            if (recentFull.Count > 0)
            {
                said.Append("ERROS RECENTES (24 h)\n");
                foreach (string line in recentFull) said.Append("  - ").Append(line).Append('\n');
            }
            said.Append("LEITURA\n  ").Append(verdict).Append('\n');
            said.Append("PEDIDO PARA COLAR\n");
            if (recentFull.Count == 0)
                said.Append("  Olha como está meu sistema e me diz se tem algo para corrigir ou melhorar. Se precisar, proponha a mudança e peça minha autorização antes de aplicar.\n");
            else
            {
                said.Append("  Olha estes erros e me ajuda a corrigir. Explique a causa em linguagem simples, proponha uma mudança pequena e segura, e peça minha autorização antes de aplicar:\n");
                foreach (string line in recentFull) said.Append("  - ").Append(line).Append('\n');
            }
            return said.ToString();
        }

        // The two busiest by processor and the two heaviest in memory, together, without repeating one.
        private List<AppUse> Heaviest()
        {
            List<AppUse> picked = new List<AppUse>();
            if (pc == null) return picked;
            List<AppUse> apps = new List<AppUse>(pc.Apps);
            apps.RemoveAll(delegate(AppUse app) { return string.IsNullOrEmpty(app.App) || app.App == "Idle" || app.App == "System Idle Process"; });
            apps.Sort(delegate(AppUse one, AppUse two) { return two.Cpu.CompareTo(one.Cpu); });
            for (int i = 0; i < apps.Count && picked.Count < 2; i++) if (apps[i].Cpu >= 1) picked.Add(apps[i]);
            apps.Sort(delegate(AppUse one, AppUse two) { return two.Memory.CompareTo(one.Memory); });
            for (int i = 0; i < apps.Count && picked.Count < 3; i++) if (!picked.Contains(apps[i]) && apps[i].Memory >= 200L * 1024 * 1024) picked.Add(apps[i]);
            return picked;
        }

        internal void DrawTo(Graphics g)
        {
            float s = DeviceDpi / 96f;
            Size size = ClientSize;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Brush ground = new SolidBrush(Mainframe.MenuLook.Panel)) g.FillRectangle(ground, 0, 0, size.Width, size.Height);
            using (LinearGradientBrush wine = new LinearGradientBrush(new RectangleF(0, -1, 16, 120 * s), Neon.Glow, Mainframe.MenuLook.Panel, LinearGradientMode.Vertical))
            {
                wine.Blend = Neon.Fade;
                g.FillRectangle(wine, 0, 0, size.Width, 120 * s);
            }
            Neon.Frame(g, size, s, 14 * s, 5 * s, 40 * s, 22 * s);

            float x = 22 * s, right = size.Width - 22 * s, y = 20 * s;
            using (Font title = Neon.Display(13f))
            using (Font small = Neon.Type(8f))
            using (Font label = Neon.Type(9.5f))
            using (Font value = Neon.Type(9.5f, true))
            using (Brush ink = new SolidBrush(Mainframe.MenuLook.Ink))
            using (Brush dim = new SolidBrush(Neon.Faint))
            using (Brush rose = new SolidBrush(Neon.Rose))
            {
                g.DrawString("CAPRI  /  SISTEMA", title, ink, x, y);
                string version = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
                SizeF mark = g.MeasureString(version, small);
                g.DrawString(version, small, dim, right - mark.Width - 28 * s, y + 5 * s);
                closeBox = new Rectangle((int)(right - 18 * s), (int)(y - 2 * s), (int)(24 * s), (int)(24 * s));
                using (Pen cross = new Pen(Neon.Brim, Math.Max(1, 1.4f * s)))
                {
                    g.DrawLine(cross, closeBox.Left + 7 * s, closeBox.Top + 7 * s, closeBox.Right - 7 * s, closeBox.Bottom - 7 * s);
                    g.DrawLine(cross, closeBox.Right - 7 * s, closeBox.Top + 7 * s, closeBox.Left + 7 * s, closeBox.Bottom - 7 * s);
                }
                y += 26 * s;
                g.DrawString("O que ela e o PC estão usando agora", small, dim, x, y);
                y += 22 * s;

                y = Section(g, "ELA AGORA", x, y, s, rose, small);
                y = Meter(g, label, value, ink, dim, "Memória", Size(workSet) + "  ·  pico " + Size(peakSet),
                    pc == null || pc.MemoryTotal <= 0 ? 0 : (double)workSet / pc.MemoryTotal, x, y, right, s, 0);
                y = Meter(g, label, value, ink, dim, "Processador", (int)Math.Round(herShare) + "% do PC",
                    herShare / 100.0, x, y, right, s, 0);
                y = Row(g, label, value, ink, dim, "Acordada", Span(awake), x, y, right, s);
                y = Row(g, label, value, ink, dim, "Linhas de execução", threads.ToString(Br), x, y, right, s);
                foreach (KeyValuePair<string, string> row in hers)
                    y = Row(g, label, value, ink, dim, row.Key, row.Value, x, y, right, s);
                y = Row(g, label, value, ink, dim, "Erros (24 h)", errors.ToString(Br), x, y, right, s);
                y += 4 * s;

                y = Section(g, "SEU PC", x, y, s, rose, small);
                y = Meter(g, label, value, ink, dim, "Processador", (int)Math.Round(pcCpu) + "%", pcCpu / 100.0, x, y, right, s, pcCpu >= 85 ? 1 : 0);
                y = Meter(g, label, value, ink, dim, "Memória",
                    pc == null ? "lendo..." : Usage.Size(pc.MemoryUsed) + " de " + Usage.Size(pc.MemoryTotal), pcMem / 100.0, x, y, right, s, pcMem >= 88 ? 1 : 0);
                string disks = "—";
                if (pc != null && pc.Drives.Count > 0)
                {
                    disks = "";
                    for (int i = 0; i < pc.Drives.Count && i < 2; i++)
                    {
                        if (i > 0) disks += "  ·  ";
                        disks += pc.Drives[i];
                    }
                }
                y = Row(g, label, value, ink, dim, "Disco", disks, x, y, right, s);
                if (pc != null && !string.IsNullOrEmpty(pc.Battery))
                    y = Row(g, label, value, ink, dim, "Bateria", pc.Battery, x, y, right, s);
                y += 4 * s;

                List<AppUse> heavy = Heaviest();
                if (heavy.Count > 0)
                {
                    y = Section(g, "MAIS PESADO AGORA", x, y, s, rose, small);
                    foreach (AppUse app in heavy)
                    {
                        string name = app.App.Length > 22 ? app.App.Substring(0, 21) + "…" : app.App;
                        string uses = (int)Math.Round(app.Cpu) + "%  ·  " + Usage.Size(app.Memory);
                        y = Row(g, label, value, ink, dim, name, uses, x, y, right, s);
                    }
                    y += 2 * s;
                }

                if (recentShort.Count > 0)
                {
                    y = Section(g, "ERROS PARA CORRIGIR", x, y, s, rose, small);
                    int show = Math.Min(2, recentShort.Count);
                    for (int i = recentShort.Count - show; i < recentShort.Count; i++)
                    {
                        string line = recentShort[i];
                        if (line.Length > 58) line = line.Substring(0, 55) + "...";
                        g.DrawString(line, label, ink, x, y);
                        y += 20 * s;
                    }
                    if (recentShort.Count > show)
                    {
                        g.DrawString("… copie o pedido para ver todos", small, dim, x, y);
                        y += 16 * s;
                    }
                    y += 2 * s;
                }

                proposalHits.Clear();
                y = Section(g, "IDEIAS PARA CONFIRMAR", x, y, s, rose, small);
                if (pendingIdeas.Count == 0)
                {
                    g.DrawString("Nenhuma ideia da Capri agora.", label, dim, x, y);
                    y += 20 * s;
                }
                else
                {
                    int show = Math.Min(2, pendingIdeas.Count);
                    for (int i = 0; i < show; i++)
                    {
                        IntelProposal p = pendingIdeas[i];
                        string titleText = p == null ? "" : (p.Title ?? "(sem título)");
                        if (titleText.Length > 34) titleText = titleText.Substring(0, 31) + "...";
                        g.DrawString("• " + titleText, label, ink, x, y + 2 * s);
                        Rectangle btn = new Rectangle((int)(right - 92 * s), (int)(y - 2 * s), (int)(92 * s), (int)(22 * s));
                        ProposalHit hit = new ProposalHit();
                        hit.Box = btn; hit.Id = p == null ? "" : p.Id; hit.Action = "apply";
                        proposalHits.Add(hit);
                        using (Pen edge = new Pen(Color.FromArgb(intelBusy ? 70 : 150, Neon.Cyan), Math.Max(1, 1.1f * s)))
                        using (GraphicsPath cut = Neon.Cut(btn, 4 * s, 1 * s, 4 * s, 1 * s))
                            g.DrawPath(edge, cut);
                        using (Brush cyan = new SolidBrush(Neon.Cyan))
                            g.DrawString(intelBusy ? "..." : "CONFIRMAR", small, intelBusy ? dim : cyan, btn.Left + 10 * s, btn.Top + 5 * s);
                        y += 26 * s;
                    }
                    if (pendingIdeas.Count > show)
                    {
                        g.DrawString("… mais ideias na fila", small, dim, x, y);
                        y += 16 * s;
                    }
                }
                if (!string.IsNullOrEmpty(intelStatus))
                {
                    string st = intelStatus;
                    if (st.Length > 62) st = st.Substring(0, 59) + "...";
                    g.DrawString(st, small, dim, x, y);
                    y += 16 * s;
                }
                y += 2 * s;

                float ay = y;
                y = Section(g, "ATUALIZAÇÕES", x, y, s, rose, small);
                allIntelBox = new Rectangle((int)(right - 78 * s), (int)(ay - 2 * s), (int)(78 * s), (int)(18 * s));
                using (Pen edgeAll = new Pen(Color.FromArgb(120, Neon.Rose), Math.Max(1, 1f * s)))
                using (GraphicsPath cutAll = Neon.Cut(allIntelBox, 3 * s, 1 * s, 3 * s, 1 * s))
                    g.DrawPath(edgeAll, cutAll);
                g.DrawString("VER TODAS", small, dim, allIntelBox.Left + 8 * s, allIntelBox.Top + 3 * s);
                if (doneIdeas.Count == 0)
                {
                    g.DrawString("Nenhuma atualização feita ainda.", label, dim, x, y);
                    y += 20 * s;
                }
                else
                {
                    int show = Math.Min(2, doneIdeas.Count);
                    for (int i = 0; i < show; i++)
                    {
                        IntelProposal p = doneIdeas[i];
                        string titleText = p == null ? "" : (p.Title ?? "(sem título)");
                        string state = p == null ? "" : (p.State ?? "");
                        string tag = state == "applied" ? "testada" : state == "confirmed" ? "confirmada" : state == "failed" ? "falhou" : state == "reverted" ? "desfeita" : state;
                        string lineText = "• " + titleText + " (" + tag + ")";
                        if (lineText.Length > 42) lineText = lineText.Substring(0, 39) + "...";
                        g.DrawString(lineText, label, ink, x, y + 2 * s);
                        if (state == "applied")
                        {
                            Rectangle btn = new Rectangle((int)(right - 84 * s), (int)(y - 2 * s), (int)(84 * s), (int)(22 * s));
                            ProposalHit hit = new ProposalHit();
                            hit.Box = btn; hit.Id = p == null ? "" : p.Id; hit.Action = "revert";
                            proposalHits.Add(hit);
                            using (Pen edge = new Pen(Color.FromArgb(intelBusy ? 70 : 140, Neon.Amber), Math.Max(1, 1.1f * s)))
                            using (GraphicsPath cut = Neon.Cut(btn, 4 * s, 1 * s, 4 * s, 1 * s))
                                g.DrawPath(edge, cut);
                            using (Brush amb = new SolidBrush(Neon.Amber))
                                g.DrawString(intelBusy ? "..." : "DESFAZER", small, intelBusy ? dim : amb, btn.Left + 10 * s, btn.Top + 5 * s);
                        }
                        y += 26 * s;
                    }
                }
                y += 2 * s;

                // The answer they came for, framed so it reads at a glance.
                float by = size.Height - 40 * s;
                float boxH = Math.Max(60 * s, Math.Min(86 * s, by - y - 14 * s));
                RectangleF box = new RectangleF(x - 8 * s, y, right - x + 16 * s, boxH);
                Color tone = blame == "PC" ? Neon.Amber : blame == "CAPRI" ? Neon.Rose : blame == "OK" ? Neon.Cyan : Neon.Faint;
                using (Pen line = new Pen(Color.FromArgb(120, tone), Math.Max(1, 1.2f * s)))
                using (GraphicsPath cut = Neon.Cut(box, 8 * s, 2 * s, 8 * s, 2 * s))
                    g.DrawPath(line, cut);
                g.DrawString("LEITURA", small, rose, box.Left + 10 * s, box.Top + 8 * s);
                RectangleF words = new RectangleF(box.Left + 10 * s, box.Top + 24 * s, box.Width - 20 * s, box.Height - 28 * s);
                using (StringFormat wrap = new StringFormat { Trimming = StringTrimming.EllipsisWord })
                    g.DrawString(verdict, label, ink, words, wrap);

                copyBox = new Rectangle((int)(x - 6 * s), (int)(by - 10 * s), (int)(86 * s), (int)(28 * s));
                using (Pen edge = new Pen(Color.FromArgb(140, Neon.Rose), Math.Max(1, 1.1f * s)))
                using (GraphicsPath cut = Neon.Cut(copyBox, 5 * s, 1 * s, 5 * s, 1 * s))
                    g.DrawPath(edge, cut);
                g.DrawString(copied ? "COPIADO" : "COPIAR", small, copied ? rose : ink, copyBox.Left + 14 * s, copyBox.Top + 8 * s);

                copyAskBox = new Rectangle(copyBox.Right + 8 * s > 0 ? (int)(copyBox.Right + 8 * s) : 0, copyBox.Top, (int)(122 * s), copyBox.Height);
                using (Pen edge = new Pen(Color.FromArgb(140, Neon.Cyan), Math.Max(1, 1.1f * s)))
                using (GraphicsPath cut = Neon.Cut(copyAskBox, 5 * s, 1 * s, 5 * s, 1 * s))
                    g.DrawPath(edge, cut);
                using (Brush cyan = new SolidBrush(Neon.Cyan))
                    g.DrawString(copiedAsk ? "COPIADO" : "COPIAR PEDIDO", small, copiedAsk ? cyan : ink, copyAskBox.Left + 10 * s, copyAskBox.Top + 8 * s);

                g.DrawString("Esc fecha", small, dim, copyAskBox.Right + 10 * s, by - 2 * s);
            }
        }

        private void ShowAllIntel()
        {
            try
            {
                using (IntelListForm form = new IntelListForm())
                {
                    form.ShowDialog(this);
                }
            }
            catch (Exception) { }
            Sample();
            Invalidate();
        }

        private sealed class IntelListForm : Form
        {
            private readonly ListView list = new ListView();
            private readonly Button refresh = new Button();
            private readonly Button confirm = new Button();
            private readonly Button revert = new Button();
            private readonly Button nova = new Button();
            private readonly Button close = new Button();
            private readonly Label status = new Label();
            private IntelProposal[] items = new IntelProposal[0];
            private bool busy;

            public IntelListForm()
            {
                Text = "Atualizações da Capri";
                StartPosition = FormStartPosition.CenterParent;
                ClientSize = new Size(700, 420);
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                ShowInTaskbar = false;

                list.View = View.Details;
                list.FullRowSelect = true;
                list.GridLines = true;
                list.HideSelection = false;
                list.MultiSelect = false;
                list.Columns.Add("Estado", 90);
                list.Columns.Add("Título", 240);
                list.Columns.Add("Motivo", 250);
                list.Columns.Add("ID", 100);
                list.Location = new Point(10, 10);
                list.Size = new Size(680, 320);
                list.SelectedIndexChanged += delegate { UpdateButtons(); };

                refresh.Text = "Atualizar";
                refresh.SetBounds(10, 340, 90, 28);
                refresh.Click += delegate { LoadItems(); };

                confirm.Text = "CONFIRMAR";
                confirm.SetBounds(110, 340, 110, 28);
                confirm.Click += delegate { RunAction("apply"); };

                revert.Text = "DESFAZER";
                revert.SetBounds(230, 340, 100, 28);
                revert.Click += delegate { RunAction("revert"); };

                nova.Text = "NOVA IDEIA";
                nova.SetBounds(340, 340, 110, 28);
                nova.Click += delegate { NewIdea(); };

                close.Text = "Fechar";
                close.SetBounds(590, 340, 100, 28);
                close.Click += delegate { Close(); };

                status.AutoSize = false;
                status.SetBounds(10, 375, 680, 36);
                status.Text = "Selecione um item pendente para confirmar, ou clique em NOVA IDEIA para registrar uma ideia. Ideias fora do código não são aplicadas automaticamente.";

                Controls.Add(list);
                Controls.Add(refresh);
                Controls.Add(confirm);
                Controls.Add(revert);
                Controls.Add(nova);
                Controls.Add(close);
                Controls.Add(status);

                Load += delegate { LoadItems(); };
            }

            private void LoadItems()
            {
                list.BeginUpdate();
                try
                {
                    list.Items.Clear();
                    items = new IntelProposal[0];
                    try { items = SystemIntel.ListProposals() ?? new IntelProposal[0]; } catch (Exception) { items = new IntelProposal[0]; }
                    foreach (IntelProposal p in items)
                    {
                        if (p == null) continue;
                        string state = p.State ?? "";
                        string tag = state == "proposed" ? "pendente" : state == "applied" ? "testada" : state == "confirmed" ? "confirmada" : state == "failed" ? "falhou" : state == "reverted" ? "desfeita" : state;
                        string title = p.Title ?? "(sem título)";
                        string why = p.Why ?? "";
                        if (why.Length > 80) why = why.Substring(0, 77) + "...";
                        ListViewItem row = new ListViewItem(new string[] { tag, title, why, p.Id ?? "" });
                        row.Tag = p;
                        list.Items.Add(row);
                    }
                    if (list.Items.Count == 0)
                    {
                        ListViewItem row = new ListViewItem(new string[] { "-", "Nenhuma atualização salva.", "", "" });
                        list.Items.Add(row);
                    }
                }
                finally { list.EndUpdate(); }
                UpdateButtons();
            }

            private IntelProposal Selected()
            {
                if (list.SelectedItems.Count == 0) return null;
                return list.SelectedItems[0].Tag as IntelProposal;
            }

            private void UpdateButtons()
            {
                IntelProposal p = Selected();
                string state = p == null ? "" : (p.State ?? "");
                bool note = p != null && string.Equals(p.Kind, "note", StringComparison.OrdinalIgnoreCase);
                confirm.Enabled = !busy && state == "proposed";
                revert.Enabled = !busy && (state == "applied" || (state == "confirmed" && note));
                refresh.Enabled = !busy;
                nova.Enabled = !busy;
            }

            private void RunAction(string action)
            {
                IntelProposal p = Selected();
                if (p == null || busy) return;
                string id = p.Id ?? "";
                if (string.IsNullOrEmpty(id)) return;
                busy = true;
                bool note = string.Equals(p.Kind, "note", StringComparison.OrdinalIgnoreCase);
                status.Text = action == "revert" ? "Desfazendo..." : (note ? "Confirmando ideia..." : "Confirmando: aplicando e testando (pode demorar)...");
                UpdateButtons();
                ThreadPool.QueueUserWorkItem(delegate
                {
                    string result = "";
                    try
                    {
                        if (action == "revert") result = SystemIntel.RevertFromUser(id);
                        else result = SystemIntel.ApplyFromUser(id);
                    }
                    catch (Exception e) { result = "Não deu: " + e.Message; }
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            busy = false;
                            status.Text = ShortStatus(result);
                            LoadItems();
                        });
                    }
                    catch (Exception) { }
                });
            }

            private void NewIdea()
            {
                string title = "", why = "";
                using (Form ask = new Form())
                {
                    ask.Text = "Nova ideia/atualização";
                    ask.StartPosition = FormStartPosition.CenterParent;
                    ask.ClientSize = new Size(420, 220);
                    ask.FormBorderStyle = FormBorderStyle.FixedDialog;
                    ask.MaximizeBox = false;
                    ask.MinimizeBox = false;
                    ask.ShowInTaskbar = false;
                    Label lt = new Label();
                    lt.Text = "Título:";
                    lt.SetBounds(10, 10, 390, 18);
                    TextBox tt = new TextBox();
                    tt.SetBounds(10, 30, 390, 24);
                    Label lw = new Label();
                    lw.Text = "Detalhes (opcional):";
                    lw.SetBounds(10, 60, 390, 18);
                    TextBox tw = new TextBox();
                    tw.SetBounds(10, 80, 390, 70);
                    tw.Multiline = true;
                    tw.ScrollBars = ScrollBars.Vertical;
                    Button ok = new Button();
                    ok.Text = "Salvar";
                    ok.SetBounds(220, 160, 85, 28);
                    ok.DialogResult = DialogResult.OK;
                    Button cancel = new Button();
                    cancel.Text = "Cancelar";
                    cancel.SetBounds(315, 160, 85, 28);
                    cancel.DialogResult = DialogResult.Cancel;
                    ask.Controls.Add(lt); ask.Controls.Add(tt); ask.Controls.Add(lw); ask.Controls.Add(tw); ask.Controls.Add(ok); ask.Controls.Add(cancel);
                    ask.AcceptButton = ok; ask.CancelButton = cancel;
                    if (ask.ShowDialog(this) != DialogResult.OK) return;
                    title = (tt.Text ?? "").Trim();
                    why = (tw.Text ?? "").Trim();
                }
                if (title.Length == 0 && why.Length == 0) return;
                try
                {
                    string result = SystemIntel.Do(new ToolArguments { Action = "propose", Kind = "idea", Title = title, Why = why });
                    LoadItems();
                    status.Text = ShortStatus(string.IsNullOrEmpty(result) ? "Ideia registrada. Confirme quando quiser." : result);
                }
                catch (Exception e) { status.Text = "Não deu: " + e.Message; }
            }

            private static string ShortStatus(string text)
            {
                if (string.IsNullOrEmpty(text)) return "";
                text = text.Replace("\r", " ").Replace("\n", " ").Trim();
                if (text.Length > 140) text = text.Substring(0, 137) + "...";
                return text;
            }
        }

        private static float Section(Graphics g, string name, float x, float y, float s, Brush rose, Font small)
        {
            g.DrawString(name, small, rose, x, y);
            using (Pen rule = new Pen(Color.FromArgb(90, Neon.Rose), Math.Max(1, 1 * s)))
                g.DrawLine(rule, x, y + 13 * s, x + 330 * s, y + 13 * s);
            return y + 22 * s;
        }

        private static float Row(Graphics g, Font label, Font value, Brush ink, Brush dim, string name, string says,
            float x, float y, float right, float s)
        {
            g.DrawString(name, label, dim, x, y);
            SizeF size = g.MeasureString(says, value);
            g.DrawString(says, value, ink, right - size.Width, y);
            return y + 21 * s;
        }

        // A row with a thin bar under it: how full that number is, and hot when it's near the limit.
        private static float Meter(Graphics g, Font label, Font value, Brush ink, Brush dim, string name, string says,
            double part, float x, float y, float right, float s, int hot)
        {
            y = Row(g, label, value, ink, dim, name, says, x, y, right, s);
            part = Math.Max(0, Math.Min(1, part));
            float width = right - x, height = 4 * s;
            using (Brush well = new SolidBrush(Color.FromArgb(120, Neon.Well))) g.FillRectangle(well, x, y, width, height);
            Color fill = hot == 1 ? Neon.Amber : Neon.Rose;
            using (Brush done = new SolidBrush(fill)) g.FillRectangle(done, x, y, (float)(width * part), height);
            return y + 11 * s;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            DrawTo(e.Graphics);
        }
    }
}
