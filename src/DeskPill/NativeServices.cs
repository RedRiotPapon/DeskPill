using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Automation;
using Windows.Media.Control;
using Windows.Media.Ocr;
using Windows.Graphics.Imaging;

namespace DeskPill
{
    public sealed class PlaybackCommandQueue
    {
        readonly object gate=new object();
        Task tail=Task.FromResult(false);
        public Task<bool?> Enqueue(Func<Task<bool?>> action)
        {
            lock(gate){
                var next=tail.ContinueWith(previous=>action(),System.Threading.CancellationToken.None,TaskContinuationOptions.None,TaskScheduler.Default).Unwrap();
                tail=next.ContinueWith(previous=>{},TaskScheduler.Default);return next;
            }
        }
    }

    public sealed class MediaSnapshot
    {
        public string Title = "", Artist = "", Album = "", Source = "", Error = "";
        public bool Playing;
        public double PositionSeconds, DurationSeconds;
        public BitmapSource Artwork;
        public bool HasMedia
        {
            get
            {
                return !string.IsNullOrEmpty(Source) || !string.IsNullOrEmpty(Title);
            }
        }
    }

    public sealed class MediaService
    {
        public volatile MediaSnapshot Current = new MediaSnapshot();
        readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        GlobalSystemMediaTransportControlsSessionManager manager;
        DateTime artworkReadUtc;
        readonly PlaybackCommandQueue playbackQueue=new PlaybackCommandQueue();
        async Task<GlobalSystemMediaTransportControlsSession> Session()
        {
            if (manager == null)
                manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            return manager.GetCurrentSession();
        }

