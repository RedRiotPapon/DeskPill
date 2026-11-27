using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DeskPill
{
    internal static class UtilityUi
    {
        public static void Setup(Window w, string title, double width, double height)
        {
            w.Title = title;
            w.Width = width;
            w.Height = height;
            w.MinWidth = 360;
            w.MinHeight = 260;
            w.Background = Ui.Hex("#101010");
            w.Foreground = Ui.White;
            w.FontFamily = new FontFamily("Segoe UI");
            w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        public static TextBox Output(string text)
        {
            var box = Ui.Input(text);
            box.IsReadOnly = true;
            box.AcceptsReturn = true;
            box.TextWrapping = TextWrapping.Wrap;
            box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            box.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            return box;
        }

        public static void Copy(string text, Action<string> notify)
        {
            try
            {
                if (!string.IsNullOrEmpty(text))
                    Clipboard.SetText(text);
                if (notify != null)
                    notify("Copied");
            }
            catch (Exception)
            {
                if (notify != null)
                    notify("Clipboard is currently unavailable.");
            }
        }
    }

    public sealed class TaskEditor : Window
    {
        public TodoItem Result;
        public bool DeleteRequested;
        readonly TodoItem original;
        TextBox name, start, end;
        TextBlock error;
        public TaskEditor(TodoItem item)
        {
            original = item;
            UtilityUi.Setup(this, item == null ? "New task" : "Edit task", 470, 380);
            var panel = new StackPanel
            {
                Margin = new Thickness(22)
            };
            Content = panel;
            panel.Children.Add(Ui.Text("Task", 22, Ui.White));
            name = Ui.Input(item == null ? "" : item.Title);
            panel.Children.Add(name);
            panel.Children.Add(Ui.Text("Start · optional local date/time (yyyy-MM-dd HH:mm)", 12, Ui.Muted));
            start = Ui.Input(Format(item == null ? null : item.StartsAt));
            panel.Children.Add(start);
            panel.Children.Add(Ui.Text("End · optional local date/time (yyyy-MM-dd HH:mm)", 12, Ui.Muted));
            end = Ui.Input(Format(item == null ? null : item.EndsAt));
            panel.Children.Add(end);
            error = Ui.Text("", 12, Ui.Hex("#FF9B9B"));
            error.TextWrapping = TextWrapping.Wrap;
            error.Margin = new Thickness(0, 10, 0, 10);
            panel.Children.Add(error);
            var save = Ui.Button("Save", Save, true);
            save.IsDefault = true;
            var cancel = Ui.Button("Cancel", delegate
            {
                DialogResult = false;
            });
            cancel.IsCancel = true;
            var row = Ui.Row(save, cancel);
            if (item != null)
                row.Children.Add(Ui.Button("Delete", delegate
                {
                    DeleteRequested = true;
                    DialogResult = true;
                }));
            panel.Children.Add(row);
            Loaded += delegate
            {
                name.Focus();
            };
        }

        static string Format(DateTime? value)
        {
            return value.HasValue ? value.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "";
        }

        static bool Parse(string text, out DateTime? value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(text))
                return true;
            DateTime parsed;
            if (!DateTime.TryParseExact(text.Trim(), "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                return false;
            parsed = DateTime.SpecifyKind(parsed, DateTimeKind.Local);
            if (TimeZoneInfo.Local.IsInvalidTime(parsed))
                return false;
            value = parsed;
            return true;
        }

        void Save()
        {
            DateTime? a, b;
            if (string.IsNullOrWhiteSpace(name.Text))
            {
                error.Text = "Enter a task title.";
                return;
            }

            if (!Parse(start.Text, out a) || !Parse(end.Text, out b))
            {
                error.Text = "Use yyyy-MM-dd HH:mm and a valid local time.";
                return;
            }

            if (a.HasValue && b.HasValue && b <= a)
            {
                error.Text = "End must be after start.";
                return;
            }

            var t = original == null ? new TodoItem
            {
                Id = Guid.NewGuid().ToString("N")
            }

            : new TodoItem
            {
                Id = original.Id,
                Title = original.Title,
                Done = original.Done,
                StartsAt = original.StartsAt,
                EndsAt = original.EndsAt,
                CreatedAt = original.CreatedAt,
                CompletedAt = original.CompletedAt,
                StartNotified = original.StartNotified,
                EndNotified = original.EndNotified
            };
            if (t.StartsAt != a)
                t.StartNotified = false;
            if (t.EndsAt != b)
                t.EndNotified = false;
            t.Title = name.Text.Trim();
            t.StartsAt = a;
            t.EndsAt = b;
            Result = t;
            DialogResult = true;
        }
    }

    public sealed class TextResultWindow : Window
    {
        readonly TextBox output;
        public TextResultWindow(string title, string text)
        {
            UtilityUi.Setup(this, title, 600, 440);
            var root = new DockPanel
            {
                Margin = new Thickness(18)
            };
            var copy = Ui.Button("Copy text", delegate
            {
                UtilityUi.Copy(output.Text, null);
            });
            DockPanel.SetDock(copy, Dock.Bottom);
            root.Children.Add(copy);
            output = UtilityUi.Output(text);
            root.Children.Add(output);
            Content = root;
        }

        public void SetText(string text)
        {
            output.Text = text ?? "";
        }
    }

    public sealed class AskWindow : Window
    {
        readonly Settings settings;
        readonly Action<string> notify;
        readonly TextBox input, output;
        readonly ComboBox language;
        readonly Button send;
        bool closed, busy;
        public AskWindow(Settings settings, Action<string> notify)
        {
            this.settings = settings;
            this.notify = notify;
            UtilityUi.Setup(this, "Ask DeskPill", 580, 440);
            var root = new DockPanel
            {
                Margin = new Thickness(20)
            };
            Content = root;
            var top = new StackPanel();
            DockPanel.SetDock(top, Dock.Top);
            root.Children.Add(top);
            top.Children.Add(Ui.Text("Ask anything", 24, Ui.White));
            top.Children.Add(Ui.Text("Arithmetic works offline. Translation uses your Gemini key.", 12, Ui.Muted));
            input = Ui.Input("");
            input.AcceptsReturn = true;
            input.Height = 80;
            input.TextWrapping = TextWrapping.Wrap;
            input.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            input.Margin = new Thickness(0, 12, 0, 10);
            top.Children.Add(input);
            language = new ComboBox
            {
                ItemsSource = new[]
                {
                    "English",
                    "Bangla",
                    "Hindi",
                    "Arabic",
                    "French",
                    "Spanish",
                    "German",
                    "Japanese"
                },
                SelectedIndex = 0,
                MinWidth = 90,
                Margin = new Thickness(0, 0, 6, 0)
            };
            send = Ui.Button("Send", delegate
            {
                Run(false);
            }, true);
            var row = Ui.Row(send, Ui.Button("Clear", delegate
            {
                if (!busy)
                {
                    input.Clear();
                    output.Clear();
                    input.Focus();
                }
            }), Ui.Button("Copy", delegate
            {
                UtilityUi.Copy(output.Text, notify);
            }), language, Ui.Button("Translate", delegate
            {
                Run(true);
            }));
            top.Children.Add(row);
            top.Children.Add(Ui.Text("Tab from the input opens translation · Ctrl+Enter sends", 11, Ui.Muted));
            output = UtilityUi.Output("");
            output.Margin = new Thickness(0, 12, 0, 0);
            root.Children.Add(output);
            input.PreviewKeyDown += delegate (object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Tab && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    e.Handled = true;
                    language.Focus();
                    language.IsDropDownOpen = true;
                }
                else if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    e.Handled = true;
                    Run(false);
                }
            };
            Closed += delegate
            {
                closed = true;
            };
        }

        async void Run(bool translate)
        {
            if (busy || string.IsNullOrWhiteSpace(input.Text))
                return;
            busy = true;
            send.IsEnabled = false;
            output.Text = translate ? "Translating…" : "Working…";
            string prompt = input.Text.Trim();
            if (!translate && prompt.EndsWith("="))
                prompt = prompt.Substring(0, prompt.Length - 1).TrimEnd();
            try
            {
                var service = new AskService();
                string answer = translate ? await service.TranslateAsync(prompt, Convert.ToString(language.SelectedItem), settings) : await service.AskAsync(prompt, settings);
                if (!closed)
                    output.Text = answer;
            }
            catch (Exception ex)
            {
                if (!closed)
                    output.Text = "Request unavailable: " + ex.Message;
            }
            finally
            {
                busy = false;
                if (!closed)
                    send.IsEnabled = true;
            }
        }
    }

    public sealed class TodayWindow : Window
    {
        readonly AppState state;
        readonly TextBox summary, calendar, weather;
        readonly ComboBox view;
        readonly DatePicker date;
        readonly TextBlock clock;
        readonly DispatcherTimer timer;
        bool closed, loading;
        List<CalendarEvent> events = new List<CalendarEvent>();
        string calendarStatus = "", loadedCalendarSource = "";
        DateTime calendarLoadedAt;
        public TodayWindow(AppState state, Action<string> notify)
        {
            this.state = state;
            UtilityUi.Setup(this, "Today", 640, 500);
            var root = new DockPanel
            {
                Margin = new Thickness(20)
            };
            Content = root;
            var top = Ui.Row(Ui.Text("Today", 25, Ui.White), Ui.Button("Refresh", delegate
            {
                Refresh();
            }));
            clock = Ui.Text("", 12, Ui.Muted);
            var header = Ui.Column(top, clock);
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);
            var tabs = new TabControl
            {
                Margin = new Thickness(0, 14, 0, 0),
                Background = Ui.Card,
                Foreground = Ui.White
            };
            root.Children.Add(tabs);
            summary = UtilityUi.Output("");
            var overview = new DockPanel();
            var controls = new StackPanel
            {
                Margin = new Thickness(8)
            };
            DockPanel.SetDock(controls, Dock.Bottom);
            overview.Children.Add(controls);
            overview.Children.Add(summary);
            tabs.Items.Add(new TabItem { Header = "Overview", Content = overview });
            float volume = SystemService.GetVolume();
            controls.Children.Add(Ui.Text(volume < 0 ? "Volume unavailable" : "Volume", 12, Ui.Muted));
            if (volume >= 0)
            {
                var slider = new Slider
                {
                    Minimum = 0,
                    Maximum = 100,
                    Value = volume * 100,
                    Margin = new Thickness(0, 6, 0, 8)
                };
                slider.ValueChanged += delegate
                {
                    SystemService.SetVolume((float)(slider.Value / 100));
                };
                controls.Children.Add(slider);
                controls.Children.Add(Ui.Button("Mute / unmute", delegate
                {
                    SystemService.ToggleMute();
                }));
            }

            int brightness = SystemService.GetBrightness();
            controls.Children.Add(Ui.Text(brightness < 0 ? "Display brightness unavailable on this monitor" : "Display brightness", 12, Ui.Muted));
            if (brightness >= 0)
            {
                var slider = new Slider
                {
                    Minimum = 0,
                    Maximum = 100,
                    Value = brightness,
                    Margin = new Thickness(0, 6, 0, 8)
                };
                slider.PreviewMouseLeftButtonUp += delegate
                {
                    if (!SystemService.SetBrightness((int)slider.Value) && notify != null)
                        notify("Brightness change unavailable.");
                };
                slider.PreviewKeyUp += delegate
                {
                    if (!SystemService.SetBrightness((int)slider.Value) && notify != null)
                        notify("Brightness change unavailable.");
                };
                controls.Children.Add(slider);
            }

            var calRoot = new DockPanel();
            var tools = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };
            date = new DatePicker
            {
                SelectedDate = DateTime.Today,
                Width = 140
            };
            view = new ComboBox
            {
                ItemsSource = new[]
                {
                    "Month",
                    "Week",
                    "Agenda"
                },
                SelectedIndex = 0,
                Width = 100
            };
            tools.Children.Add(date);
            tools.Children.Add(view);
            DockPanel.SetDock(tools, Dock.Top);
            calRoot.Children.Add(tools);
            calendar = UtilityUi.Output("");
            calRoot.Children.Add(calendar);
            tabs.Items.Add(new TabItem { Header = "Calendar", Content = calRoot });
            weather = UtilityUi.Output("");
            tabs.Items.Add(new TabItem { Header = "Weather & prayer", Content = weather });
            date.SelectedDateChanged += delegate
            {
                RenderCalendar();
            };
            view.SelectionChanged += delegate
            {
                RenderCalendar();
            };
            timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            timer.Tick += delegate
            {
                clock.Text = DateTime.Now.ToString("ddd, dd MMM yyyy · HH:mm:ss") + " local  |  " + DateTime.UtcNow.ToString("HH:mm:ss") + " UTC";
            };
            timer.Start();
            Closed += delegate
            {
                closed = true;
                timer.Stop();
            };
            Loaded += delegate
            {
                Refresh();
            };
        }

        async void Refresh()
        {
            if (loading)
                return;
            loading = true;
            var now = DateTime.Today;
            var today = state.Tasks.Where(t => t.StartsAt.HasValue && t.StartsAt.Value.ToLocalTime().Date == now || t.CompletedAt.ToLocalTime().Date == now).ToList();
            summary.Text = "TODAY’S TASKS\n" + (today.Count == 0 ? "No tasks scheduled or completed today." : string.Join("\n", today.Select(t => (t.Done ? "✓ " : "○ ") + t.Title))) + "\n\nWATER\n" + (state.WaterDate == now.ToString("yyyy-MM-dd") ? state.WaterMl : 0) + " / " + state.Settings.WaterGoalMl + " ml\n\nFOCUS\n" + state.CompletedFocus + " completed sessions (all time)";
            calendarStatus = string.IsNullOrWhiteSpace(state.Settings.CalendarSource) ? "No calendar configured. Set an ICS file or HTTPS feed in Settings." : "Loading configured calendar…";
            RenderCalendar();
            weather.Text = "Loading weather…";
            string requestedCalendarSource = state.Settings.CalendarSource;
            try
            {
                var weatherTask = new AskService().WeatherAsync(state.Settings.Latitude, state.Settings.Longitude);
                try
                {
                    var loadedEvents = await new CalendarService().LoadAsync(requestedCalendarSource);
                    if (requestedCalendarSource == state.Settings.CalendarSource)
                    {
                        events = loadedEvents;
                        loadedCalendarSource = requestedCalendarSource;
                        calendarLoadedAt = DateTime.Now;
                        calendarStatus = string.IsNullOrWhiteSpace(requestedCalendarSource) ? "No calendar configured. Set an ICS file or HTTPS feed in Settings." : "Configured calendar · " + events.Count + " events loaded. Recurring rules are not expanded.";
                    }
                    else
                    {
                        events.Clear();
                        calendarStatus = "Calendar source changed. Refresh to load the new source.";
                    }
                }
                catch (Exception ex)
                {
                    if (loadedCalendarSource != state.Settings.CalendarSource)
                        events.Clear();
                    calendarStatus = events.Count > 0 ? "Stale calendar · last loaded " + calendarLoadedAt.ToString("g") + ". Refresh failed: " + ex.Message : "Calendar unavailable: " + ex.Message;
                }

                if (closed)
                    return;
                RenderCalendar();
                string forecast = await weatherTask;
                if (closed)
                    return;
                var prayers = PrayerService.Calculate(now, state.Settings.Latitude, state.Settings.Longitude, state.Settings.PrayerMethod);
                weather.Text = "WEATHER · " + state.Settings.Latitude.ToString("0.####") + ", " + state.Settings.Longitude.ToString("0.####") + "\n" + forecast + "\n\nPRAYER · " + state.Settings.PrayerMethod + " · calculated in computer local timezone\n" + (prayers.Count == 0 ? "Prayer times unavailable for these settings/date." : string.Join("\n", prayers.Select(p => p.Name + "  " + p.Time.ToString("HH:mm")))) + "\nApproximate solar calculation; confirm with your local mosque.";
            }
            catch (Exception ex)
            {
                if (!closed)
                    weather.Text = "Unavailable: " + ex.Message;
            }
            finally
            {
                loading = false;
            }
        }

        void RenderCalendar()
        {
            if (calendar == null)
                return;
            DateTime selected = date.SelectedDate ?? DateTime.Today;
            string mode = Convert.ToString(view.SelectedItem);
            DateTime begin = mode == "Month" ? new DateTime(selected.Year, selected.Month, 1) : mode == "Week" ? selected.Date.AddDays(-((int)selected.DayOfWeek + 6) % 7) : selected.Date;
            DateTime finish = mode == "Month" ? begin.AddMonths(1) : mode == "Week" ? begin.AddDays(7) : begin.AddDays(30);
            var list = events.Where(e => e.StartsAt < finish && (e.EndsAt > begin || e.StartsAt >= begin)).OrderBy(e => e.StartsAt).ToList();
            calendar.Text = calendarStatus + "\n\n" + begin.ToString("dd MMM yyyy") + " – " + finish.AddDays(-1).ToString("dd MMM yyyy") + "\n\n" + (list.Count == 0 ? "No loaded events in this range." : string.Join("\n\n", list.Select(e => e.StartsAt.ToString("ddd dd MMM ") + (e.AllDay ? "All day" : e.StartsAt.ToString("HH:mm") + " – " + e.EndsAt.ToString("HH:mm")) + "\n" + e.Title + (string.IsNullOrEmpty(e.Note) ? "" : "\n" + e.Note))));
        }
    }

    public sealed class RegionSelector : Window
    {
        readonly TaskCompletionSource<System.Drawing.Bitmap> completion = new TaskCompletionSource<System.Drawing.Bitmap>();
        public Task<System.Drawing.Bitmap> Completion
        {
            get
            {
                return completion.Task;
            }
        }

        System.Drawing.Bitmap shot;
        readonly Canvas canvas;
        readonly Rectangle selection;
        Point origin;
        bool dragging;
        System.Drawing.Rectangle bounds;
        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int width, int height, uint flags);
        public RegionSelector()
        {
            bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
            shot = new System.Drawing.Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                using (var graphics = System.Drawing.Graphics.FromImage(shot))
                    graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
            }
            catch
            {
                shot.Dispose();
                throw;
            }

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Background = Brushes.Black;
            Cursor = Cursors.Cross;
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;
            canvas = new Canvas
            {
                ClipToBounds = true,
                Background = Brushes.Black
            };
            Content = canvas;
            BitmapSource source;
            using (var stream = new System.IO.MemoryStream())
            {
                shot.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                stream.Position = 0;
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                source = image;
            }

            var backdrop = new Image
            {
                Source = source,
                Stretch = Stretch.Fill,
                Opacity = .42
            };
            canvas.Children.Add(backdrop);
            selection = new Rectangle
            {
                Stroke = Ui.Green,
                StrokeThickness = 2,
                Fill = Ui.Hex("#225533"),
                Opacity = .7
            };
            canvas.Children.Add(selection);
            var help = Ui.Text("Drag to capture text · Esc to cancel", 18, Ui.White);
            Canvas.SetLeft(help, 30);
            Canvas.SetTop(help, 30);
            canvas.Children.Add(help);
            SizeChanged += delegate
            {
                backdrop.Width = ActualWidth;
                backdrop.Height = ActualHeight;
            };
            SourceInitialized += delegate
            {
                SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x0040);
            };
            MouseLeftButtonDown += delegate (object sender, MouseButtonEventArgs e)
            {
                origin = e.GetPosition(canvas);
                dragging = true;
                CaptureMouse();
            };
            MouseMove += delegate (object sender, MouseEventArgs e)
            {
                if (!dragging)
                    return;
                var p = e.GetPosition(canvas);
                Canvas.SetLeft(selection, Math.Min(p.X, origin.X));
                Canvas.SetTop(selection, Math.Min(p.Y, origin.Y));
                selection.Width = Math.Abs(p.X - origin.X);
                selection.Height = Math.Abs(p.Y - origin.Y);
            };
            MouseLeftButtonUp += delegate (object sender, MouseButtonEventArgs e)
            {
                if (!dragging)
                    return;
                dragging = false;
                ReleaseMouseCapture();
                var p = e.GetPosition(canvas);
                double sx = bounds.Width / canvas.ActualWidth, sy = bounds.Height / canvas.ActualHeight;
                int x = Math.Max(0, (int)Math.Floor(Math.Min(origin.X, p.X) * sx)), y = Math.Max(0, (int)Math.Floor(Math.Min(origin.Y, p.Y) * sy));
                int right = Math.Min(bounds.Width, (int)Math.Ceiling(Math.Max(origin.X, p.X) * sx)), bottom = Math.Min(bounds.Height, (int)Math.Ceiling(Math.Max(origin.Y, p.Y) * sy));
                if (right - x > 2 && bottom - y > 2)
                    completion.TrySetResult(shot.Clone(new System.Drawing.Rectangle(x, y, right - x, bottom - y), shot.PixelFormat));
                Close();
            };
            KeyDown += delegate (object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape)
                    Close();
            };
            Closed += delegate
            {
                completion.TrySetResult(null);
                if (shot != null)
                {
                    shot.Dispose();
                    shot = null;
                }
            };
        }
    }
}
