using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace DeskPill
{
    sealed class DictationKeys : IDisposable
    {
        delegate IntPtr Hook(int code, IntPtr message, IntPtr data);
        [StructLayout(LayoutKind.Sequential)]
        struct KeyboardData
        {
            public uint key, scan, flags, time;
            public UIntPtr extra;
        }

        [DllImport("user32.dll")]
        static extern IntPtr SetWindowsHookEx(int id, Hook callback, IntPtr module, uint thread);
        [DllImport("user32.dll")]
        static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")]
        static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
        readonly Hook callback;
        readonly Action toggle, cancel;
        readonly Func<bool> active;
        readonly DictationShortcutGate gate = new DictationShortcutGate();
        IntPtr hook;
        static bool Held(int key)
        {
            return (GetAsyncKeyState(key) & 0x8000) != 0;
        }

        public DictationKeys(Action toggle, Action cancel, Func<bool> active)
        {
            this.toggle = toggle;
            this.cancel = cancel;
            this.active = active;
            callback = Handle;
            hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero)
                throw new InvalidOperationException("Windows could not activate the dictation shortcut.");
        }

        IntPtr Handle(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0)
            {
                var key = (KeyboardData)Marshal.PtrToStructure(data, typeof(KeyboardData));
                bool down = message.ToInt32() == 0x100 || message.ToInt32() == 0x104;
                if ((key.flags & 0x10) == 0)
                {
                    if (key.key == 0x1B && active())
                    {
                        if (down)
                            cancel();
                        return new IntPtr(1);
                    }

                    if (gate.Process((int)key.key, down, Held(0x5B) || Held(0x5C), Held(0x10) || Held(0x11) || Held(0x12)))
                    {
                        if (gate.ToggleRequested)
                        {
                            // Mark the Win gesture as used so releasing Win does not open Start.
                            keybd_event(0xFF, 0, 0, UIntPtr.Zero);
                            keybd_event(0xFF, 0, 2, UIntPtr.Zero);
                            toggle();
                        }

                        return new IntPtr(1);
                    }
                }
            }

            return CallNextHookEx(hook, code, message, data);
        }

        public void Dispose()
        {
            if (hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(hook);
                hook = IntPtr.Zero;
            }
        }
    }

    sealed class SpeechOrb : FrameworkElement
    {
        readonly Stopwatch clock = Stopwatch.StartNew();
        bool attached;
        double displayedLevel, lastFrame;
        public double Level;
        public bool Processing;
        public SpeechOrb()
        {
            Width = Height = 44;
            Loaded += delegate
            {
                Sync();
            };
            Unloaded += delegate
            {
                Detach();
            };
            IsVisibleChanged += delegate
            {
                Sync();
            };
        }

        void Sync()
        {
            if (IsLoaded && IsVisible && Ui.AnimationsAllowed && !attached)
            {
                CompositionTarget.Rendering += Frame;
                attached = true;
            }
            else if (!IsVisible || !Ui.AnimationsAllowed)
                Detach();
            InvalidateVisual();
        }

        void Detach()
        {
            if (attached)
            {
                CompositionTarget.Rendering -= Frame;
                attached = false;
            }
        }

        void Frame(object sender, EventArgs e)
        {
            if (!Ui.AnimationsAllowed)
            {
                Detach();
            }

            double now = clock.Elapsed.TotalSeconds;
            double dt = Math.Min(.1, Math.Max(0, now - lastFrame));
            lastFrame = now;
            displayedLevel += (Level - displayedLevel) * (1 - Math.Exp(-dt / .08));
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext draw)
        {
            double time = Ui.AnimationsAllowed ? clock.Elapsed.TotalSeconds : 0, energy = Math.Max(0, Math.Min(1, Ui.AnimationsAllowed ? displayedLevel : Level));
            draw.DrawEllipse(null, new Pen(Ui.Line, 1), new Point(22, 22), 18, 18);
            draw.DrawEllipse(Ui.Green, null, new Point(22, 22), 5 + energy * 5, 5 + energy * 5);
            for (int i = 0; i < 3; i++)
            {
                double angle = time * (Processing ? 2.5 : .65) + i * Math.PI * 2 / 3;
                double radius = Processing ? 13 : 12 + energy * 3;
                draw.DrawEllipse(Ui.Green, null, new Point(22 + Math.Cos(angle) * radius, 22 + Math.Sin(angle) * radius), 1.7, 1.7);
            }
        }
    }

    sealed class DictationPanel : Window
    {
        readonly TextBlock title, help;
        readonly SpeechOrb orb = new SpeechOrb();
        readonly StackPanel actions;
        readonly StackPanel body;
        readonly Action stop, cancel;
        TextBox review;
        public DictationPanel(Window owner, Action stop, Action cancel)
        {
            this.stop = stop;
            this.cancel = cancel;
            Title = "DeskPill dictation";
            Width = 360;
            Height = 112;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Left = owner.Left + (owner.Width - Width) / 2;
            Top = owner.Top + 54;
            title = Ui.Text("Preparing microphone…", 13, Ui.White);
            title.FontWeight = FontWeights.SemiBold;
            help = Ui.Text("English · private, on-device", 11, Ui.Muted);
            help.Margin = new Thickness(0, 4, 0, 0);
            var row = new DockPanel();
            DockPanel.SetDock(orb, Dock.Left);
            orb.Margin = new Thickness(0, 0, 12, 0);
            row.Children.Add(orb);
            row.Children.Add(Ui.Column(title, help));
            actions = Ui.Row(Ui.Button("Finish", stop), Ui.Button("Cancel", cancel));
            actions.Margin = new Thickness(56, 5, 0, 0);
            body = Ui.Column(row, actions);
            Content = new Border
            {
                Background = Themes.ShellFill,
                BorderBrush = Ui.Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(16, 10, 16, 10),
                Child = body
            };
            if (Ui.AnimationsAllowed)
            {
                Opacity = 0;
                Loaded += delegate
                {
                    BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(130)));
                };
            }

            PreviewKeyDown += delegate (object s, System.Windows.Input.KeyEventArgs e)
            {
                if (e.Key == System.Windows.Input.Key.Escape)
                {
                    cancel();
                    e.Handled = true;
                }
            };
        }

        public void Listening(int seconds, double level)
        {
            title.Text = "Listening · " + seconds / 60 + ":" + (seconds % 60).ToString("00");
            help.Text = "Win+Space finishes · Esc cancels";
            orb.Level = level;
        }

        public void Processing()
        {
            title.Text = "Turning speech into text…";
            help.Text = "English · model unloads when finished";
            orb.Processing = true;
            orb.Level = 0;
            actions.Children.Clear();
            actions.Children.Add(Ui.Button("Cancel", cancel));
        }

        public void Review(string text, Action<string, bool> accept)
        {
            if (review != null)
            {
                review.Text = text;
                return;
            }

            Height = 246;
            title.Text = "Ready to use";
            help.Text = "Review your words before inserting them";
            orb.Visibility = Visibility.Collapsed;
            var input = Ui.Input(text);
            review = input;
            input.Height = 105;
            input.AcceptsReturn = true;
            input.TextWrapping = TextWrapping.Wrap;
            input.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            input.Margin = new Thickness(0, 10, 0, 0);
            body.Children.Insert(1, input);
            actions.Margin = new Thickness(0, 8, 0, 0);
            actions.Children.Clear();
            actions.Children.Add(Ui.Button("Paste", delegate
            {
                accept(input.Text, true);
            }));
            actions.Children.Add(Ui.Button("Copy", delegate
            {
                accept(input.Text, false);
            }));
            actions.Children.Add(Ui.Button("Discard", cancel));
        }
    }

    sealed class DictationController : IDisposable
    {
        readonly Window owner;
        readonly Settings settings;
        readonly Action<string> notify;
        readonly Action prepare;
        readonly DispatcherTimer timer = new DispatcherTimer();
        DictationKeys keys;
        DictationPanel panel;
        DictationRecorder recorder;
        CancellationTokenSource session;
        PasteTarget target;
        DateTime started;
        bool listening, disposed, reviewing;
        public bool Active
        {
            get
            {
                return session != null;
            }
        }

        public DictationController(Window owner, Settings settings, Action<string> notify, Action prepare)
        {
            this.owner = owner;
            this.settings = settings;
            this.notify = notify;
            this.prepare = prepare;
            timer.Interval = TimeSpan.FromMilliseconds(80);
            timer.Tick += delegate
            {
                if (!listening)
                    return;
                int seconds = (int)(DateTime.UtcNow - started).TotalSeconds;
                panel.Listening(seconds, recorder == null ? 0 : recorder.Level);
                if (seconds >= 90 || (recorder != null && recorder.Failed))
                    Finish();
            };
        }

        public void Configure()
        {
            if (keys != null)
            {
                keys.Dispose();
                keys = null;
            }

            Cancel();
            if (!settings.DictationEnabled || disposed)
                return;
            if (!SpeechAssets.Ready)
            {
                notify("Install English dictation in Settings first.");
                return;
            }

            try
            {
                keys = new DictationKeys(delegate
                {
                    owner.Dispatcher.BeginInvoke(new Action(Toggle));
                }, delegate
                {
                    owner.Dispatcher.BeginInvoke(new Action(Cancel));
                }, () => Active);
            }
            catch (Exception e)
            {
                notify(e.Message);
            }
        }

        public async void Toggle()
        {
            if (disposed)
                return;
            if (listening)
            {
                Finish();
                return;
            }

            if (Active)
                return;
            if (!settings.DictationEnabled || !SpeechAssets.Ready)
            {
                notify("Enable English dictation in Settings first.");
                return;
            }

            target = PasteTarget.Capture();
            var token = new CancellationTokenSource();
            session = token;
            prepare();
            panel = new DictationPanel(owner, Finish, Cancel);
            panel.Closed += delegate
            {
                if (Active)
                    Cancel();
            };
            panel.Show();
            try
            {
                var capture = await Task.Run(() => new DictationRecorder());
                if (token.IsCancellationRequested || disposed)
                {
                    await Task.Run((Action)capture.Dispose);
                    return;
                }

                recorder = capture;
                started = DateTime.UtcNow;
                listening = true;
                panel.Listening(0, 0);
                timer.Start();
            }
            catch (Exception e)
            {
                if (!token.IsCancellationRequested)
                {
                    Cancel();
                    notify(e.Message);
                }
            }
        }

        public async void Finish()
        {
            if (!listening || recorder == null)
                return;
            listening = false;
            timer.Stop();
            panel.Processing();
            var capture = recorder;
            recorder = null;
            var token = session;
            try
            {
                byte[] wav = await Task.Run(delegate
                {
                    try
                    {
                        return capture.Finish();
                    }
                    finally
                    {
                        capture.Dispose();
                    }
                });
                if (token.IsCancellationRequested)
                    return;
                string text = await SpeechTranscriber.TranscribeAsync(wav, token.Token);
                if (token.IsCancellationRequested)
                    return;
                if (string.IsNullOrWhiteSpace(text))
                {
                    Cancel();
                    notify("No speech detected. Try again closer to the microphone.");
                    return;
                }

                reviewing = true;
                // A changed foreground app gets review, never an unexpected focus steal or paste.
                if (target != null && (target.IsForeground || panel.IsActive))
                    await Accept(text, true);
                else
                    panel.Review(text, async delegate (string value, bool paste)
                    {
                        await Accept(value, paste);
                    });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                if (!token.IsCancellationRequested)
                {
                    Cancel();
                    notify(e.Message);
                }
            }
        }

        async Task Accept(string text, bool paste)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;
            try
            {
                Clipboard.SetText(text, TextDataFormat.UnicodeText);
            }
            catch
            {
                notify("Clipboard is busy. Your transcript is still available.");
                if (panel != null && reviewing)
                {
                    panel.Review(text, async delegate (string value, bool insert)
                    {
                        await Accept(value, insert);
                    });
                    reviewing = false;
                }

                return;
            }

            var destination = target;
            Cancel();
            if (paste && (destination == null || !await destination.TryPasteAsync()))
                notify("Dictation copied. Paste manually into your app.");
        }

        public void Cancel()
        {
            timer.Stop();
            listening = false;
            reviewing = false;
            var token = session;
            session = null;
            if (token != null)
                token.Cancel();
            var capture = recorder;
            recorder = null;
            if (capture != null)
                Task.Run((Action)capture.Dispose);
            var window = panel;
            panel = null;
            if (window != null)
                window.Close();
            target = null;
        }

        public void Dispose()
        {
            disposed = true;
            if (keys != null)
            {
                keys.Dispose();
                keys = null;
            }

            Cancel();
        }
    }
}