        public async Task RefreshAsync()
        {
            await gate.WaitAsync();
            try
            {
                var snapshot = new MediaSnapshot();
                try
                {
                    var session = await Session();
                    if (session != null)
                    {
                        var props = await session.TryGetMediaPropertiesAsync();
                        var timeline = session.GetTimelineProperties();
                        snapshot.Title = props.Title ?? "";
                        snapshot.Artist = props.Artist ?? "";
                        snapshot.Album = props.AlbumTitle ?? "";
                        snapshot.Source = session.SourceAppUserModelId ?? "";
                        snapshot.Playing = session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                        snapshot.PositionSeconds = Math.Max(0, timeline.Position.TotalSeconds);
                        snapshot.DurationSeconds = Math.Max(0, timeline.EndTime.TotalSeconds);
                        var previous = Current;
                        if (previous.Artwork != null && previous.Source == snapshot.Source && previous.Title == snapshot.Title && previous.Artist == snapshot.Artist && previous.Album == snapshot.Album && DateTime.UtcNow - artworkReadUtc < TimeSpan.FromSeconds(30))
                            snapshot.Artwork = previous.Artwork;
                        if (snapshot.Artwork == null && props.Thumbnail != null)
                        {
                            try
                            {
                                using (var stream = await props.Thumbnail.OpenReadAsync())
                                using (var input = stream.AsStreamForRead())
                                {
                                    var bitmap = new BitmapImage();
                                    bitmap.BeginInit();
                                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                    bitmap.DecodePixelWidth = 256;
                                    bitmap.StreamSource = input;
                                    bitmap.EndInit();
                                    bitmap.Freeze();
                                    snapshot.Artwork = bitmap;
                                    artworkReadUtc = DateTime.UtcNow;
                                }
                            }
                            catch
                            { /* Artwork failure must not discard track metadata. */
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    snapshot.Error = "Media unavailable (" + ex.GetType().Name + ")";
                }

                // Metadata/artwork can be slow. Read playback again at publication time.
                try{var latest=await Session();if(latest!=null&&latest.SourceAppUserModelId==snapshot.Source)snapshot.Playing=latest.GetPlaybackInfo().PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;}catch{}
                Current = snapshot;
            }
            finally
            {
                gate.Release();
            }
        }

        async Task Transport(int action, double seconds)
        {
            await gate.WaitAsync();
            try
            {
                var s = await Session();
                if (s == null)
                    return;
                var c = s.GetPlaybackInfo().Controls;
                if (action == 0 && c.IsPlayPauseToggleEnabled)
                    await s.TryTogglePlayPauseAsync();
                if (action == 1 && c.IsNextEnabled)
                    await s.TrySkipNextAsync();
                if (action == 2 && c.IsPreviousEnabled)
                    await s.TrySkipPreviousAsync();
                if (action == 3 && c.IsPauseEnabled)
                    await s.TryPauseAsync();
                if (action == 4 && c.IsPlaybackPositionEnabled && !double.IsNaN(seconds) && !double.IsInfinity(seconds))
                    await s.TryChangePlaybackPositionAsync(TimeSpan.FromSeconds(Math.Max(0, Math.Min(seconds, s.GetTimelineProperties().EndTime.TotalSeconds))).Ticks);
            }
            catch
            {
            }
            finally
            {
                gate.Release();
            }

            await RefreshAsync();
        }

        public Task<bool?> SetPlayingAsync(bool playing,string source){return playbackQueue.Enqueue(()=>SetPlayingCoreAsync(playing,source));}
        async Task<bool?> SetPlayingCoreAsync(bool playing,string source)
        {
            // Click order is captured before dispatching off the UI thread; metadata never blocks it.
            try{
                var s=await Session();
                if(s==null||s.SourceAppUserModelId!=source)return null;
                var info=s.GetPlaybackInfo();var controls=info.Controls;
                bool actual=info.PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                if(actual==playing)return actual;
                bool accepted=false;
                if(playing&&controls.IsPlayEnabled)accepted=await s.TryPlayAsync();
                else if(!playing&&controls.IsPauseEnabled)accepted=await s.TryPauseAsync();
                else if(controls.IsPlayPauseToggleEnabled)accepted=await s.TryTogglePlayPauseAsync();
                var deadline=DateTime.UtcNow.AddMilliseconds(1400);
                do{
                    actual=s.GetPlaybackInfo().PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                    if(!accepted||actual==playing)return actual;
                    await Task.Delay(50);
                }while(DateTime.UtcNow<deadline);
                return s.GetPlaybackInfo().PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            }catch{return null;}
        }

        public Task PlayPauseAsync()
        {
            return Transport(0, 0);
        }

        public Task NextAsync()
        {
            return Transport(1, 0);
        }

        public Task PreviousAsync()
        {
            return Transport(2, 0);
        }

        public Task PauseAsync()
        {
            return Transport(3, 0);
        }

        public Task SeekAsync(double seconds)
        {
            return Transport(4, seconds);
        }
    }

    public static class OcrService
    {
        public static Task<string> RecognizeAsync(System.Drawing.Bitmap bitmap)
        {
            if (bitmap == null)
                throw new ArgumentNullException("bitmap");
            byte[] bytes;
            using (var copy = new System.Drawing.Bitmap(bitmap))
            using (var stream = new MemoryStream())
            {
                copy.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                bytes = stream.ToArray();
            }

            return RecognizeBytes(bytes);
        }

        static async Task<string> RecognizeBytes(byte[] bytes)
        {
            var engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null)
                throw new InvalidOperationException("No Windows OCR language is installed.");
            using (var stream = new MemoryStream(bytes))
            {
                var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream.AsRandomAccessStream());
                if (decoder.PixelWidth > OcrEngine.MaxImageDimension || decoder.PixelHeight > OcrEngine.MaxImageDimension)
                    throw new ArgumentException("Image exceeds Windows OCR maximum dimensions.");
                using (var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied))
                    return (await engine.RecognizeAsync(bitmap)).Text;
            }
        }
    }

    public static class SystemService
    {
        [StructLayout(LayoutKind.Sequential)]
        struct Power
        {
            public byte AC, Battery, Percent, Reserved;
            public uint Life, FullLife;
        }

        [DllImport("kernel32.dll")]
        static extern bool GetSystemPowerStatus(out Power status);
        public static int BatteryPercent()
        {
            Power p;
            return GetSystemPowerStatus(out p) && p.Percent <= 100 && (p.Battery & 128) == 0 ? p.Percent : -1;
        }

        public static bool IsCharging()
        {
            Power p;
            return GetSystemPowerStatus(out p) && (p.Battery & 128) == 0 && (p.Battery & 8) != 0;
        }

        static IAudioEndpointVolume Endpoint()
        {
            object enumerator = new MMDeviceEnumerator();
            IMMDevice device = null;
            try
            {
                Marshal.ThrowExceptionForHR(((IMMDeviceEnumerator)enumerator).GetDefaultAudioEndpoint(0, 1, out device));
                object endpoint;
                Guid id = typeof(IAudioEndpointVolume).GUID;
                Marshal.ThrowExceptionForHR(device.Activate(ref id, 23, IntPtr.Zero, out endpoint));
                return (IAudioEndpointVolume)endpoint;
            }
            finally
            {
                if (device != null)
                    Marshal.ReleaseComObject(device);
                Marshal.ReleaseComObject(enumerator);
            }
        }

