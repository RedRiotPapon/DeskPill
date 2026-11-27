using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;

namespace DeskPill
{
    public class Settings
    {
        public double Scale = 1, OffsetY = 0;
        public static readonly string[] ThemeNames = { "Classic", "Glass", "Shinkai Twilight", "Sakura Shrine", "Cyberpunk Akihabara", "Ghibli Meadow", "EVA Unit-01", "Nichirin Flame", "Moonlit Sailor", "Custom" };
        public string Theme = "Classic";
        public int CustomizationVersion = 1, HoverOpenMs = 110, HoverOptionsMs = 130, HoverCloseMs = 360, MotionPace = 100, GlassOpacity = 72, GlassHighlight = 35;
        public bool GameOverlay = false, DetectFullscreenGames = true;
        public string[] GameApps = new string[0];
        public string CustomShell = "#0C111B", CustomSurface = "#1A2332", CustomAccent = "#8EC9FF", CustomSecondary = "#C2B4F3", CustomText = "#F3F6FC", CustomMuted = "#A7B3C5", CustomFinish = "Satin";
        public int CustomOutline = 18;
        public static string NormalizeTheme(string name)
        {
            var previous = new[] { "Original", "Sakura Dawn", "Neon Tokyo", "Matcha", "OLED Midnight", "Amber CRT", "High Contrast" };
            var current = new[] { "Classic", "Sakura Shrine", "Cyberpunk Akihabara", "Ghibli Meadow", "Classic", "Nichirin Flame", "Classic" };
            int legacy = Array.IndexOf(previous,name);
            if (legacy >= 0) return current[legacy];
            return Array.IndexOf(ThemeNames,name) >= 0 ? name : "Classic";
        }
        public static bool IsHexColor(string value)
        {
            if (value == null || value.Length != 7 || value[0] != '#') return false;
            for (int i = 1; i < value.Length; i++) if (!Uri.IsHexDigit(value[i])) return false;
            return true;
        }
        public static string ColorOrDefault(string value, string fallback) { return IsHexColor(value) ? value.ToUpperInvariant() : fallback; }
        public string Monitor = "", Style = "Classic", OpenMode = "Hover", StatusRing = "Water";
        public bool MirrorNotifications = true; public bool DictationEnabled = false;
        public int NotificationSeconds = 6;
        public string FocusNotifications = "History only", FullscreenNotifications = "History only";
        public string[] MutedNotificationApps = new string[0], HiddenPages = new string[0];
        public string[] VisiblePages() { var order = PageOrder ?? new[] { "Music" }; var visible = order.Where(p => !(HiddenPages ?? new string[0]).Contains(p)).ToArray(); return visible.Length > 0 ? visible : new[] { "Music" }; }
        public bool ShowClock = true, ShowMascot = true, AutoShrinkOnTabs = true, MinimalNotch = false, ShowHudPopups = true, Sound = true, HideFullscreen = true, FastAnimations = false, ClipboardEnabled = true, KeepClipboard = false, StartWithWindows = false;
        public int FocusMinutes = 25, ShortBreakMinutes = 5, LongBreakMinutes = 15, BottleMl = 250, WaterGoalMl = 2000, ActiveStart = 8, ActiveEnd = 22, ReminderMinutes = 60;
        public string CalendarSource = "", GeminiModel = "gemini-2.5-flash", ProtectedApiKey = "", WeatherLocation = "";
        public double Latitude = 23.8103, Longitude = 90.4125;
        public string PrayerMethod = "MWL";
        public bool PrayerReminder = false;
        public string[] PageOrder =
        {
            "Music",
            "Shelf",
            "Focus",
            "Water",
            "Tasks",
            "Agents"
        };
    }

    public static class ClipboardSearch
    {
        public static string[] Find(IEnumerable<string> items,string query)
        {query=(query??"").Trim();return items.Where(s=>!string.IsNullOrWhiteSpace(s)&&s.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0).Distinct().Take(50).ToArray();}
    }

    public class TodoItem
    {
        public string Id, Title;
        public bool Done;
        public DateTime? StartsAt, EndsAt;
        public DateTime CreatedAt = DateTime.UtcNow, CompletedAt = new DateTime(0, DateTimeKind.Utc);
        public bool StartNotified, EndNotified;
    }

