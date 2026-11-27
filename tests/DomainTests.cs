using System;
using System.IO;
using DeskPill;

class DomainTests
{
    static void Check(bool b, string n)
    {
        if (!b)
            throw new Exception(n);
        Console.WriteLine("PASS " + n);
    }

    static int Main()
    {
        try
        {
            var t = new DateTime(2026, 11, 26, 10, 0, 0, DateTimeKind.Utc);
            var s = new Settings();
            var f = new FocusClock(s);
            f.Start(t);
            Check(f.Remaining(t.AddMinutes(10)).TotalMinutes == 15, "deadline elapsed");
            f.Pause(t.AddMinutes(10));
            f.Pause(t.AddMinutes(12));
            f.Start(t.AddHours(2));
            Check(f.Remaining(t.AddHours(2).AddMinutes(1)).TotalMinutes == 14, "idempotent pause and resume");
            Check(f.Tick(t.AddHours(3)) && !f.Tick(t.AddHours(3)), "completion fires once");
            Check(f.Phase == "Short break" && !f.Running && f.Completed == 1, "focus completion");
            f.Skip();
            f.Skip();
            f.Skip();
            f.Skip();
            f.Skip();
            f.Skip();
            Check(f.Phase == "Long break" && f.Round == 4, "four session cycle");
            f.Skip();
            Check(f.Phase == "Focus" && f.Round == 1, "cycle returns to first focus");
            var saved = new AppState();
            f.Reset();
            f.Start(t);
            f.SaveTo(saved, t.AddSeconds(1480));
            var restored = new FocusClock(s);
            restored.RestoreFrom(saved, t.AddSeconds(1490));
            Check(restored.Remaining(t.AddSeconds(1490)).TotalSeconds == 10, "running restart keeps deadline");
            f.Pause(t.AddSeconds(1480));
            f.SaveTo(saved, t.AddSeconds(1480));
            restored.RestoreFrom(saved, t.AddHours(5));
            Check(!restored.Running && restored.Remaining(t.AddHours(5)).TotalSeconds == 20, "paused restart keeps remaining");
            f.Start(t.AddHours(5));
            f.SaveTo(saved, t.AddHours(5));
            restored.RestoreFrom(saved, t.AddHours(6));
            Check(restored.Tick(t.AddHours(6)) && !restored.Tick(t.AddHours(6)), "expired restart completes once");
            restored.SaveTo(saved, t.AddHours(6));
            var again = new FocusClock(s);
            again.RestoreFrom(saved, t.AddHours(6));
            Check(again.Completed == saved.CompletedFocus && again.Completed == 2, "restart retains completed total");
            var a = new AppState();
            a.AddWater(250, t);
            a.AddWater(250, t);
            Check(a.WaterMl == 500, "water accumulation");
            a.Normalize(t.AddDays(1));
            Check(a.WaterMl == 0 && a.Refills == 0, "local daily rollover");
            a.Settings.Scale = double.NaN;
            a.Settings.FocusMinutes = -1;
            a.Normalize(t);
            Check(!double.IsNaN(a.Settings.Scale) && a.Settings.FocusMinutes > 0, "invalid settings bounded");
            a.Settings.PageOrder = new string[]
            {
                null,
                "Tasks",
                "Tasks",
                "Bogus"
            };
            a.Normalize(t);
            Check(string.Join(",", a.Settings.PageOrder) == "Tasks,Music,Shelf,Focus,Water", "page order repairs malformed entries");
            var dir = Path.Combine(Path.GetTempPath(), "DeskPill-test-" + Guid.NewGuid());
            Directory.CreateDirectory(dir);
            try
            {
                var store = new StateStore(dir);
                a.Tasks.Add(new TodoItem { Id = "task", Title = "Keep me", StartsAt = t });
                a.Shelf.Add("C:\\example.txt");
                a.Clipboard.Add("private text");
                store.Save(a);
                var disk = File.ReadAllText(Path.Combine(dir, "state.json"));
                Check(!disk.Contains("private text") && a.Clipboard.Count == 1, "clipboard opt out preserves live history");
                File.WriteAllText(Path.Combine(dir, "state.json"), "{}");
                var legacy = store.Load();
                store.Save(legacy);
                Check(store.Load().FocusPhase == "Focus", "legacy state saves with timer defaults");
                store.Save(a);
                var b = store.Load();
                Check(b.Tasks.Count == 1 && b.Tasks[0].Title == "Keep me" && b.Shelf.Count == 1, "task and shelf JSON roundtrip");
                Check(Settings.ThemeNames.Length == 9, "Classic, seven skins and Custom registered");
                Check(Settings.NormalizeTheme("Matcha") == "Ghibli Meadow" && Settings.NormalizeTheme("Sakura Dawn") == "Sakura Shrine", "previous palette preferences migrate to refined skins");
                a.Settings.Theme = "Sakura Shrine";
                store.Save(a);
                Check(store.Load().Settings.Theme == "Sakura Shrine", "anime theme persists through JSON roundtrip");
                a.Settings.Theme = "missing-palette"; a.Normalize(DateTime.Now);
                Check(a.Settings.Theme == "Classic", "unknown theme repairs to Classic");
                a.Settings.Theme = "Custom"; a.Settings.CustomShell = "#101820"; a.Settings.CustomAccent = "#e9c46a"; a.Settings.CustomFinish = "Matte"; a.Settings.CustomOutline = 37;
                store.Save(a); var custom = store.Load().Settings;
                Check(custom.Theme == "Custom" && custom.CustomShell == "#101820" && custom.CustomAccent == "#E9C46A" && custom.CustomFinish == "Matte" && custom.CustomOutline == 37, "custom palette and surface options persist");
                a.Settings.CustomShell = "bad"; a.Settings.CustomText = null; a.Settings.CustomOutline = 100; a.Settings.CustomFinish = "unknown"; a.Normalize(DateTime.Now);
                Check(a.Settings.CustomShell == "#0C111B" && a.Settings.CustomText == "#F3F6FC" && a.Settings.CustomOutline == 60 && a.Settings.CustomFinish == "Satin", "invalid custom appearance repairs safely");
                a.Settings.KeepClipboard = true;
                store.Save(a);
                Check(store.Load().Clipboard.Count == 1, "clipboard opt in persists");
                File.WriteAllText(Path.Combine(dir, "state.json"), "{broken");
                b = store.Load();
                Check(b.Settings != null && store.LastError != "" && Directory.GetFiles(dir, "*.corrupt*").Length > 0, "corrupt data preserved");
                File.WriteAllText(Path.Combine(dir, "state.json"), "{locked");
                using (var locked = File.Open(Path.Combine(dir, "state.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    store.Load();
                }

                bool blocked = false;
                try
                {
                    store.Save(a);
                }
                catch (IOException)
                {
                    blocked = true;
                }

                Check(blocked && File.ReadAllText(Path.Combine(dir, "state.json")) == "{locked", "failed recovery prevents overwrite");
            }
            finally
            {
                string cleanup = Path.GetFullPath(dir), temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!cleanup.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(cleanup).StartsWith("DeskPill-test-"))
                    throw new IOException("Unexpected test cleanup path");
                Directory.Delete(cleanup, true);
            }

            string answer;
            Check(QuickMath.TryEvaluate("2+3*4", out answer) && answer == "14", "math precedence");
            Check(QuickMath.TryEvaluate("-(2+3)*4", out answer) && answer == "-20", "math parentheses unary");
            Check(!QuickMath.TryEvaluate("1/0", out answer) && !QuickMath.TryEvaluate("System.IO.File.Delete(1)", out answer) && !QuickMath.TryEvaluate("2+", out answer), "math rejects unsafe or invalid input");
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("FAIL " + e.Message);
            return 1;
        }
    }
}
