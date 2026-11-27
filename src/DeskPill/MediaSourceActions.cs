using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace DeskPill
{
    public static class MediaSourcePolicy
    {
        public static string Executable(string source)
        {
            if (string.IsNullOrWhiteSpace(source) || source.Length > 256 || source.Any(char.IsControl) || source.IndexOfAny(new[] { '/', '\\', ':', '"' }) >= 0)
                return "";
            if (source.Equals("Chrome", StringComparison.OrdinalIgnoreCase) || source.StartsWith("Chrome.", StringComparison.OrdinalIgnoreCase) || source.StartsWith("Google.Chrome", StringComparison.OrdinalIgnoreCase))
                return "chrome";
            if (source.StartsWith("MicrosoftEdge", StringComparison.OrdinalIgnoreCase) || source.StartsWith("MSEdge", StringComparison.OrdinalIgnoreCase))
                return "msedge";
            if (new[]
            {
                "Spotify",
                "VLC",
                "MusicBee",
                "foobar2000",
                "AIMP"
            }.Contains(source, StringComparer.OrdinalIgnoreCase))
                return source;
            foreach (string browser in new[]
            {
                "Brave",
                "Vivaldi",
                "Opera",
                "Firefox",
                "Chromium"
            }

            )
                if (source.Equals(browser, StringComparison.OrdinalIgnoreCase) || source.StartsWith(browser + ".", StringComparison.OrdinalIgnoreCase))
                    return browser.ToLowerInvariant();
            if (source.StartsWith("Mozilla.Firefox", StringComparison.OrdinalIgnoreCase))
                return "firefox";
            if (source.Equals("OperaStable", StringComparison.OrdinalIgnoreCase) || source.Equals("OperaGXStable", StringComparison.OrdinalIgnoreCase))
                return "opera";
            if (source.StartsWith("SpotifyAB.SpotifyMusic_", StringComparison.OrdinalIgnoreCase))
                return "Spotify";
            return Regex.IsMatch(source, @"^[\w][\w. -]{0,100}\.exe$", RegexOptions.IgnoreCase) ? source.Substring(0, source.Length - 4) : "";
        }

        public static bool IsBrowser(string executable)
        {
            return new[]
            {
                "chrome",
                "msedge",
                "brave",
                "vivaldi",
                "opera",
                "firefox",
                "chromium"
            }.Contains(executable, StringComparer.OrdinalIgnoreCase);
        }

        static string Words(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";
            var value = new StringBuilder();
            bool space = false;
            foreach (char c in text.Normalize(NormalizationForm.FormKC))
            {
                if (char.IsLetterOrDigit(c) || char.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark || char.GetUnicodeCategory(c) == UnicodeCategory.SpacingCombiningMark || char.GetUnicodeCategory(c) == UnicodeCategory.EnclosingMark)
                {
                    if (space && value.Length > 0)
                        value.Append(' ');
                    value.Append(char.ToLowerInvariant(c));
                    space = false;
                }
                else
                    space = true;
            }

            return value.ToString();
        }

        public static int TabScore(MediaSnapshot media, string name)
        {
            string title = Words(media.Title), tab = Words(name), artist = Words(media.Artist);
            if (title.Length == 0 || tab.Length == 0)
                return 0;
            int score = tab == title ? 100 : (title.Length >= 5 || title.Length >= 2 && artist.Length >= 3 && (" " + tab + " ").Contains(" " + artist + " ")) && (" " + tab + " ").Contains(" " + title + " ") ? 70 : 0;
            return score == 0 ? 0 : score + (artist.Length > 0 && (" " + tab + " ").Contains(" " + artist + " ") ? 40 : 0);
        }

        public static int BestTab(MediaSnapshot media, string[] names)
        {
            int best = -1, score = 0;
            bool tied = false;
            for (int i = 0; i < names.Length; i++)
            {
                int next = TabScore(media, names[i]);
                if (next > score)
                {
                    score = next;
                    best = i;
                    tied = false;
                }
                else if (next > 0 && next == score)
                    tied = true;
            }

            return tied ? -1 : best;
        }
    }

    public sealed class MediaOpenResult
    {
        public volatile bool Opened, TabSelected;
    }

    public static class MediaSourceLauncher
    {
        delegate bool WindowCallback(IntPtr window, IntPtr ignored);
        [DllImport("user32.dll")]
        static extern bool EnumWindows(WindowCallback callback, IntPtr ignored);
        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetWindowTextLength(IntPtr window);
        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")]
        static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")]
        static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")]
        static extern bool ShowWindowAsync(IntPtr window, int command);
        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        static int busy;
        sealed class AppWindow
        {
            public IntPtr Handle;
            public uint Pid;
        }

        sealed class Tab
        {
            public AppWindow Window;
            public AutomationElement Element;
            public string Name;
        }

        static bool Focus(AppWindow window)
        {
            uint pid;
            GetWindowThreadProcessId(window.Handle, out pid);
            if (!IsWindow(window.Handle) || pid != window.Pid)
                return false;
            if (IsIconic(window.Handle))
                ShowWindowAsync(window.Handle, 9);
            return SetForegroundWindow(window.Handle);
        }

        static List<AppWindow> WindowsFor(string executable)
        {
            var result = new List<AppWindow>();
            if (executable.Length == 0)
                return result;
            EnumWindows(delegate (IntPtr window, IntPtr ignored)
            {
                if (!IsWindowVisible(window) || GetWindow(window, 4) != IntPtr.Zero || GetWindowTextLength(window) == 0)
                    return true;
                uint pid;
                GetWindowThreadProcessId(window, out pid);
                try
                {
                    using (var process = Process.GetProcessById((int)pid))
                        if (process.ProcessName.Equals(executable, StringComparison.OrdinalIgnoreCase))
                            result.Add(new AppWindow { Handle = window, Pid = pid });
                }
                catch (ArgumentException)
                {
                }
                catch (InvalidOperationException)
                {
                }
                catch (System.ComponentModel.Win32Exception)
                {
                }

                return true;
            }, IntPtr.Zero);
            return result;
        }

        static bool ReadTabs(AppWindow window, List<Tab> tabs, CancellationToken token)
        {
            // Inspect browser chrome only. Never descend into the web document or read URLs/content.
            var walker = TreeWalker.ControlViewWalker;
            var pending = new Queue<Tuple<AutomationElement, int>>();
            pending.Enqueue(Tuple.Create(AutomationElement.FromHandle(window.Handle), 0));
            int visited = 0;
            bool complete = true;
            while (pending.Count > 0 && visited++ < 240)
            {
                token.ThrowIfCancellationRequested();
                var entry = pending.Dequeue();
                var type = entry.Item1.Current.ControlType;
                if (type == ControlType.TabItem)
                {
                    tabs.Add(new Tab { Window = window, Element = entry.Item1, Name = entry.Item1.Current.Name });
                    continue;
                }

                if (type == ControlType.Document)
                    continue;
                var child = walker.GetFirstChild(entry.Item1);
                if (entry.Item2 >= 12)
                {
                    if (child != null)
                        complete = false;
                    continue;
                }

                for (int i = 0; child != null && i < 160; i++)
                {
                    token.ThrowIfCancellationRequested();
                    pending.Enqueue(Tuple.Create(child, entry.Item2 + 1));
                    child = walker.GetNextSibling(child);
                }

                if (child != null)
                    complete = false;
            }

            return complete && pending.Count == 0;
        }

        static MediaOpenResult Activate(MediaSnapshot media, CancellationToken token, MediaOpenResult result)
        {
            string executable = MediaSourcePolicy.Executable(media.Source);
            var windows = WindowsFor(executable);
            if (windows.Count == 0)
                return result;
            token.ThrowIfCancellationRequested();
            result.Opened = Focus(windows[0]);
            if (!result.Opened)
                return result;
            if (MediaSourcePolicy.IsBrowser(executable))
            {
                var tabs = new List<Tab>();
                bool complete = true;
                foreach (var window in windows)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        if (!ReadTabs(window, tabs, token))
                            complete = false;
                    }
                    catch (ElementNotAvailableException)
                    {
                        complete = false;
                    }
                    catch (InvalidOperationException)
                    {
                        complete = false;
                    }
                    catch (COMException)
                    {
                        complete = false;
                    }
                }

                int best = complete ? MediaSourcePolicy.BestTab(media, tabs.Select(t => t.Name).ToArray()) : -1;
                token.ThrowIfCancellationRequested();
                if (GetForegroundWindow() != windows[0].Handle)
                    return result; // Do not steal focus after the user switches away.
                if (best >= 0)
                {
                    var tab = tabs[best];
                    if (Focus(tab.Window))
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            object pattern;
                            if (tab.Element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out pattern))
                            {
                                token.ThrowIfCancellationRequested();
                                if (GetForegroundWindow() != tab.Window.Handle)
                                    return result;
                                ((SelectionItemPattern)pattern).Select();
                                result.TabSelected = true;
                                return result;
                            }
                        }
                        catch (ElementNotAvailableException)
                        {
                        }
                        catch (InvalidOperationException)
                        {
                        }
                        catch (COMException)
                        {
                        }

                        return result;
                    }
                }
            }

            token.ThrowIfCancellationRequested();
            return result;
        }

        public static async Task<MediaOpenResult> OpenAsync(MediaSnapshot media)
        {
            if (media == null || !media.HasMedia || Interlocked.CompareExchange(ref busy, 1, 0) != 0)
                return new MediaOpenResult();
            var timeout = new CancellationTokenSource();
            timeout.CancelAfter(2500);
            var result = new MediaOpenResult();
            var work = Task.Run(() => Activate(media, timeout.Token, result));
            bool pending = false;
            Task cleanupWork = work;
            try
            {
                if (await Task.WhenAny(work, Task.Delay(2600)) != work)
                {
                    pending = true;
                    return result;
                }

                await work;
                if (result.Opened)
                    return result;
                // Only registered application IDs go to Shell; metadata never becomes a command line.
                if (media.Source != null && Regex.IsMatch(media.Source, @"^[\w.!-]{1,256}$"))
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    var launch = NotificationLauncher.OpenAsync(new DesktopNotification { AppId = media.Source }, timeout.Token);
                    if (await Task.WhenAny(launch, Task.Delay(2000)) == launch)
                        result.Opened = await launch;
                    else
                    {
                        timeout.Cancel();
                        pending = true;
                        cleanupWork = launch;
                    }
                }

                return result;
            }
            catch (OperationCanceledException)
            {
                return result;
            }
            catch (Exception)
            {
                return result;
            }
            finally
            {
                if (pending)
                {
                    var cleanup = cleanupWork.ContinueWith(t =>
                    {
                        var ignored = t.Exception;
                        timeout.Dispose();
                        Interlocked.Exchange(ref busy, 0);
                    }, TaskScheduler.Default);
                }
                else
                {
                    timeout.Dispose();
                    Interlocked.Exchange(ref busy, 0);
                }
            }
        }
    }
}
