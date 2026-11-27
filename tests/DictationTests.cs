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

    static int Main()
    {
        try
        {
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
            var gate = new DictationShortcutGate();
            Check(!gate.Process(0x20, true, false, false), "Plain Space stays untouched");
            Check(gate.Process(0x20, true, true, false) && gate.ToggleRequested, "Win+Space toggles dictation");
            Check(gate.Process(0x20, true, true, false) && !gate.ToggleRequested, "Held Space never repeats the toggle");
            Check(gate.Process(0x20, false, false, false), "Space release is balanced even after Win release");
            Check(!gate.Process(0x20, true, true, true), "Modified Windows shortcuts stay untouched");
            Check(gate.Process(0x20, true, true, false) && gate.ToggleRequested, "A fresh chord toggles again");
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
