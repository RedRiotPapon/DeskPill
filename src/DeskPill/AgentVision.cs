using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rectangle = System.Drawing.Rectangle;
using Size = System.Drawing.Size;

namespace DeskPill
{
    // Identity only. Remembering an external target never captures its pixels.
    public sealed class AgentScreenTarget
    {
        public IntPtr WindowHandle { get; private set; }
        public int ProcessId { get; private set; }
        public string Title { get; private set; }
        public Rectangle WindowBounds { get; private set; }
        public Rectangle MonitorBounds { get; private set; }
        readonly long started;
        AgentScreenTarget(IntPtr handle, int pid, long start, string title, Rectangle window, Rectangle monitor)
        { WindowHandle = handle; ProcessId = pid; started = start; Title = title; WindowBounds = window; MonitorBounds = monitor; }

        public static AgentScreenTarget CaptureForeground()
        {
            using (new AgentPhysicalCoordinates())
            {
                IntPtr handle = AgentVisionNative.GetForegroundWindow();
                uint pid;
                AgentVisionNative.GetWindowThreadProcessId(handle, out pid);
                if (pid == 0 || pid == Process.GetCurrentProcess().Id) return null;
                var title = new StringBuilder(1024);
                AgentVisionNative.GetWindowText(handle, title, title.Capacity);
                AgentVisionNative.Rect rect;
                var monitor = new AgentVisionNative.MonitorInfo();
                monitor.Size = Marshal.SizeOf(typeof(AgentVisionNative.MonitorInfo));
                if (!AgentVisionNative.IsWindowVisible(handle) || AgentVisionNative.IsIconic(handle) ||
                    !AgentVisionNative.GetWindowRect(handle, out rect) ||
                    !AgentVisionNative.GetMonitorInfo(AgentVisionNative.MonitorFromWindow(handle, 2), ref monitor)) return null;
                Rectangle bounds = rect.ToRectangle();
                if (!IsUsableIdentity(handle, (int)pid, title.ToString(), bounds)) return null;
                try
                {
                    using (Process process = Process.GetProcessById((int)pid))
                        return new AgentScreenTarget(handle, (int)pid, process.StartTime.ToUniversalTime().Ticks,
                            title.ToString(), bounds, monitor.Monitor.ToRectangle());
                }
                catch (Exception) { return null; }
            }
        }

        internal static bool IsUsableIdentity(IntPtr handle, int pid, string title, Rectangle bounds)
        { return handle != IntPtr.Zero && pid > 0 && !string.IsNullOrWhiteSpace(title) && bounds.Width > 2 && bounds.Height > 2; }

        public bool IsValid { get { Rectangle ignored; return TryValidate(out ignored); } }
        internal bool TryValidate(out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            using (new AgentPhysicalCoordinates())
            {
                uint pid;
                AgentVisionNative.GetWindowThreadProcessId(WindowHandle, out pid);
                if (pid != ProcessId || !AgentVisionNative.IsWindow(WindowHandle) ||
                    !AgentVisionNative.IsWindowVisible(WindowHandle) || AgentVisionNative.IsIconic(WindowHandle)) return false;
                var title = new StringBuilder(1024);
                AgentVisionNative.GetWindowText(WindowHandle, title, title.Capacity);
                string liveTitle = title.ToString();
                AgentVisionNative.Rect rect;
                if (!AgentVisionNative.GetWindowRect(WindowHandle, out rect)) return false;
                bounds = rect.ToRectangle();
                if (!IsUsableIdentity(WindowHandle, ProcessId, liveTitle, bounds)) return false;
                try
                {
                    using (var p = Process.GetProcessById(ProcessId))
                    {
                        if (p.StartTime.ToUniversalTime().Ticks != started) return false;
                    }
                    Title = liveTitle;
                    WindowBounds = bounds;
                    return true;
                }
                catch (Exception) { return false; }
            }
        }
    }

    public sealed class AgentAttachmentCopy : IDisposable
    {
        public string FilePath { get; private set; }
        internal AgentAttachmentCopy(string path) { FilePath = path; }
        public void Dispose() { AgentAttachment.Delete(FilePath); }
    }

