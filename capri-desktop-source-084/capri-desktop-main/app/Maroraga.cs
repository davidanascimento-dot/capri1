using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // What she has watched of their PC, kept as a habit rather than a diary: the app, what kind of app it is and how long
    // they've spent there. Window titles are read only to sort the app into a kind, and are never written down or sent.
    [DataContract]
    internal sealed class MaroragaSign
    {
        [DataMember] public string App;
        [DataMember] public string Kind;
        [DataMember] public int Seconds;
        [DataMember] public int Visits;
        [DataMember] public long At;
    }

    // One thing she got wrong, or got right after a miss, tied to the kind of app she was in. These are what let her adapt:
    // the next request in the same place starts from what actually worked last time.
    [DataContract]
    internal sealed class MaroragaLesson
    {
        [DataMember] public string Where;
        [DataMember] public string App;
        [DataMember] public string Text;
        [DataMember] public int Score;
        [DataMember] public long At;
    }

    [DataContract]
    internal sealed class MaroragaData
    {
        [DataMember] public int Format;
        [DataMember] public List<MaroragaSign> Signs;
        [DataMember] public List<MaroragaLesson> Lessons;
        [DataMember] public int Commands;
        [DataMember] public int Answered;
        [DataMember] public int Adaptations;
        [DataMember] public double ActiveSeconds;
        [DataMember] public long Updated;

        internal static MaroragaData New()
        {
            MaroragaData data = new MaroragaData { Format = 1 };
            data.Repair();
            return data;
        }

        internal void Repair()
        {
            if (Format < 1) Format = 1;
            if (Signs == null) Signs = new List<MaroragaSign>();
            if (Lessons == null) Lessons = new List<MaroragaLesson>();
            if (Signs.Count > 60) Signs.RemoveRange(0, Signs.Count - 60);
            if (Lessons.Count > 120) Lessons.RemoveRange(0, Lessons.Count - 120);
            if (ActiveSeconds < 0) ActiveSeconds = 0;
            if (Commands < 0) Commands = 0;
            if (Answered < 0) Answered = 0;
            if (Adaptations < 0) Adaptations = 0;
        }
    }

    // Maroraga is her golden anel adaptation. While its official mod is on she watches what they do in the background,
    // and every request turns the anel once: four movements, each one answering a question about the job before her hands
    // move. What she understands, and every mistake she learns from, goes into maroraga.json so it survives a restart.
    internal static class Maroraga
    {
        internal const int DefaultWheels = 4;
        internal const double WheelSeconds = 1.2;
        private const int MostSeconds = 60 * 60 * 24 * 30;

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int size);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out int process);

        private static readonly object gate = new object();
        private static Mods source;
        private static string file;
        private static MaroragaData data;
        private static bool loaded;
        private static long lastSample, lastSave, commandAt, spinAt;
        private static string currentApp, previousApp, currentKind, activeKind;
        private static bool active;
        private static string command, understanding;
        private static string[] facets;
        private static int movements;
        private static string lastAction;
        private static bool lastFailed;
        private static int streak;
        private static long burstAt;
        // One of his own gold spheres, cut from the art he sent, so the anel is made of his ring and not a redrawing.
        private static Bitmap ballArt;
        private static bool ballTried;

        // The mod instance lets every answer follow the switch straight away: turning Maroraga off in Mods turns the
        // wheels, the watching and the learning off together, with nothing left running.
        internal static void Configure(Mods mods, string dataFolder)
        {
            lock (gate)
            {
                source = mods;
                string wanted = string.IsNullOrEmpty(dataFolder) ? null : Path.Combine(dataFolder, "maroraga.json");
                if (!loaded || !string.Equals(wanted, file, StringComparison.OrdinalIgnoreCase))
                {
                    file = wanted;
                    data = Read();
                    loaded = true;
                }
                Ensure();
            }
        }

        internal static bool On { get { Mods mods = source; return mods != null && mods.MaroragaOn; } }

        internal static int WheelCount { get { return DefaultWheels; } }

        private static bool Watching
        {
            get
            {
                Mods mods = source;
                ModMaroraga about = mods == null ? null : mods.Maroraga;
                return about == null || !about.Watch.HasValue || about.Watch.Value;
            }
        }

        private static bool Remembering
        {
            get
            {
                Mods mods = source;
                ModMaroraga about = mods == null ? null : mods.Maroraga;
                return about == null || !about.Remember.HasValue || about.Remember.Value;
            }
        }

        // What the current request is, in four movements' worth of understanding. Empty while there's nothing to do.
        internal static string Understanding { get { lock (gate) return UnderstandingLocked(); } }
        internal static int Movements { get { lock (gate) return On ? movements : 0; } }
        internal static string CurrentApp { get { lock (gate) return currentApp; } }
        internal static string CurrentKind { get { lock (gate) return currentKind; } }
        internal static int CommandCount { get { lock (gate) { Ensure(); return data.Commands; } } }
        internal static int LessonCount { get { lock (gate) { Ensure(); return data.Lessons.Count; } } }
        internal static int AdaptationCount { get { lock (gate) { Ensure(); return data.Adaptations; } } }
        internal static bool Turning { get { lock (gate) return command != null && Now() - spinAt < (long)(WheelSeconds * 1000); } }
        internal static string PathFile { get { return file; } }

        // Four movements of one full turn, for her drawing: how far round the drive wheel is on the current request.
        internal static double Turn
        {
            get
            {
                lock (gate)
                {
                    if (command == null) return 0;
                    double age = (Now() - spinAt) / 1000.0;
                    if (age <= 0) return 0;
                    if (age >= WheelSeconds) return Math.PI * 2;
                    return age / WheelSeconds * Math.PI * 2;
                }
            }
        }

        // Called from her signal timer. It samples the app in front at most once a second and adds that second to the
        // habit it belongs to. Nothing here reads the screen itself, and no title is kept.
        internal static void Watch()
        {
            if (!On || !Watching) return;
            lock (gate)
            {
                long now = Now();
                if (lastSample != 0 && now - lastSample < 900) return;
                long gap = lastSample == 0 ? 250 : now - lastSample;
                lastSample = now;
                string app, title;
                if (!Front(out app, out title)) return;
                bool busy = false;
                try { busy = Native.IdleMilliseconds() < 15000; } catch (Exception) { busy = true; }
                Remember(app, title, Math.Min(5.0, Math.Max(0.25, gap / 1000.0)), busy);
            }
        }

        // The same sampler without Windows, so her smoke test can hand it a morning in a code editor and a browser.
        internal static void Observe(string app, string title, double seconds) { Remember(app, title, seconds, true); }

        private static void Remember(string app, string title, double seconds, bool busy)
        {
            app = CleanApp(app);
            if (app.Length == 0) return;
            lock (gate)
            {
                if (!On) return;
                Ensure();
                string kind = Kind(app, title);
                bool changed = !string.Equals(currentApp, app, StringComparison.OrdinalIgnoreCase);
                if (changed)
                {
                    if (!string.IsNullOrEmpty(currentApp)) previousApp = currentApp;
                    currentApp = app;
                }
                currentKind = kind;
                activeKind = kind;
                active = busy;
                MaroragaSign sign = SignLocked(app);
                if (sign.Kind == null || sign.Kind.Length == 0) sign.Kind = kind;
                if (changed) sign.Visits++;
                sign.Seconds = Math.Min(MostSeconds, sign.Seconds + (int)Math.Round(Math.Max(0, seconds)));
                sign.At = Now();
                if (busy) data.ActiveSeconds += Math.Max(0, seconds);
                data.Updated = Now();
                Save(false);
            }
        }

        // A request begins here. The anel turns once before her hands do, and each quarter adds one thing she has
        // worked out: what she has seen, what they really asked for, how she'll do it, and what to check before saying it's
        // done. It's cheap, so making the whole understanding costs microseconds; the turn is for her, not for the user.
        internal static string Begin(string request)
        {
            request = Regex.Replace(request ?? "", @"\s+", " ").Trim();
            lock (gate)
            {
                if (!On || request.Length == 0) { ClearLocked(); return ""; }
                Ensure();
                long now = Now();
                // A job begins right after the request that asked for it. It's the same command, so the wheel keeps its turn.
                if (command != null && string.Equals(command, request, StringComparison.OrdinalIgnoreCase) && now - commandAt < 2500)
                    return UnderstandingLocked();
                command = request;
                commandAt = now;
                spinAt = now;
                movements = 0;
                int wheels = WheelCount;
                facets = new string[wheels];
                // Movement one: the room she's walking into.
                if (wheels > 0) facets[0] = ContextFacetLocked();
                movements = Math.Min(1, wheels);
                // Movement two: what the words are really asking for.
                if (wheels > 1) facets[1] = IntentFacetLocked(request);
                movements = Math.Min(2, wheels);
                // Movement three: the way through, from what worked here before.
                if (wheels > 2) facets[2] = ApproachFacetLocked();
                movements = Math.Min(3, wheels);
                // Movement four: the check that keeps a near miss from being called done.
                if (wheels > 3) facets[3] = CheckFacetLocked();
                movements = wheels;
                understanding = JoinLocked();
                data.Commands++;
                data.Updated = now;
                Save(false);
                return understanding;
            }
        }

        // A job can start without a new request, on a stream restart or from another tool. It keeps the request that's
        // already turning the anel, and only starts one of its own when there wasn't any.
        internal static void Keep(string task)
        {
            lock (gate)
            {
                if (!On || command != null || string.IsNullOrWhiteSpace(task)) return;
            }
            Begin(task);
        }

        // A correction or a change they say mid-job adds to the turn already in progress instead of starting a new one.
        internal static void Heard(string words)
        {
            if (!On || string.IsNullOrWhiteSpace(words) || command == null) return;
            lock (gate)
            {
                if (facets == null || facets.Length == 0) return;
                string line = Cap(Clean(words), 220);
                facets[facets.Length - 1] = "They added mid-job: " + line + ". That wins over the rest of the request it contradicts.";
                if (understanding != null) understanding = JoinLocked();
                data.Updated = Now();
                Save(false);
            }
        }

        internal static void Finished(string outcome, string said)
        {
            if (!On) return;
            lock (gate)
            {
                Ensure();
                if (command == null) return;
                data.Answered++;
                if (outcome == "failed" || outcome == "stopped")
                    AddLessonLocked("That request ended " + outcome + ": " + Cap(Masked(said), 140) + ". Next time change the approach, not just the words.", 1);
                command = null;
                understanding = null;
                facets = null;
                movements = 0;
                lastAction = null;
                lastFailed = false;
                streak = 0;
                // Done means a victory spin of the anel, so the meme lands when she actually pulled it off.
                if (outcome == "done") burstAt = Now();
                data.Updated = Now();
                Save(outcome == "failed" || outcome == "stopped");
            }
        }

        // Every step she takes goes through here, so a wheel turn can't end with the same miss twice. Success after a miss
        // is kept too: that's the adaptation, not just the apology.
        internal static void Saw(string tool, ToolArguments args, string result, bool failed)
        {
            if (!On || !Remembering || string.IsNullOrEmpty(tool) || command == null) return;
            string action = ActionOf(tool, args);
            if (action.Length == 0) return;
            lock (gate)
            {
                Ensure();
                bool learned = false;
                bool repeat = string.Equals(action, lastAction, StringComparison.OrdinalIgnoreCase);
                if (failed)
                {
                    streak = repeat && lastFailed ? streak + 1 : 1;
                    if (streak == 2)
                    {
                        AddLessonLocked("In " + WhereWordsLocked() + ", " + action + " failed twice: " + Cap(Masked(result), 130) + ". Change one thing next time.", 2);
                        data.Adaptations++;
                        learned = true;
                    }
                    lastFailed = true;
                }
                else
                {
                    if (lastFailed && lastAction != null && !repeat)
                    {
                        AddLessonLocked("In " + WhereWordsLocked() + ", after " + lastAction + " failed, " + action + " worked. Try that first.", 2);
                        data.Adaptations++;
                        learned = true;
                    }
                    lastFailed = false;
                    streak = 0;
                }
                lastAction = action;
                data.Updated = Now();
                Save(learned);
            }
        }

        // Teaching is the strongest lesson of all: when they tell her why something is done, it belongs to the place they
        // told her in, and to everywhere at once when it's plainly about how they like things done.
        internal static void Taught(string text)
        {
            if (!On || !Remembering || string.IsNullOrWhiteSpace(text)) return;
            lock (gate)
            {
                Ensure();
                AddLessonLocked(Cap(Clean(text), 220), 3);
                data.Adaptations++;
                Save(true);
            }
        }

        // What the first turn of every job carries. It's her own understanding, not an order from the mod, and the screen
        // still wins whenever the two disagree.
        internal static string Brief()
        {
            lock (gate)
            {
                if (!On || command == null || string.IsNullOrEmpty(understanding)) return "";
                StringBuilder text = new StringBuilder("MARORAGA - your golden anel spun once for this request before you act. This is what you understood, not a script; if the screen disagrees, the screen wins.");
                if (!string.IsNullOrEmpty(understanding)) text.Append("\n" + understanding);
                List<MaroragaLesson> lessons = RelevantLocked();
                if (lessons.Count > 0)
                {
                    text.Append("\nWhat you've already learned " + WhereWordsLocked() + ":");
                    for (int i = 0; i < lessons.Count; i++) text.Append("\n- " + Cap(lessons[i].Text, 220));
                }
                return "\n" + Cap(text.ToString(), 2200);
            }
        }

        // A one-line status for her own UI and for checks.
        internal static string Hud()
        {
            lock (gate)
            {
                if (!On) return null;
                Ensure();
                string where = string.IsNullOrEmpty(currentKind) ? "waiting" : KindWords(currentKind);
                return "Maroraga · " + movements + "/" + (facets == null ? WheelCount : facets.Length) + " · " + where;
            }
        }

        private static void ClearLocked()
        {
            command = null;
            understanding = null;
            facets = null;
            movements = 0;
            lastAction = null;
            lastFailed = false;
            streak = 0;
        }

        private static string ContextFacetLocked()
        {
            StringBuilder text = new StringBuilder();
            MaroragaSign sign = currentApp == null ? null : SignLocked(currentApp);
            if (sign == null || sign.Seconds < 5)
                text.Append("I've only just started watching, so I'll read the screen before I move.");
            else
            {
                text.Append("I've been watching " + AppWords(sign.App) + " (" + KindWords(sign.Kind ?? currentKind) + ") for " + Span(sign.Seconds) + ", and it's in front now.");
                if (previousApp != null && !string.Equals(previousApp, currentApp, StringComparison.OrdinalIgnoreCase))
                    text.Append(" Before this they were in " + AppWords(previousApp) + ".");
            }
            MaroragaSign most = TopSignLocked();
            if (most != null && sign != null && !string.Equals(most.App, sign.App, StringComparison.OrdinalIgnoreCase) && most.Seconds >= 60)
                text.Append(" Most of their time lately: " + AppWords(most.App) + " (" + Span(most.Seconds) + ").");
            if (data.ActiveSeconds >= 120)
                text.Append(" They're " + (active ? "active" : "away from the keyboard") + " right now, so they'll see what I do.");
            return text.ToString();
        }

        private static string IntentFacetLocked(string request)
        {
            string purpose = Purpose(request);
            return "They asked " + Voice.Quote(Cap(request, 240)) + ". What they actually want: " + purpose;
        }

        private static string ApproachFacetLocked()
        {
            List<MaroragaLesson> lessons = RelevantLocked();
            StringBuilder text = new StringBuilder("Read the screen, then take one step at a time and look after each one.");
            if (lessons.Count > 0) text.Append(" " + lessons[0].Text);
            else text.Append(" On this PC, the same move twice with nothing changed is a rut: change the key, the button or the place I'm looking.");
            return text.ToString();
        }

        private static string CheckFacetLocked()
        {
            List<MaroragaLesson> lessons = RelevantLocked();
            string check = "Before I say it's done, check the screen really shows it, and keep anything I typed private.";
            if (lessons.Count > 1) check += " Remember: " + lessons[1].Text;
            else if (lessons.Count == 1) check += " Remember: " + lessons[0].Text;
            return check;
        }

        // What the words are for, in plain terms. It's a short read, not a parser: the brain still decides, and this only
        // stops her treating the steps they named as the whole job.
        private static string Purpose(string request)
        {
            string words = " " + Regex.Replace(request ?? "", @"\s+", " ").ToLowerInvariant() + " ";
            if (Regex.IsMatch(words, @"\b(send|message|reply|email|dm|text|tell)\b"))
                return "the message to reach the person, not just the box it's typed in.";
            if (Regex.IsMatch(words, @"\b(search|find|look up|google|research|check)\b"))
                return "the answer itself, not the search page or the first link.";
            if (Regex.IsMatch(words, @"\b(open|launch|start|play)\b"))
                return "that thing open and ready for them, then leave it alone.";
            if (Regex.IsMatch(words, @"\b(fix|set|turn|change|enable|disable|adjust)\b"))
                return "the change made and saved, then checked once.";
            if (Regex.IsMatch(words, @"\b(make|write|create|build|draw|record)\b"))
                return "the finished thing, saved where they'll look for it.";
            if (Regex.IsMatch(words, @"\b(clean|organise|organize|move|copy|sort|rename)\b"))
                return "the thing where they wanted it, with nothing of theirs lost.";
            if (Regex.IsMatch(words, @"\b(read|summarise|summarize|explain)\b"))
                return "the point of it, in words they can use.";
            if (Regex.IsMatch(words, @"\b(buy|order|book|download|install)\b"))
                return "the right thing, at the size and price they said, without surprises.";
            return "to have it done, not to watch me try. Their words are the goal; the steps are mine.";
        }

        private static string JoinLocked()
        {
            if (facets == null) return "";
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < facets.Length; i++)
            {
                if (string.IsNullOrEmpty(facets[i])) continue;
                if (text.Length > 0) text.Append("\n");
                text.Append("Spin " + (i + 1) + " of " + facets.Length + " - " + facets[i]);
            }
            return text.ToString();
        }

        private static string UnderstandingLocked()
        {
            if (!On || command == null) return "";
            if (string.IsNullOrEmpty(understanding)) understanding = JoinLocked();
            return understanding;
        }

        private static MaroragaSign SignLocked(string app)
        {
            for (int i = 0; i < data.Signs.Count; i++)
                if (data.Signs[i] != null && string.Equals(data.Signs[i].App, app, StringComparison.OrdinalIgnoreCase)) return data.Signs[i];
            MaroragaSign sign = new MaroragaSign { App = app, At = Now() };
            data.Signs.Add(sign);
            return sign;
        }

        private static MaroragaSign TopSignLocked()
        {
            MaroragaSign best = null;
            for (int i = 0; i < data.Signs.Count; i++)
                if (data.Signs[i] != null && (best == null || data.Signs[i].Seconds > best.Seconds)) best = data.Signs[i];
            return best;
        }

        private static List<MaroragaLesson> RelevantLocked()
        {
            List<MaroragaLesson> found = new List<MaroragaLesson>();
            for (int i = 0; i < data.Lessons.Count; i++)
            {
                MaroragaLesson lesson = data.Lessons[i];
                if (lesson == null || string.IsNullOrWhiteSpace(lesson.Text)) continue;
                bool here = (currentApp != null && string.Equals(lesson.App, currentApp, StringComparison.OrdinalIgnoreCase))
                    || (currentKind != null && string.Equals(lesson.Where, currentKind, StringComparison.OrdinalIgnoreCase));
                if (here) found.Add(lesson);
            }
            found.Sort(delegate(MaroragaLesson one, MaroragaLesson other)
            {
                int score = other.Score.CompareTo(one.Score);
                return score != 0 ? score : other.At.CompareTo(one.At);
            });
            if (found.Count > 3) found.RemoveRange(3, found.Count - 3);
            return found;
        }

        private static void AddLessonLocked(string text, int score)
        {
            if (!Remembering) return;
            string line = Regex.Replace(text ?? "", @"\s+", " ").Trim();
            if (line.Length == 0) return;
            if (line.Length > 260) line = line.Substring(0, 257) + "...";
            MaroragaLesson same = null;
            for (int i = 0; i < data.Lessons.Count; i++)
            {
                MaroragaLesson kept = data.Lessons[i];
                if (kept != null && string.Equals(kept.Text, line, StringComparison.OrdinalIgnoreCase)) { same = kept; break; }
            }
            if (same != null)
            {
                same.At = Now();
                if (score > same.Score) same.Score = score;
                return;
            }
            data.Lessons.Add(new MaroragaLesson
            {
                Where = currentKind ?? activeKind,
                App = currentApp,
                Text = line,
                Score = score,
                At = Now()
            });
            if (data.Lessons.Count > 120) data.Lessons.RemoveAt(0);
            // The anel gives a little spin whenever she actually adapts: the meme says she learned.
            burstAt = Now();
        }

        private static string WhereWordsLocked()
        {
            string kind = currentKind ?? activeKind;
            return KindWords(kind);
        }

        private static string ActionOf(string tool, ToolArguments args)
        {
            if (args == null) args = new ToolArguments();
            if (tool == "type") return "typing in " + (string.IsNullOrWhiteSpace(args.Target) ? "the box" : Cap(Clean(args.Target), 40));
            if (tool == "click" || tool == "double_click" || tool == "right_click" || tool == "middle_click")
            {
                string label = !string.IsNullOrWhiteSpace(args.Target) ? Cap(Clean(args.Target), 40) : null;
                return label == null ? "a " + tool.Replace('_', ' ') : "clicking " + label;
            }
            if (tool == "press") return "pressing " + (string.IsNullOrWhiteSpace(args.Name) ? "a key" : Cap(Clean(args.Name), 30));
            if (tool == "shortcut" || tool == "keys") return "the " + (string.IsNullOrWhiteSpace(args.Name) ? "shortcut" : Cap(Clean(args.Name), 40));
            if (tool == "walk") return "walking " + (string.IsNullOrWhiteSpace(args.Direction) ? "in the game" : Cap(Clean(args.Direction), 20));
            if (tool == "mouse_hold") return "holding the " + (string.IsNullOrWhiteSpace(args.Button) ? "mouse" : Cap(Clean(args.Button), 20) + " mouse button");
            if (tool == "scroll") return "scrolling " + (string.IsNullOrWhiteSpace(args.Way) ? "" : Clean(args.Way) + " ").Trim();
            if (tool == "open_app") return "opening " + Cap(Clean(args.Name), 40);
            if (tool == "open_website") return "opening " + Cap(Clean(args.Site), 60);
            if (tool == "find_file") return "searching for " + Cap(Clean(args.Name), 50);
            if (tool == "learn" || tool == "remember") return "";
            string name = Regex.Replace(tool ?? "", "_", " ").Trim();
            return name.Length == 0 ? "" : name;
        }

        private static bool Front(out string app, out string title)
        {
            app = null;
            title = null;
            IntPtr window = GetForegroundWindow();
            if (window == IntPtr.Zero) return false;
            int process;
            GetWindowThreadProcessId(window, out process);
            if (process <= 0) return false;
            try
            {
                using (Process found = Process.GetProcessById(process)) app = CleanApp(found.ProcessName);
            }
            catch (Exception) { return false; }
            StringBuilder text = new StringBuilder(512);
            GetWindowText(window, text, text.Capacity);
            title = text.ToString();
            return !string.IsNullOrEmpty(app);
        }

        private static string CleanApp(string app)
        {
            app = (app ?? "").Trim().ToLowerInvariant();
            if (app.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) app = app.Substring(0, app.Length - 4);
            return app;
        }

        private static string Kind(string app, string title)
        {
            app = CleanApp(app);
            title = (title ?? "").ToLowerInvariant();
            if (app == "code" || app == "code - insiders" || app == "devenv" || app == "rider" || app == "idea64" || app == "pycharm64"
                || app == "webstorm64" || app == "sublime_text" || app == "notepad++" || app == "eclipse" || app == "studio64" || app == "vim" || app == "nvim")
                return "code";
            if (app == "chrome" || app == "msedge" || app == "firefox" || app == "brave" || app == "opera" || app == "vivaldi" || app == "arc")
                return "browser";
            if (app == "discord" || app == "telegram" || app == "whatsapp" || app == "slack" || app == "teams" || app == "zoom" || app == "skype")
                return "chat";
            if (app == "steam" || app == "steamwebhelper" || app == "epicgameslauncher" || app == "riotclientservices" || app == "battle.net"
                || app == "origin" || app == "eadesktop" || app == "ubisoftconnect" || app == "goggalaxy" || app == "robloxplayerbeta"
                || app == "minecraft" || title.Contains("minecraft"))
                return "game";
            if (app == "spotify" || app == "vlc" || app == "mpv" || app == "mpc-hc64" || app == "musicbee" || app == "itunes" || app == "wmplayer")
                return "media";
            if (app == "winword" || app == "excel" || app == "powerpnt" || app == "acrobat" || app == "notepad" || app == "onenote"
                || app == "obsidian" || app == "notion" || app == "wordpad")
                return "document";
            if (app == "windowsterminal" || app == "cmd" || app == "powershell" || app == "pwsh" || app == "wt")
                return "terminal";
            if (app == "explorer") return "files";
            return "other";
        }

        private static string KindWords(string kind)
        {
            switch (kind)
            {
                case "code": return "in a code editor";
                case "browser": return "in a browser";
                case "chat": return "in a chat app";
                case "game": return "in a game";
                case "media": return "in a media player";
                case "document": return "in a document";
                case "terminal": return "in a terminal";
                case "files": return "in their file manager";
                default: return "on their PC";
            }
        }

        private static string AppWords(string app)
        {
            app = CleanApp(app);
            switch (app)
            {
                case "code": return "their code editor";
                case "chrome": return "Chrome";
                case "msedge": return "Edge";
                case "firefox": return "Firefox";
                case "brave": return "Brave";
                case "discord": return "Discord";
                case "telegram": return "Telegram";
                case "whatsapp": return "WhatsApp";
                case "slack": return "Slack";
                case "teams": return "Teams";
                case "explorer": return "File Explorer";
                case "windowsterminal": return "Windows Terminal";
                case "cmd": return "Command Prompt";
                case "powershell": return "PowerShell";
                case "spotify": return "Spotify";
                case "vlc": return "VLC";
                case "steam": return "Steam";
                case "winword": return "Word";
                case "excel": return "Excel";
                case "powerpnt": return "PowerPoint";
                case "notepad": return "Notepad";
                default: return app.Length == 0 ? "an app" : app;
            }
        }

        private static string Span(int seconds)
        {
            if (seconds < 60) return Math.Max(1, seconds) + " s";
            if (seconds < 3600) return (seconds / 60) + " min";
            int hours = seconds / 3600;
            int minutes = (seconds % 3600) / 60;
            return minutes == 0 ? hours + " h" : hours + " h " + minutes + " min";
        }

        private static string Cap(string text, int length)
        {
            text = Regex.Replace(text ?? "", @"\s+", " ").Trim();
            return text.Length <= length ? text : text.Substring(0, Math.Max(0, length - 1)).TrimEnd() + "…";
        }

        private static string Clean(string text)
        {
            return Regex.Replace(text ?? "", @"\s+", " ").Trim();
        }

        private static string Masked(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            try { return Computer.Mask(text); }
            catch (Exception) { return text; }
        }

        private static long Now()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }

        private static void Ensure()
        {
            if (data == null) data = MaroragaData.New();
            data.Repair();
        }

        private static MaroragaData Read()
        {
            if (file == null || !File.Exists(file)) return null;
            try
            {
                using (FileStream stream = File.OpenRead(file))
                    return (MaroragaData)new DataContractJsonSerializer(typeof(MaroragaData)).ReadObject(stream);
            }
            catch (Exception) { return null; }
        }

        // Saving a habit every second would keep the disk busy; the turn itself and every new lesson saves at once.
        private static void Save(bool force)
        {
            if (file == null || data == null) return;
            long now = Now();
            if (!force && now - lastSave < 20000) return;
            lastSave = now;
            data.Updated = now;
            try
            {
                string folder = Path.GetDirectoryName(file);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                string temporary = file + ".tmp";
                using (FileStream stream = File.Create(temporary))
                    new DataContractJsonSerializer(typeof(MaroragaData)).WriteObject(stream, data);
                if (File.Exists(file))
                {
                    try { File.Replace(temporary, file, null); }
                    catch (Exception) { File.Copy(temporary, file, true); File.Delete(temporary); }
                }
                else File.Move(temporary, file);
            }
            catch (Exception) { }
        }

        // Her smoke test starts from a clean page without touching a real user's file.
        internal static void Forget()
        {
            lock (gate)
            {
                data = MaroragaData.New();
                currentApp = previousApp = currentKind = activeKind = command = understanding = null;
                lastAction = null;
                lastFailed = false;
                streak = 0;
                movements = 0;
                lastSample = lastSave = commandAt = spinAt = 0;
                active = false;
                if (file != null) { try { if (File.Exists(file)) File.Delete(file); } catch (Exception) { } }
            }
        }

        internal static void Reload()
        {
            lock (gate)
            {
                data = Read();
                Ensure();
            }
        }

        // The anel is his own art: the gold spheres he sent, strung on gold frames and turning around the ring's own
        // axis, so it reads as a real ring riding on her head instead of a picture spinning flat. It's a meme first: a
        // slow idle turn, one fast full turn for every request, and a celebration turn when she finishes the job or
        // learns something new. Drawn after her, so her hair never hides it.
        internal static void Paint(Graphics graphics, float cx, float cy, float size, double now)
        {
            if (!On || graphics == null || size < 40) return;
            Bitmap art = BallArt();
            if (art == null) return;
            double turn = Turn;
            bool turning = command != null && turn < Math.PI * 2;
            double since = burstAt == 0 ? -1 : (Now() - burstAt) / 1000.0;
            double burst = since >= 0 && since < 1.1 ? (1.0 - since / 1.1) * Math.PI * 2 : 0;
            float radius = Math.Max(9f, size * 0.118f);
            // Seen from just above her head, so the ring reads as a shallow ellipse and its far side sits higher.
            float squash = 0.52f + 0.03f * (float)Math.Sin(now * 1.1);
            float tilt = -0.10f;
            float depthScale = (float)Math.Sqrt(1f - squash * squash);
            float pulse = 1f + (turning ? 0.05f * (float)Math.Sin(now * 13.0) : 0f);
            if (burst > 0) pulse += 0.14f * (float)(burst / (Math.PI * 2));
            float spin = (float)(now * 0.55 + turn + burst);
            float bob = (float)Math.Sin(now * 2.1) * radius * 0.05f;
            float cosT = (float)Math.Cos(tilt), sinT = (float)Math.Sin(tilt);
            int count = 8;
            PointF[] spots = new PointF[count];
            float[] depths = new float[count];
            float[] sizes = new float[count];
            for (int i = 0; i < count; i++)
            {
                float at = spin + i * (float)(Math.PI * 2.0 / count);
                bool small = (i & 1) == 1;
                // The small spheres sit a little inside the big ones, the way his ring alternates them.
                float rr = small ? 0.90f : 1f;
                float x = (float)Math.Cos(at) * rr;
                float y = (float)Math.Sin(at) * rr;
                // Positive depth is the far half of the ring, negative the near half.
                float depth = y * depthScale;
                float near = 1f / (1f + 0.30f * depth);
                float px = x * radius * pulse * near;
                float py = -y * squash * radius * pulse * near;
                spots[i] = new PointF(cx + px * cosT - py * sinT, cy + bob + px * sinT + py * cosT);
                depths[i] = depth;
                sizes[i] = radius * (small ? 0.23f : 0.33f) * near * pulse;
            }
            GraphicsState saved = graphics.Save();
            try
            {
                graphics.ResetTransform();
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.CompositingMode = CompositingMode.SourceOver;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                // Spheres and the frames between them, drawn far to near so the near half passes in front: that's the
                // depth cue that makes the turn read sideways.
                int total = count * 2;
                int[] order = new int[total];
                float[] depthOf = new float[total];
                for (int i = 0; i < total; i++)
                {
                    order[i] = i;
                    depthOf[i] = i < count ? depths[i] : (depths[i - count] + depths[(i - count + 1) % count]) / 2f;
                }
                Array.Sort(order, delegate(int one, int other) { return depthOf[other].CompareTo(depthOf[one]); });
                for (int k = 0; k < total; k++)
                {
                    int i = order[k];
                    if (i >= count) continue;
                    int next = (i + 1) % count;
                    float span = (float)Math.Sqrt(Sqr(spots[next].X - spots[i].X) + Sqr(spots[next].Y - spots[i].Y));
                    if (span < 1f) continue;
                    // The frame runs from the edge of one sphere to the edge of the next.
                    float start = Math.Min(0.46f, sizes[i] * 0.55f / span);
                    float end = 1f - Math.Min(0.46f, sizes[next] * 0.55f / span);
                    if (start >= end) continue;
                    Bar(graphics,
                        spots[i].X + (spots[next].X - spots[i].X) * start,
                        spots[i].Y + (spots[next].Y - spots[i].Y) * start,
                        spots[i].X + (spots[next].X - spots[i].X) * end,
                        spots[i].Y + (spots[next].Y - spots[i].Y) * end,
                        Math.Max(1f, radius * 0.11f * (0.7f + 0.3f * (sizes[i] + sizes[next]) / (radius * 0.54f))),
                        depthOf[i]);
                }
                // The spheres go on top of the frames, still far to near.
                for (int k = 0; k < total; k++)
                {
                    int i = order[k];
                    if (i >= count) continue;
                    Ball(graphics, art, spots[i].X, spots[i].Y, sizes[i], depths[i]);
                }
                if (turning || (since >= 0 && since < 0.9))
                {
                    for (int spark = 0; spark < 6; spark++)
                    {
                        double at = spin * 1.9 + spark * Math.PI / 3;
                        float px = (float)Math.Cos(at) * radius * 1.34f;
                        float py = -(float)Math.Sin(at) * squash * radius * 1.34f;
                        float dot = Math.Max(0.9f, radius * 0.09f) * (0.8f + 0.4f * (float)Math.Sin(now * 9 + spark));
                        using (Brush sparkle = new SolidBrush(Color.FromArgb(235, 255, 240, 170)))
                            graphics.FillEllipse(sparkle, cx + px * cosT - py * sinT - dot, cy + bob + px * sinT + py * cosT - dot, dot * 2f, dot * 2f);
                    }
                }
            }
            finally { graphics.Restore(saved); }
        }

        private static float Sqr(float value) { return value * value; }

        private static float Sqr(double value) { return (float)(value * value); }

        // One sphere of his ring, drawn from the art itself, so the gold, the sharp highlight and the dark rim are his.
        // The far half is a touch smaller (already placed that way) and dimmed, which is what depth looks like.
        private static void Ball(Graphics graphics, Bitmap art, float x, float y, float radius, float depth)
        {
            if (radius < 0.8f) return;
            graphics.DrawImage(art, x - radius, y - radius, radius * 2f, radius * 2f);
            float dim = Math.Max(0f, depth);
            if (dim > 0.02f)
                using (Brush shade = new SolidBrush(Color.FromArgb((int)(86 * dim), 34, 16, 0)))
                    graphics.FillEllipse(shade, x - radius, y - radius, radius * 2f, radius * 2f);
        }

        // One gold frame between two spheres: a dark edge, a bright body and a thin highlight, all dimmed with depth.
        private static void Bar(Graphics graphics, float x1, float y1, float x2, float y2, float width, float depth)
        {
            if (width < 0.9f) return;
            using (Pen edge = new Pen(Dim(Color.FromArgb(146, 92, 6), depth), width))
            {
                edge.StartCap = LineCap.Round; edge.EndCap = LineCap.Round;
                graphics.DrawLine(edge, x1, y1, x2, y2);
            }
            using (Pen body = new Pen(Dim(Color.FromArgb(255, 208, 48), depth), Math.Max(1f, width * 0.60f)))
            {
                body.StartCap = LineCap.Round; body.EndCap = LineCap.Round;
                graphics.DrawLine(body, x1, y1, x2, y2);
            }
            using (Pen shine = new Pen(Dim(Color.FromArgb(250, 244, 176), depth), Math.Max(0.6f, width * 0.18f)))
            {
                shine.StartCap = LineCap.Round; shine.EndCap = LineCap.Round;
                graphics.DrawLine(shine, x1, y1 - width * 0.14f, x2, y2 - width * 0.14f);
            }
        }

        private static Color Dim(Color color, float depth)
        {
            float shade = 1f / (1f + 0.34f * Math.Max(0f, depth));
            if (shade > 0.99f) return color;
            return Color.FromArgb(color.A, (int)(color.R * shade), (int)(color.G * shade), (int)(color.B * shade));
        }

        // The mod ships one of his own gold spheres inside Capri, cut from the art he sent, so no file is fetched and the
        // anel never drifts from his ring.
        private static Bitmap BallArt()
        {
            if (ballTried) return ballArt;
            ballTried = true;
            try
            {
                using (Stream stream = typeof(Maroraga).Assembly.GetManifestResourceStream("Capri.Mods.anel_maroraga_ball.png"))
                    if (stream != null) ballArt = new Bitmap(stream);
            }
            catch (Exception) { ballArt = null; }
            return ballArt;
        }

        internal static bool AnelLoaded { get { return BallArt() != null; } }

    }
}
