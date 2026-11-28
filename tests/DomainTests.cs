using System;
using System.IO;
using System.Linq;
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
            Check(!new AppState().DictationWelcomeCompleted, "New profiles offer optional dictation setup");
            var upgraded = new AppState {Version=3}; upgraded.Normalize(DateTime.Now);
            Check(upgraded.DictationWelcomeCompleted, "Existing profiles are not interrupted by onboarding after upgrade");
            var geometry=new AppState();geometry.Settings.CompactWidth=0;geometry.Settings.NotchShape=null;geometry.Normalize(DateTime.Now);
            Check(geometry.Settings.CompactWidth==150&&geometry.Settings.NotchShape=="Original","Older profiles retain original notch dimensions and shape");
            geometry.Settings.CompactWidth=999;geometry.Settings.Scale=.1;geometry.Settings.NotchShape="unknown";geometry.Normalize(DateTime.Now);
            Check(geometry.Settings.CompactWidth==240&&geometry.Settings.Scale==.8&&geometry.Settings.NotchShape=="Original","Notch customization bounds preserve readable geometry");
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
            Check(string.Join(",", a.Settings.PageOrder) == "Tasks,Music,Shelf,Focus,Water,Agents", "page order repairs malformed entries");
            var dir = Path.Combine(Path.GetTempPath(), "DeskPill-test-" + Guid.NewGuid());
            Directory.CreateDirectory(dir);
            try
            {
                var store = new StateStore(dir);
                a.Tasks.Add(new TodoItem { Id = "task", Title = "Keep me", StartsAt = t });
                a.Shelf.Add("C:\\example.txt");
                a.Clipboard.Add("private text");
                a.Agents.Add(new AgentProfile{Name="Project helper",Workspace="D:\\demo",Executable="C:\\codex.exe"});a.SelectedAgent=a.Agents[0].Id;
                a.Settings.HiddenPages=new[]{"Agents"};
                store.Save(a);
                var disk = File.ReadAllText(Path.Combine(dir, "state.json"));
                Check(!disk.Contains("private text") && a.Clipboard.Count == 1, "clipboard opt out preserves live history");
                File.WriteAllText(Path.Combine(dir, "state.json"), "{}");
                var legacy = store.Load();
                store.Save(legacy);
                Check(store.Load().FocusPhase == "Focus", "legacy state saves with timer defaults");
                store.Save(a);
                a.DictationWelcomeCompleted=true;store.Save(a);
                a.Settings.CompactWidth=210;a.Settings.NotchShape="Rounded";store.Save(a);
                var b = store.Load();
                Check(b.Settings.CompactWidth==210&&b.Settings.NotchShape=="Rounded","Notch width and shape survive restart");
                Check(b.DictationWelcomeCompleted, "Dictation welcome choice survives restart");
                Check(b.Agents.Count==1 && b.SelectedAgent==a.SelectedAgent && !b.Settings.VisiblePages().Contains("Agents"),"Connections and hidden Agents page survive JSON roundtrip");
                Check(b.Tasks.Count == 1 && b.Tasks[0].Title == "Keep me" && b.Shelf.Count == 1, "task and shelf JSON roundtrip");
                Check(Settings.ThemeNames.Length == 10 && Settings.ThemeNames[0]=="Classic" && Settings.ThemeNames[1]=="Glass", "Classic stays default, followed by Glass, seven skins and Custom");
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
                a.Settings.HoverOpenMs=0;a.Settings.MotionPace=85;a.Settings.GlassOpacity=61;a.Settings.GameOverlay=true;a.Settings.GameApps=new[]{"fixture.exe"};store.Save(a);var profile=store.Load().Settings;
                Check(profile.CustomizationVersion==1 && profile.HoverOpenMs==0 && profile.MotionPace==85 && profile.GlassOpacity==61 && profile.GameOverlay && profile.GameApps[0]=="fixture.exe","A custom motion, glass and game profile survives restart including zero delay");
                store.Save(a);a.Settings.Theme="Glass";a.Agents.Add(new AgentProfile{Name="Saved connection"});store.Save(a);
                string durable=File.ReadAllText(Path.Combine(dir,"state.json"));
                var readOnlyConstructor=typeof(StateStore).GetConstructor(new[]{typeof(string),typeof(bool)});var previewStore=readOnlyConstructor==null?new StateStore(dir):(StateStore)readOnlyConstructor.Invoke(new object[]{dir,true});var previewState=previewStore.Load();previewState.Settings.Theme="Classic";previewState.Agents.Clear();previewStore.Save(previewState);
                Check(File.ReadAllText(Path.Combine(dir,"state.json"))==durable,"Preview cannot overwrite real preferences or connections");
                Check(File.Exists(Path.Combine(dir,"state.json.bak")),"Atomic save retains last-good state backup");
                File.WriteAllText(Path.Combine(dir,"state.json"),"{damaged");var recovered=new StateStore(dir).Load();
                Check(recovered.Settings.Theme=="Custom"&&recovered.Agents.Count==a.Agents.Count-1,"Damaged primary recovers prior preferences and connections");
                store.Save(a);
                var backendField=typeof(Settings).GetField("SpeechBackend");Check(backendField!=null,"Dictation device choice exists");backendField.SetValue(a.Settings,"GPU");store.Save(a);Check((string)backendField.GetValue(store.Load().Settings)=="GPU","GPU choice survives restart");backendField.SetValue(a.Settings,"broken");a.Normalize(DateTime.Now);Check((string)backendField.GetValue(a.Settings)=="Auto","Unknown dictation device repairs to Automatic");
                var speechField=typeof(Settings).GetField("SpeechModel");Check(speechField!=null,"Dictation model choice exists");
                speechField.SetValue(a.Settings,"Parakeet");store.Save(a);Check((string)speechField.GetValue(store.Load().Settings)=="Parakeet","Parakeet selection survives restart");
                speechField.SetValue(a.Settings,"unknown");a.Normalize(DateTime.Now);Check((string)speechField.GetValue(a.Settings)=="Vosk","Unknown speech model repairs to Vosk");
                a.Settings.KeepClipboard = true;
                store.Save(a);
                Check(store.Load().Clipboard.Count == 1, "clipboard opt in persists");
                a.Settings.KeepClipboard=false;store.Save(a);
                Check(!File.ReadAllText(Path.Combine(dir,"state.json.bak")).Contains("private text"),"Last-good backup excludes clipboard history");
                string primary=File.ReadAllText(Path.Combine(dir,"state.json"));File.Delete(Path.Combine(dir,"state.json"));
                Check(new StateStore(dir).Load().Agents.Count==a.Agents.Count,"Missing primary recovers saved connections");
                File.WriteAllText(Path.Combine(dir,"state.json"),"{preview damaged");new StateStore(dir,true).Load();
                Check(File.ReadAllText(Path.Combine(dir,"state.json"))=="{preview damaged","Preview recovery never moves the real state file");
                File.WriteAllText(Path.Combine(dir,"state.json"),primary);
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

            var agentsState=new AppState();agentsState.Settings.PageOrder=new[]{"Music","Shelf","Focus","Water","Tasks"};agentsState.Normalize(DateTime.Now);
            Check(agentsState.Settings.VisiblePages().Last()=="Agents","Upgraded installs add Agents without reordering existing pages");
            agentsState.Agents.Add(new AgentProfile{Name="My Codex",Provider="Codex CLI",Workspace="D:\\demo",Executable="C:\\codex.exe"});agentsState.SelectedAgent=agentsState.Agents[0].Id;
            agentsState.Settings.HiddenPages=new[]{"Agents"};agentsState.Normalize(DateTime.Now);
            Check(!agentsState.Settings.VisiblePages().Contains("Agents") && agentsState.Agents.Count==1,"Hiding Agents retains connections");
            agentsState.Agents.Add(new AgentProfile{Id=agentsState.SelectedAgent,Provider="unknown",AllowEdits=true,Name=new string('x',100)});agentsState.Normalize(DateTime.Now);
            Check(agentsState.Agents.Select(x=>x.Id).Distinct().Count()==2 && agentsState.Agents[1].Provider=="App shortcut" && !agentsState.Agents[1].AllowEdits && agentsState.Agents[1].Name.Length==60,"Malformed agent profiles repair without granting permissions");
            var visibility=new AppState();visibility.Settings.HiddenPages=new[]{"Water","Focus","unknown","Water"};visibility.Normalize(DateTime.Now);
            Check(string.Join(",",visibility.Settings.VisiblePages())=="Music,Shelf,Tasks,Agents","hidden pages filter navigation without changing order");
            visibility.Settings.HiddenPages=visibility.Settings.PageOrder;visibility.Normalize(DateTime.Now);
            Check(visibility.Settings.VisiblePages().Length==1 && visibility.Settings.VisiblePages()[0]=="Music","malformed all-hidden state retains one page");
            visibility.Settings.NotificationSeconds=90;visibility.Settings.MutedNotificationApps=new[]{"APP","app",""};visibility.Normalize(DateTime.Now);
            Check(visibility.Settings.NotificationSeconds==15 && visibility.Settings.MutedNotificationApps.Length==1,"notification controls normalize safely");
            var tuning=new AppState();tuning.Settings.CustomizationVersion=0;tuning.Settings.MotionPace=0;tuning.Normalize(DateTime.Now);
            Check(tuning.Settings.HoverOpenMs==110 && tuning.Settings.HoverCloseMs==360 && tuning.Settings.MotionPace==100 && tuning.Settings.GlassOpacity==72,"Older installs receive safe motion and glass defaults");
            tuning.Settings.HoverOpenMs=-20;tuning.Settings.HoverOptionsMs=900;tuning.Settings.HoverCloseMs=20;tuning.Settings.MotionPace=500;tuning.Settings.GlassOpacity=0;tuning.Settings.GlassHighlight=900;tuning.Settings.GameApps=new[]{"game.exe","GAME.exe","../bad.exe","x.txt"};tuning.Normalize(DateTime.Now);
            Check(tuning.Settings.HoverOpenMs==0 && tuning.Settings.HoverOptionsMs==600 && tuning.Settings.HoverCloseMs==150 && tuning.Settings.MotionPace==130 && tuning.Settings.GlassOpacity==35 && tuning.Settings.GlassHighlight==80 && tuning.Settings.GameApps.Length==1,"Motion, glass and game preferences repair unsafe ranges");
            Check(!new Settings().GameOverlay && new Settings().Theme=="Classic","Game mode stays opt-in and Classic stays default");
            var clips=new[]{" First line\nSecond line ","project alpha","PROJECT beta","project alpha"};
            Check(ClipboardSearch.Find(clips," PROJECT ").Length==2 && ClipboardSearch.Find(clips,"second")[0]==clips[0],"clipboard search ignores query case and preserves full paste text");
            Check(ClipboardSearch.Find(clips,"missing").Length==0,"clipboard search handles no results");
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
