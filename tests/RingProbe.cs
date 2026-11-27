using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskPill;

class RingProbe
{
    static int checks;

    static byte[] Capture(Ring ring)
    {
        ring.Measure(new Size(68, 68));
        ring.Arrange(new Rect(0, 0, 68, 68));
        ring.UpdateLayout();
        var bitmap = new RenderTargetBitmap(68, 68, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(ring);
        var pixels = new byte[68 * 68 * 4];
        bitmap.CopyPixels(pixels, 68 * 4, 0);
        return pixels;
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
        Console.WriteLine("PASS " + message);
    }

    [STAThread]
    static int Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Ui.MotionEnabled = false;
        try
        {
            var ring = new Ring { Width = 68, Height = 68, Center = "100%", Progress = 1 };
            byte[] goal = Capture(ring);
            ring.Center = "112%";
            ring.Progress = 1;
            byte[] beyondGoal = Capture(ring);
            Check(!goal.SequenceEqual(beyondGoal), "Water percentage repaints beyond the completed goal");
            ring.Center = "112%";
            ring.Progress = 1;
            Check(beyondGoal.SequenceEqual(Capture(ring)), "An unchanged percentage keeps the same rendered pixels");
            Check(!ring.HasAnimatedProperties, "Immediate ring updates do not leave animation clocks");
            Console.WriteLine("Ring probe: " + checks + " checks passed");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            return 1;
        }
        finally
        {
            app.Shutdown();
        }
    }
}
