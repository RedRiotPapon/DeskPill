using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Markup;
using System.Windows.Shapes;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DeskPill
{
    public static class Ui
    {
        public static bool MotionEnabled = true;
        public static bool AnimationsAllowed
        {
            get
            {
                return MotionEnabled && SystemParameters.ClientAreaAnimation;
            }
        }

        public static readonly Brush White = Hex("#F4F4F4"), Muted = Hex("#929292"), Green = Hex("#48E572"), Card = Hex("#151515"), Line = Hex("#222222");
        public static readonly Brush DockIdle = Hex("#1A1A1A"), DockActive = Hex("#1B3B26");

        public static Brush Hex(string color)
        {
            return Themes.Brush(color);
        }

        public static TextBlock Text(string text, double size, Brush color)
        {
            return new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = size,
                Foreground = color,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
        }

        public static TextBlock Icon(string glyph, double size, Brush color)
        {
            var t = Text(glyph, size, color);
            t.FontFamily = new FontFamily("Segoe MDL2 Assets");
            return t;
        }

        static void AttachSpringScale(Button b, double hoverScale, double pressScale, int hoverMs, int pressMs)
        {
            var feedback = new ScaleTransform(1, 1);
            b.RenderTransformOrigin = new Point(.5, .5);
            b.RenderTransform = feedback;
            Action<double, int, bool> ease = delegate (double target, int milliseconds, bool bouncy)
            {
                if (!b.IsLoaded || !AnimationsAllowed)
                {
                    feedback.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    feedback.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    feedback.ScaleX = feedback.ScaleY = 1;
                    return;
                }

                IEasingFunction easing = bouncy
                    ? (IEasingFunction)new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.38 }
                    : (IEasingFunction)new CubicEase { EasingMode = EasingMode.EaseOut };
                var transition = new DoubleAnimation(target, TimeSpan.FromMilliseconds(milliseconds))
                {
                    EasingFunction = easing
                };
                feedback.BeginAnimation(ScaleTransform.ScaleXProperty, transition);
                feedback.BeginAnimation(ScaleTransform.ScaleYProperty, transition);
            };
            b.MouseEnter += delegate
            {
                ease(hoverScale, hoverMs, true);
            };
            b.MouseLeave += delegate
            {
                ease(1, hoverMs + 20, false);
            };
            b.PreviewMouseLeftButtonDown += delegate
            {
                ease(pressScale, pressMs, false);
            };
            b.PreviewMouseLeftButtonUp += delegate
            {
                ease(b.IsMouseOver ? hoverScale : 1, hoverMs - 10, true);
            };
        }

        static Button CreateButton(string text, Action action, bool primary, double hoverScale, double pressScale, int hoverMs, int pressMs)
        {
            var b = new Button
            {
                Content = text,
                Foreground = primary ? Themes.PrimaryInk : White,
                Background = primary ? Green : Hex("#1D1D1D"),
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 0, 5, 0),
                MinHeight = 28,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            b.Click += delegate
            {
                if (action != null)
                    action();
            };
            AttachSpringScale(b, hoverScale, pressScale, hoverMs, pressMs);
            return b;
        }

        public static Button Button(string text, Action action, bool primary)
        {
            return CreateButton(text, action, primary, 1.08, 0.92, 150, 55);
        }

        public static Button Button(string text, Action action)
        {
            return Button(text, action, false);
        }

        public static Button IconButton(string glyph, string label, Action action)
        {
            var b = Button("", action);
            b.Content = Icon(glyph, 14, White);
            b.ToolTip = label;
            b.Width = 30;
            b.Padding = new Thickness(4);
            System.Windows.Automation.AutomationProperties.SetName(b, label);
            return b;
        }

        public static Button DockIconButton(string glyph, string label, bool active, Action action)
        {
            var b = CreateButton("", action, false, 1.08, 0.94, 150, 55);
            b.Content = Icon(glyph, 13, active ? Green : White);
            b.ToolTip = label;
            b.Width = 30;
            b.Height = 30;
            b.MinHeight = 30;
            b.Padding = new Thickness(0);
            b.Margin = new Thickness(3, 0, 3, 0);
            b.Background = active ? DockActive : DockIdle;
            System.Windows.Automation.AutomationProperties.SetName(b, label);
            return b;
        }

        public static void SetDockButtonActive(Button b, bool active)
        {
            if (b == null)
                return;
            b.Background = active ? DockActive : DockIdle;
            var icon = b.Content as TextBlock;
            if (icon != null)
                icon.Foreground = active ? Green : White;
        }

        public static Border Panel(UIElement child)
        {
            return new Border
            {
                Background = Card,
                BorderBrush = Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(12),
                Child = child
            };
        }

        public static ControlTemplate SliderTemplate()
        {
            return (ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Slider'>
<Grid Height='26' Margin='8,0'><Border Height='3' VerticalAlignment='Center' Background='{DynamicResource DeskPillTooltipLine}' CornerRadius='1.5'/><Track x:Name='PART_Track' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' Value='{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}' IsDirectionReversed='{TemplateBinding IsDirectionReversed}'>
<Track.DecreaseRepeatButton><RepeatButton Command='{x:Static Slider.DecreaseLarge}' IsTabStop='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Height='3' VerticalAlignment='Center' Background='{DynamicResource DeskPillAccent}' CornerRadius='1.5'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
<Track.Thumb><Thumb Width='14' Height='14' Cursor='Hand'><Thumb.Template><ControlTemplate TargetType='Thumb'><Border CornerRadius='7' Background='{DynamicResource DeskPillAccent}'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
<Track.IncreaseRepeatButton><RepeatButton Command='{x:Static Slider.IncreaseLarge}' IsTabStop='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
</Track></Grid></ControlTemplate>");
        }

        public static TextBox Input(string text)
        {
            return new TextBox
            {
                Text = text ?? "",
                Foreground = White,
                Background = Hex("#222222"),
                BorderBrush = Hex("#363636"),
                Padding = new Thickness(10, 7, 10, 7),
                FontSize = 13,
                MinHeight = 32,
                CaretBrush = Green,
                SelectionBrush = Hex("#286A3B")
            };
        }

        public static StackPanel Row(params UIElement[] children)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            foreach (var c in children)
                panel.Children.Add(c);
            return panel;
        }

        public static StackPanel Column(params UIElement[] children)
        {
            var panel = new StackPanel();
            foreach (var c in children)
                panel.Children.Add(c);
            return panel;
        }

        public static void Init(Application app)
        {
            app.Resources["DeskPillAccent"] = Green;
            app.Resources["DeskPillHoverFill"] = Hex("#18FFFFFF");
            app.Resources["DeskPillHoverRim"] = Hex("#26FFFFFF");
            app.Resources["DeskPillText"] = White;
            app.Resources["DeskPillTooltipCard"] = Hex("#262626");
            app.Resources["DeskPillTooltipLine"] = Hex("#444444");
            app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(@"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
    <Style TargetType='Button'><Setter Property='FontFamily' Value='Segoe UI'/><Setter Property='FontSize' Value='11'/><Setter Property='BorderThickness' Value='0'/><Setter Property='HorizontalContentAlignment' Value='Center'/><Setter Property='VerticalContentAlignment' Value='Center'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border x:Name='shell' Background='{TemplateBinding Background}' CornerRadius='15' BorderThickness='1' BorderBrush='Transparent'><Grid><Border x:Name='glow' CornerRadius='14' Background='{DynamicResource DeskPillHoverFill}' Opacity='0' IsHitTestVisible='False'/><ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center'/></Grid></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='glow' Property='Opacity' Value='1'/><Setter TargetName='shell' Property='BorderBrush' Value='{DynamicResource DeskPillHoverRim}'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='shell' Property='Opacity' Value='.84'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter TargetName='shell' Property='Opacity' Value='.34'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='shell' Property='BorderThickness' Value='1'/><Setter TargetName='shell' Property='BorderBrush' Value='{DynamicResource DeskPillAccent}'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
    <Style TargetType='CheckBox'><Setter Property='Foreground' Value='{DynamicResource DeskPillText}'/><Setter Property='FontSize' Value='13'/><Setter Property='Margin' Value='0,6,0,6'/></Style>
    <Style TargetType='ToolTip'><Setter Property='Background' Value='{DynamicResource DeskPillTooltipCard}'/><Setter Property='Foreground' Value='{DynamicResource DeskPillText}'/><Setter Property='BorderBrush' Value='{DynamicResource DeskPillTooltipLine}'/></Style>
    <Style TargetType='ScrollViewer'><Setter Property='HorizontalScrollBarVisibility' Value='Disabled'/><Setter Property='VerticalScrollBarVisibility' Value='Auto'/></Style>
   </ResourceDictionary>"));
        }
    }

    public sealed class Ring : FrameworkElement
    {
        static readonly DependencyProperty DisplayProgressProperty = DependencyProperty.Register("DisplayProgress", typeof(double), typeof(Ring), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
        static readonly Typeface SegoeUiFace = new Typeface("Segoe UI");
        static readonly Typeface Mdl2Face = new Typeface("Segoe MDL2 Assets");
        static readonly Brush ThinTrackBrush = Ui.Hex("#182838");
        static readonly Brush WideTrackBrush = Ui.Hex("#252525");

        double progress;
        Brush cachedAccent;
        double cachedStroke = -1;
        Pen cachedTrackPen, cachedAccentPen;
        public double Progress
        {
            get
            {
                return progress;
            }

            set
            {
                double next = Math.Max(0, Math.Min(1, value));
                if (Math.Abs(next - progress) < .00001)
                    return;
                progress = next;
                if (IsLoaded && IsVisible && Ui.AnimationsAllowed)
                    BeginAnimation(DisplayProgressProperty, new DoubleAnimation(next, TimeSpan.FromMilliseconds(180)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
                else
                {
                    BeginAnimation(DisplayProgressProperty, null);
                    SetValue(DisplayProgressProperty, next);
                }
            }
        }

        public Brush Accent = Ui.Green;
        public double Stroke = 4;
        string center = "";
        public string Center
        {
            get
            {
                return center;
            }

            set
            {
                string next = value ?? "";
                if (center == next)
                    return;
                center = next;
                InvalidateVisual();
            }
        }

        string iconGlyph = "";
        public string IconGlyph
        {
            get
            {
                return iconGlyph;
            }

            set
            {
                string next = value ?? "";
                if (iconGlyph == next)
                    return;
                iconGlyph = next;
                InvalidateVisual();
            }
        }

        public double IconSize = 7.5;

        void EnsurePens()
        {
            if (cachedTrackPen != null && cachedAccentPen != null && cachedStroke == Stroke && object.ReferenceEquals(cachedAccent, Accent))
                return;
            Brush track = Stroke <= 2.5 ? ThinTrackBrush : WideTrackBrush;
            cachedTrackPen = new Pen(track, Stroke);

            cachedAccentPen = new Pen(Accent, Stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

            cachedStroke = Stroke;
            cachedAccent = Accent;
        }

        protected override void OnRender(DrawingContext d)
        {
            base.OnRender(d);
            EnsurePens();
            double displayed = (double)GetValue(DisplayProgressProperty);
            double r = Math.Max(1, Math.Min(ActualWidth, ActualHeight) / 2 - Stroke), x = ActualWidth / 2, y = ActualHeight / 2;
            d.DrawEllipse(null, cachedTrackPen, new Point(x, y), r, r);
            if (displayed >= .999)
                d.DrawEllipse(null, cachedAccentPen, new Point(x, y), r, r);
            else if (displayed > 0)
            {
                double a = displayed * Math.PI * 2;
                var geometry = new StreamGeometry();
                using (var c = geometry.Open())
                {
                    c.BeginFigure(new Point(x, y - r), false, false);
                    c.ArcTo(new Point(x + Math.Sin(a) * r, y - Math.Cos(a) * r), new Size(r, r), 0, displayed > .5, SweepDirection.Clockwise, true, false);
                }

                geometry.Freeze();
                d.DrawGeometry(null, cachedAccentPen, geometry);
            }

            if (!string.IsNullOrEmpty(Center))
            {
                var t = new FormattedText(Center, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, SegoeUiFace, Math.Min(18, r * .65), Ui.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                d.DrawText(t, new Point(x - t.Width / 2, y - t.Height / 2));
            }
            else if (!string.IsNullOrEmpty(iconGlyph))
            {
                var g = new FormattedText(iconGlyph, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Mdl2Face, IconSize, Ui.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                d.DrawText(g, new Point(x - g.Width / 2, y - g.Height / 2));
            }
        }
    }

    public sealed class EqBars : FrameworkElement
    {
        static readonly double[] IdleHeights = { 0.36, 0.72, 0.50, 0.86, 0.42 };
        readonly DispatcherTimer timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        readonly double[] levels = { 0.36, 0.72, 0.50, 0.86, 0.42 };
        double phase;
        bool playing;
        public Brush IdleBrush = Ui.Hex("#8E8E8E");
        public Brush ActiveBrush = Ui.Green;

        public EqBars()
        {
            Width = 18;
            Height = 12;
            IsHitTestVisible = false;
            timer.Tick += delegate
            {
                if (!playing || !IsVisible || !Ui.AnimationsAllowed)
                {
                    timer.Stop();
                    return;
                }

                phase += 0.22;
                for (int i = 0; i < 5; i++)
                {
                    double wave = 0.48 + 0.46 * Math.Sin(phase * (1.1 + i * 0.21) + i * 1.35);
                    levels[i] = levels[i] + (wave - levels[i]) * 0.36;
                }

                InvalidateVisual();
            };
            IsVisibleChanged += delegate
            {
                SyncTimer();
            };
            Unloaded += delegate
            {
                timer.Stop();
            };
        }

        public bool Playing
        {
            get
            {
                return playing;
            }

            set
            {
                if (playing == value)
                    return;
                playing = value;
                if (!playing)
                {
                    for (int i = 0; i < 5; i++)
                        levels[i] = IdleHeights[i];
                }

                SyncTimer();
                InvalidateVisual();
            }
        }

        void SyncTimer()
        {
            if (playing && IsLoaded && IsVisible && Ui.AnimationsAllowed)
            {
                if (!timer.IsEnabled)
                    timer.Start();
            }
            else if (timer.IsEnabled)
            {
                timer.Stop();
            }
        }

        protected override void OnRender(DrawingContext d)
        {
            base.OnRender(d);
            double w = ActualWidth > 0 ? ActualWidth : Width;
            double h = ActualHeight > 0 ? ActualHeight : Height;
            Brush brush = playing ? ActiveBrush : IdleBrush;
            double barW = 2.0;
            double gap = Math.Max(1.2, (w - 5.0 * barW) / 4.0);
            for (int i = 0; i < 5; i++)
            {
                double x = i * (barW + gap);
                double barH = Math.Max(3.0, levels[i] * h);
                double y = (h - barH) * 0.5;
                d.DrawRoundedRectangle(brush, null, new Rect(x, y, barW, barH), 1.0, 1.0);
            }
        }
    }

    public sealed class NotchMascot : FrameworkElement
    {
        static readonly Brush EyeBrush = Ui.Hex("#F4F4F4");
        static readonly Brush CheekBrush = Ui.Hex("#5548E572");
        static readonly Brush StarBrush = Ui.Hex("#E7BE54");
        static readonly Brush ZBrush = Ui.Hex("#9CD4FF");
        static readonly Typeface SegoeUiFace = new Typeface("Segoe UI");

        readonly DispatcherTimer timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        double lastTime;

        int mood; // 0=Awake, 1=Drowsy, 2=Sleeping, 3=Alert
        double moodElapsed;
        double blinkCooldown = 2.1;
        double blinkProgress = -1.0;
        double idleSeconds;
        double sleepPhase;
        uint lcg = 0x13572468;

        double eyeOpen = 1.0, targetEyeOpen = 1.0;
        double lookX, targetLookX;
        double lookY, targetLookY;
        double bounceY, targetBounceY;
        double happyAmount, targetHappy;
        double sleepZ, targetSleepZ;
        Pen cachedHappyPen;
        bool active;

        public NotchMascot()
        {
            Width = 22;
            Height = 16;
            IsHitTestVisible = false;
            cachedHappyPen = new Pen(EyeBrush, 1.65) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

            timer.Tick += delegate
            {
                Step();
            };
            IsVisibleChanged += delegate
            {
                SyncTimer();
            };
            Unloaded += delegate
            {
                timer.Stop();
            };
        }

        public void SetActive(bool value)
        {
            active = value;
            SyncTimer();
            if (!value)
                InvalidateVisual();
        }

        public void Wake(double lookDirX)
        {
            mood = 3;
            moodElapsed = 0;
            idleSeconds = 0;
            blinkProgress = -1.0;
            targetLookX = Math.Max(-1.0, Math.Min(1.0, lookDirX));
            targetLookY = -0.35;
            if (!Ui.AnimationsAllowed)
            {
                eyeOpen = 1.0;
                happyAmount = 0;
                sleepZ = 0;
                InvalidateVisual();
            }
        }

        public void NudgeLook(double normalizedX)
        {
            idleSeconds = 0;
            if (mood == 1 || mood == 2)
                Wake(normalizedX);
            else
                targetLookX = Math.Max(-1.0, Math.Min(1.0, normalizedX));
        }

        void SyncTimer()
        {
            if (active && IsLoaded && IsVisible && Ui.AnimationsAllowed)
            {
                lastTime = clock.Elapsed.TotalSeconds;
                if (!timer.IsEnabled)
                    timer.Start();
            }
            else if (timer.IsEnabled)
            {
                timer.Stop();
            }
        }

        double NextRand()
        {
            lcg = lcg * 1664525u + 1013904223u;
            return ((lcg >> 8) & 0xFFFF) / 65535.0;
        }

        void Step()
        {
            if (!active || !IsVisible || !Ui.AnimationsAllowed)
            {
                timer.Stop();
                return;
            }

            double now = clock.Elapsed.TotalSeconds;
            double dt = Math.Max(0.005, Math.Min(0.05, now - lastTime));
            lastTime = now;

            moodElapsed += dt;
            idleSeconds += dt;
            sleepPhase += dt * 2.1;

            if (mood == 0 && idleSeconds > 25.0)
            {
                mood = 1;
                moodElapsed = 0;
            }

            if (mood == 3)
            {
                if (moodElapsed > 5.2)
                {
                    mood = 0;
                    moodElapsed = 0;
                    targetBounceY = 0;
                    targetHappy = 0;
                    targetEyeOpen = 1.0;
                }
                else if (moodElapsed < 0.3)
                {
                    targetEyeOpen = 1.15;
                    targetBounceY = -1.4;
                    targetHappy = 0;
                }
                else
                {
                    targetEyeOpen = 1.0;
                    targetBounceY = 0;
                    targetHappy = (moodElapsed > 0.4 && moodElapsed < 2.9) ? 1.0 : 0.0;
                    targetSleepZ = (moodElapsed > 0.3 && moodElapsed < 3.2) ? 1.0 : 0.0;
                }
            }
            else if (mood == 1)
            {
                if (moodElapsed > 5.5)
                {
                    mood = 2;
                    moodElapsed = 0;
                }
                else
                {
                    targetEyeOpen = 0.6;
                    targetLookX = Math.Sin(moodElapsed * 2.2) * 0.9;
                    targetLookY = Math.Sin(moodElapsed * 1.4) * 0.2;
                    targetBounceY = 0.3;
                    targetHappy = 0;
                    targetSleepZ = 0;
                }
            }
            else if (mood == 2)
            {
                targetEyeOpen = 0.12;
                targetLookX = 0;
                targetLookY = 0.9;
                targetBounceY = Math.Sin(sleepPhase) * 0.4;
                targetHappy = 0;
                targetSleepZ = 1.0;
            }
            else
            {
                targetEyeOpen = 1.0;
                targetBounceY = Math.Sin(sleepPhase * 0.6) * 0.25;
                targetHappy = 0;
                targetSleepZ = 0;
                if (blinkProgress < 0 && Math.Abs(lookX - targetLookX) < 0.05 && blinkCooldown > 1.2)
                    targetLookY = 0;
            }

            if (mood == 0 || mood == 1 || mood == 3)
            {
                blinkCooldown -= dt;
                if (blinkProgress < 0 && blinkCooldown <= 0)
                    blinkProgress = 0;
                if (blinkProgress >= 0)
                {
                    double blinkDur = mood == 1 ? 0.32 : 0.15;
                    blinkProgress += dt / blinkDur;
                    if (blinkProgress >= 1.0)
                    {
                        blinkProgress = -1.0;
                        blinkCooldown = mood == 1 ? (1.4 + NextRand() * 1.2) : (1.8 + NextRand() * 2.5);
                        if (mood == 0)
                            targetLookX = (NextRand() - 0.5) * 0.9;
                    }
                }
            }

            double k = Math.Min(1.0, dt * 14.0);
            eyeOpen += (targetEyeOpen - eyeOpen) * k;
            lookX += (targetLookX - lookX) * (k * 0.6);
            lookY += (targetLookY - lookY) * (k * 0.6);
            bounceY += (targetBounceY - bounceY) * k;
            happyAmount += (targetHappy - happyAmount) * (k * 0.8);
            sleepZ += (targetSleepZ - sleepZ) * (k * 0.6);

            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext d)
        {
            base.OnRender(d);
            double w = ActualWidth > 0 ? ActualWidth : Width;
            double h = ActualHeight > 0 ? ActualHeight : Height;
            double cy = h * 0.5 + bounceY + lookY * 0.8;
            double cx = w * 0.5 + lookX * 1.4;

            double blinkFactor = 1.0;
            if (blinkProgress >= 0)
            {
                double b = Math.Sin(blinkProgress * Math.PI);
                blinkFactor = Math.Max(0.08, 1.0 - b * 0.92);
            }

            double effectiveOpen = eyeOpen * blinkFactor;
            double eyeGap = 7.4;

            // Anime rosy blush cheeks (soft when awake, brighter when excited/happy)
            double cheekAlpha = (happyAmount > 0.2 || mood == 3) ? 0.92 : (mood == 0 ? 0.42 : 0.22);
            d.PushOpacity(cheekAlpha);
            d.DrawEllipse(CheekBrush, null, new Point(cx - 7.1, cy + 3.0), 1.65, 0.95);
            d.DrawEllipse(CheekBrush, null, new Point(cx + 7.1, cy + 3.0), 1.65, 0.95);
            d.Pop();

            for (int side = -1; side <= 1; side += 2)
            {
                double ex = cx + side * (eyeGap * 0.5);
                if (happyAmount > 0.35 && mood == 3)
                {
                    var arch = new StreamGeometry();
                    using (var c = arch.Open())
                    {
                        c.BeginFigure(new Point(ex - 2.1, cy + 0.6), false, false);
                        c.QuadraticBezierTo(new Point(ex, cy - 2.2), new Point(ex + 2.1, cy + 0.6), true, false);
                    }
                    arch.Freeze();
                    d.DrawGeometry(null, cachedHappyPen, arch);
                }
                else
                {
                    double eh = Math.Max(1.3, 4.4 * effectiveOpen);
                    double ew = 3.2;
                    d.DrawRoundedRectangle(EyeBrush, null, new Rect(ex - ew * 0.5, cy - eh * 0.5, ew, eh), 1.5, Math.Min(1.6, eh * 0.5));
                }
            }

            if (mood == 3 && happyAmount > 0.25)
            {
                double pulse = 0.72 + 0.28 * Math.Sin(moodElapsed * 7.0);
                double sx = cx + 8.6, sy = cy - 4.4;
                var sparkle = new StreamGeometry();
                using (var c = sparkle.Open())
                {
                    c.BeginFigure(new Point(sx, sy - 2.2), true, true);
                    c.LineTo(new Point(sx + 0.65, sy - 0.65), true, false);
                    c.LineTo(new Point(sx + 2.2, sy), true, false);
                    c.LineTo(new Point(sx + 0.65, sy + 0.65), true, false);
                    c.LineTo(new Point(sx, sy + 2.2), true, false);
                    c.LineTo(new Point(sx - 0.65, sy + 0.65), true, false);
                    c.LineTo(new Point(sx - 2.2, sy), true, false);
                    c.LineTo(new Point(sx - 0.65, sy - 0.65), true, false);
                }
                sparkle.Freeze();
                d.PushOpacity(Math.Min(1.0, happyAmount) * pulse);
                d.DrawGeometry(StarBrush, null, sparkle);
                d.Pop();
            }

            if (mood == 2 && sleepZ > 0.25)
            {
                double floatOff = (sleepPhase * 0.6) % 1.0;
                var ft = new FormattedText("z", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, SegoeUiFace, 7.5, ZBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                d.PushOpacity(Math.Max(0, 1.0 - floatOff) * sleepZ);
                d.DrawText(ft, new Point(cx + 5.5, cy - 6.5 - floatOff * 3.5));
                d.Pop();
            }
        }
    }
}
