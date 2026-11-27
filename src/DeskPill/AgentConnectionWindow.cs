using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace DeskPill
{
    sealed class AgentConnectionWindow : Window
    {
        readonly ComboBox provider;
        readonly TextBox name, executable, workspace;
        readonly CheckBox edits;
        readonly Button detect;
        readonly TextBlock description, error;
        readonly AgentProfile original;
        string previousProvider="";
        public AgentProfile Result { get; private set; }
        public Func<string> SaveError;
        public AgentConnectionWindow(AgentProfile existing)
        {
            original=existing;
            Title=existing==null?"Connect an agent · DeskPill":"Edit connection · DeskPill";
            Width=530;Height=640;MinWidth=450;MinHeight=540;
            WindowStartupLocation=WindowStartupLocation.CenterScreen;
            Background=Ui.Hex("#000000");Foreground=Ui.White;
            var body=new StackPanel{Margin=new Thickness(24,20,24,20)};
            var title=Ui.Text(existing==null?"Bring your agent closer":"Your connection",23,Ui.White);title.FontWeight=FontWeights.SemiBold;body.Children.Add(title);
            description=Ui.Text("",12,Ui.Muted);description.TextWrapping=TextWrapping.Wrap;description.Margin=new Thickness(0,8,0,12);body.Children.Add(description);
            provider=AgentControls.Picker();provider.ItemsSource=new[]{"Codex CLI","Claude Code","Gemini CLI","App shortcut"};provider.SelectedItem=existing==null?"Codex CLI":existing.Provider;provider.Height=32;
            Add(body,"Connection type",provider);
            name=Ui.Input(existing==null?"":existing.Name);Add(body,"Name",name);
            executable=Ui.Input(existing==null?"":existing.Executable);
            var exeRow=new DockPanel();var browse=Ui.Button("Browse…",delegate{
                var picker=new OpenFileDialog{Filter="Native application or official npm CLI (*.exe;*.cmd)|*.exe;*.cmd",CheckFileExists=true};if(picker.ShowDialog(this)==true)executable.Text=picker.FileName;
            });DockPanel.SetDock(browse,Dock.Right);exeRow.Children.Add(browse);exeRow.Children.Add(executable);Add(body,"Application",exeRow);
            detect=Ui.Button("Find installed CLI",async delegate{
                string selected=Convert.ToString(provider.SelectedItem);detect.IsEnabled=false;error.Text="Looking for a native or official npm CLI…";
                try{
                    string path=await Task.Run(()=>AgentTools.FindExecutable(selected));
                    if(!IsLoaded||selected!=Convert.ToString(provider.SelectedItem))return;
                    if(string.IsNullOrEmpty(path))error.Text="No supported CLI found. Browse to its .exe or official npm .cmd. Install and sign in separately.";
                    else {executable.Text=path;error.Text="Found installed CLI. Choose the project folder to connect.";}
                }catch(Exception e){if(IsLoaded)error.Text="Could not inspect the installation: "+e.Message;}
                finally{detect.IsEnabled=true;}
            });body.Children.Add(detect);
            workspace=Ui.Input(existing==null?"":existing.Workspace);
            var folderRow=new DockPanel();var folder=Ui.Button("Choose…",delegate{
                using(var picker=new System.Windows.Forms.FolderBrowserDialog{Description="Choose the project this agent may work with",ShowNewFolderButton=false})
                    if(picker.ShowDialog()==System.Windows.Forms.DialogResult.OK)workspace.Text=picker.SelectedPath;
            });DockPanel.SetDock(folder,Dock.Right);folderRow.Children.Add(folder);folderRow.Children.Add(workspace);Add(body,"Project folder",folderRow);
            edits=new CheckBox{Content="Allow Codex to edit this project",IsChecked=existing!=null&&existing.AllowEdits,Margin=new Thickness(0,14,0,6),Foreground=Ui.White};body.Children.Add(edits);
            var note=Ui.Text("Review-only is the default. Tools use their own sign-in and provider billing. DeskPill never grants permission requests automatically.",11,Ui.Muted);note.TextWrapping=TextWrapping.Wrap;body.Children.Add(note);
            error=Ui.Text("",12,Ui.Hex("#FF375F"));error.TextWrapping=TextWrapping.Wrap;error.Margin=new Thickness(0,10,0,5);body.Children.Add(error);
            var buttons=Ui.Row(Ui.Button("Cancel",Close),Ui.Button(existing==null?"Connect agent":"Save connection",Save));buttons.HorizontalAlignment=HorizontalAlignment.Right;buttons.Margin=new Thickness(0,8,0,0);body.Children.Add(buttons);
            Content=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
            provider.SelectionChanged+=delegate{UpdateProvider();};UpdateProvider();
            System.Windows.Automation.AutomationProperties.SetName(provider,"Agent connection type");
            System.Windows.Automation.AutomationProperties.SetName(name,"Connection name");
            System.Windows.Automation.AutomationProperties.SetName(executable,"Agent application path");
            System.Windows.Automation.AutomationProperties.SetName(workspace,"Agent project folder");
            PreviewKeyDown+=delegate(object sender,System.Windows.Input.KeyEventArgs e){if(e.Key==System.Windows.Input.Key.Escape){if(provider.IsDropDownOpen)provider.IsDropDownOpen=false;else Close();e.Handled=true;}};
        }
        static void Add(Panel panel,string label,UIElement input)
        {
            var text=Ui.Text(label,12,Ui.Muted);text.Margin=new Thickness(0,10,0,4);panel.Children.Add(text);panel.Children.Add(input);
        }
        void UpdateProvider()
        {
            string type=Convert.ToString(provider.SelectedItem);edits.Visibility=type=="Codex CLI"?Visibility.Visible:Visibility.Collapsed;
            description.Text=type=="App shortcut"?"For Antigravity, desktop/cloud tools, or your own agent. Open the app and copy a prompt to hand over; live session control is unavailable.":"Start a dedicated "+type+" session for this project. Send text or reviewed voice prompts and see its response here.";
            if(string.IsNullOrWhiteSpace(name.Text)||name.Text==previousProvider)name.Text=type=="App shortcut"?"My agent":type;
            bool changed=previousProvider.Length>0&&previousProvider!=type;
            if(changed){executable.Text="";edits.IsChecked=false;error.Text="";}
            detect.Visibility=type=="App shortcut"?Visibility.Collapsed:Visibility.Visible;
            previousProvider=type;
        }
        void Save()
        {
            string blocked=SaveError==null?"":SaveError();if(!string.IsNullOrEmpty(blocked)){error.Text=blocked;return;}
            var result=new AgentProfile{Id=original==null?Guid.NewGuid().ToString("N"):original.Id,Name=name.Text.Trim(),Provider=Convert.ToString(provider.SelectedItem),Executable=executable.Text.Trim(),Workspace=workspace.Text.Trim(),AllowEdits=Convert.ToString(provider.SelectedItem)=="Codex CLI"&&edits.IsChecked==true};
            string problem=AgentTools.Validate(result);
            if(!string.IsNullOrEmpty(problem)){error.Text=problem;return;}
            if(result.Name.Length==0 || result.Name.Length>60){error.Text="Give the connection a name between 1 and 60 characters.";return;}
            Result=result;Close();
        }
    }
}
