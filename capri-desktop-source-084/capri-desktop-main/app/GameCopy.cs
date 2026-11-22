using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace CapriDesktop
{
#if !CROSS
    // Watching how they play a game, keeping the route they took, and playing it back: the keys, clicks and mouse turns
    // in the same order and at the same pace. Once it's copied, her game job carries on from there to try to finish it.
    // Only the front game is watched, and only keys and the mouse are read: nothing touches a game's memory.
    internal static class GameCopy
    {
        // <data>\routes, set at startup. One plain text route per game, which they can read or delete.
        internal static string Folder;
        // True while one of her jobs is running, so she never starts this on top of one.
        internal static Func<bool> Busy = delegate { return false; };

        private const int SampleMs = 33;
        private const int MostMs = 10 * 60 * 1000;
        private const int MostMoves = 30000;
        // How long the game may be out of the front before she gives up on the replay.
        private const int LetGoMs = 2500;
        private const int MouseReach = 350;

        private sealed class Move
        {
            internal char Kind;
            internal int At, A, B;

            internal static Move Key(int at, int key, bool down)
            {
                return new Move { Kind = 'k', At = at, A = key, B = down ? 1 : 0 };
            }

            internal static Move Button(int at, int button, bool down)
            {
                return new Move { Kind = 'b', At = at, A = button, B = down ? 1 : 0 };
            }

            internal static Move Mouse(int at, int dx, int dy)
            {
                return new Move { Kind = 'm', At = at, A = dx, B = dy };
            }
        }

        private static readonly object gate = new object();
        private static readonly object movesGate = new object();
        private static readonly List<Move> watched = new List<Move>();
        private static string mode = "idle";
        private static string game = "";
        private static string route;
        private static IntPtr window;
        private static int process;
        private static volatile bool stop;
        private static volatile bool abort;
        private static Thread watcher;
        private static string ended = "";
        private static int seconds;

        // Every key a game might use, apart from Esc, which is how she's stopped.
        private static readonly int[] PlayKeys = Keys();

        private static int[] Keys()
        {
            List<int> keys = new List<int>();
            for (int key = 0x30; key <= 0x39; key++) keys.Add(key);
            for (int key = 0x41; key <= 0x5A; key++) keys.Add(key);
            for (int key = 0x60; key <= 0x6F; key++) keys.Add(key);
            for (int key = 0x70; key <= 0x7B; key++) keys.Add(key);
            keys.AddRange(new[] { 0x08, 0x09, 0x0D, 0x10, 0x11, 0x12, 0x14, 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2D, 0x2E });
            keys.AddRange(new[] { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0xBA, 0xBB, 0xBC, 0xBD, 0xBE, 0xBF, 0xC0, 0xDB, 0xDC, 0xDD, 0xDE });
            return keys.ToArray();
        }

        // Watch, stop, copy or status, from her tools or a brain's call.
        internal static string Do(string action)
        {
            switch (action)
            {
                case "watch": return Start();
                case "stop": return Stop();
                case "copy": return Copy();
                case "status": return Status();
            }
            return "Say watch, stop, copy or status.";
        }

        internal static string Start()
        {
            lock (gate)
            {
                if (mode == "copying") return "I'm still copying what you did. Give me a moment.";
                if (mode == "watching") return "I'm already watching how you play. When you're ready, say faz isso.";
                if (Busy()) return "I'm in the middle of something else. Let me finish that first.";
            }
            string named;
            GameSeen seen = Front(out named);
            if (seen == null) return "Open your game and bring it to the front, then tell me to watch how you play.";
            if (named.Length == 0) return "I can't tell which game that is, so I can't keep the route just now.";
            string path = RouteFor(named);
            if (path == null) return "I can't save that just now.";
            lock (gate)
            {
                game = named;
                route = path;
                window = seen.Window;
                process = seen.Process;
                mode = "watching";
                stop = false;
                abort = false;
                ended = "";
                seconds = 0;
                lock (movesGate) watched.Clear();
                watcher = new Thread(Watch);
                watcher.IsBackground = true;
                watcher.Name = "Capri game copy watch";
                watcher.Start();
            }
            return "I'm watching how you play " + game + ". Play as you normally would; when you want me to try, put it back at the start and say \u201cfaz isso\u201d.";
        }

        internal static string Stop()
        {
            Thread thread;
            lock (gate)
            {
                if (mode == "idle") return ended.Length > 0 ? ended : "I'm not watching anything right now.";
                if (mode == "copying")
                {
                    abort = true;
                    stop = true;
                    try { Computer.Interrupt("You asked me to stop."); } catch (Exception) { }
                    mode = "idle";
                    ended = "I stopped.";
                    return "Okay, I stopped copying.";
                }
                stop = true;
                thread = watcher;
            }
            if (thread != null) thread.Join(2000);
            lock (gate)
            {
                if (mode == "copying") return "Okay, I stopped copying.";
                if (mode == "watching") mode = "idle";
                return ended.Length > 0 ? ended : "I stopped watching.";
            }
        }

        internal static string Copy()
        {
            lock (gate)
            {
                if (mode == "copying") return "I'm already copying it.";
                if (Busy()) return "I'm in the middle of something else. Let me finish that first.";
            }
            if (mode == "watching") Stop();
            string named;
            GameSeen seen = Front(out named);
            IntPtr target;
            if (seen != null)
            {
                target = seen.Window;
            }
            else
            {
                lock (gate)
                {
                    named = game;
                    target = window;
                }
                if (target == IntPtr.Zero || !GameFinder.Alive(new GameSeen { Window = target, Process = process }))
                    return "Open your game and bring it to the front, then say that again.";
            }
            if (named.Length == 0) { lock (gate) named = game; }
            if (named.Length == 0) return "Open your game first, so I know which one you mean.";
            string path = RouteFor(named);
            if (path == null || !File.Exists(path)) return "I haven't watched you play " + named + " yet. Say \u201cobserva como eu jogo\u201d first.";
            List<Move> moves = Load(path);
            if (moves.Count == 0) return "That route came out empty, so there's nothing to copy. Show me again and I'll watch.";
            int took = Took(path, moves);
            string summary = Summary(moves);
            Remember(named, summary, took);
            string line;
            lock (gate)
            {
                game = named;
                route = path;
                window = target;
                mode = "copying";
                stop = false;
                abort = false;
                ended = "";
                line = "Right. I watched what you did in " + game + ". I'll try to do it the same way, but looking at the screen: if something goes wrong, I'll try another way instead of repeating the same mistake. Press Esc to stop me.";
                Thread thread = new Thread(delegate() { TryIt(moves.Count, took, summary); });
                thread.IsBackground = true;
                thread.Name = "Capri game copy";
                thread.Start();
            }
            return line;
        }

        internal static string Status()
        {
            lock (gate)
            {
                if (mode == "copying") return "I'm doing what you did in " + game + " and trying to finish it.";
                if (mode == "watching") return "I'm watching how you play" + (game.Length == 0 ? "." : " " + game + ".");
            }
            string named;
            GameSeen seen = Front(out named);
            string path = named.Length == 0 ? null : RouteFor(named);
            if (path == null || !File.Exists(path))
            {
                lock (gate) if (route != null && File.Exists(route)) { path = route; named = game; }
            }
            if (path == null || !File.Exists(path)) return "Open your game, then I can watch how you play and learn it.";
            return "I've watched " + Count(path) + " moves in " + named + ". Say \u201cfaz isso\u201d and I'll copy them.";
        }

        // Watching, in their words. Portuguese and English, and only when there's a route or a game to watch, so a plain
        // "do that" in the middle of something else stays with her brain.
        internal static ToolArguments Request(string text)
        {
            string said = Regex.Replace((text ?? "").Trim().ToLowerInvariant(), @"\s+", " ");
            if (said.Length == 0) return null;
            if (WatchSaid.IsMatch(said)) return new ToolArguments { Action = "watch" };
            if (StatusSaid.IsMatch(said)) return new ToolArguments { Action = "status" };
            if (CopySaid.IsMatch(said) && HasContext()) return new ToolArguments { Action = "copy" };
            if (StopSaid.IsMatch(said) && (Watching() || StrongStop.IsMatch(said) && HasContext())) return new ToolArguments { Action = "stop" };
            return null;
        }

        private static bool Watching() { lock (gate) return mode == "watching" || mode == "copying"; }

        private static bool HasContext()
        {
            lock (gate)
            {
                if (mode == "watching" || mode == "copying") return true;
                if (route != null && File.Exists(route)) return true;
            }
            string named;
            GameSeen seen = Front(out named);
            if (seen == null) return false;
            string path = RouteFor(named);
            return path != null && File.Exists(path);
        }

        private static readonly Regex WatchSaid = new Regex(
            @"^(?:capri[,\s]+)?(?:(?:fica|fique|continua|continue)\s+de\s+olho\b.*\b(?:jog\w*|game|games|play|partida|fase|mapa|map|level|obby)\b" +
            @"|(?:observa|observe|olha|olhe|repara|repare|presta\s+aten\w*o|aprende|aprenda|assiste|assista|grava|grave|anota|anote|memoriza|memorize)\b.*\b(?:como|enquanto|eu|meu|minha|jog\w*|fa[cç]o|faz|game|play)\b" +
            @"|(?:watch|observe|study|learn|record|note|remember)\b.*\b(?:how\s+i\s+(?:play|do)|me\s+play|my\s+(?:moves|route|play)|what\s+i\s+do)\b)",
            RegexOptions.IgnoreCase);

        private static readonly Regex StatusSaid = new Regex(
            @"^(?:o\s+que\s+voc[eê]\s+(?:est[aá]\s+)?(?:assistindo|observando|vendo|anotando)|voc[eê]\s+(?:est[aá]\s+)?(?:assistindo|observando)|what\s+are\s+you\s+watching|what\s+did\s+you\s+watch)$",
            RegexOptions.IgnoreCase);

        private static readonly Regex CopySaid = new Regex(
            @"^(?:agora\s+)?(?:faz|fa[cç]a)\s+(?:isso|igual|o\s+mesmo|do\s+mesmo\s+jeito|desse\s+jeito|a[ií])$" +
            @"|^(?:copia|copie|repete|repita|imita|imite)\s+(?:isso|o\s+que\s+eu\s+fiz|o\s+que\s+eu\s+fa[cç]o|isso\s+a[ií]|ele)$" +
            @"|^(?:agora\s+[eé]\s+voc[eê]|sua\s+vez|tenta\s+voc[eê]|tenta\s+(?:fazer\s+)?isso|tenta\s+chegar\s+no\s+final|vai\s+at[eé]\s+o\s+final|termina(?:\s+isso)?|termina\s+(?:pra|para)\s+mim|acaba\s+isso|faz\s+melhor)$" +
            @"|^(?:do\s+(?:that|the\s+same|it\s+again)|copy\s+(?:that|it|what\s+i\s+did)|now\s+you\s+try|your\s+turn|try\s+to\s+finish(?:\s+it)?|finish\s+it|beat\s+it)$",
            RegexOptions.IgnoreCase);

        private static readonly Regex StopSaid = new Regex(
            @"^(?:p[aá]ra|pare|pode\s+parar|chega|j[aá]\s+chega|chega\s+disso|para\s+(?:de\s+)?(?:observar|olhar|gravar|anotar|assistir)|stop|stop\s+(?:watching|observing|recording|copying|it)|that'?s\s+enough|enough)$",
            RegexOptions.IgnoreCase);

        private static readonly Regex StrongStop = new Regex(
            @"^(?:para\s+(?:de\s+)?(?:observar|olhar|gravar|anotar|assistir)|stop\s+(?:watching|observing|recording|copying))$",
            RegexOptions.IgnoreCase);

        // The game in front, and what to call it: its title first, then its program.
        private static GameSeen Front(out string named)
        {
            named = "";
            GameSeen seen = GameFinder.Front();
            if (seen == null) return null;
            Sight sight = Computer.Glimpse();
            string title = sight != null && sight.Window == seen.Window ? sight.Title : "";
            named = Tidy(GameNotes.Name(title));
            if (named.Length == 0) named = Tidy(GameNotes.Name(seen.Name));
            return seen;
        }

        private static string Tidy(string text)
        {
            string name = Regex.Replace(text ?? "", @"\s+", " ").Trim(' ', '.', '-');
            name = Regex.Replace(name, @"\.exe$", "", RegexOptions.IgnoreCase).Trim();
            return name.Length > 70 ? name.Substring(0, 70).Trim() : name;
        }

        private static string RouteFor(string named)
        {
            if (string.IsNullOrEmpty(Folder) || string.IsNullOrEmpty(named)) return null;
            string slug = Regex.Replace(named.ToLowerInvariant(), @"[^\p{L}\p{N}]+", "-").Trim('-');
            if (slug.Length == 0) return null;
            try { Directory.CreateDirectory(Folder); }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            return Path.Combine(Folder, slug + ".route");
        }

        // The sampler: reads the keys, mouse buttons and mouse movement while the game is the front window, and keeps
        // them in order. Nothing is recorded while they're in another window.
        private static void Watch()
        {
            Stopwatch clock = Stopwatch.StartNew();
            bool[] down = new bool[PlayKeys.Length];
            bool[] buttons = new bool[3];
            Point last = Point.Empty;
            bool known = false;
            List<Move> caught = new List<Move>();
            string why = null;
            try
            {
                while (!stop)
                {
                    int at = (int)clock.ElapsedMilliseconds;
                    if (Native.GetForegroundWindow() == window)
                    {
                        for (int i = 0; i < PlayKeys.Length; i++)
                        {
                            bool now = (Native.GetAsyncKeyState(PlayKeys[i]) & 0x8000) != 0;
                            if (now != down[i]) { caught.Add(Move.Key(at, PlayKeys[i], now)); down[i] = now; }
                        }
                        for (int i = 0; i < 3; i++)
                        {
                            bool now = (Native.GetAsyncKeyState(i == 0 ? 0x01 : i == 1 ? 0x02 : 0x04) & 0x8000) != 0;
                            if (now != buttons[i]) { caught.Add(Move.Button(at, i + 1, now)); buttons[i] = now; }
                        }
                        Point at2 = Cursor.Position;
                        if (known)
                        {
                            int dx = at2.X - last.X, dy = at2.Y - last.Y;
                            if (dx != 0 || dy != 0) caught.Add(Move.Mouse(at, Reach(dx), Reach(dy)));
                        }
                        last = at2;
                        known = true;
                    }
                    else
                    {
                        known = false;
                        for (int i = 0; i < down.Length; i++) down[i] = false;
                        for (int i = 0; i < buttons.Length; i++) buttons[i] = false;
                    }
                    if (at >= MostMs || caught.Count >= MostMoves)
                    {
                        why = at >= MostMs ? "That's as long as I can watch at once, so I stopped." : "That's as much as I can remember at once, so I stopped.";
                        break;
                    }
                    Thread.Sleep(SampleMs);
                }
            }
            catch (Exception error)
            {
                why = "I couldn't keep watching just now.";
                Brain.Log("game copy", -1, error.Message);
            }
            int length = (int)clock.ElapsedMilliseconds;
            string saved = Save(caught, length);
            lock (movesGate) { watched.Clear(); watched.AddRange(caught); }
            lock (gate)
            {
                if (mode == "watching") mode = "idle";
                seconds = (length + 500) / 1000;
                ended = why ?? saved ?? "I watched " + caught.Count + " moves over " + seconds + " seconds in " + game + ". When you want me to try, put it back at the start and say \u201cfaz isso\u201d.";
            }
        }

        private static int Reach(int value) { return value > MouseReach ? MouseReach : value < -MouseReach ? -MouseReach : value; }

        private static string Save(List<Move> all, int length)
        {
            if (route == null) return "I couldn't save that just now.";
            StringBuilder text = new StringBuilder();
            text.AppendLine("# Capri play route");
            text.AppendLine("game=" + game);
            text.AppendLine("seconds=" + ((length + 500) / 1000));
            foreach (Move move in all)
                text.Append(move.Kind).Append(' ').Append(move.At.ToString(CultureInfo.InvariantCulture)).Append(' ')
                    .Append(move.A.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(move.B.ToString(CultureInfo.InvariantCulture)).AppendLine();
            try { File.WriteAllText(route, text.ToString(), new UTF8Encoding(false)); }
            catch (IOException) { return "I couldn't keep that just now."; }
            catch (UnauthorizedAccessException) { return "I couldn't keep that just now."; }
            return null;
        }

        private static List<Move> Load(string path)
        {
            List<Move> all = new List<Move>();
            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch (IOException) { return all; }
            catch (UnauthorizedAccessException) { return all; }
            foreach (string line in lines)
            {
                if (string.IsNullOrEmpty(line) || line[0] == '#') continue;
                string[] parts = line.Split(' ');
                if (parts.Length != 4 || parts[0].Length != 1) continue;
                int at, a, b;
                if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out at)) continue;
                if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out a)) continue;
                if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out b)) continue;
                char kind = parts[0][0];
                if (kind != 'k' && kind != 'b' && kind != 'm') continue;
                if (at < 0) continue;
                all.Add(new Move { Kind = kind, At = at, A = a, B = b });
                if (all.Count >= MostMoves) break;
            }
            return all;
        }

        private static int Count(string path)
        {
            List<Move> all = Load(path);
            return all.Count;
        }

        // Tries to follow what the user showed, but by watching the screen and adapting: if a move doesn't work or
        // she ends up in the wrong place, she looks, compares and tries another way instead of repeating the same mistake.
        private static void TryIt(int count, int took, string summary)
        {
            string result = null;
            try
            {
                if (Hands.Front(window)) Computer.Pause(800);
                if (abort || Computer.Interrupted) { result = "You stopped me, so I stopped."; }
                else
                {
                    string task = HandOver(count, took, summary);
                    result = Hands.Work(task, null);
                }
            }
            catch (Exception error)
            {
                result = "I couldn't try that just now.";
                Brain.Log("game copy", -1, error.Message);
            }
            finally
            {
                lock (gate)
                {
                    mode = "idle";
                    ended = abort ? "I stopped." : (string.IsNullOrEmpty(result) ? "I finished trying." : result);
                }
            }
        }

        private static string HandOver(int count, int took, string summary)
        {
            StringBuilder task = new StringBuilder();
            task.Append("Play the game ").Append(game).Append(" and try to reach the goal.");
            task.Append(" The user showed you how they do it: over about ").Append(took).Append(" seconds they made around ").Append(count).Append(" moves.");
            if (!string.IsNullOrEmpty(summary)) task.Append(" What they did: ").Append(summary).Append(".");
            task.Append(" Watch the screen and try to follow what they showed, step by step, from where the game is now.");
            task.Append(" Keep looking at what is on screen to understand where you are, where you can go and what is in the way.");
            task.Append(" If the same move or path does not work, if you fall, get stuck or hit an obstacle, do not repeat the same mistake: compare where you ended up, try a different move, timing or route, and learn what works.");
            task.Append(" Like in a fight, if one combination does not work, try another. Keep adapting until you make progress or reach the goal.");
            task.Append(" If you discover something useful about how to pass this part, remember it. Then tell the user how it went.");
            return task.ToString();
        }

        private static int Took(string path, List<Move> moves)
        {
            try
            {
                foreach (string line in File.ReadAllLines(path))
                {
                    if (line.StartsWith("seconds="))
                    {
                        int value;
                        if (int.TryParse(line.Substring(8).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0) return value;
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return moves == null || moves.Count == 0 ? 0 : (moves[moves.Count - 1].At + 500) / 1000;
        }

        private static void Remember(string named, string summary, int took)
        {
            if (string.IsNullOrEmpty(named) || string.IsNullOrEmpty(summary)) return;
            try
            {
                string line = "The user showed me how to play for about " + took + " seconds: " + summary + ". Try to follow this, but adapt by looking at the screen and change approach if it fails.";
                GameNotes.Learn(named, line);
            }
            catch (Exception error) { Brain.Log("game copy", -1, error.Message); }
        }

        private static string Summary(List<Move> moves)
        {
            if (moves == null || moves.Count == 0) return "";
            List<string> keys = new List<string>();
            bool mouse = false, left = false, right = false, middle = false;
            foreach (Move move in moves)
            {
                if (move.Kind == 'm') { mouse = mouse || move.A != 0 || move.B != 0; continue; }
                if (move.Kind == 'b')
                {
                    if (move.B != 0)
                    {
                        if (move.A == 1) left = true;
                        else if (move.A == 2) right = true;
                        else if (move.A == 3) middle = true;
                    }
                    continue;
                }
                if (move.Kind == 'k' && move.B != 0)
                {
                    string name = KeyName(move.A);
                    if (!keys.Contains(name)) keys.Add(name);
                }
            }
            List<string> parts = new List<string>();
            if (keys.Count > 0)
            {
                int show = Math.Min(keys.Count, 12);
                StringBuilder list = new StringBuilder();
                for (int i = 0; i < show; i++) { if (i > 0) list.Append(", "); list.Append(keys[i]); }
                if (keys.Count > show) list.Append(", ...");
                parts.Add("pressed " + list);
            }
            if (mouse) parts.Add("moved the mouse/camera");
            if (left) parts.Add("used left click");
            if (right) parts.Add("used right click");
            if (middle) parts.Add("used middle click");
            if (parts.Count == 0) return "some moves";
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0) text.Append(i == parts.Count - 1 ? " and " : ", ");
                text.Append(parts[i]);
            }
            return text.ToString();
        }

        private static string KeyName(int key)
        {
            if (key >= 0x41 && key <= 0x5A) return ((char)key).ToString();
            if (key >= 0x30 && key <= 0x39) return ((char)key).ToString();
            if (key >= 0x60 && key <= 0x69) return "Num" + (key - 0x60);
            switch (key)
            {
                case 0x08: return "Backspace";
                case 0x09: return "Tab";
                case 0x0D: return "Enter";
                case 0x10: return "Shift";
                case 0x11: return "Ctrl";
                case 0x12: return "Alt";
                case 0x14: return "CapsLock";
                case 0x20: return "Space";
                case 0x21: return "PageUp";
                case 0x22: return "PageDown";
                case 0x23: return "End";
                case 0x24: return "Home";
                case 0x25: return "Left";
                case 0x26: return "Up";
                case 0x27: return "Right";
                case 0x28: return "Down";
                case 0x2D: return "Insert";
                case 0x2E: return "Delete";
                case 0xA0: return "Shift";
                case 0xA1: return "Shift";
                case 0xA2: return "Ctrl";
                case 0xA3: return "Ctrl";
                case 0xA4: return "Alt";
                case 0xA5: return "Alt";
            }
            if (key >= 0x70 && key <= 0x7B) return "F" + (key - 0x6F);
            return "key " + key.ToString(CultureInfo.InvariantCulture);
        }
    }
#endif
}
