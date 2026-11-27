using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DeskPill
{
    public sealed partial class IslandWindow
    {
        AgentHub agentHub;
        System.Windows.Shapes.Ellipse agentIndicator;
        TextBlock agentPageStatus,agentPageSummary,agentDetailStatus;
        Button agentPageAction,agentSend,agentVoice,agentStop;
        TextBox agentPrompt,agentResult;
        ComboBox agentPicker,agentHomePicker;
        string agentDetailId="";
        int agentRefreshQueued;
        bool agentVoiceHeld;
        AgentConnectionWindow agentEditor;
        DictationRecorder agentRecording;
        CancellationTokenSource agentSpeech;
        readonly DispatcherTimer agentVoiceTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(60)};
        DictationWaveform agentWave;
        string agentVoiceTarget="";
        DateTime agentVoiceStarted;
        int agentVoiceRevision;

        AgentHub Agents
        {
            get
            {
                if(agentHub==null){agentHub=new AgentHub();agentHub.Changed+=QueueAgentRefresh;}
                return agentHub;
            }
        }
        AgentProfile SelectedAgent()
        {
            return state.Agents.FirstOrDefault(a=>a.Id==state.SelectedAgent)??state.Agents.FirstOrDefault();
        }
        void QueueAgentRefresh()
        {
            if(Dispatcher.HasShutdownStarted||Interlocked.Exchange(ref agentRefreshQueued,1)!=0)return;
            Dispatcher.BeginInvoke(new Action(delegate{
                if(stopping){Interlocked.Exchange(ref agentRefreshQueued,0);return;}
                var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};
                timer.Tick+=delegate{timer.Stop();Interlocked.Exchange(ref agentRefreshQueued,0);if(!stopping)RefreshAgentViews();};timer.Start();
            }));
        }
        void BuildAgents(Canvas target)
        {
            if(state.Agents.Count==0)
            {
                var welcome=Ui.Text("Your agents, close at hand",16,Ui.White);welcome.FontWeight=FontWeights.SemiBold;Put(target,welcome,32,6);
                var note=Ui.Text("Choose a tool and project. Stay in the flow.",11.5,Ui.Muted);note.Width=320;Put(target,note,32,34);
                Put(target,Ui.Button("Connect agent",delegate{EditAgentConnection(null);}),32,68);
                var hide=Ui.Button("Hide this page",HideAgentsPage);hide.Background=Brushes.Transparent;Put(target,hide,168,68);
                return;
            }
            var selected=SelectedAgent();if(selected==null)return;
            var picker=AgentControls.Picker();agentHomePicker=picker;picker.Width=232;picker.Height=28;picker.ItemsSource=state.Agents.ToArray();picker.SelectedItem=selected;picker.ToolTip="Selected agent and project";
            System.Windows.Automation.AutomationProperties.SetName(picker,"Selected agent");
            picker.SelectionChanged+=delegate{var profile=picker.SelectedItem as AgentProfile;if(profile!=null&&state.SelectedAgent!=profile.Id){state.SelectedAgent=profile.Id;Save();RefreshAgentViews();}};
            Put(target,picker,32,0);
            var manage=Ui.IconButton("\uE713","Manage agent connections",ManageAgents);manage.Background=Brushes.Transparent;Put(target,manage,312,-2);
            agentPageStatus=Ui.Text("Ready",11.5,PageAccent("Agents"));agentPageStatus.Width=325;Put(target,agentPageStatus,32,35);
            agentPageSummary=Ui.Text("",11.5,Ui.Muted);agentPageSummary.Width=325;Put(target,agentPageSummary,32,55);
            agentPageAction=Ui.Button("Open conversation",delegate{var profile=SelectedAgent();if(profile!=null)ShowAgentConversation(profile.Id);});Put(target,agentPageAction,32,80);
            var add=Ui.Button("+ Connect",delegate{EditAgentConnection(null);});add.Background=Brushes.Transparent;Put(target,add,205,80);
            RefreshAgentViews();
        }
        void RefreshAgentViews()
        {
            if(stopping)return;
            UpdateAgentIndicator();
            var selected=SelectedAgent();
            if(agentHomePicker!=null&&selected!=null&&agentHomePicker.SelectedItem!=selected)agentHomePicker.SelectedItem=selected;
            if(selected!=null && agentPageStatus!=null)
            {
                var session=Agents.Get(selected.Id);
                agentPageStatus.Text=AgentTools.IsShortcut(selected)?"App shortcut · manual handoff":session.Status+" · "+selected.Provider;
                agentPageSummary.Text=session.Status=="Ready"||string.IsNullOrWhiteSpace(session.Summary)?ProjectName(selected.Workspace):session.Summary;
                agentPageSummary.ToolTip=agentPageSummary.Text;
            }
            var detail=state.Agents.FirstOrDefault(a=>a.Id==agentDetailId);
            if(detail==null||pose!=NotchPose.InlineAgents||agentResult==null)return;
            var active=Agents.Get(detail.Id);
            agentDetailStatus.Text=(AgentTools.IsShortcut(detail)?"Manual handoff":active.Status)+" · "+(detail.AllowEdits?"Project edits allowed":"Review only")+" · "+ProjectName(detail.Workspace);
            string output=active.Status=="Needs attention"?active.Summary+Environment.NewLine+active.Output:string.IsNullOrEmpty(active.Output)?(AgentTools.IsShortcut(detail)?"Write a prompt below. Copy it, then open your app to choose its destination.":"Ask about this project. Responses appear here; voice fills your draft for review."):active.Output;
            if(agentResult.Text!=output){bool atEnd=agentResult.VerticalOffset>=agentResult.ExtentHeight-agentResult.ViewportHeight-8;agentResult.Text=output;if(atEnd)agentResult.ScrollToEnd();}
            agentSend.IsEnabled=!active.Running&&agentSpeech==null&&!string.IsNullOrWhiteSpace(agentPrompt.Text);
            agentStop.IsEnabled=active.Running;
            agentVoice.IsEnabled=!active.Running&&SpeechAssets.Ready;
            agentStop.Visibility=AgentTools.IsShortcut(detail)?Visibility.Collapsed:Visibility.Visible;
        }
        void UpdateAgentIndicator()
        {
            if(agentIndicator==null)return;
            int working=0,attention=0;
            if(agentHub!=null&&!gameModeActive&&Order().Contains("Agents"))foreach(var profile in state.Agents){var session=agentHub.Get(profile.Id);if(session.Running)working++;else if(session.Status=="Needs attention")attention++;}
            agentIndicator.Visibility=working+attention>0?Visibility.Visible:Visibility.Collapsed;
            agentIndicator.Fill=attention>0?Ui.Hex("#FFCA70"):Ui.Green;
            if(compact!=null)compact.ToolTip=working+attention>0?working+" agent(s) working · "+attention+" need attention · Agents page":null;
        }
        static string ProjectName(string folder)
        {
            try{return string.IsNullOrWhiteSpace(folder)?"No project":new DirectoryInfo(folder).Name;}catch{return "Project unavailable";}
        }
        void PreserveAgentDraft()
        {
            if(agentPrompt!=null && !string.IsNullOrEmpty(agentDetailId) && agentHub!=null)Agents.Get(agentDetailId).Draft=agentPrompt.Text;
            if(agentSpeech!=null)CancelAgentVoice();
        }
        void ShowAgentConversation(string id)
        {
            var profile=state.Agents.FirstOrDefault(a=>a.Id==id);if(profile==null)return;
            PreserveAgentDraft();CloseVolumeMenu();
            if(dictation!=null&&dictation.Active)dictation.Cancel();
            agentDetailId=id;state.SelectedAgent=id;Save();
            var body=new Grid();body.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});body.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});body.RowDefinitions.Add(new RowDefinition{Height=new GridLength(54)});body.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var top=new DockPanel();var back=Ui.Button("←",ExitInlineView);back.ToolTip="Back to Agents";back.Padding=new Thickness(7,2,7,2);back.MinHeight=24;DockPanel.SetDock(back,Dock.Left);top.Children.Add(back);
            agentPicker=AgentControls.Picker();agentPicker.ItemsSource=state.Agents.ToArray();agentPicker.SelectedItem=profile;agentPicker.Height=26;agentPicker.Margin=new Thickness(5,0,5,0);agentPicker.MaxWidth=230;System.Windows.Automation.AutomationProperties.SetName(agentPicker,"Prompt destination");
            var settings=Ui.IconButton("\uE713","Edit this connection",delegate{EditAgentConnection(state.Agents.FirstOrDefault(a=>a.Id==agentDetailId));});settings.Height=26;DockPanel.SetDock(settings,Dock.Right);top.Children.Add(settings);top.Children.Add(agentPicker);body.Children.Add(top);
            agentResult=Ui.Input("");agentResult.IsReadOnly=true;agentResult.AcceptsReturn=true;agentResult.TextWrapping=TextWrapping.Wrap;agentResult.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;agentResult.Background=Brushes.Transparent;agentResult.BorderThickness=new Thickness(0);agentResult.Padding=new Thickness(1,6,1,4);agentResult.FontSize=11.5;Grid.SetRow(agentResult,1);body.Children.Add(agentResult);System.Windows.Automation.AutomationProperties.SetName(agentResult,"Agent response");
            agentPrompt=Ui.Input(Agents.Get(id).Draft??"");agentPrompt.MaxLength=16000;agentPrompt.AcceptsReturn=true;agentPrompt.TextWrapping=TextWrapping.Wrap;agentPrompt.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;agentPrompt.FontSize=12;agentPrompt.ToolTip="Prompt for "+profile.Name+" · Ctrl+Enter sends";Grid.SetRow(agentPrompt,2);body.Children.Add(agentPrompt);System.Windows.Automation.AutomationProperties.SetName(agentPrompt,"Prompt for "+profile.Name);
            var footer=new DockPanel{Margin=new Thickness(0,5,0,0)};
            agentSend=Ui.Button(AgentTools.IsShortcut(profile)?"Copy prompt":"Send",SendAgentPrompt);agentSend.MinHeight=26;agentSend.Padding=new Thickness(10,3,10,3);DockPanel.SetDock(agentSend,Dock.Right);footer.Children.Add(agentSend);
            agentStop=Ui.Button("Stop",async delegate{await Agents.StopAsync(id);});agentStop.MinHeight=26;agentStop.Padding=new Thickness(8,3,8,3);DockPanel.SetDock(agentStop,Dock.Right);footer.Children.Add(agentStop);
            var open=Ui.IconButton("\uE8A7",AgentTools.IsShortcut(profile)?"Open agent app":"Open project folder",delegate{OpenAgentApp(profile);});open.Width=28;open.Height=26;DockPanel.SetDock(open,Dock.Left);footer.Children.Add(open);
            agentVoice=Ui.IconButton("\uE720",SpeechAssets.Ready?"Hold to dictate a prompt; release to review":"Install optional English speech in Settings → Dictation",delegate{});agentVoice.Width=28;agentVoice.Height=26;DockPanel.SetDock(agentVoice,Dock.Left);footer.Children.Add(agentVoice);
            var voiceButton=agentVoice;
            agentVoice.PreviewMouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){if(agentSpeech==null){agentVoiceHeld=true;voiceButton.CaptureMouse();BeginAgentVoice(id);}e.Handled=true;};
            agentVoice.PreviewMouseLeftButtonUp+=delegate(object sender,MouseButtonEventArgs e){agentVoiceHeld=false;voiceButton.ReleaseMouseCapture();FinishAgentVoice();e.Handled=true;};
            agentVoice.PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Space&&!e.IsRepeat&&agentSpeech==null){agentVoiceHeld=true;BeginAgentVoice(id);e.Handled=true;}};
            agentVoice.PreviewKeyUp+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Space){agentVoiceHeld=false;FinishAgentVoice();e.Handled=true;}};
            agentVoice.LostMouseCapture+=delegate{agentVoiceHeld=false;FinishAgentVoice();};
            agentVoice.LostKeyboardFocus+=delegate{if(agentVoiceHeld){agentVoiceHeld=false;FinishAgentVoice();}};
            agentWave=new DictationWaveform{Width=100,Height=26,LayoutTransform=new ScaleTransform(.7,.7),Visibility=Visibility.Collapsed};DockPanel.SetDock(agentWave,Dock.Left);footer.Children.Add(agentWave);
            agentDetailStatus=Ui.Text("",10,Ui.Muted);agentDetailStatus.TextTrimming=TextTrimming.CharacterEllipsis;footer.Children.Add(agentDetailStatus);
            Grid.SetRow(footer,3);body.Children.Add(footer);
            var promptInput=agentPrompt;var destinationPicker=agentPicker;
            agentPrompt.TextChanged+=delegate{if(agentHub!=null)Agents.Get(id).Draft=promptInput.Text;RefreshAgentViews();};
            agentPrompt.PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Enter&&(Keyboard.Modifiers&ModifierKeys.Control)!=0){SendAgentPrompt();e.Handled=true;}};
            agentPicker.SelectionChanged+=delegate{var next=destinationPicker.SelectedItem as AgentProfile;if(next!=null&&next.Id!=agentDetailId)ShowAgentConversation(next.Id);};
            // ShowInlineView may open the shell, whose closing cleanup clears the inline host.
            SetOpen(true);ShowInlineView(NotchPose.InlineAgents,484,body);
            RefreshAgentViews();
            Dispatcher.BeginInvoke(new Action(delegate{if(pose==NotchPose.InlineAgents&&agentPrompt!=null)agentPrompt.Focus();}));
        }
        async void SendAgentPrompt()
        {
            var profile=state.Agents.FirstOrDefault(a=>a.Id==agentDetailId);
            if(profile==null||agentPrompt==null||agentSpeech!=null)return;
            string prompt=agentPrompt.Text.Trim();if(prompt.Length==0||Agents.Get(profile.Id).Running)return;
            if(AgentTools.IsShortcut(profile))
            {
                try{Clipboard.SetText(prompt);agentDetailStatus.Text="Copied · open your app to choose the session";}catch{agentDetailStatus.Text="Clipboard busy · try again";}return;
            }
            string problem=AgentTools.Validate(profile);if(!string.IsNullOrEmpty(problem)){agentDetailStatus.Text=problem;return;}
            string sentDraft=agentPrompt.Text;
            try{var run=Agents.SendAsync(profile,prompt);RefreshAgentViews();await run;
                if(!stopping){var session=Agents.Get(profile.Id);if(session.Status=="Done"&&session.Draft==sentDraft){session.Draft="";if(agentDetailId==profile.Id&&agentPrompt!=null&&agentPrompt.Text==sentDraft)agentPrompt.Clear();}}
            }
            catch(Exception e){if(!stopping)Notify("Agent could not start: "+e.Message);}
            RefreshAgentViews();
        }
        void ManageAgents()
        {
            var selected=SelectedAgent();if(selected==null){EditAgentConnection(null);return;}
            var menu=new ContextMenu{Background=Ui.Card,Foreground=Ui.White};
            AddMenu(menu,"Connect another agent…",delegate{EditAgentConnection(null);});
            AddMenu(menu,"New conversation",delegate{if(Agents.Get(selected.Id).Running){Notify("Stop this run before starting a new conversation.");return;}Agents.Reset(selected.Id);RefreshAgentViews();});
            AddMenu(menu,"Edit "+selected.Name+"…",delegate{EditAgentConnection(selected);});
            AddMenu(menu,"Remove "+selected.Name,async delegate{
                await Agents.StopAsync(selected.Id);if(stopping)return;if(agentDetailId==selected.Id)ExitInlineIfNeeded();state.Agents.Remove(selected);state.SelectedAgent=state.Agents.Count==0?"":state.Agents[0].Id;Save();RebuildCurrentPage(true);
            });
            AddMenu(menu,"Hide Agents page",HideAgentsPage);menu.Opened+=delegate{contextMenuOpen=true;};menu.Closed+=delegate{contextMenuOpen=false;};menu.IsOpen=true;
        }
        void EditAgentConnection(AgentProfile profile)
        {
            if(agentEditor!=null){agentEditor.Activate();return;}
            if(profile!=null&&Agents.Get(profile.Id).Running){Notify("Stop this agent before editing its connection.");return;}
            if(profile==null&&state.Agents.Count>=20){Notify("Up to 20 agent connections are supported.");return;}
            PreserveAgentDraft();
            var editor=new AgentConnectionWindow(profile);agentEditor=editor;
            bool wasPinned=pinned;pinned=true;
            try
            {
                if(editor.ShowDialog()==true)
                {
                    var result=editor.Result;int index=profile==null?-1:state.Agents.IndexOf(profile);
                    if(index<0)state.Agents.Add(result);else state.Agents[index]=result;
                    state.SelectedAgent=result.Id;Save();
                    if(IsInlinePose(pose))ExitInlineView();page="Agents";SetOpen(true);RebuildCurrentPage(true);
                }
            }
            finally{agentEditor=null;pinned=wasPinned;}
        }
        void HideAgentsPage()
        {
            if(state.Settings.VisiblePages().Length<=1){Notify("Keep at least one page visible. Choose pages in Settings.");return;}
            PreserveAgentDraft();ExitInlineIfNeeded();state.Settings.HiddenPages=state.Settings.HiddenPages.Concat(new[]{"Agents"}).Distinct().ToArray();
            Save();page=Order()[0];BuildDockTray(motion.TopOffset);ContextMenu=BuildMenu();RebuildCurrentPage(true);
        }
        void OpenAgentApp(AgentProfile profile)
        {
            try
            {
                if(AgentTools.IsShortcut(profile))
                {
                    string problem=AgentTools.Validate(profile);if(!string.IsNullOrEmpty(problem)){Notify(problem);return;}
                    Process.Start(new ProcessStartInfo(profile.Executable){UseShellExecute=true,WorkingDirectory=string.IsNullOrWhiteSpace(profile.Workspace)?Path.GetDirectoryName(profile.Executable):profile.Workspace});
                }
                else if(Directory.Exists(profile.Workspace))Process.Start(new ProcessStartInfo("explorer.exe",AgentTools.Quote(profile.Workspace)){UseShellExecute=true});
                pinned=false;SetOpen(false);
            }
            catch(Exception e){Notify("Could not open: "+e.Message);}
        }
        async void BeginAgentVoice(string id)
        {
            if(agentSpeech!=null||!SpeechAssets.Ready||agentPrompt==null)return;
            agentSpeech=new CancellationTokenSource();var token=agentSpeech;agentVoiceTarget=id;int revision=++agentVoiceRevision;
            agentWave.Visibility=Visibility.Visible;agentWave.Processing=false;agentDetailStatus.Text="Preparing microphone…";agentSend.IsEnabled=false;
            try
            {
                var recorder=await Task.Run(()=>new DictationRecorder());
                if(token.IsCancellationRequested||stopping||revision!=agentVoiceRevision){await Task.Run((Action)recorder.Dispose);return;}
                agentRecording=recorder;agentVoiceStarted=DateTime.UtcNow;
                agentVoiceTimer.Tick-=AgentVoiceTick;agentVoiceTimer.Tick+=AgentVoiceTick;agentVoiceTimer.Start();agentDetailStatus.Text="Listening · release to review";
                if(!agentVoiceHeld)FinishAgentVoice();
            }
            catch(Exception e){if(!token.IsCancellationRequested){CancelAgentVoice();if(agentDetailStatus!=null)agentDetailStatus.Text=e.Message;}}
        }
        void AgentVoiceTick(object sender,EventArgs e)
        {
            if(agentRecording==null)return;
            agentWave.Level=agentRecording.Level;
            if(!agentVoiceHeld||agentRecording.Failed||DateTime.UtcNow-agentVoiceStarted>TimeSpan.FromSeconds(90))FinishAgentVoice();
        }
        async void FinishAgentVoice()
        {
            if(agentRecording==null)return;
            var recorder=agentRecording;agentRecording=null;var token=agentSpeech;string id=agentVoiceTarget;int revision=agentVoiceRevision;
            agentVoiceTimer.Stop();agentWave.Processing=true;agentWave.Level=0;agentDetailStatus.Text="Turning speech into a draft…";
            try
            {
                byte[] wave=await Task.Run(delegate{try{return recorder.Finish();}finally{recorder.Dispose();}});
                string text=await SpeechTranscriber.TranscribeAsync(wave,token.Token);
                if(token.IsCancellationRequested||revision!=agentVoiceRevision||agentDetailId!=id||agentPrompt==null)return;
                agentPrompt.Text=(agentPrompt.Text.Length==0?"":agentPrompt.Text+" ")+text;agentPrompt.CaretIndex=agentPrompt.Text.Length;
                CancelAgentVoice();agentDetailStatus.Text="Review your words, then Send · "+state.Agents.First(a=>a.Id==id).Name;agentPrompt.Focus();
            }
            catch(OperationCanceledException){}
            catch(Exception e){if(!token.IsCancellationRequested){CancelAgentVoice();if(agentDetailStatus!=null)agentDetailStatus.Text=e.Message;}}
        }
        void CancelAgentVoice()
        {
            ++agentVoiceRevision;agentVoiceHeld=false;agentVoiceTimer.Stop();var token=agentSpeech;agentSpeech=null;if(token!=null)token.Cancel();
            var recorder=agentRecording;agentRecording=null;if(recorder!=null)Task.Run((Action)recorder.Dispose);
            if(agentWave!=null)agentWave.Visibility=Visibility.Collapsed;if(agentVoice!=null&&agentVoice.IsMouseCaptured)agentVoice.ReleaseMouseCapture();RefreshAgentViews();
        }
        void CleanupAgents()
        {
            CancelAgentVoice();if(agentHub!=null){agentHub.Changed-=QueueAgentRefresh;agentHub.Dispose();agentHub=null;}if(agentEditor!=null)agentEditor.Close();
        }
    }
}
