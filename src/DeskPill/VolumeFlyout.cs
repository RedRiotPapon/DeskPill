using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DeskPill
{
    public sealed class LatestVolumeWriter : IDisposable
    {
        sealed class Command
        {
            public float Value;
            public Action Action;
        }

        readonly object gate = new object ();
        readonly Action<float> apply;
        readonly Queue<Command> pending = new Queue<Command>();
        Command latest;
        bool running, disposed;
        Task completion = Task.FromResult(0);
        public LatestVolumeWriter(Action<float> apply)
        {
            this.apply = apply;
        }

        public bool IsBusy
        {
            get
            {
                lock (gate)
                    return running;
            }
        }

        public Task Completion
        {
            get
            {
                lock (gate)
                    return completion;
            }
        }

        void Start()
        {
            if (!running)
            {
                running = true;
                completion = Task.Run((Func<Task>)Drain);
            }
        }

        public void Set(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return;
            lock (gate)
            {
                if (disposed)
                    return;
                if (latest == null)
                {
                    latest = new Command();
                    pending.Enqueue(latest);
                }

                latest.Value = Math.Max(0, Math.Min(1, value));
                Start();
            }
        }

        public void Invoke(Action action)
        {
            if (action == null)
                return;
            lock (gate)
            {
                if (disposed)
                    return;
                latest = null;
                pending.Enqueue(new Command { Action = action });
                Start();
            }
        }

        async Task Drain()
        {
            while (true)
            {
                await Task.Delay(16).ConfigureAwait(false);
                Command command;
                lock (gate)
                {
                    if (pending.Count == 0)
                    {
                        running = false;
                        return;
                    }

                    command = pending.Dequeue();
                    if (command == latest)
                        latest = null;
                }

                try
                {
                    if (command.Action != null)
                        command.Action();
                    else
                        apply(command.Value);
                }
                catch
                { /* A disconnected endpoint must not interrupt pointer input. */
                }
            }
        }

        public void Dispose()
        {
            lock (gate)
                disposed = true;
        } // Drain final input without blocking WPF.
    }

    sealed class VolumeFlyout : IDisposable
    {
        readonly Border root;
        readonly Popup popup;
        readonly Slider slider;
        readonly TextBlock percentage;
        readonly Button mute;
        readonly LatestVolumeWriter writer;
        readonly bool ownsWriter;
        readonly Action closed;
        readonly DispatcherTimer timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        DateTime deadline, lastInput;
        bool opened, dragging, updating, muted, disposed;
        public bool IsOpen
        {
            get
            {
                return opened;
            }
        }

        public VolumeFlyout(float current, bool muted, Action<float> apply, Action toggleMute, Action closed) : this(current, muted, new LatestVolumeWriter(apply), toggleMute, closed)
        {
            ownsWriter = true;
        }

        public VolumeFlyout(float current, bool muted, LatestVolumeWriter writer, Action toggleMute, Action closed)
        {
            this.closed = closed;
            this.muted = muted;
            this.writer = writer;
            var heading = new DockPanel();
            percentage = Ui.Text(Math.Round(current * 100) + "%", 12, Ui.White);
            percentage.MinWidth = 38;
            percentage.TextAlignment = TextAlignment.Right;
            percentage.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(percentage, Dock.Right);
            heading.Children.Add(percentage);
            mute = Ui.IconButton(muted ? "\uE74F" : "\uE767", muted ? "Unmute" : "Mute", delegate
            {
                if (!opened)
                    return;
                Touch();
                writer.Invoke(toggleMute);
            });
            mute.Margin = new Thickness(-4, 0, 5, 0);
            mute.Width = 26;
            mute.Height = 26;
            DockPanel.SetDock(mute, Dock.Left);
            heading.Children.Add(mute);
            var label = Ui.Text("System volume", 12, Ui.Muted);
            label.VerticalAlignment = VerticalAlignment.Center;
            heading.Children.Add(label);
            slider = new Slider
            {
                Minimum = 0,
                Maximum = 100,
                Value = current * 100,
                Width = 202,
                Height = 28,
                IsMoveToPointEnabled = true,
                IsSnapToTickEnabled = false,
                SmallChange = 1,
                LargeChange = 5,
                Template = Ui.SliderTemplate(),
                ToolTip = "System volume"
            };
            System.Windows.Automation.AutomationProperties.SetName(slider, "System volume");
            slider.ValueChanged += delegate
            {
                percentage.Text = Math.Round(slider.Value) + "%";
                if (!updating && opened)
                {
                    lastInput = DateTime.UtcNow;
                    writer.Set((float)(slider.Value / 100));
                    Touch();
                }
            };
            slider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(delegate
            {
                dragging = true;
                timer.Stop();
            }));
            slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(delegate
            {
                dragging = false;
                Touch(root.IsMouseOver ? 2500 : 450);
            }));
            root = new Border
            {
                Width = 228,
                Background = Ui.Card,
                BorderBrush = Ui.Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12, 8, 12, 7),
                Child = Ui.Column(heading, slider)
            };
            root.MouseEnter += delegate
            {
                Touch();
            };
            root.MouseLeave += delegate
            {
                if (!dragging)
                    Touch(450);
            };
            root.PreviewKeyDown += delegate (object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape)
                {
                    Close();
                    e.Handled = true;
                }
                else
                    Touch();
            };
            popup = new Popup
            {
                Child = root,
                StaysOpen = false,
                AllowsTransparency = true,
                Placement = PlacementMode.Bottom,
                VerticalOffset = 6,
                PopupAnimation = PopupAnimation.None
            };
            popup.Closed += delegate
            {
                Close();
            };
            timer.Tick += delegate
            {
                OnIdle();
            };
        }

        public void Open(FrameworkElement anchor)
        {
            if (disposed)
                return;
            popup.PlacementTarget = anchor;
            opened = true;
            popup.IsOpen = true;
            Touch();
        }

        void Touch(int milliseconds = 2500)
        {
            deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
            if (opened && !dragging)
                timer.Start();
        }

        void OnIdle()
        {
            if (opened && !dragging && DateTime.UtcNow >= deadline)
                Close();
        }

        public void Update(float volume, bool isMuted)
        {
            if (!opened)
                return;
            if (muted != isMuted)
            {
                muted = isMuted;
                mute.Content = Ui.Icon(muted ? "\uE74F" : "\uE767", 16, Ui.White);
                mute.ToolTip = muted ? "Unmute" : "Mute";
            }

            if (volume < 0 || writer.IsBusy || dragging || slider.IsMouseCaptureWithin || DateTime.UtcNow - lastInput < TimeSpan.FromMilliseconds(250))
                return;
            updating = true;
            try
            {
                slider.Value = volume * 100;
            }
            finally
            {
                updating = false;
            }
        }

        public void Close()
        {
            if (!opened)
                return;
            opened = false;
            timer.Stop();
            popup.IsOpen = false;
            if (ownsWriter)
                writer.Dispose();
            disposed = true;
            popup.Child = null;
            popup.PlacementTarget = null;
            closed();
        }

        public void Dispose()
        {
            Close();
            timer.Stop();
            if (ownsWriter)
                writer.Dispose();
            disposed = true;
        }
    }
}
