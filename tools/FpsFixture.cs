using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DeskPill;

class FpsFixture
{
    [STAThread]
    static int Main(string[] args)
    {
        System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
        var app = new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };
        int code = 2;
        PresentMonCapture capture = null;
        var watch = Stopwatch.StartNew();
        var canvas = new Canvas
        {
            Background = Brushes.Black
        };
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 32,
            Height = 32,
            Fill = Brushes.Cyan
        };
        canvas.Children.Add(dot);
        var window = new Window
        {
            Title = "DeskPill FPS verification fixture",
            Width = 420,
            Height = 160,
            Content = canvas,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        EventHandler render = delegate
        {
            Canvas.SetLeft(dot, 170 + Math.Sin(watch.Elapsed.TotalSeconds * 3) * 130);
            Canvas.SetTop(dot, 45);
        };
        window.Loaded += async delegate
        {
            try
            {
                if (!PresentMonAssets.Ready)
                {
                    if (Array.IndexOf(args, "--install") < 0)
                        throw new Exception("Install the FPS component in DeskPill Settings > Gaming first.");
                    await PresentMonAssets.InstallAsync();
                }

                capture = new PresentMonCapture((uint)Process.GetCurrentProcess().Id);
                CompositionTarget.Rendering += render;
                await Task.Delay(9000);
                double? fps = capture.Fps;
                if (fps.HasValue)
                {
                    Console.WriteLine("PASS Measured " + fps.Value.ToString("0.0") + " presented FPS from the isolated fixture.");
                    code = 0;
                }
                else
                    Console.WriteLine("UNAVAILABLE " + capture.Status + ". No FPS number was fabricated.");
            }
            catch (Exception e)
            {
                Console.WriteLine("FAIL " + e.Message);
                code = 1;
            }

            if (capture != null)
                await capture.StopAsync();
            CompositionTarget.Rendering -= render;
            app.Shutdown();
        };
        app.Run(window);
        return code;
    }
}
