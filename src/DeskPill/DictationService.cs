using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DeskPill
{
    public static class DictationAudio
    {
        public const int MaxBytes = 16000 * 2 * 90;
        public static byte[] Wave(byte[] pcm)
        {
            if (pcm == null || pcm.Length > MaxBytes || (pcm.Length & 1) != 0)
                throw new ArgumentException("Invalid dictation audio length.");
            using (var memory = new MemoryStream())
            using (var writer = new BinaryWriter(memory))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + pcm.Length);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(16000);
                writer.Write(32000);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(pcm.Length);
                writer.Write(pcm);
                return memory.ToArray();
            }
        }
    }

    public static class DictationInsertion
    {
        // Keep the result cancellable and visible until SendInput can safely use Ctrl+V.
        public static async Task<bool> WaitForReleaseAsync(Func<bool> held, Func<bool> sourceCurrent, Action waiting, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            bool shown = false;
            while (held())
            {
                token.ThrowIfCancellationRequested();
                if (!sourceCurrent())
                    return false;
                if (!shown)
                {
                    waiting();
                    shown = true;
                }

                await Task.Delay(25, token);
            }

            token.ThrowIfCancellationRequested();
            return sourceCurrent();
        }
    }

    public sealed class DictationShortcutGate
    {
        bool held, recording, spaceDown;
        public bool ToggleRequested { get; private set; }
        public bool FinishRequested { get; private set; }

        public bool Process(int key, bool down, bool control, bool otherModifiers)
        {
            ToggleRequested = FinishRequested = false;
            if (!down && (key == 0x11 || key == 0xA2 || key == 0xA3) && recording)
            {
                recording = false;
                FinishRequested = true;
            }

            if (key != 0x20)
                return false;
            bool repeated = spaceDown && down;
            spaceDown = down;
            if (held)
            {
                if (!down)
                {
                    held = false;
                    FinishRequested = recording;
                    recording = false;
                }

                return true;
            }

            if (!down || repeated || !control || otherModifiers)
                return false;
            held = recording = true;
            ToggleRequested = true;
            return true;
        }
    }

    // Event callbacks keep wave APIs off a driver callback and all audio work off WPF's thread.
    public sealed class DictationRecorder : IDisposable
    {
        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        struct Format
        {
            public ushort format, channels;
            public uint rate, average;
            public ushort align, bits, extra;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Header
        {
            public IntPtr data;
            public uint length, recorded;
            public UIntPtr user;
            public uint flags, loops;
            public IntPtr next;
            public UIntPtr reserved;
        }

        [DllImport("winmm.dll")]
        static extern uint waveInOpen(out IntPtr handle, uint device, ref Format format, IntPtr callback, IntPtr instance, uint flags);
        [DllImport("winmm.dll")]
        static extern uint waveInPrepareHeader(IntPtr handle, IntPtr header, uint size);
        [DllImport("winmm.dll")]
        static extern uint waveInUnprepareHeader(IntPtr handle, IntPtr header, uint size);
        [DllImport("winmm.dll")]
        static extern uint waveInAddBuffer(IntPtr handle, IntPtr header, uint size);
        [DllImport("winmm.dll")]
        static extern uint waveInStart(IntPtr handle);
        [DllImport("winmm.dll")]
        static extern uint waveInReset(IntPtr handle);
        [DllImport("winmm.dll")]
        static extern uint waveInClose(IntPtr handle);
        readonly AutoResetEvent signal = new AutoResetEvent(false);
        readonly List<IntPtr> headers = new List<IntPtr>(), buffers = new List<IntPtr>();
        readonly MemoryStream audio = new MemoryStream();
        readonly object shutdown = new object (), audioGate = new object ();
        readonly uint size = (uint)Marshal.SizeOf(typeof(Header));
        IntPtr device;
        Task pump;
        volatile bool running;
        bool closed;
        volatile Exception fault;
        public bool Failed
        {
            get
            {
                return fault != null;
            }
        }

        volatile float level;
        public double Level
        {
            get
            {
                return level;
            }
        }

        public DictationRecorder()
        {
            try
            {
                var format = new Format
                {
                    format = 1,
                    channels = 1,
                    rate = 16000,
                    average = 32000,
                    align = 2,
                    bits = 16
                };
                Check(waveInOpen(out device, 0xFFFFFFFF, ref format, signal.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, 0x50000));
                for (int i = 0; i < 4; i++)
                {
                    IntPtr data = Marshal.AllocHGlobal(3200), header = Marshal.AllocHGlobal((int)size);
                    buffers.Add(data);
                    headers.Add(header);
                    Marshal.StructureToPtr(new Header { data = data, length = 3200 }, header, false);
                    Check(waveInPrepareHeader(device, header, size));
                    Check(waveInAddBuffer(device, header, size));
                }

                running = true;
                Check(waveInStart(device));
                pump = Task.Run((Action)Pump);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        static void Check(uint code)
        {
            if (code != 0)
                throw new InvalidOperationException("Microphone could not start (Windows audio code " + code + "). Check microphone access and your default input device.");
        }

        void Read(IntPtr pointer, bool requeue)
        {
            var header = (Header)Marshal.PtrToStructure(pointer, typeof(Header));
            if ((header.flags & 1) == 0)
                return;
            int count = Math.Min((int)header.recorded, DictationAudio.MaxBytes - (int)audio.Length);
            if (count > 0)
            {
                var bytes = new byte[count];
                Marshal.Copy(header.data, bytes, 0, count);
                audio.Write(bytes, 0, count);
                int peak = 0;
                for (int i = 0; i + 1 < count; i += 2)
                    peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(bytes, i)));
                level = peak / 32768f;
            }

            header.recorded = 0;
            Marshal.StructureToPtr(header, pointer, false);
            if (requeue)
                Check(waveInAddBuffer(device, pointer, size));
        }

        void Pump()
        {
            try
            {
                while (running)
                {
                    signal.WaitOne(100);
                    if (!running)
                        break;
                    lock (audioGate)
                    {
                        if (running)
                            foreach (var header in headers)
                                Read(header, true);
                    }
                }
            }
            catch (Exception e)
            {
                fault = e;
                running = false;
            }
        }

        public byte[] Finish()
        {
            lock (shutdown)
            {
                Close();
                if (fault != null)
                    throw new InvalidOperationException("Microphone capture failed.", fault);
                return DictationAudio.Wave(audio.ToArray());
            }
        }

        void Close()
        {
            if (closed)
                return;
            closed = true;
            running = false;
            if (device != IntPtr.Zero)
            {
                lock (audioGate)
                    waveInReset(device);
                signal.Set();
                if (pump != null)
                    pump.Wait();
                foreach (var header in headers)
                    Read(header, false);
                foreach (var header in headers)
                    waveInUnprepareHeader(device, header, size);
                waveInClose(device);
                device = IntPtr.Zero;
            }

            foreach (var header in headers)
                Marshal.FreeHGlobal(header);
            foreach (var data in buffers)
                Marshal.FreeHGlobal(data);
            signal.Dispose();
        }

        public void Dispose()
        {
            lock (shutdown)
            {
                Close();
                audio.Dispose();
            }
        }
    }

    public static class SpeechAssets
    {
        public const string RuntimeUrl = "https://github.com/alphacep/vosk-api/releases/download/v0.3.45/vosk-win64-0.3.45.zip";
        public const string ModelUrl = "https://alphacephei.com/vosk/models/vosk-model-small-en-us-0.15.zip";
        public const string RuntimeHash = "F1DCC9CCA460630F81EA8F71794F69C80BED6556D2A4E6237B5785E1D2DFF34B";
        public const string ModelHash = "30F26242C4EB449F948E42CB302DD7A686CB29A3423A8367F99FF41780942498";
        public static string Root
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskPill", "speech", "english-1");
            }
        }

        public static string Runtime
        {
            get
            {
                return Path.Combine(Root, "vosk-win64-0.3.45");
            }
        }

        public static string Model
        {
            get
            {
                return Path.Combine(Root, "vosk-model-small-en-us-0.15");
            }
        }

        public static bool Ready
        {
            get
            {
                return File.Exists(Path.Combine(Runtime, "libvosk.dll")) && File.Exists(Path.Combine(Model, "am", "final.mdl")) && File.Exists(Path.Combine(Root, "verified.txt"));
            }
        }

        public static bool HashMatches(string file, string expected)
        {
            using (var hash = System.Security.Cryptography.SHA256.Create())
            using (var stream = File.OpenRead(file))
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").Equals(expected, StringComparison.OrdinalIgnoreCase);
        }

        public static void Extract(string zip, string target)
        {
            string root = Path.GetFullPath(target) + Path.DirectorySeparatorChar;
            long expanded = 0;
            using (var archive = System.IO.Compression.ZipFile.OpenRead(zip))
                foreach (var entry in archive.Entries)
                {
                    string path = Path.GetFullPath(Path.Combine(root, entry.FullName));
                    expanded += entry.Length;
                    if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || expanded > 600000000)
                        throw new InvalidDataException("Speech archive contains an invalid path or size.");
                    if (entry.Name.Length == 0)
                    {
                        Directory.CreateDirectory(path);
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    using (var input = entry.Open())
                    using (var output = File.Create(path))
                        input.CopyTo(output);
                }
        }

        static readonly SemaphoreSlim installLock = new SemaphoreSlim(1, 1);
        public static async Task InstallAsync(Action<string> progress, CancellationToken cancellation, bool repair = false)
        {
            if (!Environment.Is64BitOperatingSystem)
                throw new InvalidOperationException("Offline dictation requires 64-bit Windows.");
            if (!await installLock.WaitAsync(0))
                throw new InvalidOperationException("Speech installation is already running.");
            string parent = Path.GetDirectoryName(Root), stage = Path.Combine(parent, "install-" + Guid.NewGuid().ToString("N"));
            try
            {
                if (Ready && !repair)
                {
                    progress("English dictation is ready.");
                    return;
                }

                Directory.CreateDirectory(stage);
                foreach (var asset in new[]
                {
                    new[]
                    {
                        "runtime",
                        RuntimeUrl,
                        RuntimeHash
                    },
                    new[]
                    {
                        "English model",
                        ModelUrl,
                        ModelHash
                    }
                }

                )
                {
                    string file = Path.Combine(stage, asset[0] + ".zip");
                    using (var client = new System.Net.WebClient())
                    using (cancellation.Register(client.CancelAsync))
                    {
                        int lastPercent = -1;
                        client.DownloadProgressChanged += delegate (object sender, System.Net.DownloadProgressChangedEventArgs e)
                        {
                            if (lastPercent != e.ProgressPercentage)
                            {
                                lastPercent = e.ProgressPercentage;
                                progress("Downloading " + asset[0] + " · " + e.ProgressPercentage + "%");
                            }
                        };
                        cancellation.ThrowIfCancellationRequested();
                        await client.DownloadFileTaskAsync(new Uri(asset[1]), file);
                    }

                    cancellation.ThrowIfCancellationRequested();
                    progress("Verifying " + asset[0] + "…");
                    await Task.Run(delegate
                    {
                        if (!HashMatches(file, asset[2]))
                            throw new InvalidDataException("Speech download verification failed. Please retry.");
                        Extract(file, stage);
                        File.Delete(file);
                    });
                }

                cancellation.ThrowIfCancellationRequested();
                File.WriteAllText(Path.Combine(stage, "verified.txt"), RuntimeHash + "\n" + ModelHash);
                // Replace only this exact app-owned, versioned asset directory after complete verification.
                if (Path.GetDirectoryName(Path.GetFullPath(Root)) != Path.GetFullPath(parent))
                    throw new IOException("Unexpected speech asset directory.");
                string backup=Root+".old-"+Guid.NewGuid().ToString("N");bool replaced=false;
                try{if(Directory.Exists(Root))Directory.Move(Root,backup);Directory.Move(stage,Root);replaced=true;}
                finally{if(!replaced&&Directory.Exists(backup)&&!Directory.Exists(Root))Directory.Move(backup,Root);}
                if(Directory.Exists(backup)&&Path.GetDirectoryName(Path.GetFullPath(backup))==Path.GetFullPath(parent))Directory.Delete(backup,true);
                progress("English dictation is ready. Enable it and save to use Ctrl+Space.");
            }
            finally
            {
                try{
                    if (Directory.Exists(stage) && Path.GetDirectoryName(Path.GetFullPath(stage)) == Path.GetFullPath(parent) && Path.GetFileName(stage).StartsWith("install-", StringComparison.Ordinal))Directory.Delete(stage, true);
                }finally{installLock.Release();}
            }
        }
    }

    public static class SpeechTranscriber
    {
        public static Task<string> TranscribeAsync(byte[] wav,CancellationToken cancellation){return TranscribeAsync(wav,cancellation,"Vosk");}
        public static Task<string> TranscribeAsync(byte[] wav,CancellationToken cancellation,string speechModel){return TranscribeAsync(wav,cancellation,speechModel,"Auto");}
        public static async Task<string> TranscribeAsync(byte[] wav, CancellationToken cancellation,string speechModel,string speechBackend)
        {
            string backend=SpeechModels.NormalizeBackend(speechBackend);
            bool parakeet=SpeechModels.Normalize(speechModel)=="Parakeet";
            cancellation.ThrowIfCancellationRequested();
            if (!SpeechModels.IsReady(speechModel))
                throw new InvalidOperationException("Install "+SpeechModels.Name(speechModel)+" in Settings > Dictation first.");
            string executable = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DeskPill.Speech.exe");
            if (!File.Exists(executable))
                throw new InvalidOperationException("The speech worker is missing. Extract all files from the DeskPill portable ZIP.");
            using (var process = new Process
            {
                StartInfo = new ProcessStartInfo(executable,parakeet?"--parakeet \""+ParakeetAssets.Runtime+"\" \""+ParakeetAssets.Model+"\" "+backend:"\"" + SpeechAssets.Runtime + "\" \"" + SpeechAssets.Model + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            }

            )
            {
                cancellation.ThrowIfCancellationRequested();
                process.Start();
                var output = process.StandardOutput.ReadToEndAsync();
                var errors = process.StandardError.ReadToEndAsync();
                using (cancellation.Register(delegate
                {
                    try
                    {
                        if (!process.HasExited)
                            process.Kill();
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }))
                {
                    try
                    {
                        await process.StandardInput.BaseStream.WriteAsync(wav, 0, wav.Length, cancellation);
                        process.StandardInput.Close();
                        var exit = Task.Run((Action)process.WaitForExit);
                        if (await Task.WhenAny(exit, Task.Delay(TimeSpan.FromSeconds(parakeet?600:120))) != exit)
                        {
                            process.Kill();
                            await exit;
                            throw new TimeoutException("Transcription took too long. Try a shorter recording.");
                        }

                        await exit;
                        cancellation.ThrowIfCancellationRequested();
                        string text = await output;
                        string detail=await errors;
                        if(process.ExitCode!=0&&parakeet&&backend=="Auto")return await TranscribeAsync(wav,cancellation,speechModel,"CPU");
                        if (process.ExitCode != 0)
                            throw new InvalidOperationException(parakeet?"Parakeet could not finish. "+(detail.Length<600?detail:"Check available memory or repair the speech pack."):"The speech engine could not finish. Try reinstalling the English speech pack.");
                        return text.Trim();
                    }
                    finally
                    {
                        if (!process.HasExited)
                        {
                            process.Kill();
                        }
                    }
                }
            }
        }
    }
}
