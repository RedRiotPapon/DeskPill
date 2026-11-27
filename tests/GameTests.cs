using System;
using DeskPill;

class GameTests
{
    static int checks;
    static void Check(bool value, string message)
    {
        if (!value)
            throw new Exception(message);
        checks++;
        Console.WriteLine("PASS " + message);
    }

    static int Main()
    {
        try
        {
            var settings = new Settings();
            Check(GamePolicy.Matches(settings, "game.exe", true, true, false), "Game detection remains active for automatic tuck with overlay disabled");
            settings.GameOverlay = true;
            Check(GamePolicy.Matches(settings, "game.exe", true, true, false), "Fullscreen graphics app can use automatic game mode");
            Check(!GamePolicy.Matches(settings, "chrome.exe", true, true, false) && !GamePolicy.Matches(settings, "game.exe", true, true, true), "Browsers and desktop window classes are excluded");
            Check(!GamePolicy.Matches(settings, "notes.exe", false, true, false) && !GamePolicy.Matches(settings, "notes.exe", true, false, false), "Ordinary windows are not treated as games");
            settings.DetectFullscreenGames = false;
            settings.GameApps = new[]
            {
                "MYGAME.EXE"
            };
            Check(GamePolicy.Matches(settings, "mygame.exe", false, false, false), "Explicit games match windowed play without graphics inspection");
            Check(!GamePolicy.Matches(settings, "other.exe", true, true, false), "Disabling detection keeps only selected games");
            var now = DateTime.UtcNow;
            var samples = new FrameRateSamples(42);
            samples.ReadLine("Application,ProcessID,SwapChainAddress,MsBetweenPresents", now);
            Check(!samples.Fps(now).HasValue, "FPS is unavailable until real samples arrive");
            for (int i = 0; i < 6; i++)
                samples.ReadLine("\"demo, game.exe\",42,0x1,16.6666667", now);
            Check(Math.Abs(samples.Fps(now).Value - 60) < .01, "Quoted CSV produces 60 FPS from 16.67 ms presents");
            for (int i = 0; i < 3; i++)
                samples.ReadLine("game,42,0x2,5", now);
            Check(Math.Abs(samples.Fps(now).Value - 60) < .01, "Separate swap chains never add their frame rates");
            for (int i = 0; i < 20; i++)
                samples.ReadLine("other,99,0x3,1", now);
            Check(Math.Abs(samples.Fps(now).Value - 60) < .01, "Samples from another process are ignored");
            samples.ReadLine("game,42,0x1,NaN", now);
            samples.ReadLine("game,42,0x1,0", now);
            samples.ReadLine("game,42,0x1,Infinity", now);
            Check(Math.Abs(samples.Fps(now).Value - 60) < .01, "Invalid durations do not invent FPS values");
            Check(!samples.Fps(now.AddSeconds(2)).HasValue, "Stale game frames become unavailable");
            Check(FrameRateSamples.Csv("\"a\"\"b,c\",x")[0] == "a\"b,c", "CSV escaped quotes remain balanced");
            Console.WriteLine("Game tests: " + checks + " passed");
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine("FAIL " + e.Message);
            return 1;
        }
    }
}
