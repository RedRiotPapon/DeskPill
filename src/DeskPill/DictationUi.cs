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
        readonly Action toggle, finish, cancel;
        readonly Func<bool> active;
        readonly DictationShortcutGate gate = new DictationShortcutGate();
        IntPtr hook;
        static bool Held(int key)
        {
            return (GetAsyncKeyState(key) & 0x8000) != 0;
        }

        internal static bool PasteModifiersHeld()
        {
            return Held(0x5B) || Held(0x5C) || Held(0x10) || Held(0x11) || Held(0x12);
        }

        public DictationKeys(Action toggle, Action finish, Action cancel, Func<bool> active)
        {
            this.toggle = toggle;
            this.finish = finish;
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

                    bool consumed = gate.Process((int)key.key, down, Held(0x5B) || Held(0x5C), Held(0x10) || Held(0x11) || Held(0x12));
                    if (gate.FinishRequested)
                        finish();
                    if (consumed)
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

    sealed class DictationWaveform : FrameworkElement
    {
        readonly Stopwatch clock = Stopwatch.StartNew();
        bool attached;
        double displayedLevel, lastFrame;
        readonly double[] levels = new double[18];
        double sampleTime;
        double level;
        public double Level
        {
            get
            {
                return level;
            }

            set
            {
                level = Math.Max(0, Math.Min(1, value));
                if (!Ui.AnimationsAllowed)
                    InvalidateVisual();
            }
        }

        public bool Processing;
        public DictationWaveform()
        {
            Width = 100;
            Height = 42;
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
                lastFrame = clock.Elapsed.TotalSeconds;
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
            sampleTime += dt;
            while (sampleTime >= .045)
            {
                sampleTime -= .045;
                Array.Copy(levels, 1, levels, 0, levels.Length - 1);
                levels[levels.Length - 1] = Math.Sqrt(Math.Max(0, Math.Min(1, displayedLevel)));
            }

            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext draw)
        {
            double time = Ui.AnimationsAllowed ? clock.Elapsed.TotalSeconds : 0;
            double energy = Math.Sqrt(Math.Max(0, Math.Min(1, Ui.AnimationsAllowed ? displayedLevel : Level)));
            // A microphone silhouette, with a quiet breathing halo driven by actual input.
            var pen = new Pen(Ui.Green, 1.5)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            double lift = Processing && Ui.AnimationsAllowed ? Math.Sin(time * 3) * 1.2 : -energy;
            draw.PushTransform(new TranslateTransform(0, lift));
            draw.DrawRoundedRectangle(Ui.Green, null, new Rect(10, 11, 7, 13), 3.5, 3.5);
            var arc = new StreamGeometry();
            using (var c = arc.Open())
            {
                c.BeginFigure(new Point(7, 20), false, false);
                c.ArcTo(new Point(20, 20), new Size(6.5, 8), 0, false, SweepDirection.Counterclockwise, true, false);
            }

            draw.DrawGeometry(null, pen, arc);
            draw.DrawLine(pen, new Point(13.5, 28), new Point(13.5, 32));
            draw.DrawLine(pen, new Point(10, 32), new Point(17, 32));
            draw.Pop();
            var barPen = new Pen(Ui.Green, 2.4)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            for (int i = 0; i < levels.Length; i++)
            {
                double amplitude = Ui.AnimationsAllowed ? levels[i] : energy;
                double height = Processing ? 2 : 2 + amplitude * 27;
                double y = Processing && Ui.AnimationsAllowed ? Math.Sin(time * 3.4 - i * .4) * 3 : 0;
                double x = 31 + i * 3.8;
                draw.DrawLine(barPen, new Point(x, 21 + y - height / 2), new Point(x, 21 + y + height / 2));
            }
        }
    }

    sealed class DictationView : Border
    {
        readonly TextBlock title, help;
        readonly DictationWaveform orb = new DictationWaveform();
        readonly StackPanel actions;
        readonly StackPanel body;
        readonly Action cancel;
        public bool HoldToTalk = true;
        TextBox review;
        public DictationView(Action stop, Action cancel)
        {
            this.cancel = cancel;
            Width = 348;
            Height = 70;
            Padding = new Thickness(10, 8, 10, 6);
            title = Ui.Text("Preparing microphone…", 13, Ui.White);
            title.FontWeight = FontWeights.SemiBold;
            help = Ui.Text("English · private, on-device", 11, Ui.Muted);
            help.Margin = new Thickness(0, 4, 0, 0);
            var row = new DockPanel();
            DockPanel.SetDock(orb, Dock.Left);
            orb.Margin = new Thickness(0, 0, 12, 0);
            orb.ToolTip = "Finish dictation";
            orb.MouseLeftButtonUp += delegate
            {
                stop();
            };
            row.Children.Add(orb);
            row.Children.Add(Ui.Column(title, help));
            actions = Ui.Row();
            actions.Visibility = Visibility.Collapsed;
            actions.Margin = new Thickness(56, 5, 0, 0);
            body = Ui.Column(row, actions);
            Child = body;
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
            help.Text = HoldToTalk ? "Release to insert · Esc cancels" : "Click mic to finish · Esc cancels";
            orb.Level = level;
        }

        public void Processing()
        {
            title.Text = "Turning speech into text…";
            help.Text = "On-device English · Esc cancels";
            orb.Processing = true;
            orb.Level = 0;
            orb.InvalidateVisual();
            actions.Children.Clear();
            actions.Visibility = Visibility.Collapsed;
        }

        public void WaitingForRelease()
        {
            title.Text = "Ready to insert";
            help.Text = "Release shortcut keys · Esc cancels";
            orb.Processing = false;
            orb.Level = 0;
            orb.InvalidateVisual();
        }

        public void Review(string text, Action<string, bool> accept)
        {
            if (review != null)
            {
                review.Text = text;
                return;
            }

            Height = 218;
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
            actions.Visibility = Visibility.Visible;
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
        readonly IslandWindow owner;
        readonly Settings settings;
        readonly Action<string> notify;
        readonly Action prepare;
        readonly DispatcherTimer timer = new DispatcherTimer();
        DictationKeys keys;
        DictationView panel;
        DictationRecorder recorder;
        CancellationTokenSource session;
        PasteTarget target;
        DateTime started;
        bool listening, disposed, reviewing, finishPending;
        public bool Active
        {
            get
            {
                return session != null;
            }
        }

        public DictationController(IslandWindow owner, Settings settings, Action<string> notify, Action prepare)
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
                    owner.Dispatcher.BeginInvoke(new Action(Begin));
                }, delegate
                {
                    owner.Dispatcher.BeginInvoke(new Action(Finish));
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

        public void Toggle()
        {
            if (Active)
                Finish();
            else
            {
                Begin();
                if (panel != null)
                    panel.HoldToTalk = false;
            }
        }

        public async void Begin()
        {
            if (disposed)
                return;
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
            finishPending = false;
            prepare();
            panel = new DictationView(Finish, Cancel);
            owner.ShowDictation(panel);
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
                if (finishPending)
                    Finish();
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
            {
                if (Active && !reviewing)
                    finishPending = true;
                return;
            }

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
                if (target != null && await DictationInsertion.WaitForReleaseAsync(DictationKeys.PasteModifiersHeld, () => target != null && target.IsForeground, panel.WaitingForRelease, token.Token))
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
            finishPending = false;
            var token = session;
            session = null;
            if (token != null)
                token.Cancel();
            var capture = recorder;
            recorder = null;
            if (capture != null)
                Task.Run((Action)capture.Dispose);
            var view = panel;
            panel = null;
            if (view != null)
                owner.HideDictation(view);
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
