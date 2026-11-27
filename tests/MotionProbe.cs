using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DeskPill;

class MotionProbe
{
    static int checks;
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static object Field(object instance, string name)
    {
        return instance.GetType().GetField(name, Private).GetValue(instance);
    }

    static void SetField(object instance, string name, object value)
    {
        instance.GetType().GetField(name, Private).SetValue(instance, value);
    }

    static void Call(object instance, string name, params object[] args)
    {
        instance.GetType().GetMethods(Private).Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(instance, args);
    }

    static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
        checks++;
        Console.WriteLine("PASS " + message);
    }

    static double Height(IslandWindow w)
    {
        return ((System.Windows.Shapes.Path)Field(w, "shell")).Data.Bounds.Height;
    }

    static double ShapeWidth(IslandWindow w)
    {
        return ((System.Windows.Shapes.Path)Field(w, "shell")).Data.Bounds.Width;
    }

    static void Pump(int ms)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(ms)
        };
        timer.Tick += delegate
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    static void Escape(IslandWindow w)
    {
        w.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(w), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    }

    static void Hover(IslandWindow w)
    {
        DateTime now = DateTime.UtcNow;
        Call(w, "ProcessPointer", 0.0, 260.0, now);
        Call(w, "ProcessPointer", 270.0, 12.0, now.AddMilliseconds(20));
        Call(w, "ProcessPointer", 270.0, 12.0, now.AddMilliseconds(160));
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

    static void Next(IslandWindow w)
    {
        Controls<Button>((Canvas)Field(w, "pagerHost")).Single(b => Convert.ToString(b.ToolTip) == "Next page").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    static void Frame(IslandWindow w, string folder, string name)
    {
        if (folder == null)
            return;
        w.UpdateLayout();
        var b = new RenderTargetBitmap((int)Math.Ceiling(w.ActualWidth), (int)Math.Ceiling(w.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        // Composite the actual WPF visual onto a neutral QA background so the outline is visible.
        var visual = new DrawingVisual();
        using (var c = visual.RenderOpen())
        {
            var bounds = new Rect(0, 0, w.ActualWidth, w.ActualHeight);
            c.DrawRectangle(new SolidColorBrush(Color.FromRgb(43, 48, 57)), null, bounds);
        }

        b.Render(visual);
        b.Render((Grid)w.Content);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(b));
        using (var file = File.Create(Path.Combine(folder, name + ".png")))
            png.Save(file);
        Console.WriteLine("FRAME " + name + " shape=" + ShapeWidth(w).ToString("0.0") + "x" + Height(w).ToString("0.0") + " window=" + w.Width + "x" + w.Height);
    }

    static void VerifySpringPhysics()
    {
        var type = typeof(IslandWindow).Assembly.GetType("DeskPill.IslandMotion");
        var ctor = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single();
        var shell = new System.Windows.Shapes.Path();
        var rim = new System.Windows.Shapes.Path();
        var host = new Canvas();
        object motion = ctor.Invoke(new object[] { shell, rim, host, null });
        var snap = type.GetMethod("Snap", BindingFlags.Instance | BindingFlags.Public);
        var moveTo = type.GetMethod("MoveTo", BindingFlags.Instance | BindingFlags.Public);
        var step = type.GetMethod("StepPhysics", BindingFlags.Instance | BindingFlags.NonPublic);
        var stop = type.GetMethod("Stop", BindingFlags.Instance | BindingFlags.Public);
        var curH = type.GetProperty("CurrentHeight", BindingFlags.Instance | BindingFlags.Public);
        var dockReveal = type.GetProperty("DockReveal", BindingFlags.Instance | BindingFlags.Public);
        var expReveal = type.GetProperty("ExpandedReveal", BindingFlags.Instance | BindingFlags.Public);

        snap.Invoke(motion, new object[] { 172.0, 30.0, false });
        moveTo.Invoke(motion, new object[] { 396.0, 172.0, false, true, false, false, null });
        stop.Invoke(motion, null);
        double maxOpen = 0;
        for (int i = 0; i < 65; i++)
        {
            step.Invoke(motion, new object[] { 0.01 });
            double h = (double)curH.GetValue(motion, null);
            if (h > maxOpen)
                maxOpen = h;
        }

        double settledOpen = (double)curH.GetValue(motion, null);
        double revealOpen = (double)expReveal.GetValue(motion, null);
        Check(maxOpen > 172.5 && maxOpen < 186.0, "Opening spring exhibits natural overshoot (" + maxOpen.ToString("0.0") + "px)");
        Check(Math.Abs(settledOpen - 172.0) < 0.3 && revealOpen > 0.999, "Opening spring settles cleanly to 172px with full reveal (" + settledOpen.ToString("0.00") + "px)");

        // Verify hover re-expand spring (172 -> 206 + OptionsDock) shows organic elastic bloom and settles.
        snap.Invoke(motion, new object[] { 396.0, 172.0, false });
        moveTo.Invoke(motion, new object[] { 424.0, 206.0, true, false, false, false, null });
        stop.Invoke(motion, null);
        double maxDock = 0;
        for (int i = 0; i < 80; i++)
        {
            step.Invoke(motion, new object[] { 0.01 });
            double h = (double)curH.GetValue(motion, null);
            if (h > maxDock)
                maxDock = h;
        }

        double settledDock = (double)curH.GetValue(motion, null);
        double finalReveal = (double)dockReveal.GetValue(motion, null);
        Check(maxDock > 206.5 && maxDock < 216.0, "Options dock re-expand spring exhibits plush bloom (" + maxDock.ToString("0.0") + "px)");
        Check(Math.Abs(settledDock - 206.0) < 0.3 && finalReveal > 0.99, "Options dock settles cleanly at 206px with full dock reveal");
    }

    [STAThread]
    static int Main(string[] args)
    {
        string folder = args.Length > 0 ? Path.GetFullPath(args[0]) : null;
        if (folder != null)
            Directory.CreateDirectory(folder);
        string temporary = Path.Combine(Path.GetTempPath(), "DeskPill-MotionFixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var app = new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };
        Ui.Init(app);
        try
        {
            var type = typeof(IslandWindow).Assembly.GetType("DeskPill.IslandMotion");
            var outline = type.GetMethod("Outline", BindingFlags.Static | BindingFlags.NonPublic);
            foreach (bool floating in new[] { false, true })
            {
                double last = 0;
                double expectedTop = floating ? 10.0 : 0.0;
                for (int i = 0; i <= 20; i++)
                {
                    Geometry g = (Geometry)outline.Invoke(null, new object[] { i / 20.0, floating });
                    Rect bounds = g.Bounds;
                    Check(bounds.Left >= 0 && bounds.Right <= 540 && Math.Abs(bounds.Top - expectedTop) < 0.001 && bounds.Height >= last && bounds.Height <= 172.01, (floating ? "Floating" : "Classic") + " outline stays centered and bounded at " + i * 5 + "%");
                    Check(Math.Abs(bounds.Left + bounds.Right - 540) < 0.001, "Outline symmetry");
                    last = bounds.Height;
                }
            }

            var outlineSize = type.GetMethod("OutlineSize", BindingFlags.Static | BindingFlags.NonPublic);
            foreach (bool floating in new[] { false, true })
            {
                Geometry tinyGeo = (Geometry)outlineSize.Invoke(null, new object[] { 52.0, 6.0, floating });
                Rect tb = tinyGeo.Bounds;
                Check(tb.Width >= 50.0 && tb.Height >= 5.5 && tb.Height <= 6.5 && Math.Abs(tb.Left + tb.Right - 540.0) < 0.001, (floating ? "Floating" : "Classic") + " Tiny outline centered & symmetric");
                Geometry hudGeo = (Geometry)outlineSize.Invoke(null, new object[] { 296.0, 38.0, floating });
                Rect hb = hudGeo.Bounds;
                Check(hb.Width >= 294.0 && hb.Height >= 37.5 && hb.Height <= 38.5 && Math.Abs(hb.Left + hb.Right - 540.0) < 0.001, (floating ? "Floating" : "Classic") + " HudPill outline centered & symmetric");
            }

            VerifySpringPhysics();

            var state = new AppState();
            state.Settings.ClipboardEnabled = false;
            state.Settings.MirrorNotifications = false;
            state.Settings.Sound = false;
            state.Settings.HideFullscreen = false;
            state.Settings.ShowClock = false;
            state.Settings.FastAnimations = false;
            var w = new IslandWindow(state, new StateStore(temporary), true, false);
            w.GetType().GetProperty("RuntimeProbeMode", Private).SetValue(w,true,null);
            SetField(w, "polling", true);
            w.GetType().GetProperty("SuppressCursorWatch", Private).SetValue(w, true, null);
            Call(w, "ChangePage", 2);
            Check(!w.IsVisible && Field(w, "outgoingPageVisual") == null, "Unshown page navigation remains immediate");
            w.Show();
            Pump(150);
            ((DispatcherTimer)Field(w, "heartbeat")).Stop();
            ((DispatcherTimer)Field(w, "closeTimer")).Stop();
            ((DispatcherTimer)Field(w, "hoverTimer")).Stop();

            var expandedLayer = (Canvas)Field(w, "expandedLayer");
            var compactLayer = (Canvas)Field(w, "compactLayer");
            Check(expandedLayer.Opacity >= 0.999 && compactLayer.Opacity == 0.0, "Expanded content starts at full 100% opacity");
            Frame(w, folder, "expanded");
            bool systemMotion = SystemParameters.ClientAreaAnimation;
            Check(w.Width == 540 && w.Height == 276, "Host window stays fixed-size (540x276) with zero resize jank");

            Escape(w);
            if (systemMotion)
            {
                Check(w.Height == 276, "Closing retains fixed window size throughout spring morph");
                Pump(80);
                Frame(w, folder, "close-80ms");
                Check(Height(w) > 30 && Height(w) < 172, "Closing produces a real intermediate outline (" + Height(w).ToString("0.0") + "px)");
                Check(object.ReferenceEquals(((Canvas)Field(w, "contentHost")).Clip, ((System.Windows.Shapes.Path)Field(w, "shell")).Data), "Content clip shares the moving outline");
                Pump(500);
            }

            Check(Math.Abs(Height(w) - 30) < 0.3 && w.Height == 276 && compactLayer.Opacity == 1.0 && expandedLayer.Opacity == 0.0, "Collapsed final geometry (30px), full compact opacity, zero expanded opacity");
            Frame(w, folder, "compact");

            Hover(w);
            if (systemMotion)
            {
                Pump(80);
                Frame(w, folder, "open-80ms");
                Check(Height(w) > 30 && Height(w) < 185 && w.Height == 276 && (expandedLayer.Opacity > 0.05 && expandedLayer.Opacity < 0.95), "Opening fades content progressively with the shape (" + ShapeWidth(w).ToString("0.0") + "px, opacity=" + expandedLayer.Opacity.ToString("0.00") + ")");
                Escape(w);
                Pump(50);
                Hover(w);
                Pump(650);
            }

            Check(Math.Abs(Height(w) - 172) < 0.3 && expandedLayer.Opacity >= 0.999 && object.ReferenceEquals(expandedLayer, Field(w, "expandedLayer")), "Rapid open-close-reopen preserves permanent layer at 100% opacity and 172px");

            // Hover-on-dots graceful OptionsDock re-expansion test
            Call(w, "SetPose", NotchPose.OptionsDock);
            if (systemMotion)
            {
                Pump(90);
                Frame(w, folder, "dock-90ms");
                Pump(650);
            }

            var dockTray = (Border)Field(w, "dockTray");
            var dotsStrip = (FrameworkElement)Field(w, "dotsStrip");
            Check(Math.Abs(Height(w) - 202) < 0.4 && dockTray.Opacity > 0.95 && dockTray.IsHitTestVisible && dotsStrip.Opacity < 0.1 && expandedLayer.Opacity >= 0.999, "Options dock gracefully re-expands to 202px with full interactive dock");
            Frame(w, folder, "options-dock");

            // Switch page while inside OptionsDock and verify dock stays open smoothly
            Next(w);
            if (systemMotion)
                Pump(450);
            Check(Math.Abs(Height(w) - 202) < 0.5 && Field(w, "outgoingPageVisual") == null && expandedLayer.Opacity >= 0.999, "Switching page inside OptionsDock preserves dock cleanly");

            // Relax back from OptionsDock to Expanded
            Call(w, "SetPose", NotchPose.Expanded);
            if (systemMotion)
                Pump(600);
            Check(Math.Abs(Height(w) - 172) < 0.4 && expandedLayer.Opacity >= 0.999, "Options dock relaxes cleanly back to 172px");

            // Inline Ask & Inline Today contextual re-expansion checks
            Call(w, "ShowInlineAsk");
            if (systemMotion)
                Pump(650);
            Check(Math.Abs(Height(w) - 228) < 0.5 && Math.Abs(ShapeWidth(w) - 460) < 0.5 && expandedLayer.Opacity >= 0.999, "InlineAsk re-expands smoothly to 460x228 (" + ShapeWidth(w).ToString("0.0") + "x" + Height(w).ToString("0.0") + ")");
            Frame(w, folder, "inline-ask");

            Call(w, "ShowInlineToday");
            if (systemMotion)
                Pump(650);
            Check(Math.Abs(Height(w) - 238) < 0.5 && Math.Abs(ShapeWidth(w) - 484) < 0.5 && expandedLayer.Opacity >= 0.999, "InlineToday re-expands smoothly to 484x238 (" + ShapeWidth(w).ToString("0.0") + "x" + Height(w).ToString("0.0") + ")");

            Escape(w);
            if (systemMotion)
                Pump(600);
            Check(Math.Abs(Height(w) - 172) < 0.5 && expandedLayer.Opacity >= 0.999, "Escape from InlineToday returns smoothly to Expanded 172px");

            // Browser tab-overlap shrink to Tiny (52x6) and hover bloom back
            Escape(w);
            if (systemMotion)
                Pump(500);
            Call(w, "SetTabOverlapForProbe", true);
            if (systemMotion)
                Pump(650);
            var tinyLayer = (Canvas)Field(w, "tinyLayer");
            var hudLayer = (Canvas)Field(w, "hudLayer");
            Check(Math.Abs(Height(w) - 6.0) < 0.4 && Math.Abs(ShapeWidth(w) - 52.0) < 0.6 && compactLayer.Opacity < 0.05 && tinyLayer.Opacity > 0.95, "Tab overlap shrinks cleanly to Tiny notch (52x6)");
            Frame(w, folder, "tiny");

            Hover(w);
            if (systemMotion)
                Pump(650);
            Check(Math.Abs(Height(w) - 172.0) < 0.4 && expandedLayer.Opacity >= 0.999 && tinyLayer.Opacity < 0.05, "Hovering Tiny notch blooms cleanly to Expanded 172px (pose=" + Field(w, "pose") + ", open=" + Field(w, "open") + ", height=" + Height(w) + ", opacity=" + expandedLayer.Opacity + ")");

            Call(w, "SetTabOverlapForProbe", false);
            Escape(w);
            if (systemMotion)
                Pump(550);

            // Sleek HUD Pill transition + smooth rolling percentage check (Compact -> 296x38 HudPill)
            var hudValueBlock = (System.Windows.Controls.TextBlock)Field(w, "hudValue");
            var hudProgress = (System.Windows.Controls.ProgressBar)Field(w, "hudBar");
            Call(w, "ShowRollingHud", "\uE767", "Volume", 0.72, 0.20, "%", 2000);
            if (systemMotion)
            {
                Pump(70);
                double midMeter = (double)hudProgress.Value;
                Check(midMeter > 0.22 && midMeter < 0.70, "ShowRollingHud transitions smoothly through intermediate percentages without jumping (" + hudValueBlock.Text + ", bar=" + midMeter.ToString("0.00") + ")");
                Pump(450);
                Check(Math.Abs(hudProgress.Value - 0.72) < 0.006 && hudValueBlock.Text == "72%", "ShowRollingHud settles crisply at 72% (" + hudValueBlock.Text + ", bar=" + hudProgress.Value.ToString("0.000") + ")");
            }
            Check(Math.Abs(Height(w) - 38.0) < 0.4 && Math.Abs(ShapeWidth(w) - 296.0) < 0.6 && hudLayer.Opacity > 0.95, "ShowRollingHud springs cleanly to 296x38 HUD pill");
            Frame(w, folder, "hud-pill");
            Call(w, "SetOpen", true);
            if (systemMotion)
                Pump(550);

            // Spring carousel page switch checks
            Next(w);
            if (systemMotion)
            {
                var outPage = (Canvas)Field(w, "outgoingPageVisual");
                Check(outPage != null && !outPage.IsHitTestVisible, "Outgoing page cannot receive clicks during carousel slide");
                Pump(50);
                Frame(w, folder, "page-50ms");
                Pump(40);
                Frame(w, folder, "page-90ms");
                Next(w);
                Pump(550);
                Check(Field(w, "outgoingPageVisual") == null && ((Canvas)Field(w, "currentPageVisual")).Opacity == 1.0, "Interrupted page carousel cleans old page layer and settles cleanly");
            }

            state.Settings.FastAnimations = true;
            Escape(w);
            Check(Math.Abs(Height(w) - 30) < 0.1 && compactLayer.Opacity == 1.0 && expandedLayer.Opacity == 0.0 && Field(w, "outgoingPageVisual") == null, "Fast animations collapse immediately with full compact visibility");
            Hover(w);
            Check(Math.Abs(Height(w) - 172) < 0.1 && expandedLayer.Opacity >= 0.999 && compactLayer.Opacity == 0.0 && Field(w, "outgoingPageVisual") == null, "Fast animations expand immediately at 100% opacity");
            Call(w, "SetPose", NotchPose.OptionsDock);
            Check(Math.Abs(Height(w) - 202) < 0.1 && dockTray.Opacity == 1.0 && expandedLayer.Opacity >= 0.999, "Fast animations re-expand options dock immediately");

            state.Settings.FastAnimations = false;
            Escape(w);
            Pump(40);
            w.Hide();
            Check(Field(w, "outgoingPageVisual") == null && Math.Abs(Height(w) - 30) < 0.2 && compactLayer.Opacity == 1.0 && expandedLayer.Opacity == 0.0, "Hiding cancels active spring loop into clean settled state");
            Call(w, "Cleanup");
            w.Close();
            Console.WriteLine("Motion probe: " + checks + " checks passed; system motion=" + systemMotion);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            return 1;
        }
        finally
        {
            app.Shutdown();
            string resolved = Path.GetFullPath(temporary);
            string tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(Directory.GetParent(resolved).FullName, tempRoot, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("DeskPill-MotionFixture-", StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing cleanup outside the generated temporary fixture.");
            Directory.Delete(resolved, true);
        }
    }
}