        public static float GetVolume()
        {
            IAudioEndpointVolume e = null;
            try
            {
                e = Endpoint();
                float v;
                Marshal.ThrowExceptionForHR(e.GetMasterVolumeLevelScalar(out v));
                return v;
            }
            catch
            {
                return -1;
            }
            finally
            {
                if (e != null)
                    Marshal.ReleaseComObject(e);
            }
        }

        public static bool GetMuted()
        {
            IAudioEndpointVolume e = null;
            try
            {
                e = Endpoint();
                bool v;
                Marshal.ThrowExceptionForHR(e.GetMute(out v));
                return v;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (e != null)
                    Marshal.ReleaseComObject(e);
            }
        }

        public static void SetVolume(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return;
            IAudioEndpointVolume e = null;
            try
            {
                e = Endpoint();
                Guid g = Guid.Empty;
                e.SetMasterVolumeLevelScalar(Math.Max(0, Math.Min(1, value)), ref g);
            }
            catch
            {
            }
            finally
            {
                if (e != null)
                    Marshal.ReleaseComObject(e);
            }
        }

        public static void ToggleMute()
        {
            IAudioEndpointVolume e = null;
            try
            {
                e = Endpoint();
                bool muted;
                Marshal.ThrowExceptionForHR(e.GetMute(out muted));
                Guid g = Guid.Empty;
                e.SetMute(!muted, ref g);
            }
            catch
            {
            }
            finally
            {
                if (e != null)
                    Marshal.ReleaseComObject(e);
            }
        }

