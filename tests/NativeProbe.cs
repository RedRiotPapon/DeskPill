using System;
using System.Drawing;
using System.Threading.Tasks;
using DeskPill;

class NativeProbe
{
    static void Check(bool ok, string name)
    {
        if (!ok)
            throw new Exception(name);
        Console.WriteLine("PASS " + name);
    }

    static void Main()
    {
        Run().GetAwaiter().GetResult();
    }

    static async Task Run()
    {
        Check(!new MediaSnapshot().HasMedia, "empty snapshot");
        var media = new MediaService();
        await Task.WhenAll(media.RefreshAsync(), media.RefreshAsync());
        Check(media.Current != null, "concurrent media refresh");
        Check(media.Current.Artwork == null || media.Current.Artwork.IsFrozen, "artwork frozen");
        float volume = SystemService.GetVolume();
        Check(volume == -1 || (volume >= 0 && volume <= 1), "volume read range");
        int battery = SystemService.BatteryPercent();
        Check(battery >= -1 && battery <= 100, "battery read range");
        int brightness = SystemService.GetBrightness();
        Check(brightness >= -1 && brightness <= 100, "brightness read range");
        NativeMethods.IsForegroundFullscreen(IntPtr.Zero);
        Task<string> recognition;
        using (var bitmap = new Bitmap(900, 160))
        using (var graphics = Graphics.FromImage(bitmap))
        using (var font = new Font("Segoe UI", 42))
        {
            graphics.Clear(Color.White);
            graphics.DrawString("DeskPill OCR 2026", font, Brushes.Black, 15, 30);
            recognition = OcrService.RecognizeAsync(bitmap);
        }

        string result = await recognition;
        Check(result.IndexOf("2026", StringComparison.Ordinal) >= 0, "real OCR fixture survives caller bitmap disposal");
        Console.WriteLine("Native read-only probes completed; no playback, volume, brightness, hotkey or clipboard mutations.");
    }
}
