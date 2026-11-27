using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DeskPill;

class MediaTests
{
    static int checks;
    static void Check(bool ok, string message)
    {
        if (!ok)
            throw new Exception(message);
        checks++;
        Console.WriteLine("PASS " + message);
    }

    static int Main()
    {
        try
        {
            var policy = typeof(MediaSnapshot).Assembly.GetType("DeskPill.MediaSourcePolicy");
            Check(policy != null, "Media source matching is available");
            Func<string, string> exe = s => (string)policy.GetMethod("Executable").Invoke(null, new object[] { s });
            Check(exe("chrome.exe") == "chrome" && exe("Chrome.Profile.1") == "chrome", "Chrome media IDs resolve only to Chrome");
            Check(exe("MicrosoftEdge") == "msedge" && exe("Spotify.exe") == "Spotify", "Edge and desktop player IDs resolve");
            foreach (string browser in new[]
            {
                "Brave",
                "Vivaldi",
                "Opera",
                "Firefox",
                "Chromium"
            }

            )
                Check(exe(browser) == browser.ToLowerInvariant() && exe(browser.ToLowerInvariant() + ".exe").Equals(browser, StringComparison.OrdinalIgnoreCase), browser + " supports browser and executable source IDs");
            Check(exe("Mozilla.Firefox.Profile") == "firefox" && exe("OperaGXStable") == "opera", "Firefox profiles and Opera GX map to their browser processes");
            Check(exe("C:\\unsafe.exe") == "" && exe("bad.exe --arg") == "" && exe("../bad.exe") == "", "Source metadata cannot become a command line or path");
            var song = new MediaSnapshot
            {
                Title = "A Lovely Song",
                Artist = "Test Artist"
            };
            Func<string[], int> best = names => (int)policy.GetMethod("BestTab").Invoke(null, new object[] { song, names });
            Check(best(new[] { "Inbox", "Test Artist — A Lovely Song - YouTube" }) == 1, "Browser tab matching uses track metadata");
            Check(best(new[] { "a lovely song - YouTube", "A LOVELY SONG – Test Artist" }) == 1, "Artist distinguishes otherwise matching tabs");
            Check(best(new[] { "A Lovely Song - YouTube", "A Lovely Song - YouTube" }) == -1, "Ambiguous tabs never select an arbitrary result");
            Check(best(new[] { "A Lovely Songbook", "A Lovely" }) == -1, "Partial words and incomplete titles are not matches");
            song.Title = "Hi";
            song.Artist = "";
            Check(best(new[] { "History", "Hi" }) == 1, "Short track names require an exact tab title");
            song.Artist = "Example artist";
            Check(best(new[] { "Hi — Example artist - YouTube", "History" }) == 0, "Short song titles can match with a full artist name");
            song.Artist = "";
            song.Title = "তুমি";
            Check(best(new[] { "তোমা", "তুমি" }) == 1, "Bengali vowel marks remain part of the track title");
            song.Title = "";
            Check(best(new[] { "YouTube" }) == -1, "Missing media titles never pick a browser tab");
            var writerType = typeof(MediaSnapshot).Assembly.GetType("DeskPill.LatestVolumeWriter");
            Check(writerType != null, "Audio writes can leave the UI thread");
            Check(writerType.GetMethod("Invoke") != null, "Mute operations share the ordered audio queue");
            var open = typeof(NotificationLauncher).GetMethod("OpenAsync", new[] { typeof(DesktopNotification), typeof(CancellationToken) });
            Check(open != null, "Source app activation supports cancellation");
            var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var skipped = (Task<bool>)open.Invoke(null, new object[] { new DesktopNotification { AppId = "" }, cancelled.Token });
            Check(!skipped.GetAwaiter().GetResult(), "Cancelled source activation never starts Shell work");
            var entered = new ManualResetEventSlim();
            var release = new ManualResetEventSlim();
            var values = new List<float>();
            Action<float> apply = v =>
            {
                lock (values)
                    values.Add(v);
                entered.Set();
                release.Wait(2000);
            };
            var writer = Activator.CreateInstance(writerType, new object[] { apply });
            var queue = writerType.GetMethod("Set");
            queue.Invoke(writer, new object[] { .1f });
            Check(entered.Wait(1000), "Volume worker applies values in the background");
            for (int i = 0; i <= 100; i++)
                queue.Invoke(writer, new object[] { i / 100f });
            ((IDisposable)writer).Dispose();
            release.Set();
            var completion = (Task)writerType.GetProperty("Completion").GetValue(writer, null);
            Check(completion.Wait(2000), "Closing drains the last requested volume");
            lock (values)
            {
                Check(values.Count <= 3 && Math.Abs(values[values.Count - 1] - 1) < .001, "Rapid drag writes are coalesced without losing the final value");
            }

            int toggles = 0, active = 0, maxActive = 0;
            Action<float> noVolume = delegate
            {
            };
            var ordered = Activator.CreateInstance(writerType, new object[] { noVolume });
            Action toggle = () =>
            {
                int count = Interlocked.Increment(ref active);
                maxActive = Math.Max(maxActive, count);
                Thread.Sleep(10);
                Interlocked.Increment(ref toggles);
                Interlocked.Decrement(ref active);
            };
            var invoke = writerType.GetMethod("Invoke");
            invoke.Invoke(ordered, new object[] { toggle });
            invoke.Invoke(ordered, new object[] { toggle });
            ((Task)writerType.GetProperty("Completion").GetValue(ordered, null)).GetAwaiter().GetResult();
            Check(toggles == 2 && maxActive == 1, "Rapid mute clicks execute twice in order without racing");
            ((IDisposable)ordered).Dispose();
            Console.WriteLine("Media tests: " + checks + " passed");
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine("FAIL " + (e.InnerException ?? e).Message);
            return 1;
        }
    }
}