        public static int GetBrightness()
        {
            try
            {
                using (var search = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active = True"))
                using (var results = search.Get())
                    foreach (ManagementObject item in results)
                        using (item)
                            return Convert.ToInt32(item["CurrentBrightness"]);
            }
            catch
            {
            }

            return -1;
        }

        public static bool SetBrightness(int percent)
        {
            try
            {
                using (var search = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active = True"))
                using (var results = search.Get())
                {
                    bool changed = false;
                    foreach (ManagementObject item in results)
                        using (item)
                        {
                            item.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)Math.Max(0, Math.Min(100, percent)) });
                            changed = true;
                        }

                    return changed;
                }
            }
            catch
            {
                return false;
            }
        }
    }

    // Immediate (~0ms latency) Core Audio endpoint volume & mute watcher via IAudioEndpointVolumeCallback.
    public sealed class VolumeWatcher : IAudioEndpointVolumeCallback, IDisposable
    {
        readonly Action<float, bool> onChanged;
        IMMDeviceEnumerator enumerator;
        IMMDevice device;
        IAudioEndpointVolume endpoint;
        IntPtr callbackPtr;
        bool disposed;

        public VolumeWatcher(Action<float, bool> onChanged)
        {
            this.onChanged = onChanged;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                if (enumerator.GetDefaultAudioEndpoint(0, 1, out device) == 0 && device != null)
                {
                    object rawEndpoint;
                    Guid id = typeof(IAudioEndpointVolume).GUID;
                    if (device.Activate(ref id, 23, IntPtr.Zero, out rawEndpoint) == 0 && rawEndpoint != null)
                    {
                        endpoint = (IAudioEndpointVolume)rawEndpoint;
                        callbackPtr = Marshal.GetComInterfaceForObject(this, typeof(IAudioEndpointVolumeCallback));
                        if (endpoint.RegisterControlChangeNotify(callbackPtr) != 0)
                        {
                            Marshal.Release(callbackPtr);
                            callbackPtr = IntPtr.Zero;
                        }
                    }
                }
            }
            catch
            {
                Dispose();
            }
        }

        public int OnNotify(IntPtr notifyData)
        {
            if (disposed || notifyData == IntPtr.Zero || onChanged == null)
                return 0;
            try
            {
                bool muted = Marshal.ReadInt32(notifyData, 16) != 0;
                byte[] bytes = BitConverter.GetBytes(Marshal.ReadInt32(notifyData, 20));
                float vol = BitConverter.ToSingle(bytes, 0);
                if (!float.IsNaN(vol) && !float.IsInfinity(vol))
                    onChanged(Math.Max(0f, Math.Min(1f, vol)), muted);
            }
            catch
            {
            }

            return 0;
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            try
            {
                if (endpoint != null && callbackPtr != IntPtr.Zero)
                    endpoint.UnregisterControlChangeNotify(callbackPtr);
            }
            catch
            {
            }
            try
            {
                if (callbackPtr != IntPtr.Zero)
                    Marshal.Release(callbackPtr);
            }
            catch
            {
            }
            callbackPtr = IntPtr.Zero;
            if (endpoint != null) { try { Marshal.ReleaseComObject(endpoint); } catch { } endpoint = null; }
            if (device != null) { try { Marshal.ReleaseComObject(device); } catch { } device = null; }
            if (enumerator != null) { try { Marshal.ReleaseComObject(enumerator); } catch { } enumerator = null; }
        }
    }

    [ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioEndpointVolumeCallback
    {
        [PreserveSig]
        int OnNotify(IntPtr notifyData);
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumerator
    {
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int flow, uint mask, out IntPtr devices);
        [PreserveSig]
        int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object result);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig]
        int RegisterControlChangeNotify(IntPtr p);
        [PreserveSig]
        int UnregisterControlChangeNotify(IntPtr p);
        [PreserveSig]
        int GetChannelCount(out uint count);
        [PreserveSig]
        int SetMasterVolumeLevel(float level, ref Guid context);
        [PreserveSig]
        int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig]
        int GetMasterVolumeLevel(out float level);
        [PreserveSig]
        int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig]
        int SetChannelVolumeLevel(uint channel, float level, ref Guid context);
        [PreserveSig]
        int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig]
        int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig]
        int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig]
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    public static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")]
        static extern bool AddClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll")]
        static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
        public static void EnableClipboardListener(IntPtr hwnd)
        {
            AddClipboardFormatListener(hwnd);
        }

        public static void DisableClipboardListener(IntPtr hwnd)
        {
            RemoveClipboardFormatListener(hwnd);
        }

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [StructLayout(LayoutKind.Sequential)]
        struct Rect
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MonitorInfo
        {
            public int Size;
            public Rect Monitor, Work;
            public uint Flags;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int max);
        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")]
        static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")]
        static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")]
        static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("gdi32.dll")]
        static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int width, int height, IntPtr hdcSrc, int xSrc, int ySrc, int rop);
        [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
        static extern uint GetWindowProcessId(IntPtr hwnd, out uint processId);

        public static bool IsForegroundFullscreen(IntPtr ownWindow)
        {
            var h = GetForegroundWindow();
            if (h == IntPtr.Zero || h == ownWindow)
                return false;
            var name = new System.Text.StringBuilder(256);
            GetClassName(h, name, 256);
            if (name.ToString() == "Progman" || name.ToString() == "WorkerW" || name.ToString() == "Shell_TrayWnd")
                return false;
            Rect r;
            var info = new MonitorInfo();
            info.Size = Marshal.SizeOf(info);
            if (!GetWindowRect(h, out r) || !GetMonitorInfo(MonitorFromWindow(h, 2), ref info))
                return false;
            return r.Left <= info.Monitor.Left && r.Top <= info.Monitor.Top && r.Right >= info.Monitor.Right && r.Bottom >= info.Monitor.Bottom;
        }

        delegate bool EnumWindowCallback(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowCallback callback, IntPtr parameter);
        [DllImport("dwmapi.dll")]
        static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

        static bool OccupiesNotch(IntPtr window, string cls, int left, int top, int width, int height)
        {
            IntPtr hit;
            int x = left + width / 2, y = top + height / 2;
            if (!cls.StartsWith("Chrome_WidgetWin", StringComparison.Ordinal) && SendMessageTimeout(window, 0x84, IntPtr.Zero, new IntPtr(unchecked((y << 16) | (x & 0xffff))), 2, 60, out hit) != IntPtr.Zero && hit.ToInt32() == 2)
                return false; // Native empty draggable caption, not client content.
            if (!cls.StartsWith("Chrome_WidgetWin", StringComparison.Ordinal)) return true;
            // Chromium custom title bars are client areas. Query the app's tree,
            // not ElementFromPoint (which would find our own topmost overlay).
            // This runs off the dispatcher; an unresponsive provider cannot stall motion.
            try
            {
                var area = new System.Windows.Rect(left, top, width, height);
                var pending = new System.Collections.Generic.Queue<AutomationElement>();
                pending.Enqueue(AutomationElement.FromHandle(window));
                var watch = Stopwatch.StartNew(); int visited = 0; bool exposed = false;
                while (pending.Count > 0)
                {
                    if (++visited > 350 || watch.ElapsedMilliseconds > 250) return true;
                    var node = pending.Dequeue();
                    var current = node.Current;
                    if (current.IsOffscreen) continue;
                    var bounds = current.BoundingRectangle;
                    if (!bounds.IsEmpty && !bounds.IntersectsWith(area)) continue;
                    var type = current.ControlType;
                    if (type == ControlType.Button || type == ControlType.Edit || type == ControlType.TabItem || type == ControlType.Text || type == ControlType.Hyperlink || type == ControlType.ComboBox || type == ControlType.MenuItem || type == ControlType.CheckBox || type == ControlType.RadioButton || type == ControlType.Slider)
                    {
                        if (!bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0) return true;
                    }
                    var child = TreeWalker.ControlViewWalker.GetFirstChild(node);
                    while (child != null)
                    {
                        exposed = true; pending.Enqueue(child);
                        if (pending.Count > 350 || watch.ElapsedMilliseconds > 250) return true;
                        child = TreeWalker.ControlViewWalker.GetNextSibling(child);
                    }
                }
                return !exposed; // A missing accessibility tree is not proof of empty space.
            }
            catch { return true; }
        }

        public static bool IsForegroundOverlappingTabs(IntPtr ownWindow, int notchCenterX, int notchTopY, int notchWidth, int notchHeight)
        {
            // Walk in Z order: an empty title bar must not inherit content from a covered window.
            int left = notchCenterX - notchWidth / 2, right = left + notchWidth;
            bool overlaps = false;
            EnumWindows(delegate (IntPtr window, IntPtr ignored)
            {
                if (window == ownWindow || !IsWindowVisible(window) || IsIconic(window)) return true;
                Rect bounds;
                if (!GetWindowRect(window, out bounds) || bounds.Left >= right || bounds.Right <= left
                    || bounds.Top >= notchTopY + notchHeight || bounds.Bottom <= notchTopY) return true;
                long style = IntPtr.Size == 8 ? GetWindowLong64(window, -20).ToInt64() : GetWindowLong32(window, -20);
                if ((style & 0x80) != 0) return true; // Other overlays/tool windows aren't work surfaces.
                var name = new System.Text.StringBuilder(128);
                GetClassName(window, name, name.Capacity);
                string cls = name.ToString();
                if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd") return true;
                int cloaked;
                if (DwmGetWindowAttribute(window, 14, out cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
                overlaps = OccupiesNotch(window, cls, left, notchTopY, notchWidth, notchHeight);
                return false;
            }, IntPtr.Zero);
            return overlaps;
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        static extern int GetWindowLong32(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        static extern int SetWindowLong32(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        static extern IntPtr GetWindowLong64(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        static extern IntPtr SetWindowLong64(IntPtr hwnd, int index, IntPtr value);
        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out PointStruct pt);

        [StructLayout(LayoutKind.Sequential)]
        struct PointStruct
        {
            public int X, Y;
        }

        public static void SetClickThrough(IntPtr window,bool enabled)
        {
            if(window==IntPtr.Zero)return;
            if(IntPtr.Size==8){long style=GetWindowLong64(window,-20).ToInt64();SetWindowLong64(window,-20,new IntPtr(enabled?style|0x20L:style&~0x20L));}
            else {int style=GetWindowLong32(window,-20);SetWindowLong32(window,-20,enabled?style|0x20:style&~0x20);}
        }

        public static void SetToolWindow(IntPtr hwnd)
        {

            if (IntPtr.Size == 8)
            {
                long style = GetWindowLong64(hwnd, -20).ToInt64();
                SetWindowLong64(hwnd, -20, new IntPtr((style | 0x08000080L) & ~0x40000L));
            }
            else
            {
                int style = GetWindowLong32(hwnd, -20);
                SetWindowLong32(hwnd, -20, (style | 0x08000080) & ~0x40000);
            }
        }

        public static void EnsureTopmost(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return;
            // HWND_TOPMOST (-1), SWP_NOSIZE (0x1) | SWP_NOMOVE (0x2) | SWP_NOACTIVATE (0x10)
            SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0013);
        }

        public static bool TryGetCursorPos(out int x, out int y)
        {
            PointStruct p;
            if (GetCursorPos(out p))
            {
                x = p.X;
                y = p.Y;
                return true;
            }

            x = 0;
            y = 0;
            return false;
        }

        [DllImport("user32.dll")]
        static extern short GetKeyState(int key);
        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hwnd, IntPtr process);
        [DllImport("user32.dll")]
        static extern IntPtr GetKeyboardLayout(uint thread);
        public static bool CapsLock()
        {
            return (GetKeyState(0x14) & 1) != 0;
        }

        public static string InputLanguage()
        {
            try
            {
                return System.Globalization.CultureInfo.GetCultureInfo((int)(GetKeyboardLayout(GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero)).ToInt64() & 0xffff)).TwoLetterISOLanguageName.ToUpperInvariant();
            }
            catch
            {
                return "";
            }
        }
    }
}
