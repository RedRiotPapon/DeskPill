using System;
using System.IO;
using DeskPill;

class DictationTests
{
    static int checks;
    static void Check(bool value, string message)
    {
        if (!value)
            throw new Exception(message);
        checks++;
        Console.WriteLine("PASS " + message);
    }

    static bool Reject(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    static void TarFixture(string path,string name,byte kind,int declared,byte[] body)
    {
        byte[] header=new byte[512];var nameBytes=System.Text.Encoding.ASCII.GetBytes(name);Buffer.BlockCopy(nameBytes,0,header,0,nameBytes.Length);var size=System.Text.Encoding.ASCII.GetBytes(Convert.ToString(declared,8).PadLeft(11,'0')+"\0");Buffer.BlockCopy(size,0,header,124,size.Length);header[156]=kind;
        using(var file=File.Create(path))using(var zip=new System.IO.Compression.GZipStream(file,System.IO.Compression.CompressionMode.Compress)){zip.Write(header,0,512);zip.Write(body,0,body.Length);if(body.Length==declared){var end=new byte[(512-body.Length%512)%512+1024];zip.Write(end,0,end.Length);}}
    }
    static bool RejectArchive(string archive,string directory){try{ParakeetAssets.ExtractRuntime(archive,directory,System.Threading.CancellationToken.None);return false;}catch(InvalidDataException){return true;}}

    static int Main()
    {
        try
        {
            // A handled agent start must never fall through to the external paste path.
            var routing = typeof(DictationAudio).Assembly.GetType("DeskPill.DictationRouting");
            Check(routing != null, "Shortcut routing supports the open agent draft");
            var beginRoute = routing.GetMethod("Begin");
            var endRoute = routing.GetMethod("End");
            int normalStarts = 0, agentStops = 0, normalStops = 0;
            beginRoute.Invoke(null, new object[] { new Func<bool>(() => true), new Action(() => normalStarts++) });
            Check(normalStarts == 0, "Agent draft dictation never starts ordinary paste capture");
            beginRoute.Invoke(null, new object[] { new Func<bool>(() => false), new Action(() => normalStarts++) });
            Check(normalStarts == 1, "Outside agent view the ordinary shortcut still starts");
            endRoute.Invoke(null, new object[] { true, new Action(() => agentStops++), new Action(() => normalStops++) });
            Check(agentStops == 1 && normalStops == 0, "Release and cancellation route only to active agent voice");
            endRoute.Invoke(null, new object[] { false, new Action(() => agentStops++), new Action(() => normalStops++) });
            Check(agentStops == 1 && normalStops == 1, "Ordinary dictation retains its release and cancellation path");
            Check(SpeechModels.NormalizeBackend("bad")=="Auto"&&SpeechModels.NormalizeBackend("GPU")=="GPU","Device choices preserve explicit GPU and repair invalid values");
            Check(new Settings().SpeechModel=="Vosk"&&SpeechModels.Normalize("unknown")=="Vosk","Lightweight speech remains default and repairs unknown selections");
            Check(SpeechModels.Name("Parakeet")=="Parakeet Unified English"&&SpeechModels.IsReady("Vosk")==SpeechAssets.Ready,"Model catalog routes readiness independently");
            using(var cancelPack=new System.Threading.CancellationTokenSource()){
                cancelPack.Cancel();bool cancelledPack=false;try{ParakeetAssets.InstallAsync(delegate{},cancelPack.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){cancelledPack=true;}Check(cancelledPack,"Cancelled Parakeet installation starts no download");
            }
            string packs=Path.Combine(Path.GetTempPath(),"DeskPill-SpeechPacks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(packs);
            try{
                string archive=Path.Combine(packs,"runtime.tar.gz"),destination=Path.Combine(packs,"valid");
                TarFixture(archive,"runtime/probe.txt",(byte)'0',4,new byte[]{1,2,3,4});ParakeetAssets.ExtractRuntime(archive,destination,System.Threading.CancellationToken.None);Check(File.ReadAllBytes(Path.Combine(destination,"runtime","probe.txt")).Length==4,"Parakeet runtime extracts bounded regular files");
                TarFixture(archive,"../escape",(byte)'0',0,new byte[0]);Check(RejectArchive(archive,Path.Combine(packs,"unsafe")),"Parakeet archive rejects traversal");
                TarFixture(archive,"link",(byte)'2',0,new byte[0]);Check(RejectArchive(archive,Path.Combine(packs,"link")),"Parakeet archive rejects links");
                TarFixture(archive,"huge",(byte)'0',80000001,new byte[0]);Check(RejectArchive(archive,Path.Combine(packs,"large")),"Parakeet archive rejects oversized expansion");
                TarFixture(archive,"short",(byte)'0',10,new byte[]{1});Check(RejectArchive(archive,Path.Combine(packs,"short")),"Parakeet archive rejects truncated data");
            }finally{string resolved=Path.GetFullPath(packs),temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;if(!resolved.StartsWith(temp,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(resolved).StartsWith("DeskPill-SpeechPacks-"))throw new IOException("Unexpected test cleanup path");Directory.Delete(resolved,true);}
            var data = new byte[]
            {
                1,
                2,
                3,
                4
            };
            var wave = DictationAudio.Wave(data);
            Check(wave.Length == 48 && System.Text.Encoding.ASCII.GetString(wave, 0, 4) == "RIFF" && BitConverter.ToInt32(wave, 24) == 16000 && wave[44] == 1 && wave[47] == 4, "PCM is encoded as bounded mono 16 kHz WAV");
            Check(Reject(() => DictationAudio.Wave(null)) && Reject(() => DictationAudio.Wave(new byte[3])) && Reject(() => DictationAudio.Wave(new byte[DictationAudio.MaxBytes + 2])), "Malformed and oversized audio is rejected");
            var backwards = new DictationShortcutGate();
            backwards.Process(0x20, true, false, false);
            Check(!backwards.Process(0x20, true, true, false) && !backwards.ToggleRequested, "Space held before Ctrl never starts recording on autorepeat");
            Check(!backwards.Process(0x20, false, true, false), "A plain Space press retains its matching release");
            var gate = new DictationShortcutGate();
            Check(!gate.Process(0x20, true, false, false) && !gate.Process(0x20, false, false, false), "Plain Space stays untouched");
            Check(gate.Process(0x20, true, true, false) && gate.ToggleRequested, "Ctrl+Space begins hold-to-talk");
            Check(gate.Process(0x20, true, true, false) && !gate.ToggleRequested, "Held Space never restarts capture");
            gate.Process(0xA2, false, false, false);
            var finish = typeof(DictationShortcutGate).GetProperty("FinishRequested");
            Check(finish != null && (bool)finish.GetValue(gate, null), "Releasing Ctrl first finishes recording");
            Check(gate.Process(0x20, true, false, false) && !gate.ToggleRequested, "Space autorepeat after Ctrl release stays swallowed");
            Check(gate.Process(0x20, false, false, false) && !(bool)finish.GetValue(gate, null), "Trailing Space release is swallowed without a second finish");
            Check(!gate.Process(0x20, true, true, true) && !gate.Process(0x20, false, true, true), "Extra-modifier Ctrl shortcuts stay untouched");
            Check(gate.Process(0x20, true, true, false) && gate.ToggleRequested, "A fresh chord begins again");
            Check(gate.Process(0x20, false, true, false) && (bool)finish.GetValue(gate, null), "Releasing Space first finishes recording");
            gate.Process(0xA2, false, false, false);
            Check(!(bool)finish.GetValue(gate, null), "Releasing Ctrl after Space never finishes twice");
            foreach (int controlKey in new[]{0x11,0xA2,0xA3})
            {
                var controlGate = new DictationShortcutGate();
                Check(controlGate.Process(0x20,true,true,false) && controlGate.ToggleRequested, "Ctrl variant starts recording: " + controlKey);
                controlGate.Process(controlKey,false,false,false);
                Check(controlGate.FinishRequested, "Ctrl variant release finishes: " + controlKey);
                Check(controlGate.Process(0x20,false,false,false) && !controlGate.FinishRequested, "Ctrl variant finishes only once: " + controlKey);
            }
            var windowsGate = new DictationShortcutGate();
            windowsGate.Process(0x20,true,true,false);
            windowsGate.Process(0x5B,false,true,true);
            Check(!windowsGate.FinishRequested, "Windows key release does not finish Ctrl dictation");
            var insertion = typeof(DictationAudio).Assembly.GetType("DeskPill.DictationInsertion");
            Check(insertion != null, "Dictation retains the transcript until shortcut modifiers are released");
            var release = insertion.GetMethod("WaitForReleaseAsync");
            int polls = 0;
            bool waiting = false;
            Func<bool> held = () => ++polls < 3;
            Func<bool> foreground = () => true;
            var wait = (System.Threading.Tasks.Task<bool>)release.Invoke(null, new object[] { held, foreground, new Action(() => waiting = true), System.Threading.CancellationToken.None });
            Check(wait.GetAwaiter().GetResult() && waiting && polls == 3, "Insertion waits for released Ctrl before it can paste");
            wait = (System.Threading.Tasks.Task<bool>)release.Invoke(null, new object[] { new Func<bool>(() => true), new Func<bool>(() => false), new Action(() =>
            {
            }), System.Threading.CancellationToken.None });
            Check(!wait.GetAwaiter().GetResult(), "Changing foreground while Ctrl is held switches to review");
            var abort = new System.Threading.CancellationTokenSource();
            wait = (System.Threading.Tasks.Task<bool>)release.Invoke(null, new object[] { new Func<bool>(() => true), foreground, new Action(() => abort.Cancel()), abort.Token });
            bool aborted = false;
            try
            {
                wait.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                aborted = true;
            }

            Check(aborted, "Esc can cancel while waiting for modifier release");
            Check(!new Settings().DictationEnabled, "Dictation stays off by default");
            var cancelled = new System.Threading.CancellationTokenSource();
            cancelled.Cancel();
            bool stopped = false;
            try
            {
                SpeechTranscriber.TranscribeAsync(new byte[0], cancelled.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                stopped = true;
            }

            Check(stopped, "Cancellation prevents speech worker launch");
            string file = Path.GetTempFileName();
            try
            {
                File.WriteAllText(file, "fixture");
                Check(!SpeechAssets.HashMatches(file, new string ('0', 64)), "Speech downloads reject a wrong checksum");
            }
            finally
            {
                File.Delete(file);
            }

            Console.WriteLine("Dictation tests: " + checks + " passed");
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine("FAIL " + e.Message);
            return 1;
        }
    }
}
