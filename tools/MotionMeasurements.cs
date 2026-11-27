using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskPill;
using Forms = System.Windows.Forms;

class MotionMeasurements
{
    static string themeName="Ghibli Meadow";
    static readonly BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
    static object Field(object value, string name)
    {
        return value.GetType().GetField(name, F).GetValue(value);
    }

    static void Call(object value, string name, params object[] args)
    {
        value.GetType().GetMethods(F).Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(value, args);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct Mode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string device;
        public ushort spec, driver, size, extra;
        public uint fields;
        public int x, y;
        public uint orientation, fixedOutput;
        public short color, duplex, yResolution, option, collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string form;
        public ushort logPixels;
        public uint bits, width, height, flags, frequency, method, intent, media, dither, reserved1, reserved2, panWidth, panHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool EnumDisplaySettings(string device, int number, ref Mode mode);
    static double P(List<double> values, double percentile)
    {
        var ordered = values.OrderBy(x => x).ToArray();
        return ordered.Length == 0 ? 0 : Math.Round(ordered[Math.Min(ordered.Length - 1, (int)Math.Ceiling(percentile * ordered.Length) - 1)], 3);
    }

    static async Task<object> Measure(Forms.Screen screen, string directory)
    {
        var state = new AppState();
        state.Settings.Theme = themeName;
        state.Settings.Monitor = screen.DeviceName;
        state.Settings.MirrorNotifications = false;
        state.Settings.ClipboardEnabled = false;
        state.Settings.Sound = false;
        var w = new IslandWindow(state, new StateStore(directory), true, false);
        var isolated = w.GetType().GetProperty("RuntimeProbeMode", F);
        if (isolated != null)
            isolated.SetValue(w, true, null);
        w.GetType().GetField("polling", F).SetValue(w, true);
        w.GetType().GetProperty("SuppressCursorWatch", F).SetValue(w, true, null);
        w.Show();
        foreach (string timer in new[]
        {
            "heartbeat",
            "notificationTimer",
            "hoverTimer",
            "closeTimer",
            "tabWatchTimer"
        }

        )
            ((System.Windows.Threading.DispatcherTimer)Field(w, timer)).Stop();
        await Task.Delay(600);
        var dpi = VisualTreeHelper.GetDpi(w);
        var mode = new Mode
        {
            size = (ushort)Marshal.SizeOf(typeof(Mode))
        };
        bool display = EnumDisplaySettings(screen.DeviceName, -1, ref mode);
        double hz = display && mode.frequency > 1 ? mode.frequency : 60;
        var intervals = new List<double>();
        var renderIntervals = new List<double>();
        long last = 0;
        TimeSpan lastRender = TimeSpan.MinValue;
        var elapsed = Stopwatch.StartNew();
        EventHandler handler = delegate (object sender, EventArgs args)
        {
            var at = ((RenderingEventArgs)args).RenderingTime;
            if (at == lastRender)
                return;
            long now = Stopwatch.GetTimestamp();
            if (last != 0)
            {
                intervals.Add((now - last) * 1000.0 / Stopwatch.Frequency);
                renderIntervals.Add((at - lastRender).TotalMilliseconds);
            }

            last = now;
            lastRender = at;
        };
        CompositionTarget.Rendering += handler;
        try
        {
            var poses = new[]
            {
                NotchPose.Compact,
                NotchPose.Expanded,
                NotchPose.OptionsDock,
                NotchPose.Expanded,
                NotchPose.Compact,
                NotchPose.Tiny,
                NotchPose.Expanded,
                NotchPose.Compact
            };
            for (int i = 0; i < 24; i++)
            {
                Call(w, "SetPose", poses[i % poses.Length]);
                if (i % 4 == 1)
                    Call(w, "ChangePage", 1);
                await Task.Delay(320);
            }
        }
        finally
        {
            CompositionTarget.Rendering -= handler;
            elapsed.Stop();
        }

        Call(w, "SetPose", NotchPose.Expanded);
        Call(w, "SnapPoseImmediate");
        w.UpdateLayout();
        var root = (FrameworkElement)Field(w, "root");
        var shell = (System.Windows.Shapes.Path)Field(w, "shell");
        var software = new List<object>();
        foreach (int targetDpi in new[]
        {
            96,
            120,
            144,
            192
        }

        )
        {
            double scale = targetDpi / 96.0;
            var b = new RenderTargetBitmap((int)Math.Ceiling(w.ActualWidth * scale), (int)Math.Ceiling(w.ActualHeight * scale), targetDpi, targetDpi, PixelFormats.Pbgra32);
            b.Render(root);
            var bytes = new byte[b.PixelWidth * b.PixelHeight * 4];
            b.CopyPixels(bytes, b.PixelWidth * 4, 0);
            int left = b.PixelWidth, right = -1, top = b.PixelHeight, bottom = -1;
            for (int y = 0; y < b.PixelHeight; y++)
                for (int x = 0; x < b.PixelWidth; x++)
                    if (bytes[(y * b.PixelWidth + x) * 4 + 3] > 24)
                    {
                        left = Math.Min(left, x);
                        right = Math.Max(right, x);
                        top = Math.Min(top, y);
                        bottom = Math.Max(bottom, y);
                    }

            bool matches = right >= left && Math.Abs((right - left + 1) - shell.Data.Bounds.Width * scale) <= 4 && Math.Abs((bottom - top + 1) - shell.Data.Bounds.Height * scale) <= 4;
            software.Add(new { dpi = targetDpi, alphaBoundsMatch = matches, width = right - left + 1, height = bottom - top + 1 });
            if (!matches)
                throw new Exception("Software DPI render bounds failed at " + targetDpi);
        }

        Call(w, "Cleanup");
        w.Close();
        double budget = 1000 / hz;
        return new
        {
            device = screen.DeviceName,
            pixelWidth = screen.Bounds.Width,
            pixelHeight = screen.Bounds.Height,
            dpiX = dpi.PixelsPerInchX,
            dpiY = dpi.PixelsPerInchY,
            refreshHz = hz,
            refreshDetected = display && mode.frequency > 1,
            seconds = Math.Round(elapsed.Elapsed.TotalSeconds, 3),
            samples = intervals.Count,
            medianMs = P(intervals, .5),
            p95Ms = P(intervals, .95),
            p99Ms = P(intervals, .99),
            maxMs = intervals.Count == 0 ? 0 : Math.Round(intervals.Max(), 3),
            renderTimestampP95Ms = P(renderIntervals, .95),
            overBudgetIntervals = intervals.Count(t => t > budget * 1.5),
            estimatedSkippedIntervals = intervals.Sum(t => Math.Max(0, (int)Math.Round(t / budget) - 1)),
            softwareDpiRenders = software
        };
    }

    [STAThread]
    static int Main(string[] args)
    {
        string output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath("artifacts/motion-measurement.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        int code = 0;
        if(args.Length>1)themeName=Settings.NormalizeTheme(args[1]);
        var app = new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };
        Ui.Init(app);
        app.Startup += async delegate
        {
            try
            {
                var displays = new List<object>();
                foreach (var screen in Forms.Screen.AllScreens)
                    displays.Add(await Measure(screen, Path.Combine(Path.GetDirectoryName(output), "probe-state")));
                var report = new
                {
                    measuredAtUtc = DateTime.UtcNow.ToString("o"),
                    theme = themeName,
                    metric = "WPF CompositionTarget.Rendering callback intervals, isolated transitions",
                    renderTier = RenderCapability.Tier >> 16,
                    windowsAnimations = SystemParameters.ClientAreaAnimation,
                    hardwareDisplayCount = displays.Count,
                    mixedDpiHardwareVerified = false,
                    limitations = "Software DPI render bounds do not verify cross-monitor hardware transitions. Rendering callbacks are not DWM presentation timestamps. No private app state or desktop captures are collected.",
                    displays = displays
                };
                File.WriteAllText(output, new JavaScriptSerializer().Serialize(report));
                Console.WriteLine("Saved " + output);
            }
            catch (Exception error)
            {
                code = 1;
                Console.WriteLine("Measurement failed: " + error.Message);
            }
            finally
            {
                app.Shutdown();
            }
        };
        app.Run();
        return code;
    }
}
