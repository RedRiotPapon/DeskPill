using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DeskPill;

class UiTests
{
    static int passed, failed;
    [STAThread]
    static int Main()
    {
        var app = new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };
        Ui.Init(app);
        Test("Task Cancel preserves original", TaskCancel);
        Test("Task Save clones and resets changed reminders", TaskSave);
        Test("Task rejects invalid dates and empty title", TaskValidation);
        Test("Task allows optional dates", TaskOptionalDates);
        Test("Settings Cancel stages across pages", SettingsCancel);
        Test("Settings Save updates existing reference once", SettingsSave);
        Test("Closing Settings restores the saved non-Classic theme after repeated previews", SettingsPreviewClose);
        Test("Ask Send evaluates arithmetic without API key", AskMath);
        Test("Task uncheck persists UTC empty completion", IslandTask);
        Test("Shelf Remove survives state normalization", IslandShelf);
        Test("Focus controls persist transitions", IslandFocus);
        Test("Palettes recolor rendered rings and existing controls without freezing shared brushes", ThemeSwitch);
        Test("Custom colors preview immediately, reject partial hex and roll back on Cancel", CustomPreview);
        Test("Custom palette saves without losing surface options", CustomSave);
        Test("Skin text and primary action contrast remain readable", ThemeContrast);
        Test("Clipboard search selects and pastes the exact full text", ClipboardWorkflowUI);
        Test("Clipboard copy-only and empty results never request a paste", ClipboardCopyOnlyUI);
        Test("Interruption and page preferences save together without losing hidden page data", WorkflowSettings);
        Test("Recent notifications expose source actions and clear only local history", RecentUI);
        app.Shutdown();
        Console.WriteLine("UI behavioral tests: " + passed + " passed, " + failed + " failed");
        return failed == 0 ? 0 : 1;
    }

    static void SettingsPreviewClose()
    {
        var state = new AppState(); state.Settings.Theme = "Shinkai Twilight";
        Themes.Apply(state.Settings.Theme); int calls = 0;
        var w = new SettingsWindow(state, delegate { calls++; }, null);
        Dialog(w, delegate
        {
            Click(w, "Appearance");
            foreach (string theme in new[] { "EVA Unit-01", "Classic", "Moonlit Sailor" })
            {
                Controls<RadioButton>(w).Single(c => (string)c.Tag == theme).IsChecked = true;
                Check(Themes.Current == theme && state.Settings.Theme == "Shinkai Twilight", "Preview was delayed or persisted");
            }
            w.Close();
        });
        Check(Themes.Current == "Shinkai Twilight" && state.Settings.Theme == "Shinkai Twilight" && calls == 0, "Close did not restore saved theme");
        Themes.Apply("Classic");
    }

    static TextBox ColorInput(Window window, string label)
    {
        return Controls<TextBox>(window).Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == label + " color");
    }
    static void CustomPreview()
    {
        var state = new AppState(); Themes.Apply("Classic"); int calls=0;
        var w = new SettingsWindow(state, delegate { calls++; }, null);
        Dialog(w, delegate
        {
            Click(w,"Appearance"); ColorInput(w,"Accent").Text="#E9C46A";
            Check(Themes.Current=="Custom" && Themes.AccentColor==System.Windows.Media.Color.FromRgb(233,196,106),"Custom edit did not preview");
            Check(state.Settings.Theme=="Classic" && state.Settings.CustomAccent=="#8EC9FF","Custom preview mutated saved settings");
            ColorInput(w,"Accent").Text="#E9";
            Check(Themes.AccentColor==System.Windows.Media.Color.FromRgb(233,196,106),"Invalid hex destroyed last good preview");
            ColorInput(w,"Accent").Text="#AACCFF";
            Check(Themes.AccentColor==System.Windows.Media.Color.FromRgb(170,204,255),"Corrected color did not resume preview");
            Click(w,"Cancel");
        });
        Check(Themes.Current=="Classic" && calls==0 && state.Settings.CustomAccent=="#8EC9FF","Custom Cancel did not restore saved appearance");
    }
    static void CustomSave()
    {
        var state = new AppState(); Themes.Apply("Classic"); int calls=0;
        var w = new SettingsWindow(state, delegate { calls++; Themes.ConfigureCustom(state.Settings); Themes.Apply(state.Settings.Theme); }, null);
        Dialog(w, delegate
        {
            Click(w,"Appearance"); ColorInput(w,"Shell").Text="#132021"; ColorInput(w,"Accent").Text="#E9C46A";
            Click(w,"Matte");
            Controls<Slider>(w).Single().Value=31; Click(w,"Save changes");
        });
        Check(calls==1 && state.Settings.Theme=="Custom" && state.Settings.CustomShell=="#132021" && state.Settings.CustomFinish=="Matte" && state.Settings.CustomOutline==31 && Themes.Current=="Custom","Custom settings did not commit");
        Themes.ConfigureCustom(new Settings()); Themes.Apply("Classic");
    }

    static void ThemeContrast()
    {
        Func<System.Windows.Media.Brush,System.Windows.Media.Color> color = b => ((System.Windows.Media.SolidColorBrush)b).Color;
        Themes.ConfigureCustom(new Settings());
        foreach(string name in Themes.Names)
        {
            Themes.Apply(name);
            var shell=((System.Windows.Media.GradientBrush)Themes.ShellFill).GradientStops[0].Color;
            Check(Themes.ContrastRatio(color(Ui.White),shell)>=4.5 && Themes.ContrastRatio(color(Ui.Muted),shell)>=4.5,"Unreadable text in "+name);
            Check(Themes.ContrastRatio(color(Themes.PrimaryInk),color(Ui.Green))>=4.5,"Unreadable primary action in "+name);
        }
        var custom=new Settings {CustomAccent="#152030"}; Themes.ConfigureCustom(custom); Themes.Apply("Custom");
        Check(Themes.ContrastRatio(color(Themes.PrimaryInk),color(Ui.Green))>=4.5,"Dark custom accent did not choose readable action text");
        Themes.ConfigureCustom(new Settings()); Themes.Apply("Classic");
    }

    static void ClipboardWorkflowUI()
    {
        string chosen=null;bool paste=false;ClipboardPalette w=null;
        w=new ClipboardPalette(new[]{"alpha one","alpha\ntwo","other"},true,"fixture",delegate(string text,bool insert){chosen=text;paste=insert;w.Close();});
        Dialog(w,delegate {
            Controls<TextBox>(w).Single().Text="ALPHA";
            var list=Controls<ListBox>(w).Single();Check(list.Items.Count==2 && list.SelectedIndex==0,"Search did not filter newest-first");
            list.SelectedIndex=1;Click(w,"Paste");
        });
        Check(chosen=="alpha\ntwo"&&paste,"Palette changed text or selected the wrong item");
    }
    static void ClipboardCopyOnlyUI()
    {
        string chosen=null;bool paste=true;ClipboardPalette w=null;
        w=new ClipboardPalette(new[]{"hello"},false,"",delegate(string text,bool insert){chosen=text;paste=insert;w.Close();});
        Dialog(w,delegate {
            Check(!Controls<Button>(w).Single(b=>Convert.ToString(b.Content)=="Paste").IsEnabled,"Copy-only palette enabled Paste");
            Controls<TextBox>(w).Single().Text="missing";Click(w,"Copy");Check(chosen==null,"Empty search copied stale selection");
            Controls<TextBox>(w).Single().Text="hello";Click(w,"Copy");
        });
        Check(chosen=="hello"&&!paste,"Copy-only requested insertion");
    }
    static void WorkflowSettings()
    {
        var state=new AppState();state.WaterDate=DateTime.Today.ToString("yyyy-MM-dd");state.WaterMl=850;int applied=0;
        var apps=new[]{new DesktopNotification {AppId="fixture.mail",AppName="Mail fixture"}};
        var w=new SettingsWindow(state,delegate{applied++;},null,null,apps);
        Dialog(w,delegate {
            Click(w,"Notifications");Controls<TextBox>(w).Single().Text="4";
            Controls<ComboBox>(w).Single(c=>((string[])c.ItemsSource).Contains("Allow peeks")).SelectedItem="Allow peeks";
            Controls<CheckBox>(w).Single(c=>Convert.ToString(c.Content)=="Mail fixture").IsChecked=false;
            Click(w,"Pages");Controls<CheckBox>(w).Single(c=>Convert.ToString(c.Content)=="Water").IsChecked=false;Click(w,"Save changes");
        });
        Check(applied==1 && state.Settings.NotificationSeconds==4 && state.Settings.FocusNotifications=="Allow peeks" && state.Settings.MutedNotificationApps.Contains("fixture.mail") && state.Settings.HiddenPages.Contains("Water") && state.WaterMl==850,"Settings lost preferences or hidden page data");
        var cancel=new SettingsWindow(state,delegate{applied++;},null,null,apps);
        Dialog(cancel,delegate {Click(cancel,"Pages");Controls<CheckBox>(cancel).Single(c=>Convert.ToString(c.Content)=="Water").IsChecked=true;Click(cancel,"Cancel");});
        Check(applied==1&&state.Settings.HiddenPages.Contains("Water"),"Cancel changed page visibility");
    }
    static void RecentUI()
    {
        var history=new NotificationHistory();var n=new DesktopNotification {Key="test",AppName="Fixture",AppId="Vendor.App!App",Title="Test title",Body="Full body",CreatedAt=DateTimeOffset.UtcNow};history.Receive(n,new Settings(),false,false);int opened=0;string copied=null;
        var w=new NotificationHistoryWindow(history,delegate(DesktopNotification item){if(item==n)opened++;},delegate(string text){copied=text;});
        Dialog(w,delegate {Click(w,"Open app");Click(w,"Copy");Check(opened==1&&copied=="Test title\nFull body","Recent actions dropped source or text");Click(w,"Clear recent");Check(history.Items.Length==0,"Clear did not update Recent");});
    }

    static void ThemeSwitch()
    {
        var accent = Ui.Green;
        var label = Ui.Text("Theme fixture", 12, Ui.White);
        var ring = new Ring { Width = 40, Height = 40, Progress = .5, Accent = accent };
        ring.Measure(new Size(40,40)); ring.Arrange(new Rect(0,0,40,40));
        var image = new System.Windows.Media.Imaging.RenderTargetBitmap(40,40,96,96,System.Windows.Media.PixelFormats.Pbgra32);
        image.Render(ring); // Exercise pen caches before changing the live brushes.
        Themes.Apply("Classic");
        var original = ((System.Windows.Media.SolidColorBrush)accent).Color;
        foreach (string name in Themes.Names)
        {
            Themes.Apply(name);
            Check(object.ReferenceEquals(accent, Ui.Green), "Palette replaced a live brush");
            Check(!accent.IsFrozen, "Rendered ring froze shared palette");
            Check(((System.Windows.Media.SolidColorBrush)label.Foreground).Color == ((System.Windows.Media.SolidColorBrush)Ui.White).Color, "Existing text did not follow palette");
            if (name != "Classic") Check(((System.Windows.Media.SolidColorBrush)accent).Color != original, "Accent did not change for " + name);
            image.Render(ring);
        }
        Themes.Apply("Unknown future palette");
        Check(Themes.Current == "Classic" && ((System.Windows.Media.SolidColorBrush)accent).Color == original, "Invalid theme did not restore Classic palette");
    }

    static void Test(string name, Action action)
    {
        try
        {
            action();
            passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception ex)
        {
            failed++;
            Console.WriteLine("FAIL " + name + ": " + ex.Message);
        }
    }

    static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    static IEnumerable<T> Controls<T>(DependencyObject parent)
        where T : DependencyObject
    {
        foreach (object child in LogicalTreeHelper.GetChildren(parent))
        {
            var d = child as DependencyObject;
            if (d == null)
                continue;
            if (d is T)
                yield return (T)d;
            foreach (var item in Controls<T>(d))
                yield return item;
        }
    }

    static void Click(Window w, string label)
    {
        var b = Controls<Button>(w).Single(x => Convert.ToString(x.Content) == label);
        b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    static void Dialog(Window w, Action action)
    {
        Exception error = null;
        var timeout = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(10)
        };
        timeout.Tick += delegate
        {
            error = new Exception("Dialog timed out");
            w.Close();
        };
        w.Loaded += delegate
        {
            w.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    if (w.IsVisible)
                        w.Close();
                }
            }));
        };
        try
        {
            timeout.Start();
            w.ShowDialog();
        }
        finally
        {
            timeout.Stop();
            if (w.IsVisible)
                w.Close();
        }

        if (error != null)
            throw error;
    }

    static TodoItem Original()
    {
        return new TodoItem
        {
            Id = "test",
            Title = "Original",
            Done = true,
            StartsAt = new DateTime(2030, 1, 2, 10, 0, 0, DateTimeKind.Local),
            EndsAt = new DateTime(2030, 1, 2, 11, 0, 0, DateTimeKind.Local),
            CreatedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CompletedAt = new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            StartNotified = true,
            EndNotified = true
        };
    }

    static void Unchanged(TodoItem t)
    {
        Check(t.Title == "Original" && t.StartNotified && t.EndNotified && t.StartsAt.Value.Hour == 10 && t.EndsAt.Value.Hour == 11, "Original task mutated");
    }

    static void TaskCancel()
    {
        var t = Original();
        var w = new TaskEditor(t);
        Dialog(w, delegate
        {
            var boxes = Controls<TextBox>(w).ToArray();
            boxes[0].Text = "Changed";
            boxes[1].Text = "";
            Click(w, "Cancel");
        });
        Unchanged(t);
        Check(w.Result == null && !w.DeleteRequested, "Cancel returned a task");
    }

    static void TaskSave()
    {
        var t = Original();
        var w = new TaskEditor(t);
        Dialog(w, delegate
        {
            var boxes = Controls<TextBox>(w).ToArray();
            boxes[0].Text = "  Revised  ";
            boxes[1].Text = "2030-01-02 09:00";
            Click(w, "Save");
        });
        Unchanged(t);
        var r = w.Result;
        Check(r != null && !object.ReferenceEquals(r, t), "Save did not return clone");
        Check(r.Title == "Revised" && r.Id == t.Id && r.Done && r.CreatedAt == t.CreatedAt && r.CompletedAt == t.CompletedAt, "Saved clone lost fields");
        Check(!r.StartNotified && r.EndNotified && r.StartsAt.Value.Hour == 9, "Reminder flags not reset selectively");
    }

    static void TaskValidation()
    {
        var w = new TaskEditor(null);
        Dialog(w, delegate
        {
            var b = Controls<TextBox>(w).ToArray();
            Click(w, "Save");
            Check(w.IsVisible && w.Result == null && Controls<TextBlock>(w).Any(x => x.Text == "Enter a task title."), "Empty title accepted");
            b[0].Text = "Valid title";
            b[1].Text = "2030-02-30 10:00";
            Click(w, "Save");
            Check(w.IsVisible && w.Result == null && Controls<TextBlock>(w).Any(x => x.Text.Contains("valid local time")), "Invalid date accepted");
            b[1].Text = "2030-01-02 10:00";
            b[2].Text = "2030-01-02 09:00";
            Click(w, "Save");
            Check(w.IsVisible && w.Result == null && Controls<TextBlock>(w).Any(x => x.Text == "End must be after start."), "Reversed date range accepted");
            Click(w, "Cancel");
        });
    }

    static void TaskOptionalDates()
    {
        var w = new TaskEditor(null);
        Dialog(w, delegate
        {
            Controls<TextBox>(w).First().Text = "No schedule";
            Click(w, "Save");
        });
        Check(w.Result != null && !w.Result.StartsAt.HasValue && !w.Result.EndsAt.HasValue && !string.IsNullOrEmpty(w.Result.Id), "Optional dates did not save");
    }

    static void SettingsCancel()
    {
        var state = new AppState();
        var original = state.Settings;
        var order = string.Join(",", original.PageOrder);
        int calls = 0;
        var w = new SettingsWindow(state, delegate
        {
            calls++;
        }, null);
        Dialog(w, delegate
        {
            Controls<ComboBox>(w).ElementAt(1).SelectedIndex = 2;
            Click(w, "Focus & water");
            Controls<TextBox>(w).First().Text = "42";
            Click(w, "Pages");
            Click(w, "Move down");
            Click(w, "Appearance");
            Controls<RadioButton>(w).Single(c => (string)c.Tag == "Sakura Shrine").IsChecked = true;
            Check(Themes.Current == "Sakura Shrine", "Theme did not preview before Save");
            Check(state.Settings.Theme == "Classic", "Preview changed persisted settings");
            Click(w, "Cancel");
        });
        Check(state.Settings.Theme == "Classic" && Themes.Current == "Classic", "Cancel failed to restore saved theme");
        Check(object.ReferenceEquals(original, state.Settings) && original.Scale == 1 && original.FocusMinutes == 25 && string.Join(",", original.PageOrder) == order && calls == 0, "Cancel applied staged settings");
    }

    static void SettingsSave()
    {
        var state = new AppState();
        var original = state.Settings;
        int calls = 0;
        string notice = null;
        var w = new SettingsWindow(state, delegate
        {
            calls++;
            Check(object.ReferenceEquals(original, state.Settings) && original.FocusMinutes == 42, "Callback saw stale settings");
        }, delegate (string text)
        {
            notice = text;
        });
        Dialog(w, delegate
        {
            Controls<ComboBox>(w).ElementAt(1).SelectedIndex = 2;
            Click(w, "Focus & water");
            Controls<TextBox>(w).First().Text = "42";
            Click(w, "Pages");
            Click(w, "Move down");
            Click(w, "Appearance");
            Controls<RadioButton>(w).Single(c => (string)c.Tag == "Cyberpunk Akihabara").IsChecked = true;
            Check(Themes.Current == "Cyberpunk Akihabara" && state.Settings.Theme == "Classic", "Save preview changed stored theme too soon");
            Click(w, "Save changes");
            Check(Themes.Current == "Cyberpunk Akihabara", "Saving rolled back the selected theme");
            Check(!w.IsVisible, "Settings Save did not close");
        });
        Check(state.Settings.Theme == "Cyberpunk Akihabara", "Selected palette was not saved");
        Check(object.ReferenceEquals(original, state.Settings) && original.Scale == 1.3 && original.FocusMinutes == 42 && original.PageOrder[0] == "Shelf" && original.PageOrder[1] == "Music" && calls == 1 && notice == "Settings saved", "Settings Save did not apply staged values exactly once");
    }

    static void AskMath()
    {
        var settings = new Settings
        {
            ProtectedApiKey = ""
        };
        int notices = 0;
        var w = new AskWindow(settings, delegate
        {
            notices++;
        });
        Dialog(w, delegate
        {
            var boxes = Controls<TextBox>(w).ToArray();
            var input = boxes.Single(x => !x.IsReadOnly);
            var output = boxes.Single(x => x.IsReadOnly);
            input.Text = "2+3*4=";
            Click(w, "Send");
            Check(output.Text == "14", "Expected offline result 14, got " + output.Text);
            Check(Controls<Button>(w).Single(x => Convert.ToString(x.Content) == "Send").IsEnabled, "Send remained busy");
            Click(w, "Clear");
            Check(input.Text == "" && output.Text == "" && notices == 0, "Clear failed or unexpected notification");
        });
    }

    static Button Tip(IslandWindow w, string tip)
    {
        return Controls<Button>(w).Single(x => Convert.ToString(x.ToolTip) == tip);
    }

    static void Press(Button b)
    {
        b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    static void Next(IslandWindow w, int count)
    {
        for (int i = 0; i < count; i++)
            Press(Tip(w, "Next page"));
        Check(!w.IsVisible, "Island unexpectedly shown");
    }

    static void Fixture(Action<AppState, StateStore, string> run)
    {
        string folder = Path.Combine(Path.GetTempPath(), "DeskPill-UiFixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var state = new AppState();
            state.Settings.ClipboardEnabled = false;
            run(state, new StateStore(folder), folder);
        }
        finally
        {
            string cleanup = Path.GetFullPath(folder), temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!cleanup.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(cleanup).StartsWith("DeskPill-UiFixture-"))
                throw new IOException("Unexpected test cleanup path");
            Directory.Delete(cleanup, true);
        }
    }

    static void IslandTask()
    {
        Fixture(delegate (AppState state, StateStore store, string folder)
        {
            state.Tasks.Add(new TodoItem { Id = "done", Title = "Completed task", Done = true, CompletedAt = DateTime.UtcNow });
            var w = new IslandWindow(state, store, true, false);
            Next(w, 4);
            var check = Controls<CheckBox>(w).Single();
            check.IsChecked = false;
            check.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            Check(!state.Tasks[0].Done, "Task remained complete");
            Check(File.Exists(Path.Combine(folder, "state.json")), "Uncheck did not save state");
            var saved = store.Load();
            Check(saved.Tasks.Count == 1 && !saved.Tasks[0].Done && saved.Tasks[0].CompletedAt.Ticks == 0 && saved.Tasks[0].CompletedAt.Kind == DateTimeKind.Utc, "Uncheck failed to persist UTC empty completion");
        });
    }

    static void IslandShelf()
    {
        Fixture(delegate (AppState state, StateStore store, string folder)
        {
            string file = Path.Combine(folder, "shelf-item.txt");
            File.WriteAllText(file, "test fixture");
            state.Shelf.Add(file);
            var w = new IslandWindow(state, store, true, false);
            Next(w, 1);
            var item = Controls<Button>(w).Single(x => Convert.ToString(x.Content) == "shelf-item.txt");
            var remove = item.ContextMenu.Items.OfType<MenuItem>().Single(x => Convert.ToString(x.Header) == "Remove from shelf");
            state.Normalize(DateTime.Now);
            remove.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(state.Shelf.Count == 0, "Remove acted on stale shelf list");
            Check(File.Exists(Path.Combine(folder, "state.json")) && store.Load().Shelf.Count == 0, "Remove was not persisted");
            Check(File.Exists(file), "Remove deleted the source file");
        });
    }

    static void IslandFocus()
    {
        Fixture(delegate (AppState state, StateStore store, string folder)
        {
            var w = new IslandWindow(state, store, true, false);
            Next(w, 2);
            Press(Controls<Button>(w).Single(x => Convert.ToString(x.Content).EndsWith("Start")));
            var started = store.Load();
            Check(started.FocusRunning && started.FocusPhase == "Focus" && started.FocusDeadlineUtc > DateTime.UtcNow && started.FocusRemainingSeconds > 1490, "Start not persisted");
            Press(Controls<Button>(w).Single(x => Convert.ToString(x.Content).EndsWith("Pause")));
            var paused = store.Load();
            Check(!paused.FocusRunning && paused.FocusPhase == "Focus" && paused.FocusRemainingSeconds > 1490 && paused.FocusRemainingSeconds <= started.FocusRemainingSeconds, "Pause not persisted");
            Press(Tip(w, "Skip this phase"));
            var skipped = store.Load();
            Check(!skipped.FocusRunning && skipped.FocusPhase == "Short break" && skipped.FocusRemainingSeconds == 300 && skipped.FocusRound == 1, "Skip not persisted");
            Press(Tip(w, "Reset focus timer"));
            var reset = store.Load();
            Check(!reset.FocusRunning && reset.FocusPhase == "Focus" && reset.FocusRemainingSeconds == 1500 && reset.FocusRound == 1, "Reset not persisted");
            Check(!w.IsVisible, "Focus events showed island");
        });
    }
}
