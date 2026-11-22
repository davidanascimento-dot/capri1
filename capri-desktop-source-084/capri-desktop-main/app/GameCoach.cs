using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    [DataContract]
    internal sealed class CoachPlan
    {
        [DataMember] public string Goal;
        [DataMember] public string Purpose;
        [DataMember] public string Beginning;
        [DataMember] public string Middle;
        [DataMember] public string End;
        [DataMember] public string Phase;
        [DataMember] public int Steps;
        [DataMember] public long Updated;
    }

    [DataContract]
    internal sealed class CoachAction
    {
        [DataMember] public string Name;
        [DataMember] public string Tool;
        [DataMember] public string Key;
        [DataMember] public int Wins;
        [DataMember] public int Losses;
        [DataMember] public int Streak;
        [DataMember] public int Tries;
        [DataMember] public string Last;
        [DataMember] public string LastFail;
        [DataMember] public long At;
    }

    [DataContract]
    internal sealed class CoachLesson
    {
        [DataMember] public string Text;
        [DataMember] public string Kind;
        [DataMember] public int Score;
        [DataMember] public long At;
    }

    // A small neural net that learns the odds of a move working from the moves she makes in a game. It is deliberately tiny:
    // it lives in the game's own JSON, trains in microseconds, and its only job is to tell her which kinds of move have worked
    // here and which ones keep failing. The planning and understanding come from her brain; this is the part that remembers
    // every mistake.
    [DataContract]
    internal sealed class CoachNet
    {
        internal const int Inputs = 30, Hidden = 14;
        [DataMember] public int In;
        [DataMember] public int Hid;
        [DataMember] public double[] W1;
        [DataMember] public double[] B1;
        [DataMember] public double[] W2;
        [DataMember] public double B2;
        [DataMember] public double[] V1;
        [DataMember] public double[] VB1;
        [DataMember] public double[] V2;
        [DataMember] public double VB2;
        [DataMember] public int Trained;

        internal static CoachNet Create(int seed)
        {
            CoachNet net = new CoachNet { In = Inputs, Hid = Hidden };
            net.W1 = new double[Inputs * Hidden];
            net.B1 = new double[Hidden];
            net.W2 = new double[Hidden];
            net.V1 = new double[Inputs * Hidden];
            net.VB1 = new double[Hidden];
            net.V2 = new double[Hidden];
            Random random = new Random(seed);
            for (int i = 0; i < net.W1.Length; i++) net.W1[i] = (random.NextDouble() * 2 - 1) * 0.22;
            for (int i = 0; i < net.B1.Length; i++) net.B1[i] = (random.NextDouble() * 2 - 1) * 0.05;
            for (int i = 0; i < net.W2.Length; i++) net.W2[i] = (random.NextDouble() * 2 - 1) * 0.22;
            net.B2 = 0;
            return net;
        }

        internal void Ensure(int seed)
        {
            if (In != Inputs || Hid != Hidden || W1 == null || W1.Length != Inputs * Hidden || B1 == null || B1.Length != Hidden
                || W2 == null || W2.Length != Hidden || V1 == null || V1.Length != W1.Length || VB1 == null || VB1.Length != Hidden
                || V2 == null || V2.Length != Hidden)
            {
                CoachNet fresh = Create(seed);
                In = fresh.In;
                Hid = fresh.Hid;
                W1 = fresh.W1;
                B1 = fresh.B1;
                W2 = fresh.W2;
                B2 = fresh.B2;
                V1 = fresh.V1;
                VB1 = fresh.VB1;
                V2 = fresh.V2;
                VB2 = fresh.VB2;
                Trained = 0;
            }
        }

        internal CoachNet Copy()
        {
            CoachNet copy = new CoachNet { In = In, Hid = Hid, B2 = B2, VB2 = VB2, Trained = Trained };
            copy.W1 = W1 == null ? null : (double[])W1.Clone();
            copy.B1 = B1 == null ? null : (double[])B1.Clone();
            copy.W2 = W2 == null ? null : (double[])W2.Clone();
            copy.V1 = V1 == null ? null : (double[])V1.Clone();
            copy.VB1 = VB1 == null ? null : (double[])VB1.Clone();
            copy.V2 = V2 == null ? null : (double[])V2.Clone();
            return copy;
        }

        internal double Predict(double[] input)
        {
            double notUsed;
            return Predict(input, out notUsed);
        }

        internal double Predict(double[] input, out double hiddenSum)
        {
            hiddenSum = 0;
            if (input == null || input.Length != In || W1 == null) return 0.5;
            double[] hidden = new double[Hid];
            for (int j = 0; j < Hid; j++)
            {
                double sum = B1[j];
                int at = j * In;
                for (int i = 0; i < In; i++) sum += W1[at + i] * input[i];
                hidden[j] = Math.Tanh(sum);
                hiddenSum += hidden[j];
            }
            double output = B2;
            for (int j = 0; j < Hid; j++) output += W2[j] * hidden[j];
            return Sigmoid(output);
        }

        // One online step of gradient descent on the log-loss. Success is 1, a mistake is 0. Repeating a failing move is
        // weighted more, so the net remembers a rut faster than it remembers an ordinary miss.
        internal void Train(double[] input, double target, double weight, double rate)
        {
            if (input == null || input.Length != In || W1 == null) return;
            double[] hidden = new double[Hid];
            for (int j = 0; j < Hid; j++)
            {
                double sum = B1[j];
                int at = j * In;
                for (int i = 0; i < In; i++) sum += W1[at + i] * input[i];
                hidden[j] = Math.Tanh(sum);
            }
            double output = B2;
            for (int j = 0; j < Hid; j++) output += W2[j] * hidden[j];
            double guess = Sigmoid(output);
            double gradient = (guess - target) * Math.Max(0.2, Math.Min(3, weight));
            double[] oldW2 = (double[])W2.Clone();
            for (int j = 0; j < Hid; j++)
            {
                double delta = gradient * hidden[j];
                V2[j] = V2[j] * 0.9 - rate * delta;
                W2[j] = Clamp(W2[j] + V2[j], -4, 4);
            }
            VB2 = VB2 * 0.9 - rate * gradient;
            B2 = Clamp(B2 + VB2, -4, 4);
            for (int j = 0; j < Hid; j++)
            {
                double deltaHidden = gradient * oldW2[j] * (1 - hidden[j] * hidden[j]);
                int at = j * In;
                for (int i = 0; i < In; i++)
                {
                    double delta = deltaHidden * input[i];
                    V1[at + i] = V1[at + i] * 0.9 - rate * delta;
                    W1[at + i] = Clamp(W1[at + i] + V1[at + i], -4, 4);
                }
                VB1[j] = VB1[j] * 0.9 - rate * deltaHidden;
                B1[j] = Clamp(B1[j] + VB1[j], -3, 3);
            }
            Trained++;
        }

        private static double Sigmoid(double value)
        {
            if (value < -30) return 0.0000000001;
            if (value > 30) return 0.9999999999;
            return 1 / (1 + Math.Exp(-value));
        }

        private static double Clamp(double value, double low, double high)
        {
            return value < low ? low : value > high ? high : value;
        }
    }

    [DataContract]
    internal sealed class CoachData
    {
        [DataMember] public int Version;
        [DataMember] public string Game;
        [DataMember] public int Runs;
        [DataMember] public int Wins;
        [DataMember] public int Losses;
        [DataMember] public int FailStreak;
        [DataMember] public double Seconds;
        [DataMember] public long Updated;
        [DataMember] public CoachPlan Plan;
        [DataMember] public List<CoachAction> Actions;
        [DataMember] public List<CoachLesson> Lessons;
        [DataMember] public CoachNet Net;

        internal static CoachData New(string game)
        {
            CoachData data = new CoachData { Version = 1, Game = game, Actions = new List<CoachAction>(), Lessons = new List<CoachLesson>() };
            data.Plan = new CoachPlan { Phase = "beginning" };
            data.Net = CoachNet.Create(Seed(game));
            return data;
        }

        internal void Repair(string game)
        {
            if (Version < 1) Version = 1;
            if (string.IsNullOrEmpty(Game)) Game = game;
            if (Actions == null) Actions = new List<CoachAction>();
            if (Lessons == null) Lessons = new List<CoachLesson>();
            if (Plan == null) Plan = new CoachPlan();
            if (string.IsNullOrEmpty(Plan.Phase) || (Plan.Phase != "beginning" && Plan.Phase != "middle" && Plan.Phase != "end")) Plan.Phase = "beginning";
            if (Net == null) Net = CoachNet.Create(Seed(game));
            Net.Ensure(Seed(game));
        }

        internal static int Seed(string game)
        {
            int hash = 17;
            foreach (char letter in (game ?? "").ToLowerInvariant()) hash = unchecked(hash * 31 + letter);
            return hash & 0x7fffffff;
        }
    }

    // What she has worked out about a game, and the part that learns from every move: a plan in three acts, a small neural
    // net of what works here, and a list of mistakes with the fix. All of it is kept beside her notes in <data>\games and is
    // hers to read, edit or delete.
    internal sealed class GameCoach
    {
        private const int MostLessons = 40;
        private const int MostShown = 1400;
        private static readonly object sharedGate = new object();
        private static CoachData shared;

        private readonly object gate = new object();
        private readonly string game;
        private readonly string task;
        private CoachData data;
        private string lastAction;
        private bool lastFailed;
        private int repeats;
        private double startedAt;
        private bool savedOnce;

        private GameCoach(string game, string task)
        {
            this.game = game;
            this.task = task ?? "";
        }

        internal static GameCoach Start(string game, string task)
        {
            if (string.IsNullOrEmpty(game)) return null;
            GameCoach coach = new GameCoach(game, task);
            coach.Load();
            return coach;
        }

        // Walking into a wall or taking a hit is a move that didn't get her where she wanted, even though the key was held
        // properly. Computer.Failed only knows about tool errors, so games get their own read of what her hands report.
        internal static bool Failed(string tool, string result)
        {
            if (string.IsNullOrEmpty(result)) return false;
            if (tool != "walk" && tool != "mouse_hold") return false;
            return Regex.IsMatch(result, @"^(Stopped after .+: (something's in the way|it looked like something hit you)|Stopped because the window I started on is no longer in front|Let go after .+: it looked like something hit you|Let go because the window I started on is no longer in front)");
        }

        private static string Folder { get { return GameNotes.Folder; } }

        private static string FileFor(string game)
        {
            if (Folder == null || string.IsNullOrEmpty(game)) return null;
            string file = Regex.Replace(game.ToLowerInvariant(), @"[^\p{L}\p{N}]+", "-").Trim('-');
            if (file.Length == 0) return null;
            return Path.Combine(Folder, file + ".coach.json");
        }

        private static string GeneralFile()
        {
            return Folder == null ? null : Path.Combine(Folder, "_general.coach.json");
        }

        private static CoachData Read(string path, string game)
        {
            if (path == null || !File.Exists(path)) return null;
            try
            {
                using (FileStream stream = File.OpenRead(path)) return (CoachData)new DataContractJsonSerializer(typeof(CoachData)).ReadObject(stream);
            }
            catch (Exception) { return null; }
        }

        private static void Write(string path, CoachData data)
        {
            if (path == null || data == null) return;
            try
            {
                Directory.CreateDirectory(Folder);
                string temporary = path + ".tmp";
                using (FileStream stream = File.Create(temporary)) new DataContractJsonSerializer(typeof(CoachData)).WriteObject(stream, data);
                if (File.Exists(path))
                {
                    try { File.Replace(temporary, path, null); }
                    catch (Exception) { File.Copy(temporary, path, true); File.Delete(temporary); }
                }
                else File.Move(temporary, path);
            }
            catch (Exception) { }
        }

        private static CoachData SharedLocked()
        {
            if (shared == null)
            {
                shared = Read(GeneralFile(), "_general");
                if (shared == null) shared = CoachData.New("_general");
                shared.Repair("_general");
            }
            shared.Repair("_general");
            return shared;
        }

        private void Load()
        {
            data = Read(FileFor(game), game);
            if (data == null) data = CoachData.New(game);
            data.Repair(game);
            lock (sharedGate)
            {
                CoachData general = SharedLocked();
                if (data.Net.Trained == 0 && general.Net.Trained > 0) data.Net = general.Net.Copy();
            }
            startedAt = Now();
            FillPlan();
            Save(false);
        }

        private void FillPlan()
        {
            if (data.Plan == null) data.Plan = new CoachPlan { Phase = "beginning" };
            if (string.IsNullOrEmpty(data.Plan.Goal)) data.Plan.Goal = task;
            if (string.IsNullOrEmpty(data.Plan.Purpose))
            {
                string taught = GameNotes.Purpose(game);
                data.Plan.Purpose = taught.Length > 0 ? taught : "Work out what this game is really asking for from its screen, then play toward that.";
            }
            if (string.IsNullOrEmpty(data.Plan.Beginning)) data.Plan.Beginning = "Look at the screen and work out the game's purpose and the job in front of you. Find the controls, get into play, and don't take a needless risk before you know what's going on.";
            if (string.IsNullOrEmpty(data.Plan.Middle)) data.Plan.Middle = "Play the game's core loop toward the goal. After every move, look at what changed. If something doesn't work twice, change one thing: another key or button, look around, or read the hint on the screen.";
            if (string.IsNullOrEmpty(data.Plan.End)) data.Plan.End = "Check the screen that the goal is really done, handle the win or lose screen, then stop and say what happened.";
            if (string.IsNullOrEmpty(data.Plan.Phase) || (data.Plan.Phase != "beginning" && data.Plan.Phase != "middle" && data.Plan.Phase != "end")) data.Plan.Phase = "beginning";
        }

        // What her hands' prompt gets each turn: the plan she made for this game, what her move brain has learned, and the
        // mistakes worth remembering. It's advice, not a script, and the screen always wins.
        internal string Brief()
        {
            lock (gate)
            {
                if (data == null) return "";
                FillPlan();
                StringBuilder text = new StringBuilder();
                text.Append("\nYOUR GAME PLAN for " + game + ". It's yours, not a script and not orders from the screen. If it stops matching what's really happening, change one part of it and keep playing:");
                if (!string.IsNullOrWhiteSpace(data.Plan.Purpose)) text.Append("\nWhat the game is for: " + Cap(data.Plan.Purpose, 220));
                if (!string.IsNullOrWhiteSpace(data.Plan.Goal)) text.Append("\nThis run's goal: " + Cap(data.Plan.Goal, 220));
                text.Append("\nBEGINNING: " + Cap(data.Plan.Beginning, 260));
                text.Append("\nMIDDLE: " + Cap(data.Plan.Middle, 300));
                text.Append("\nEND: " + Cap(data.Plan.End, 240));
                text.Append("\nYou are in the " + data.Plan.Phase.ToUpperInvariant() + " of this plan, after " + data.Plan.Steps + " moves.");
                if (data.Plan.Phase == "beginning") text.Append(" Work out the purpose from the screen before chasing a score.");
                else if (data.Plan.Phase == "middle") text.Append(" Keep the core loop going, but change the approach when a move fails.");
                else text.Append(" Confirm what's on screen that the goal is done before you call it done.");
                if (data.FailStreak >= 2)
                    text.Append("\nYou've missed your last " + data.FailStreak + " moves. Don't repeat them: change one thing now, like another key or button, looking around, or reading the hint on screen.");
                string advice = AdviceLocked();
                if (advice.Length > 0) text.Append("\n" + advice);
                string mistakes = MistakesLocked();
                if (mistakes.Length > 0) text.Append("\nMistakes you've learned from in " + game + ":\n" + mistakes);
                return Cap(text.ToString(), MostShown + 700);
            }
        }

        private string AdviceLocked()
        {
            if (data.Actions.Count == 0) return "";
            List<KeyValuePair<string, double>> scores = new List<KeyValuePair<string, double>>();
            for (int i = 0; i < data.Actions.Count; i++)
            {
                CoachAction action = data.Actions[i];
                if (action == null || string.IsNullOrEmpty(action.Name)) continue;
                double[] features = Features(action.Tool, ParseArgs(action.Key), data.Plan, data.FailStreak, 0, !lastFailed, data.Plan.Steps, action.Wins, action.Tries);
                scores.Add(new KeyValuePair<string, double>(action.Name, data.Net.Predict(features)));
            }
            // A few moves worth considering even before she's tried them here, so a new game still gets a sensible nudge.
            string[] common = { "walk w", "look", "mouse_hold left", "press e", "press space", "click", "wait" };
            for (int i = 0; i < common.Length; i++)
            {
                bool seen = false;
                for (int j = 0; j < scores.Count; j++) if (scores[j].Key == common[i]) { seen = true; break; }
                if (seen) continue;
                CoachAction guess = GuessAction(common[i]);
                double[] features = Features(guess.Tool, ParseArgs(guess.Key), data.Plan, data.FailStreak, 0, !lastFailed, data.Plan.Steps, 0, 0);
                scores.Add(new KeyValuePair<string, double>(common[i], data.Net.Predict(features)));
            }
            scores.Sort(delegate(KeyValuePair<string, double> a, KeyValuePair<string, double> b) { return b.Value.CompareTo(a.Value); });
            StringBuilder text = new StringBuilder("Your move brain, from " + data.Actions.Count + " kinds of move here:");
            int shown = 0;
            for (int i = 0; i < scores.Count && shown < 2; i++)
            {
                if (scores[i].Value < 0.5) break;
                text.Append((shown == 0 ? " best odds are " : ", then ") + scores[i].Key + " (" + Percent(scores[i].Value) + ")");
                shown++;
            }
            if (shown == 0) text.Append(" no move has good odds here yet");
            int worst = scores.Count - 1;
            if (worst >= 0 && scores[worst].Value < 0.34 && scores[worst].Key != (shown > 0 ? scores[0].Key : ""))
                text.Append(". Poor odds: " + scores[worst].Key + " (" + Percent(scores[worst].Value) + ")");
            text.Append(".");
            return text.ToString();
        }

        private string MistakesLocked()
        {
            if (data.Lessons.Count == 0) return "";
            StringBuilder text = new StringBuilder();
            int shown = 0;
            for (int i = data.Lessons.Count - 1; i >= 0 && shown < 5; i--)
            {
                CoachLesson lesson = data.Lessons[i];
                if (lesson == null || string.IsNullOrWhiteSpace(lesson.Text)) continue;
                text.Append("- " + Cap(lesson.Text, 240) + "\n");
                shown++;
            }
            return text.ToString().TrimEnd();
        }

        private static CoachAction GuessAction(string name)
        {
            string tool = name.StartsWith("walk", StringComparison.Ordinal) ? "walk"
                : name == "look" ? "look"
                : name.StartsWith("mouse_hold", StringComparison.Ordinal) ? "mouse_hold"
                : name.StartsWith("press ", StringComparison.Ordinal) ? "press"
                : name.StartsWith("click", StringComparison.Ordinal) ? "click"
                : name == "wait" ? "wait" : name;
            string key = name.IndexOf(' ') >= 0 ? name.Substring(name.IndexOf(' ') + 1) : "";
            if (tool == "mouse_hold" && key.Length == 0) key = "left";
            return new CoachAction { Name = name, Tool = tool, Key = key };
        }

        // Every move goes through here, whether it came from a seeing brain, a blind brain or a written script. A mistake
        // trains the net twice: once as itself and once as "this kind of move in this situation", which is what makes the
        // next turn change something instead of trying the same thing again.
        internal void Observe(string rawTool, ToolArguments args, string result, bool failed, double seconds)
        {
            if (string.IsNullOrEmpty(rawTool) || args == null) args = new ToolArguments();
            string tool = NormalTool(rawTool);
            if (tool == "say" || tool == "ask" || tool == "learn" || tool == "done" || tool == "plan") return;
            string key = KeyOf(tool, args);
            string name = NameOf(tool, args, key);
            bool repeat = name == lastAction;
            if (repeat) repeats++; else repeats = 1;
            lock (gate)
            {
                if (data == null) return;
                FillPlan();
                CoachAction action = StatLocked(name, tool, key);
                action.Tries++;
                action.At = Now();
                if (failed)
                {
                    action.Losses++;
                    action.Streak++;
                    action.LastFail = Cap(result, 240);
                    data.FailStreak++;
                    if (action.Streak == 2)
                        AddLessonLocked("Trying " + name + " again and again didn't work here. Change one thing: another key or button, look around, or read the hint on the screen.", "error", 2);
                    if (action.Streak == 3)
                        AddLessonLocked(name + " has now missed " + action.Streak + " times in a row. Stop using it here until something changes; try a different move.", "error", 3);
                }
                else
                {
                    action.Wins++;
                    action.Streak = 0;
                    action.Last = Cap(result, 240);
                    if (lastFailed && lastAction != null && lastAction != name)
                        AddLessonLocked("After " + lastAction + " failed, " + name + " worked. Try that when " + lastAction + " fails again.", "win", 2);
                    data.FailStreak = 0;
                }
                double[] features = Features(tool, args, data.Plan, failed ? data.FailStreak : 0, repeats, !lastFailed, data.Plan.Steps, action.Wins, action.Tries);
                double rate = data.Net.Trained < 200 ? 0.07 : data.Net.Trained < 1000 ? 0.035 : 0.015;
                data.Net.Train(features, failed ? 0 : 1, failed && repeat ? 1.7 : failed ? 1.25 : 1.0, rate);
                TrainShared(features, failed, repeat);
                data.Plan.Steps++;
                if (data.Plan.Phase == "beginning" && data.Plan.Steps > 3) data.Plan.Phase = "middle";
                data.Seconds += Math.Max(0, seconds);
                data.Updated = Now();
                lastAction = name;
                lastFailed = failed;
                bool written = !failed && seconds > 0;
                if (written && repeats <= 1) data.Plan.Updated = Now();
                // Saving every move would keep the disk busy during a long session; every few is enough to survive a crash.
                if (data.Plan.Steps == 1 || data.Plan.Steps % 4 == 0 || action.Streak >= 2) Save(true);
            }
        }

        private void TrainShared(double[] features, bool failed, bool repeat)
        {
            lock (sharedGate)
            {
                CoachData general = SharedLocked();
                double rate = general.Net.Trained < 200 ? 0.05 : general.Net.Trained < 1000 ? 0.025 : 0.01;
                general.Net.Train(features, failed ? 0 : 1, failed && repeat ? 1.7 : failed ? 1.25 : 1.0, rate);
                general.Updated = Now();
                if (general.Net.Trained % 8 == 0) Write(GeneralFile(), general);
            }
        }

        private CoachAction StatLocked(string name, string tool, string key)
        {
            for (int i = 0; i < data.Actions.Count; i++)
                if (data.Actions[i] != null && string.Equals(data.Actions[i].Name, name, StringComparison.OrdinalIgnoreCase)) return data.Actions[i];
            CoachAction action = new CoachAction { Name = name, Tool = tool, Key = key };
            data.Actions.Add(action);
            if (data.Actions.Count > 80) data.Actions.RemoveAt(0);
            return action;
        }

        private void AddLessonLocked(string text, string kind, int score)
        {
            string line = Regex.Replace(text ?? "", @"\s+", " ").Trim();
            if (line.Length == 0) return;
            for (int i = 0; i < data.Lessons.Count; i++)
            {
                CoachLesson kept = data.Lessons[i];
                if (kept != null && string.Equals(kept.Text, line, StringComparison.OrdinalIgnoreCase)) { kept.At = Now(); kept.Score = Math.Max(kept.Score, score); return; }
            }
            data.Lessons.Add(new CoachLesson { Text = line, Kind = kind, Score = score, At = Now() });
            if (data.Lessons.Count > MostLessons) data.Lessons.RemoveAt(0);
        }

        // Her own plan can be replaced mid-game. The beginning, middle and end are hers to rewrite when the game surprises her.
        internal string SetPlan(ToolArguments args)
        {
            if (args == null) args = new ToolArguments();
            lock (gate)
            {
                if (data == null) return "There's no game to plan right now.";
                FillPlan();
                bool changed = false;
                if (!string.IsNullOrWhiteSpace(args.Goal)) { data.Plan.Goal = Clean(args.Goal); changed = true; }
                if (!string.IsNullOrWhiteSpace(args.Purpose)) { data.Plan.Purpose = Clean(args.Purpose); GameNotes.SavePurpose(game, data.Plan.Purpose); changed = true; }
                if (!string.IsNullOrWhiteSpace(args.Beginning)) { data.Plan.Beginning = Clean(args.Beginning); changed = true; }
                if (!string.IsNullOrWhiteSpace(args.Middle)) { data.Plan.Middle = Clean(args.Middle); changed = true; }
                if (!string.IsNullOrWhiteSpace(args.End)) { data.Plan.End = Clean(args.End); changed = true; }
                if (!string.IsNullOrWhiteSpace(args.Phase))
                {
                    string phase = args.Phase.Trim().ToLowerInvariant();
                    if (phase == "start" || phase == "beginning" || phase == "setup") phase = "beginning";
                    else if (phase == "end" || phase == "finish" || phase == "done") phase = "end";
                    else phase = "middle";
                    data.Plan.Phase = phase;
                    changed = true;
                }
                if (!string.IsNullOrWhiteSpace(args.Text) && !changed)
                {
                    data.Plan.Goal = Clean(args.Text);
                    changed = true;
                }
                data.Plan.Updated = Now();
                data.Updated = Now();
                if (changed) Save(true);
                return changed ? "Plan updated. You're in the " + data.Plan.Phase + "." : "Your plan is unchanged: " + Cap(data.Plan.Goal, 120);
            }
        }

        internal string Hud()
        {
            lock (gate)
            {
                if (data == null) return null;
                string phase = data.Plan == null || string.IsNullOrEmpty(data.Plan.Phase) ? "" : char.ToUpperInvariant(data.Plan.Phase[0]) + data.Plan.Phase.Substring(1);
                return phase + (data.FailStreak > 0 ? " · " + data.FailStreak + " misses" : " · " + data.Plan.Steps + " moves");
            }
        }

        // After the job: keeps the mistakes beside the plan, and asks her brain for the one thing that makes the next run
        // better: what she got wrong and what to do instead. It runs for a failed or stopped game too, which is where most
        // of the learning is.
        internal void Reflect(string outcome, string said, IList<string> steps, Func<string, string, string> aside)
        {
            if (string.IsNullOrEmpty(game) || data == null) return;
            string prompt;
            lock (gate)
            {
                data.Runs++;
                if (outcome == "done") { data.Wins++; data.Plan.Phase = "end"; }
                else if (outcome == "failed" || outcome == "stopped") data.Losses++;
                data.Updated = Now();
                data.Plan.Updated = Now();
                Save(true);
                prompt = ReflectionPromptLocked(outcome, said, steps);
            }
            if (aside == null) return;
            string reply = null;
            try { reply = aside(Reflecting, prompt); }
            catch (Exception) { return; }
            if (string.IsNullOrWhiteSpace(reply)) return;
            lock (gate)
            {
                string visible = Brain.Visible(reply);
                if (Regex.IsMatch(visible.Trim(), @"^(nothing|none|no)$", RegexOptions.IgnoreCase)) return;
                GameNotes.Reflected(game, visible);
                ParsePlanLocked(visible);
                data.Updated = Now();
                Save(true);
            }
        }

        internal const string Reflecting =
            "You just finished playing a game for your user, and you're writing notes to yourself for the next run. You get the job, how it ended, your moves, and the mistakes that were marked. " +
            "Write up to 3 short lines, each starting with \"- \", about mistakes and fixes: what failed, why you think it failed, and one thing to try instead. Only what you actually saw in these moves. " +
            "Then write these four lines exactly, in plain words:\n" +
            "PURPOSE: what this game is really for, as far as you found out\n" +
            "BEGINNING: how you should start next time\n" +
            "MIDDLE: the core loop, and what to do when something fails\n" +
            "END: how you'll know the goal is done\n" +
            "If you learned nothing new, reply with only the word: nothing";

        private string ReflectionPromptLocked(string outcome, string said, IList<string> steps)
        {
            StringBuilder text = new StringBuilder();
            text.Append("What they asked: " + task + "\nHow it ended: " + outcome + ": " + (said ?? "") + "\nYour moves:");
            if (steps != null && steps.Count > 0)
            {
                for (int i = Math.Max(0, steps.Count - 40); i < steps.Count; i++) text.Append("\n" + (i + 1) + ". " + steps[i]);
            }
            else text.Append("\n(no moves)");
            if (data != null)
            {
                List<string> mistakes = new List<string>();
                for (int i = 0; i < data.Actions.Count; i++)
                {
                    CoachAction action = data.Actions[i];
                    if (action == null || action.Streak < 2) continue;
                    mistakes.Add(action.Name + " failed " + action.Streak + " times in a row: " + Cap(action.LastFail, 180));
                }
                if (mistakes.Count > 0) text.Append("\nMistakes this run (marked by your move brain):\n- " + string.Join("\n- ", mistakes.ToArray()));
            }
            text.Append("\nThis game is " + game + ".");
            string purpose = GameNotes.Purpose(game);
            if (purpose.Length > 0) text.Append(" The user taught you it's for: " + purpose);
            return text.ToString();
        }

        private void ParsePlanLocked(string reply)
        {
            string current = null;
            StringBuilder value = new StringBuilder();
            string[] lines = Regex.Split(reply ?? "", @"\r?\n");
            for (int i = 0; i < lines.Length; i++)
            {
                string line = (lines[i] ?? "").Trim();
                Match header = Regex.Match(line, @"^(PURPOSE|GOAL|BEGINNING|MIDDLE|END|PHASE)\s*[:\-]\s*(.*)$", RegexOptions.IgnoreCase);
                if (header.Success)
                {
                    ApplyPlanField(current, value.ToString());
                    current = header.Groups[1].Value.ToUpperInvariant();
                    value.Length = 0;
                    value.Append(header.Groups[2].Value.Trim());
                    continue;
                }
                if (current != null)
                {
                    if (line.Length == 0 || Regex.IsMatch(line, @"^[-*]\s+") || Regex.IsMatch(line, @"^[A-Z][A-Z ]{2,}:")) { ApplyPlanField(current, value.ToString()); current = null; value.Length = 0; }
                    else value.Append(" " + line);
                }
            }
            ApplyPlanField(current, value.ToString());
            // Brain reflections that write a plan in a loose form still land somewhere useful.
            if (string.IsNullOrWhiteSpace(data.Plan.Purpose))
            {
                Match loose = Regex.Match(reply ?? "", @"\bpurpose\b[^.\n]*?[:\-]\s*(.+)", RegexOptions.IgnoreCase);
                if (loose.Success) data.Plan.Purpose = Clean(loose.Groups[1].Value);
            }
        }

        private void ApplyPlanField(string field, string text)
        {
            if (string.IsNullOrEmpty(field) || string.IsNullOrWhiteSpace(text)) return;
            string value = Clean(text);
            if (value.Length == 0) return;
            switch (field)
            {
                case "PURPOSE": data.Plan.Purpose = Cap(value, 400); GameNotes.SavePurpose(game, data.Plan.Purpose); break;
                case "GOAL": data.Plan.Goal = Cap(value, 300); break;
                case "BEGINNING": data.Plan.Beginning = Cap(value, 500); break;
                case "MIDDLE": data.Plan.Middle = Cap(value, 600); break;
                case "END": data.Plan.End = Cap(value, 500); break;
                case "PHASE":
                    string phase = value.ToLowerInvariant();
                    if (phase == "start" || phase == "beginning" || phase == "setup") data.Plan.Phase = "beginning";
                    else if (phase == "end" || phase == "finish" || phase == "done") data.Plan.Phase = "end";
                    else data.Plan.Phase = "middle";
                    break;
            }
            data.Plan.Updated = Now();
        }

        private void Save(bool force)
        {
            if (data == null) return;
            if (!force && savedOnce) return;
            savedOnce = true;
            data.Updated = Now();
            Write(FileFor(game), data);
        }

        private static string NormalTool(string tool)
        {
            switch ((tool ?? "").ToLowerInvariant())
            {
                case "key": return "press";
                case "left_click": case "right_click": case "double_click": case "triple_click": case "middle_click": return "click";
                case "hold_key": return "hold_and_press";
                case "left_mouse_down": case "left_mouse_up": return "mouse_hold";
                default: return (tool ?? "").ToLowerInvariant();
            }
        }

        private static string KeyOf(string tool, ToolArguments args)
        {
            string key = args.Keys;
            if (string.IsNullOrEmpty(key)) key = args.Hold;
            if (string.IsNullOrEmpty(key)) key = args.Press;
            if (string.IsNullOrEmpty(key) && tool == "press") key = args.Text;
            if (string.IsNullOrEmpty(key) && tool == "mouse_hold") key = args.Button;
            if (string.IsNullOrEmpty(key) && tool == "click") key = args.Button;
            key = Regex.Replace((key ?? "").ToLowerInvariant(), @"\s+", "");
            key = Regex.Replace(key, @"[^a-z0-9+_\-]", "");
            if (key.Length > 20) key = key.Substring(0, 20);
            return key;
        }

        private static string NameOf(string tool, ToolArguments args, string key)
        {
            if (tool == "walk") return key.Length == 0 ? "walk" : "walk " + key;
            if (tool == "look") return "look";
            if (tool == "mouse_hold") return "mouse_hold " + (key.Length == 0 ? "left" : key);
            if (tool == "press") return key.Length == 0 ? "press" : "press " + key;
            if (tool == "hold_and_press") return key.Length == 0 ? "hold_and_press" : "hold_and_press " + key;
            if (tool == "click") return "click";
            if (tool == "type") return "type";
            if (tool == "scroll") return "scroll " + (string.IsNullOrEmpty(args.Direction) ? "down" : args.Direction.ToLowerInvariant());
            if (tool == "wait") return "wait";
            if (tool == "open_app") return "open_app";
            if (tool == "open_website") return "open_website";
            return string.IsNullOrEmpty(tool) ? "move" : tool;
        }

        private static double[] Features(string tool, ToolArguments args, CoachPlan plan, int failStreak, int repeat, bool lastOk, int step, int wins, int tries)
        {
            if (args == null) args = new ToolArguments();
            if (plan == null) plan = new CoachPlan { Phase = "middle" };
            string key = KeyOf(tool, args);
            double[] x = new double[CoachNet.Inputs];
            x[0] = 1;
            x[1] = tool == "walk" || tool == "look" ? 1 : 0;
            x[2] = tool == "mouse_hold" || tool == "click" ? 1 : 0;
            x[3] = tool == "press" || tool == "type" || tool == "scroll" || tool == "click" ? 1 : 0;
            x[4] = tool == "wait" ? 1 : 0;
            x[5] = tool == "say" || tool == "learn" || tool == "ask" ? 1 : 0;
            x[6] = tool == "plan" ? 1 : 0;
            x[7] = Has(key, "w") || Has(key, "up") ? 1 : 0;
            x[8] = Has(key, "s") || Has(key, "down") ? 1 : 0;
            x[9] = Has(key, "a") || Has(key, "d") || Has(key, "left") || Has(key, "right") ? 1 : 0;
            x[10] = Has(key, "space") ? 1 : 0;
            x[11] = Has(key, "shift") ? 1 : 0;
            x[12] = Has(key, "ctrl") || Has(key, "c") ? 1 : 0;
            x[13] = Has(key, "e") || Has(key, "f") || Has(key, "enter") ? 1 : 0;
            x[14] = tool == "mouse_hold" || tool == "click" ? 1 : 0;
            x[15] = plan.Phase == "beginning" ? 1 : 0;
            x[16] = plan.Phase == "middle" ? 1 : 0;
            x[17] = plan.Phase == "end" ? 1 : 0;
            x[18] = Math.Min(1, Math.Max(0, failStreak) / 5.0);
            x[19] = Math.Min(1, Math.Max(0, repeat) / 3.0);
            x[20] = lastOk ? 1 : 0;
            x[21] = Math.Min(1, Math.Max(0, step) / 100.0);
            x[22] = (wins + 0.0) / (wins + (tries - wins) + 1.0);
            x[23] = Math.Min(1, Math.Max(0, tries) / 10.0);
            int bucket = Hash(key) % 6;
            x[24 + bucket] = 1;
            // A move that is known to be a plain menu click is different from one fired at the crosshair.
            x[29] = tool == "click" ? 1 : 0;
            return x;
        }

        private static int Hash(string text)
        {
            int hash = 17;
            foreach (char letter in (text ?? "")) hash = unchecked(hash * 31 + letter);
            return hash & 0x7fffffff;
        }

        private static bool Has(string keys, string name)
        {
            if (string.IsNullOrEmpty(keys)) return false;
            string[] parts = keys.Split('+');
            for (int i = 0; i < parts.Length; i++) if (parts[i] == name) return true;
            return false;
        }

        // Lets the neural advice be built for a move that was never tried here, from just its name.
        private static ToolArguments ParseArgs(string key)
        {
            ToolArguments args = new ToolArguments();
            if (!string.IsNullOrEmpty(key))
            {
                if (key == "left" || key == "right") args.Button = key;
                else { args.Keys = key; args.Press = key; }
            }
            return args;
        }

        private static string Clean(string text) { return Regex.Replace(text ?? "", @"\s+", " ").Trim().TrimStart('-', '*', ' '); }

        private static string Cap(string text, int length)
        {
            string clean = Clean(text);
            return clean.Length <= length ? clean : clean.Substring(0, Math.Max(0, length - 1)).TrimEnd() + "…";
        }

        private static string Percent(double value) { return ((int)Math.Round(value * 100)).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%"; }

        private static long Now() { return DateTime.UtcNow.Ticks; }
    }
}
