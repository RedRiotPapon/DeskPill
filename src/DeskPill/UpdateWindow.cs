using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DeskPill
{
    public sealed class UpdateWindow : Window
    {
        static UpdateWindow current;
        readonly TextBlock status,detail;
        readonly ProgressBar progress;
        readonly Button check,cancel,folder;
        CancellationTokenSource operation;
        string download;
        public static void Open()
        {
            if(current!=null){current.Activate();return;}
            current=new UpdateWindow();current.Closed+=delegate{current=null;};current.Show();
        }
        public UpdateWindow()
        {
            Title="DeskPill updates";Width=500;Height=330;MinWidth=440;MinHeight=300;
            WindowStartupLocation=WindowStartupLocation.CenterScreen;Background=Ui.Hex("#090909");Foreground=Ui.White;FontFamily=new FontFamily("Segoe UI");
            var root=new Grid {Margin=new Thickness(26)};root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
            var heading=Ui.Column(Ui.Text("DeskPill updates",24,Ui.White),Ui.Text("Installed version "+Assembly.GetExecutingAssembly().GetName().Version.ToString(3),12,Ui.Muted));root.Children.Add(heading);
            var body=new StackPanel {Margin=new Thickness(0,24,0,18)};Grid.SetRow(body,1);root.Children.Add(body);
            status=Ui.Text("Ready to check",16,Ui.White);status.TextWrapping=TextWrapping.Wrap;status.TextTrimming=TextTrimming.None;body.Children.Add(status);
            progress=new ProgressBar {Minimum=0,Maximum=100,Height=4,Foreground=Ui.Green,Background=Ui.Card,Margin=new Thickness(0,14,0,12),Visibility=Visibility.Collapsed};body.Children.Add(progress);
            detail=Ui.Text("Checks only when you ask. New versions download here without interrupting the notch.",12,Ui.Muted);detail.TextWrapping=TextWrapping.Wrap;detail.TextTrimming=TextTrimming.None;body.Children.Add(detail);
            var actions=new StackPanel {Orientation=Orientation.Horizontal};Grid.SetRow(actions,2);root.Children.Add(actions);
            check=Ui.Button("Check again",Run,true);actions.Children.Add(check);
            cancel=Ui.Button("Cancel download",delegate{if(operation!=null)operation.Cancel();});cancel.Visibility=Visibility.Collapsed;actions.Children.Add(cancel);
            folder=Ui.Button("Open download folder",delegate{try{Process.Start(new ProcessStartInfo("explorer.exe","/select,\""+download+"\""){UseShellExecute=true});}catch(Exception ex){detail.Text="Couldn't open the folder: "+ex.Message;}});folder.Visibility=Visibility.Collapsed;actions.Children.Add(folder);
            actions.Children.Add(Ui.Button("Release notes",delegate{try{Process.Start(new ProcessStartInfo("https://github.com/RedRiotPapon/DeskPill/releases/latest"){UseShellExecute=true});}catch(Exception ex){detail.Text=ex.Message;}}));
            Content=root;
            Loaded+=delegate{Run();};Closed+=delegate{if(operation!=null)operation.Cancel();};
        }
        async void Run()
        {
            if(operation!=null)return;
            var cancellation=new CancellationTokenSource();operation=cancellation;check.Visibility=Visibility.Collapsed;cancel.Visibility=Visibility.Visible;folder.Visibility=Visibility.Collapsed;
            progress.Visibility=Visibility.Visible;progress.IsIndeterminate=true;status.Text="Checking GitHub…";detail.Text="DeskPill stays available while this runs.";
            try{
                using(var service=new UpdateService()){
                    var release=await service.CheckAsync(Assembly.GetExecutingAssembly().GetName().Version,cancellation.Token);
                    if(release==null){status.Text="You’re up to date.";detail.Text="You have the latest stable version. No download needed.";return;}
                    status.Text="Downloading DeskPill "+release.Version.ToString(3)+"…";detail.Text="The portable ZIP will be verified before it is saved.";progress.IsIndeterminate=false;progress.Value=0;
                    string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DeskPill","updates");
                    download=await service.DownloadAsync(release,directory,percent=>Dispatcher.BeginInvoke(new Action(delegate{progress.Value=percent;})),cancellation.Token);
                    status.Text="DeskPill "+release.Version.ToString(3)+" is ready.";
                    detail.Text="Open the folder, extract the ZIP and keep both executables together. Exit the current DeskPill before launching the new version. Your settings stay saved.";
                    folder.Visibility=Visibility.Visible;check.Visibility=Visibility.Collapsed;
                }
            }catch(OperationCanceledException){status.Text=cancellation.IsCancellationRequested?"Download cancelled.":"The connection timed out.";detail.Text="You can check again whenever you’re ready.";}
            catch(Exception ex){status.Text="Couldn’t complete the update.";detail.Text=ex is System.Net.Http.HttpRequestException?"GitHub may be unavailable or limiting requests. Try again later or open Release notes.":ex.Message;}
            finally{
                operation=null;cancellation.Dispose();progress.Visibility=Visibility.Collapsed;cancel.Visibility=Visibility.Collapsed;
                if(folder.Visibility!=Visibility.Visible)check.Visibility=Visibility.Visible;
            }
        }
    }
}
