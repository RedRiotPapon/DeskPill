using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using System.Reflection;

[assembly: AssemblyTitle("DeskPill")]
[assembly: AssemblyDescription("A living notch for Windows")]
[assembly: AssemblyVersion("2.1.3.0")]
namespace DeskPill
{
    public static class Program
    {
        static Mutex instance;
        [STAThread]
        public static void Main(string[] args)
        {
            bool preview = Array.IndexOf(args, "--preview") >= 0, capture = Array.IndexOf(args, "--capture") >= 0;
            bool first;
            instance = new Mutex(true, "Local\\DeskPill" + (preview || capture ? "Preview" : ""), out first);
            if (!first)
            {
                MessageBox.Show("DeskPill is already running. Open it from its system tray icon.", "DeskPill");
                instance.Dispose();
                return;
            }

            try
            {
                System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
                var app = new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown
                };
                Ui.Init(app);
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskPill");
                var store = new StateStore(directory,preview || capture);
                var state = store.Load();
                if(store.RecoveryBlocked)throw new IOException("Your saved profile could not be read safely. Close other DeskPill instances or resolve file access, then restart. Your original state file has not been replaced.\n\n"+store.LastError);
                var window = new IslandWindow(state, store, preview, capture);
                app.MainWindow = window;
                app.DispatcherUnhandledException += delegate (object sender, DispatcherUnhandledExceptionEventArgs e)
                {
                    window.ReportError(e.Exception);
                    e.Handled = true;
                };
                app.Run(window);
            }
            catch (Exception ex)
            {
                MessageBox.Show("DeskPill couldn't start.\n\n" + ex.Message, "DeskPill", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                try
                {
                    instance.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                }

                instance.Dispose();
            }
        }
    }
}
