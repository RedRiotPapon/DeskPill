using System;
using System.Drawing;
using System.IO;
using DeskPill;

internal static class AgentVisionTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    [STAThread]
    static int Main()
    {
        try
        {
            Check(!AgentScreenTarget.IsUsableIdentity(IntPtr.Zero, 20, "Editor", new Rectangle(0, 0, 100, 100)), "Missing handle rejected");
            Check(!AgentScreenTarget.IsUsableIdentity(new IntPtr(2), 0, "Editor", new Rectangle(0, 0, 100, 100)), "Missing process rejected");
            Check(!AgentScreenTarget.IsUsableIdentity(new IntPtr(2), 20, " ", new Rectangle(0, 0, 100, 100)), "Missing title rejected");
            Check(!AgentScreenTarget.IsUsableIdentity(new IntPtr(2), 20, "Editor", new Rectangle(0, 0, 0, 100)), "Empty bounds rejected");
            Check(AgentScreenTarget.IsUsableIdentity(new IntPtr(2), 20, "Editor", new Rectangle(-3840, -200, 3840, 2160)), "Negative physical screen coordinates valid");
            Check(AgentVision.BoundedSize(7680, 2160) == new Size(2048, 576), "Wide capture bounded proportionally");
            Check(AgentVision.BoundedSize(2160, 7680) == new Size(576, 2048), "Tall capture bounded proportionally");
            Check(AgentVision.BoundedSize(100, 80) == new Size(100, 80), "Small capture unchanged");
            var desktop = new Rectangle(-3840, -400, 6400, 2560);
            Check(AgentCapturePicker.SelectionBounds(100, 100, -3000, -200, desktop) == new Rectangle(-3000, -200, 3100, 300), "Reverse drag keeps physical coordinates across DPI boundary");
            Check(AgentCapturePicker.SelectionBounds(-5000, -600, 4000, 3000, desktop) == desktop, "Area clipped to physical virtual desktop");
            string original = null, copy = null;
            using (var bitmap = new Bitmap(200, 200))
            {
                using (var g = Graphics.FromImage(bitmap)) g.Clear(Color.Black);
                Check(AgentVision.IsBlack(bitmap), "Black capture rejected");
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.Black);
                    g.FillRectangle(Brushes.White, 17, 19, 4, 12);
                }
                Check(!AgentVision.IsBlack(bitmap), "Thin light stroke on black background accepted");
                using (var g = Graphics.FromImage(bitmap)) g.Clear(Color.CornflowerBlue);
                Check(!AgentVision.IsBlack(bitmap), "Visible content accepted");
                AgentAttachment attachment = AgentVision.Save(bitmap, "Synthetic test image");
                try
                {
                    original = attachment.FilePath;
                    Check(File.Exists(original) && Path.GetExtension(original) == ".png", "PNG exists during draft ownership");
                    Check(attachment.Preview.IsFrozen && attachment.Preview.PixelWidth == 200, "Preview frozen and loaded independently");
                    Check(attachment.SourceLabel == "Synthetic test image" && attachment.CapturedUtc.Kind == DateTimeKind.Utc, "Review metadata retained");
                    using (var owned = attachment.CopyForRun())
                    {
                        copy = owned.FilePath;
                        Check(copy != original && File.Exists(copy), "Run gets independent file");
                        attachment.Dispose();
                        Check(!File.Exists(original) && File.Exists(copy), "Removing draft preserves owned run image");
                        using (var reopened = new Bitmap(copy)) Check(reopened.Width == 200 && reopened.Height == 200, "Run copy is valid PNG");
                        bool rejected = false;
                        try { attachment.CopyForRun(); } catch (ObjectDisposedException) { rejected = true; }
                        Check(rejected, "Disposed draft cannot create another copy");
                    }
                    Check(!File.Exists(copy), "Terminal run disposal deletes copied PNG");
                }
                finally { attachment.Dispose(); }
                Check(!File.Exists(original), "Draft disposal is idempotent");
            }
            using (var large = new Bitmap(4096, 100))
            using (var bounded = AgentVision.Save(large, "Synthetic bounded image"))
                Check(bounded.Preview.PixelWidth == 2048 && bounded.Preview.PixelHeight == 50, "Persisted PNG bounded");
            Console.WriteLine("Agent vision: " + checks + " synthetic checks passed; no screen capture performed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