    public class AgentProfile
    {
        public string Id = Guid.NewGuid().ToString("N"), Name = "", Provider = "Codex CLI", Executable = "", Workspace = "";
        public bool AllowEdits;
        public override string ToString() { return Name; }
    }

    public class AppState
    {
        public string FocusPhase = "Focus";
        public int FocusRound = 1;
        public bool FocusRunning = false;
        public double FocusRemainingSeconds = 0;
        public DateTime FocusDeadlineUtc = new DateTime(0, DateTimeKind.Utc);
        public int Version = 3;
        public Settings Settings = new Settings();
        public List<AgentProfile> Agents = new List<AgentProfile>();
        public string SelectedAgent = "";
        public List<TodoItem> Tasks = new List<TodoItem>();
        public List<string> Shelf = new List<string>(), Clipboard = new List<string>();
        public string WaterDate;
        public int WaterMl, Refills, CompletedFocus;
        static int Clamp(int v, int lo, int hi)
        {
            return Math.Max(lo, Math.Min(hi, v));
        }

        static double Bound(double v, double lo, double hi, double fallback)
        {
            return double.IsNaN(v) || double.IsInfinity(v) ? fallback : Math.Max(lo, Math.Min(hi, v));
        }

        public void Normalize(DateTime localNow)
        {
            if (Settings == null)
                Settings = new Settings();
            Agents = (Agents ?? new List<AgentProfile>()).Where(x=>x!=null).Take(20).ToList();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach(var agent in Agents)
            {
                Guid id;
                if(!Guid.TryParse(agent.Id,out id)||!ids.Add(agent.Id)){agent.Id=Guid.NewGuid().ToString("N");ids.Add(agent.Id);}
                agent.Name = (agent.Name ?? "").Trim();if(agent.Name.Length>60)agent.Name=agent.Name.Substring(0,60);
                if(agent.Name.Length==0)agent.Name="My agent";
                if(!new[]{"Codex CLI","Claude Code","Gemini CLI","App shortcut"}.Contains(agent.Provider))agent.Provider="App shortcut";
                agent.Executable=(agent.Executable??"").Trim();agent.Workspace=(agent.Workspace??"").Trim();
                if(agent.Executable.Length>1024)agent.Executable="";if(agent.Workspace.Length>1024)agent.Workspace="";
                if(agent.Provider!="Codex CLI")agent.AllowEdits=false;
            }
            if(!Agents.Any(x=>x.Id==SelectedAgent))SelectedAgent=Agents.Count==0?"":Agents[0].Id;
            var s = Settings;
            if (Version < 2)
            {
                s.ShowClock = true;
                s.ShowMascot = true;
                s.AutoShrinkOnTabs = true;
                s.ShowHudPopups = true;
            }
            if (Version < 3) s.MirrorNotifications = true;
            s.Theme = Settings.NormalizeTheme(s.Theme);
            if (string.IsNullOrEmpty(s.CustomShell)) s.CustomOutline = 18;
            s.CustomShell = Settings.ColorOrDefault(s.CustomShell, "#0C111B");
            s.CustomSurface = Settings.ColorOrDefault(s.CustomSurface, "#1A2332");
            s.CustomAccent = Settings.ColorOrDefault(s.CustomAccent, "#8EC9FF");
            s.CustomSecondary = Settings.ColorOrDefault(s.CustomSecondary, "#C2B4F3");
            s.CustomText = Settings.ColorOrDefault(s.CustomText, "#F3F6FC");
            s.CustomMuted = Settings.ColorOrDefault(s.CustomMuted, "#A7B3C5");
            s.CustomOutline = Clamp(s.CustomOutline, 0, 60);
            if (s.CustomFinish != "Matte") s.CustomFinish = "Satin";
            s.Scale = Bound(s.Scale, .65, 2, 1);
            s.OffsetY = Bound(s.OffsetY, 0, 1000, 0);
            s.FocusMinutes = Clamp(s.FocusMinutes, 1, 240);
            s.ShortBreakMinutes = Clamp(s.ShortBreakMinutes, 1, 120);
            s.LongBreakMinutes = Clamp(s.LongBreakMinutes, 1, 240);
            s.BottleMl = Clamp(s.BottleMl, 1, 5000);
            s.WaterGoalMl = Clamp(s.WaterGoalMl, 1, 20000);
            s.ActiveStart = Clamp(s.ActiveStart, 0, 23);
            s.ActiveEnd = Clamp(s.ActiveEnd, 0, 24);
            s.ReminderMinutes = Clamp(s.ReminderMinutes, 1, 1440);
            s.Latitude = Bound(s.Latitude, -90, 90, 23.8103);
            s.Longitude = Bound(s.Longitude, -180, 180, 90.4125);
            if (s.CustomizationVersion < 1) {s.HoverOpenMs=110;s.HoverOptionsMs=130;s.HoverCloseMs=360;s.MotionPace=100;s.GlassOpacity=72;s.GlassHighlight=35;s.DetectFullscreenGames=true;s.CustomizationVersion=1;}
            s.HoverOpenMs=Clamp(s.HoverOpenMs,0,600);s.HoverOptionsMs=Clamp(s.HoverOptionsMs,0,600);s.HoverCloseMs=Clamp(s.HoverCloseMs,150,1200);s.MotionPace=Clamp(s.MotionPace,70,130);
            s.GlassOpacity=Clamp(s.GlassOpacity,35,95);s.GlassHighlight=Clamp(s.GlassHighlight,0,80);
            s.GameApps=(s.GameApps??new string[0]).Where(x=>!string.IsNullOrWhiteSpace(x)&&x.Length<180&&x.IndexOfAny(new[]{'/', '\\', ':', '\"'})<0&&x.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).Take(40).ToArray();
            if (s.Style != "Floating")
                s.Style = "Classic";
            if (s.OpenMode != "Click")
                s.OpenMode = "Hover";
            var pages = new List<string>();
            var known = new Settings().PageOrder;
            if (s.PageOrder != null)
                foreach (var page in s.PageOrder)
                {
                    if (page != null && Array.IndexOf(known, page) >= 0 && !pages.Contains(page))
                        pages.Add(page);
                }

            foreach (var page in known)
                if (!pages.Contains(page))
                    pages.Add(page);
            s.PageOrder = pages.ToArray();
            s.HiddenPages = (s.HiddenPages ?? new string[0]).Where(p => pages.Contains(p)).Distinct().ToArray();
            if (s.HiddenPages.Length == pages.Count) s.HiddenPages = s.HiddenPages.Where(p => p != "Music").ToArray();
            s.MutedNotificationApps = (s.MutedNotificationApps ?? new string[0]).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).Take(200).ToArray();
            s.NotificationSeconds = s.NotificationSeconds == 0 ? 6 : Clamp(s.NotificationSeconds, 2, 15);
            if (s.FocusNotifications != "Allow peeks") s.FocusNotifications = "History only";
            if (s.FullscreenNotifications != "After fullscreen") s.FullscreenNotifications = "History only";
            if (Tasks == null)
                Tasks = new List<TodoItem>();
            Tasks.RemoveAll(delegate (TodoItem t)
            {
                return t == null;
            });
            if (Tasks.Count > 1000)
                Tasks.RemoveRange(1000, Tasks.Count - 1000);
            foreach (var t in Tasks)
            {
                if (string.IsNullOrEmpty(t.Id))
                    t.Id = Guid.NewGuid().ToString("N");
                t.Title = Limit(t.Title, 2000);
            }

