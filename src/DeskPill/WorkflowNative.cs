using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Threading;

namespace DeskPill
{
    public sealed class PasteTarget
    {
        public readonly IntPtr Window;
        public readonly uint ProcessId;
        public readonly string Name;
        PasteTarget(IntPtr window, uint pid, string name)
        {
            Window = window;
            ProcessId = pid;
            Name = name;
        }

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll")]
        static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetClassName(IntPtr window, System.Text.StringBuilder text, int count);
        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint count, INPUT[] input, int size);
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public uint type;
            public INPUTUNION data;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct INPUTUNION
        {
            [FieldOffset(0)]
            public KEYBOARD keyboard;
            [FieldOffset(0)]
            public MOUSE mouse;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBOARD
        {
            public ushort key, scan;
            public uint flags, time;
            public UIntPtr extra;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSE
        {
            public int x, y;
            public uint data, flags, time;
            public UIntPtr extra;
        }

        public static PasteTarget Capture()
        {
            var window = GetForegroundWindow();
            uint pid;
            GetWindowThreadProcessId(window, out pid);
            if (window == IntPtr.Zero || pid == 0 || pid == Process.GetCurrentProcess().Id || !IsWindowVisible(window))
                return null;
            var cls = new System.Text.StringBuilder(128);
            GetClassName(window, cls, cls.Capacity);
            if (new[]
            {
                "Shell_TrayWnd",
                "Shell_SecondaryTrayWnd",
                "NotifyIconOverflowWindow",
                "Progman",
                "WorkerW",
                "#32768"
            }.Contains(cls.ToString()))
                return null;
            string name = "previous app";
            try
            {
                name = Process.GetProcessById((int)pid).ProcessName;
            }
            catch
            {
            }

            return new PasteTarget(window, pid, name);
        }

        public static bool CanInject(IntPtr window, uint expected, uint actual, bool foreground, bool modifiers)
        {
            return window != IntPtr.Zero && expected != 0 && expected == actual && actual != Process.GetCurrentProcess().Id && foreground && !modifiers;
        }

        public async Task<bool> TryPasteAsync()
        {
            uint actual;
            GetWindowThreadProcessId(Window, out actual);
            if (!IsWindow(Window) || actual != ProcessId || !SetForegroundWindow(Window))
                return false;
            await Task.Delay(60);
            GetWindowThreadProcessId(Window, out actual);
            bool modifiers = new[]
            {
                0x10,
                0x11,
                0x12,
                0x5B,
                0x5C
            }.AnyHeld();
            if (!IsWindow(Window) || !CanInject(Window, ProcessId, actual, GetForegroundWindow() == Window, modifiers))
                return false;
            var input = new[]
            {
                Key(0x11, 0),
                Key(0x56, 0),
                Key(0x56, 2),
                Key(0x11, 2)
            };
            return SendInput(4, input, Marshal.SizeOf(typeof(INPUT))) == 4;
        }

        static INPUT Key(ushort key, uint flags)
        {
            return new INPUT
            {
                type = 1,
                data = new INPUTUNION
                {
                    keyboard = new KEYBOARD
                    {
                        key = key,
                        flags = flags
                    }
                }
            };
        }

        internal static bool Held(int key)
        {
            return (GetAsyncKeyState(key) & 0x8000) != 0;
        }
    }

    static class ModifierKeysCheck
    {
        public static bool AnyHeld(this int[] keys)
        {
            foreach (int key in keys)
                if (PasteTarget.Held(key))
                    return true;
            return false;
        }
    }

    public static class NotificationLauncher
    {
        public static bool CanOpen(string appId)
        {
            return !string.IsNullOrWhiteSpace(appId) && appId.Length <= 512 && appId.IndexOf('"') < 0 && !System.Linq.Enumerable.Any(appId, c => char.IsControl(c));
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct ShellExecuteInfo
        {
            public int size;
            public uint mask;
            public IntPtr window;
            public string verb, file, parameters, directory;
            public int show;
            public IntPtr instance, idList;
            public string className;
            public IntPtr classKey;
            public uint hotKey;
            public IntPtr icon, process;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHParseDisplayName(string name, IntPtr context, out IntPtr list, uint requested, out uint attributes);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool ShellExecuteEx(ref ShellExecuteInfo info);
        static IntPtr Resolve(string appId)
        {
            if (!CanOpen(appId))
                return IntPtr.Zero;
            IntPtr list;
            uint attributes;
            int result = SHParseDisplayName("shell:AppsFolder\\" + appId, IntPtr.Zero, out list, 0, out attributes);
            if (result < 0)
            {
                if (list != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(list);
                return IntPtr.Zero;
            }

            return list;
        }

        public static bool IsRegistered(string appId)
        {
            IntPtr list = IntPtr.Zero;
            try
            {
                list = Resolve(appId);
                return list != IntPtr.Zero;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (list != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(list);
            }
        }

        [DllImport("ole32.dll")]
        static extern int CoInitializeEx(IntPtr reserved, uint apartment);
        [DllImport("ole32.dll")]
        static extern void CoUninitialize();
        public static Task<bool> OpenAsync(DesktopNotification item)
        {
            string id = item.AppId;
            var completion = new TaskCompletionSource<bool>();
            var worker = new Thread(delegate ()
            {
                int initialized = CoInitializeEx(IntPtr.Zero, 2);
                try
                {
                    completion.TrySetResult(initialized >= 0 && Open(new DesktopNotification { AppId = id }));
                }
                catch
                {
                    completion.TrySetResult(false);
                }
                finally
                {
                    if (initialized >= 0)
                        CoUninitialize();
                }
            });
            worker.IsBackground = true;
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
            return completion.Task;
        }

        public static bool Open(DesktopNotification item)
        {
            IntPtr list = IntPtr.Zero;
            try
            {
                list = Resolve(item.AppId);
                if (list == IntPtr.Zero)
                    return false;
                // Invoke the registered Shell item, with no command line from notification text.
                // https://learn.microsoft.com/windows/win32/api/shellapi/ns-shellapi-shellexecuteinfow
                var info = new ShellExecuteInfo
                {
                    size = Marshal.SizeOf(typeof(ShellExecuteInfo)),
                    mask = 0x0000000C | 0x00000400 | 0x00000100,
                    idList = list,
                    show = 1
                };
                return ShellExecuteEx(ref info);
            }
            catch
            {
                return false;
            }
            finally
            {
                if (list != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(list);
            }
        }
    }
}
