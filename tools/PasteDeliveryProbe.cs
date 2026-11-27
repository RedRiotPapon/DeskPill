using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DeskPill;

class PasteDeliveryProbe
{
    [STAThread]
    static int Main(string[] args)
    {
        bool receiver = args.Length > 0 && args[0] == "--receiver";
        string file = receiver ? args[1] : Path.GetFullPath("artifacts/paste-delivery-" + Guid.NewGuid().ToString("N") + ".txt");
        int result = 1;
        var app = new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };
        var input = new TextBox
        {
            Text = "Controlled test window. Paste is intercepted before any clipboard access.",
            Margin = new Thickness(20),
            TextWrapping = TextWrapping.Wrap
        };
        var window = new Window
        {
            Title = receiver ? "DeskPill paste receiver" : "DeskPill paste delivery probe",
            Width = 420,
            Height = 180,
            Content = input,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        if (receiver)
        {
            window.PreviewKeyDown += async delegate (object sender, KeyEventArgs e)
            {
                if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
                {
                    e.Handled = true;
                    File.WriteAllText(file, "paste-chord-delivered");
                    await Task.Delay(200);
                    app.Shutdown();
                }
            };
            window.Loaded += delegate
            {
                input.Focus();
            };
        }
        else
        {
            window.Loaded += async delegate
            {
                Process child = null;
                try
                {
                    child = Process.Start(new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location, "--receiver \"" + file + "\"") { UseShellExecute = false, CreateNoWindow = true });
                    PasteTarget target = null;
                    for (int i = 0; i < 30; i++)
                    {
                        await Task.Delay(100);
                        target = PasteTarget.Capture();
                        if (target != null && target.ProcessId == child.Id)
                            break;
                    }

                    if (target == null || target.ProcessId != child.Id)
                        throw new Exception("Receiver could not obtain foreground focus.");
                    window.Activate();
                    await Task.Delay(150);
                    if (!await target.TryPasteAsync())
                        throw new Exception("Paste target guard rejected the controlled receiver.");
                    for (int i = 0; i < 20 && !File.Exists(file); i++)
                        await Task.Delay(100);
                    if (!File.Exists(file) || File.ReadAllText(file) != "paste-chord-delivered")
                        throw new Exception("Ctrl+V did not reach the receiver.");
                    Console.WriteLine("PASS Controlled native target received Ctrl+V; clipboard contents were never read or changed.");
                    result = 0;
                }
                catch (Exception error)
                {
                    Console.WriteLine("FAIL " + error.Message);
                }
                finally
                {
                    if (child != null && !child.HasExited)
                        child.CloseMainWindow();
                    if (File.Exists(file))
                        File.Delete(file);
                    app.Shutdown();
                }
            };
        }

        app.Run(window);
        return receiver ? 0 : result;
    }
}