    public sealed class AgentAttachment : IDisposable
    {
        readonly object gate = new object();
        bool disposed;
        public BitmapSource Preview { get; private set; }
        public string SourceLabel { get; private set; }
        public DateTime CapturedUtc { get; private set; }
        public string FilePath { get; private set; }
        internal AgentAttachment(BitmapSource preview, string path, string source)
        { Preview = preview; FilePath = path; SourceLabel = source; CapturedUtc = DateTime.UtcNow; }
        public AgentAttachmentCopy CopyForRun()
        {
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException("AgentAttachment");
                string path = AgentVision.NewPath();
                try { File.Copy(FilePath, path, false); return new AgentAttachmentCopy(path); }
                catch { Delete(path); throw; }
            }
        }
        public void Dispose() { lock (gate) { disposed = true; Delete(FilePath); } }
        internal static void Delete(string path)
        { if (path != null) { try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } } }
    }

    public static class AgentVision
    {
        public const int MaximumEdge = 2048;
        static readonly string captureRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskPill", "agent-captures");
        internal static string NewPath()
        {
            // Restrict this folder to the current account rather than inheriting broad permissions.
            var security = new DirectorySecurity();
            SecurityIdentifier account = WindowsIdentity.GetCurrent().User;
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(account, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            Directory.CreateDirectory(captureRoot, security);
            Directory.SetAccessControl(captureRoot, security);
            return Path.Combine(captureRoot, Guid.NewGuid().ToString("N") + ".png");
        }
        public static Task<AgentAttachment> CaptureWindowAsync(AgentScreenTarget target)
        {
            return Task.Run(delegate
            {
                Rectangle bounds;
                if (target == null || !target.TryValidate(out bounds)) throw new InvalidOperationException("The remembered window changed. Return to it and try again.");
                uint affinity;
                if (AgentVisionNative.GetWindowDisplayAffinity(target.WindowHandle, out affinity) && affinity != 0)
                    throw new InvalidOperationException("This window protects its screen content.");
                using (new AgentPhysicalCoordinates())
                {
                    Rectangle visible = Rectangle.Intersect(bounds, AgentVisionNative.VirtualBounds());
                    if (visible.Width < 3 || visible.Height < 3) throw new InvalidOperationException("The window is outside the visible desktop.");
                    var result = Capture(visible, "Window: " + target.Title + " · visible pixels (may include overlapping windows)");
                    if (!target.TryValidate(out bounds)) { result.Dispose(); throw new InvalidOperationException("The window changed during capture. Try again."); }
                    return result;
                }
            });
        }
        public static Task<AgentAttachment> CaptureMonitorAsync(AgentScreenTarget target)
        {
            return Task.Run(delegate
            {
                Rectangle ignored;
                if (target == null || !target.TryValidate(out ignored)) throw new InvalidOperationException("Select an external window on the monitor first.");
                return CaptureMonitorFromHandle(target.WindowHandle, true);
            });
        }
        public static Task<AgentAttachment> CaptureMonitorByHandleAsync(IntPtr referenceWindow)
        {
            return Task.Run(delegate
            {
                return CaptureMonitorFromHandle(referenceWindow, false);
            });
        }
        static AgentAttachment CaptureMonitorFromHandle(IntPtr referenceWindow, bool requireWindowMatch)
        {
            using (new AgentPhysicalCoordinates())
            {
                IntPtr monitorHandle;
                if (referenceWindow != IntPtr.Zero)
                    monitorHandle = AgentVisionNative.MonitorFromWindow(referenceWindow, 2);
                else
                {
                    AgentVisionNative.NativePoint cursor;
                    AgentVisionNative.GetCursorPos(out cursor);
                    monitorHandle = AgentVisionNative.MonitorFromPoint(cursor, 2);
                }
                var monitor = new AgentVisionNative.MonitorInfo();
                monitor.Size = Marshal.SizeOf(typeof(AgentVisionNative.MonitorInfo));
                if (!AgentVisionNative.GetMonitorInfo(monitorHandle, ref monitor))
                    throw new InvalidOperationException(requireWindowMatch ? "The monitor arrangement changed. Select the window again." : "Current monitor is unavailable.");
                Rectangle monBounds = monitor.Monitor.ToRectangle();
                return Capture(monBounds, "Monitor · visible desktop pixels " + monBounds.Width + " × " + monBounds.Height);
            }
        }
        public static async Task<AgentAttachment> CaptureAreaAsync(Window owner)
        {
            var picker = new AgentCapturePicker();
            // No owned-window relationship: the caller may temporarily hide the notch.
            picker.Show();
            Rectangle? area = await picker.Completion;
            if (!area.HasValue) return null;
            await Task.Delay(120); // Allow the selection overlay to leave the desktop compositor.
            return await Task.Run(delegate { using (new AgentPhysicalCoordinates()) return Capture(area.Value, "Selected area · visible desktop pixels"); });
        }
        internal static Size BoundedSize(int width, int height)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException("width");
            double scale = Math.Min(1, MaximumEdge / (double)Math.Max(width, height));
            return new Size(Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
        }
        static AgentAttachment Capture(Rectangle bounds, string label)
        {
            if (bounds.Width < 3 || bounds.Height < 3) throw new InvalidOperationException("Select a larger screen area.");
            Size size = BoundedSize(bounds.Width, bounds.Height);
            // Desktop GDI writes RGB channels, but does not populate an alpha channel.
            using (var bitmap = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                IntPtr screen = AgentVisionNative.GetDC(IntPtr.Zero), destination = IntPtr.Zero;
                if (screen == IntPtr.Zero) throw new InvalidOperationException("Screen capture is unavailable.");
                try
                {
                    destination = graphics.GetHdc();
                    AgentVisionNative.SetStretchBltMode(destination, 4);
                    if (!AgentVisionNative.StretchBlt(destination, 0, 0, size.Width, size.Height, screen,
                        bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x40CC0020))
                        throw new InvalidOperationException("Screen capture failed.");
                }
                finally { if (destination != IntPtr.Zero) graphics.ReleaseHdc(destination); AgentVisionNative.ReleaseDC(IntPtr.Zero, screen); }
                if (IsBlack(bitmap)) throw new InvalidOperationException("The capture is black or protected. Choose another source.");
                return Save(bitmap, label);
            }
        }
        internal static bool IsBlack(Bitmap bitmap)
        {
            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            try
            {
                int absStride = Math.Abs(data.Stride);
                byte[] row = new byte[absStride];
                // Images are bounded to 2048px; scan every pixel so thin text cannot fall between samples.
                for (int y = 0; y < bitmap.Height; y++)
                {
                    IntPtr rowPtr = new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride);
                    Marshal.Copy(rowPtr, row, 0, absStride);
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        int offset = x * 4;
                        if (row[offset] > 3 || row[offset + 1] > 3 || row[offset + 2] > 3)
                            return false;
                    }
                }
                return true;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }
        internal static AgentAttachment Save(Bitmap bitmap, string source)
        {
            string path = NewPath();
            try
            {
                Size size = BoundedSize(bitmap.Width, bitmap.Height);
                using (var output = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
                {
                    using (var graphics = Graphics.FromImage(output))
                    { graphics.InterpolationMode = InterpolationMode.HighQualityBicubic; graphics.DrawImage(bitmap, 0, 0, size.Width, size.Height); }
                    output.Save(path, ImageFormat.Png);
                }
                var image = new BitmapImage();
                using (var stream = File.OpenRead(path))
                { image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze(); }
                return new AgentAttachment(image, path, source);
            }
            catch { AgentAttachment.Delete(path); throw; }
        }
    }

    internal sealed class AgentPhysicalCoordinates : IDisposable
    {
        IntPtr previous;
        internal AgentPhysicalCoordinates() { try { previous = AgentVisionNative.SetThreadDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { } }
        public void Dispose() { if (previous != IntPtr.Zero) AgentVisionNative.SetThreadDpiAwarenessContext(previous); }
    }

    internal static class AgentVisionNative
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; public Rectangle ToRectangle() { return Rectangle.FromLTRB(Left, Top, Right, Bottom); } }
        [StructLayout(LayoutKind.Sequential)] internal struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
        [StructLayout(LayoutKind.Sequential)] internal struct NativePoint { public int X, Y; }
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr window, StringBuilder title, int count);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] internal static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll")] internal static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")] internal static extern bool GetWindowDisplayAffinity(IntPtr window, out uint affinity);
        [DllImport("user32.dll")] internal static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] internal static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] internal static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll")] internal static extern bool StretchBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sx, int sy, int sw, int sh, uint operation);
        [DllImport("gdi32.dll")] internal static extern int SetStretchBltMode(IntPtr dc, int mode);
        [DllImport("user32.dll")] internal static extern bool GetCursorPos(out NativePoint point);
        [DllImport("user32.dll")] internal static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        internal static Rectangle VirtualBounds() { return new Rectangle(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79)); }
    }
}