            Shelf = Clean(Shelf, 200, 32768);
            Clipboard = Clean(Clipboard, 50, 20000);
            var date = localNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (WaterDate != date)
            {
                WaterDate = date;
                WaterMl = 0;
                Refills = 0;
            }

            WaterMl = Clamp(WaterMl, 0, 100000);
            Refills = Clamp(Refills, 0, 100000);
            CompletedFocus = Clamp(CompletedFocus, 0, 1000000);
            Version = 3;
        }

        static string Limit(string s, int n)
        {
            return s == null ? "" : s.Length > n ? s.Substring(0, n) : s;
        }

        static List<string> Clean(List<string> l, int count, int size)
        {
            var r = new List<string>();
            if (l != null)
                foreach (var x in l)
                {
                    if (x != null && r.Count < count)
                        r.Add(Limit(x, size));
                }

            return r;
        }

        public void AddWater(int ml, DateTime now)
        {
            Normalize(now);
            if (ml > 0)
                WaterMl = (int)Math.Min(100000L, (long)WaterMl + ml);
        }
    }

    public class FocusClock
    {
        readonly Settings settings;
        TimeSpan left;
        DateTime deadline;
        public string Phase = "Focus";
        public bool Running;
        public int Round = 1, Completed;
        public FocusClock(Settings settings)
        {
            this.settings = settings ?? new Settings();
            Reset();
        }

        TimeSpan Duration()
        {
            int n = Phase == "Focus" ? settings.FocusMinutes : Phase == "Short break" ? settings.ShortBreakMinutes : settings.LongBreakMinutes;
            return TimeSpan.FromMinutes(Math.Max(1, Math.Min(240, n)));
        }

        public TimeSpan Remaining(DateTime utcNow)
        {
            var r = Running ? deadline - utcNow : left;
            return r < TimeSpan.Zero ? TimeSpan.Zero : r;
        }

        public void Start(DateTime utcNow)
        {
            if (Running)
                return;
            if (left <= TimeSpan.Zero)
                left = Duration();
            deadline = utcNow + left;
            Running = true;
        }

        public void Pause(DateTime utcNow)
        {
            if (!Running)
                return;
            left = Remaining(utcNow);
            Running = false;
        }

        public void SaveTo(AppState state, DateTime utcNow)
        {
            state.FocusPhase = Phase;
            state.FocusRound = Round;
            state.FocusRunning = Running;
            state.FocusRemainingSeconds = Remaining(utcNow).TotalSeconds;
            state.FocusDeadlineUtc = Running ? deadline : DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
            state.CompletedFocus = Completed;
        }

        public void RestoreFrom(AppState state, DateTime utcNow)
        {
            Reset();
            Completed = Math.Max(0, state.CompletedFocus);
            bool valid = (state.FocusPhase == "Focus" || state.FocusPhase == "Short break" || state.FocusPhase == "Long break") && state.FocusRound >= 1 && state.FocusRound <= 4;
            if (!valid)
                return;
            Phase = state.FocusPhase;
            Round = state.FocusRound;
            double seconds = state.FocusRemainingSeconds;
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0 || seconds > 14400)
                seconds = 0;
            left = seconds > 0 || state.FocusDeadlineUtc.Ticks > 0 ? TimeSpan.FromSeconds(seconds) : Duration();
            if (state.FocusRunning && state.FocusDeadlineUtc.Ticks > 0)
            {
                deadline = DateTime.SpecifyKind(state.FocusDeadlineUtc, DateTimeKind.Utc);
                Running = true;
                left = Remaining(utcNow);
            }
            else
                Running = false;
        }

        public void Reset()
        {
            Running = false;
            Phase = "Focus";
            Round = 1;
            left = Duration();
        }

        public bool Tick(DateTime utcNow)
        {
            if (!Running || utcNow < deadline)
                return false;
            Running = false;
            if (Phase == "Focus")
                Completed++;
            Advance();
            return true;
        }

        public void Skip()
        {
            Running = false;
            Advance();
        }

        void Advance()
        {
            if (Phase == "Focus")
                Phase = Round == 4 ? "Long break" : "Short break";
            else
            {
                if (Phase == "Long break")
                    Round = 1;
                else
                    Round++;
                Phase = "Focus";
            }

            left = Duration();
        }
    }

    public class StateStore
    {
        public string DirectoryPath, LastError = "";
        readonly object gate = new object ();
        bool recoveryBlocked;
        public StateStore(string directory)
        {
            DirectoryPath = Path.GetFullPath(directory);
        }

        public AppState Load()
        {
            lock (gate)
            {
                LastError = "";
                recoveryBlocked = false;
                string path = Path.Combine(DirectoryPath, "state.json");
                try
                {
                    if (!File.Exists(path))
                        return Fresh();
                    if (new FileInfo(path).Length > 8 * 1024 * 1024)
                        throw new InvalidDataException("State exceeds size limit.");
                    AppState a;
                    using (var f = File.OpenRead(path))
                        a = (AppState)new DataContractJsonSerializer(typeof(AppState)).ReadObject(f);
                    if (a == null)
                        throw new InvalidDataException("Empty state.");
                    a.Normalize(DateTime.Now);
                    return a;
                }
                catch (Exception e)
                {
                    LastError = e.Message;
                    try
                    {
                        if (File.Exists(path))
                            File.Move(path, path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N"));
                    }
                    catch (Exception preserve)
                    {
                        recoveryBlocked = true;
                        LastError += " Recovery: " + preserve.Message;
                    }

                    return Fresh();
                }
            }
        }

        static AppState Fresh()
        {
            var a = new AppState();
            a.Normalize(DateTime.Now);
            return a;
        }

        public void Save(AppState state)
        {
            if (state == null)
                throw new ArgumentNullException("state");
            lock (gate)
            {
                if (recoveryBlocked)
                    throw new IOException("Save blocked: unreadable state could not be preserved. Reload after resolving the file lock or permissions.");
                LastError = "";
                Directory.CreateDirectory(DirectoryPath);
                string path = Path.Combine(DirectoryPath, "state.json"), temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    // Serialize an independent snapshot so privacy filtering never mutates live history.
                    var serializer = new DataContractJsonSerializer(typeof(AppState));
                    AppState copy;
                    using (var m = new MemoryStream())
                    {
                        serializer.WriteObject(m, state);
                        m.Position = 0;
                        copy = (AppState)serializer.ReadObject(m);
                    }

                    copy.Normalize(DateTime.Now);
                    if (!copy.Settings.KeepClipboard)
                        copy.Clipboard.Clear();
                    using (var f = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        serializer.WriteObject(f, copy);
                        f.Flush(true);
                    }

                    if (File.Exists(path))
                        File.Replace(temp, path, null);
                    else
                        File.Move(temp, path);
                }
                catch (Exception e)
                {
                    LastError = e.Message;
                    throw;
                }
                finally
                {
                    if (File.Exists(temp))
                        File.Delete(temp);
                }
            }
        }
    }

    public static class QuickMath
    {
        public static bool TryEvaluate(string expression, out string answer)
        {
            answer = "";
            if (string.IsNullOrWhiteSpace(expression) || expression.Length > 512)
                return false;
            try
            {
                var p = new Parser(expression);
                double v = p.Expression();
                p.Space();
                if (p.Position != expression.Length || double.IsNaN(v) || double.IsInfinity(v))
                    return false;
                answer = v.ToString("G15", CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        sealed class Parser
        {
            readonly string text;
            public int Position;
            int depth;
            public Parser(string s)
            {
                text = s;
            }

            public void Space()
            {
                while (Position < text.Length && char.IsWhiteSpace(text[Position]))
                    Position++;
            }

            bool Eat(char c)
            {
                Space();
                if (Position < text.Length && text[Position] == c)
                {
                    Position++;
                    return true;
                }

                return false;
            }

            public double Expression()
            {
                double v = Term();
                while (true)
                {
                    if (Eat('+'))
                        v += Term();
                    else if (Eat('-'))
                        v -= Term();
                    else
                        return v;
                }
            }

            double Term()
            {
                double v = Unary();
                while (true)
                {
                    if (Eat('*'))
                        v *= Unary();
                    else if (Eat('/'))
                    {
                        double d = Unary();
                        if (d == 0)
                            throw new FormatException();
                        v /= d;
                    }
                    else if (Eat('%'))
                    {
                        double d = Unary();
                        if (d == 0)
                            throw new FormatException();
                        v %= d;
                    }
                    else
                        return v;
                }
            }

            double Unary()
            {
                if (++depth > 64)
                    throw new FormatException();
                double v;
                if (Eat('+'))
                    v = Unary();
                else if (Eat('-'))
                    v = -Unary();
                else if (Eat('('))
                {
                    v = Expression();
                    if (!Eat(')'))
                        throw new FormatException();
                }
                else
                {
                    Space();
                    int start = Position;
                    while (Position < text.Length && (char.IsDigit(text[Position]) || text[Position] == '.'))
                        Position++;
                    if (start == Position)
                        throw new FormatException();
                    v = double.Parse(text.Substring(start, Position - start), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
                }

                depth--;
                return v;
            }
        }
    }
}
