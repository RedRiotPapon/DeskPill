using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DeskPill
{
    public static class GamePolicy
    {
        static readonly string[] Excluded =
        {
            "chrome.exe",
            "msedge.exe",
            "firefox.exe",
            "brave.exe",
            "opera.exe",
            "vivaldi.exe",
            "code.exe",
            "antigravity.exe",
            "chatgpt.exe",
            "gpt.exe",
            "vlc.exe",
            "mpv.exe",
            "explorer.exe",
            "applicationframehost.exe",
            "dwm.exe",
            "photos.exe",
            "microsoft.photos.exe",
            "video.ui.exe",
            "powerpnt.exe",
            "acrobat.exe",
            "acrord32.exe",
            "taskmgr.exe",
            "snippingtool.exe",
            "winword.exe",
            "excel.exe",
            "discord.exe",
            "spotify.exe",
            "msedgewebview2.exe",
            "steam.exe",
            "epicgameslauncher.exe",
            "obs64.exe",
            "deskpill.exe"
        };
        public static bool Matches(Settings settings, string executable, bool fullscreen, bool graphics, bool desktopClass)
        {
            if (!settings.GameOverlay || string.IsNullOrWhiteSpace(executable))
                return false;
            if ((settings.GameApps ?? new string[0]).Contains(executable, StringComparer.OrdinalIgnoreCase))
                return true;
            return settings.DetectFullscreenGames && fullscreen && graphics && !desktopClass && !Excluded.Contains(executable, StringComparer.OrdinalIgnoreCase);
        }
    }

    public sealed class GameWindow
    {
        public uint ProcessId;
        public string Executable = "";
        public bool Matches;
        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetClassName(IntPtr window, StringBuilder name, int length);
        public static uint ForegroundPid
        {
            get
            {
                uint pid;
                GetWindowThreadProcessId(GetForegroundWindow(), out pid);
                return pid;
            }
        }

        public static GameWindow Read(Settings settings)
        {
            var result = new GameWindow();
            IntPtr window = GetForegroundWindow();
            uint pid;
            GetWindowThreadProcessId(window, out pid);
            result.ProcessId = pid;
            if (pid == 0 || pid == Process.GetCurrentProcess().Id)
                return result;
            try
            {
                using (var process = Process.GetProcessById((int)pid))
                {
                    result.Executable = process.ProcessName + ".exe";
                    bool listed = (settings.GameApps ?? new string[0]).Contains(result.Executable, StringComparer.OrdinalIgnoreCase);
                    bool fullscreen = NativeMethods.IsForegroundFullscreen(IntPtr.Zero), graphics = false;
                    var name = new StringBuilder(200);
                    GetClassName(window, name, 200);
                    string cls = name.ToString();
                    bool desktop = cls.StartsWith("Chrome_WidgetWin", StringComparison.Ordinal) || cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd";
                    if (!listed && fullscreen && !desktop)
                        try
                        {
                            foreach (ProcessModule module in process.Modules)
                            {
                                string m = module.ModuleName.ToLowerInvariant();
                                if (m == "d3d9.dll" || m == "d3d10.dll" || m == "d3d11.dll" || m == "d3d12.dll" || m == "vulkan-1.dll" || m == "opengl32.dll")
                                {
                                    graphics = true;
                                    break;
                                }
                            }
                        }
                        catch (System.ComponentModel.Win32Exception)
                        {
                        }

                    result.Matches = GamePolicy.Matches(settings, result.Executable, fullscreen, graphics, desktop);
                }
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

            return result;
        }
    }

    public sealed class FrameRateSamples
    {
        sealed class Sample
        {
            public DateTime At;
            public double Milliseconds;
        }

        readonly Dictionary<string, Queue<Sample>> chains = new Dictionary<string, Queue<Sample>>();
        readonly object gate = new object ();
        readonly uint processId;
        int pidIndex = -1, chainIndex = -1, timeIndex = -1;
        public FrameRateSamples(uint processId)
        {
            this.processId = processId;
        }

        public static string[] Csv(string line)
        {
            var fields = new List<string>();
            var field = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                        quoted = !quoted;
                }
                else if (c == ',' && !quoted)
                {
                    fields.Add(field.ToString());
                    field.Clear();
                }
                else
                    field.Append(c);
            }

            fields.Add(field.ToString());
            return fields.ToArray();
        }

        public void ReadLine(string line, DateTime now)
        {
            if (string.IsNullOrWhiteSpace(line) || line.Length > 8192)
                return;
            var fields = Csv(line);
            lock (gate)
            {
                if (fields.Contains("ProcessID"))
                {
                    pidIndex = Array.IndexOf(fields, "ProcessID");
                    chainIndex = Array.IndexOf(fields, "SwapChainAddress");
                    timeIndex = Array.IndexOf(fields, "MsBetweenPresents");
                    return;
                }

                int max = Math.Max(pidIndex, Math.Max(chainIndex, timeIndex));
                if (pidIndex < 0 || chainIndex < 0 || timeIndex < 0 || fields.Length <= max)
                    return;
                uint pid;
                double ms;
                if (!uint.TryParse(fields[pidIndex], out pid) || pid != processId || !double.TryParse(fields[timeIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out ms) || double.IsNaN(ms) || double.IsInfinity(ms) || ms < .05 || ms > 5000)
                    return;
                string key = fields[chainIndex];
                Queue<Sample> samples;
                if (!chains.TryGetValue(key, out samples))
                {
                    if (chains.Count >= 8)
                        return;
                    chains[key] = samples = new Queue<Sample>();
                }

                samples.Enqueue(new Sample { At = now, Milliseconds = ms });
                while (samples.Count > 1024 || (samples.Count > 0 && (now - samples.Peek().At).TotalSeconds > 1.5))
                    samples.Dequeue();
            }
        }

        public double? Fps(DateTime now)
        {
            lock (gate)
            {
                Sample[] best = null;
                foreach (var chain in chains.Values)
                {
                    var recent = chain.Where(s => (now - s.At).TotalSeconds <= 1.5).ToArray();
                    if (recent.Length >= 3 && (best == null || recent.Length > best.Length))
                        best = recent;
                }

                return best == null ? (double? )null : 1000.0 / best.Average(x => x.Milliseconds);
            }
        }
    }

    public static class PresentMonAssets
    {
        public const string Download = "https://github.com/GameTechDev/PresentMon/releases/download/v2.6.0/PresentMon-2.6.0-x64.exe";
        public const string License = @"Copyright (C) 2017-2024 Intel Corporation

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the ""Software""),
to deal in the Software without restriction, including without limitation
the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom
the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included
in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND, EXPRESS
OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.  IN NO EVENT SHALL
THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES
OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE
OR OTHER DEALINGS IN THE SOFTWARE.
";
        public const string Hash = "B2A706BC6AD475749E3B7E3409263AA1E6906D45BDCF993F6DBC0F660188F1AF";
        public static string Executable
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskPill", "fps", "PresentMon-2.6.0.exe");
            }
        }

        public static bool Ready
        {
            get
            {
                return File.Exists(Executable);
            }
        }

        public static bool TracePermission
        {
            get
            {
                try
                {
                    var principal = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent());
                    return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator) || principal.IsInRole(new System.Security.Principal.SecurityIdentifier("S-1-5-32-559"));
                }
                catch (System.Security.SecurityException)
                {
                    return false;
                }
            }
        }

        static readonly SemaphoreSlim installing = new SemaphoreSlim(1, 1);
        public static async Task InstallAsync()
        {
            if (!Environment.Is64BitOperatingSystem)
                throw new InvalidOperationException("FPS tracing requires 64-bit Windows.");
            if (!await installing.WaitAsync(0))
                throw new InvalidOperationException("FPS installation is already running.");
            string partial = Executable + "." + Guid.NewGuid().ToString("N") + ".partial";
            try
            {
                if (Ready && SpeechAssets.HashMatches(Executable, Hash))
                {
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(Executable), "LICENSE.txt"), License);
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(Executable));
                using (var client = new WebClient())
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45)))
                using (timeout.Token.Register(client.CancelAsync))
                    await client.DownloadFileTaskAsync(new Uri(Download), partial);
                if (!SpeechAssets.HashMatches(partial, Hash))
                    throw new InvalidDataException("FPS download verification failed. Try again.");
                if (File.Exists(Executable))
                    File.Delete(Executable);
                File.Move(partial, Executable);
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(Executable), "LICENSE.txt"), License);
            }
            finally
            {
                if (File.Exists(partial))
                    File.Delete(partial);
                installing.Release();
            }
        }
    }

    public sealed class PresentMonCapture : IDisposable
    {
        readonly FrameRateSamples samples;
        readonly string session = "DeskPill-" + Guid.NewGuid().ToString("N");
        readonly uint pid;
        readonly object gate = new object ();
        Process process;
        volatile bool stopped;
        volatile string status = "Waiting for game presents";
        public string Status
        {
            get
            {
                return status;
            }
        }

        public double? Fps
        {
            get
            {
                return samples.Fps(DateTime.UtcNow);
            }
        }

        public PresentMonCapture(uint pid)
        {
            this.pid = pid;
            samples = new FrameRateSamples(pid);
            Task.Run((Action)Run);
        }

        ProcessStartInfo Info(string args)
        {
            return new ProcessStartInfo(PresentMonAssets.Executable, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
        }

        void Run()
        {
            try
            {
                if (!PresentMonAssets.Ready)
                {
                    status = "Install the FPS component in Settings";
                    return;
                }

                if (!SpeechAssets.HashMatches(PresentMonAssets.Executable, PresentMonAssets.Hash))
                {
                    status = "FPS component needs reinstalling";
                    return;
                }

                if (!PresentMonAssets.TracePermission)
                {
                    status = "FPS unavailable: Windows Performance Log Users access is needed";
                    return;
                }

                while (!stopped)
                {
                    using (var current = new Process
                    {
                        StartInfo = Info("--process_id " + pid + " --output_stdout --no_console_stats --v1_metrics --no_track_gpu --no_track_input --no_track_display --session_name " + session + " --terminate_on_proc_exit --timed 30 --terminate_after_timed")
                    }

                    )
                    {
                        lock (gate)
                        {
                            if (stopped)
                                return;
                            process = current;
                            current.Start();
                        }

                        current.ErrorDataReceived += delegate
                        {
                        };
                        current.BeginErrorReadLine();
                        string line;
                        while ((line = current.StandardOutput.ReadLine()) != null)
                        {
                            if (!stopped)
                                samples.ReadLine(line, DateTime.UtcNow);
                        }

                        current.WaitForExit();
                        lock (gate)
                            process = null;
                        if (current.ExitCode != 0)
                        {
                            status = "FPS unavailable: Windows trace access or game support";
                            return;
                        }
                    }
                }
            }
            catch (Exception)
            {
                status = "FPS unavailable: presentation trace could not start";
            }
        }

        public Task StopAsync()
        {
            stopped = true;
            return Task.Run(delegate
            {
                Process owned;
                lock (gate)
                    owned = process;
                if (owned == null)
                    return;
                try
                {
                    using (var stop = new Process
                    {
                        StartInfo = Info("--session_name " + session + " --terminate_existing_session")
                    }

                    )
                    {
                        stop.Start();
                        var output = stop.StandardOutput.ReadToEndAsync();
                        var error = stop.StandardError.ReadToEndAsync();
                        if (!stop.WaitForExit(1500))
                            stop.Kill();
                    }

                    if (!owned.WaitForExit(1500))
                        owned.Kill();
                }
                catch (InvalidOperationException)
                {
                }
                catch (System.ComponentModel.Win32Exception)
                {
                }
            });
        }

        public void Dispose()
        {
            StopAsync();
        }
    }
}
