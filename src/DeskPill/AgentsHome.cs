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
        Button agentPageAction,agentSend,agentVoice,agentStop,agentDetails;
        TextBox agentPrompt,agentResult;
        ComboBox agentPicker,agentHomePicker;
        string agentDetailId="";
        int agentRefreshQueued;
        bool agentVoiceHeld,agentDiagnosticsShown;
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
                var welcome=Ui.Text("Agents Home · Experimental",16,Ui.White);welcome.FontWeight=FontWeights.SemiBold;Put(target,welcome,32,6);
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
            agentPageAction=Ui.Button("Ask your agent…",delegate{var profile=SelectedAgent();if(profile!=null)ShowAgentConversation(profile.Id);});Put(target,agentPageAction,32,80);
            var add=Ui.Button("+ Connect",delegate{EditAgentConnection(null);});add.Background=Brushes.Transparent;Put(target,add,245,80);
            var homeMic=Ui.IconButton("\uE720","Dictate a prompt",delegate{var p=SelectedAgent();if(p!=null)BeginAgentVoiceFromHome(p.Id);});Put(target,homeMic,205,80);
            RefreshAgentViews();
        }
        void RefreshAgentViews()
        {
            if(stopping)return;
            ObserveAgentTransitions();
            UpdateAgentIndicator();
            var selected=SelectedAgent();
            if(agentHomePicker!=null&&selected!=null&&agentHomePicker.SelectedItem!=selected)agentHomePicker.SelectedItem=selected;
            if(selected!=null && agentPageStatus!=null)
            {
                var session=Agents.Get(selected.Id);
                agentPageStatus.Text="Experimental · "+(AgentTools.IsShortcut(selected)?"App shortcut · manual handoff":session.Status+" · "+selected.Provider);
                agentPageSummary.Text=session.Status=="Ready"||string.IsNullOrWhiteSpace(session.Summary)?ProjectName(selected.Workspace):session.Summary;
                agentPageSummary.ToolTip=agentPageSummary.Text;
            }
            var detail=state.Agents.FirstOrDefault(a=>a.Id==agentDetailId);
            if(detail==null||pose!=NotchPose.InlineAgents||agentResult==null)return;
            var active=Agents.Get(detail.Id);
            if(!string.IsNullOrEmpty(agentCaptureNote)){agentDetailStatus.Text=agentCaptureNote;agentDetailStatus.Foreground=Ui.Hex("#FF375F");}
            else{agentDetailStatus.Foreground=Ui.Muted;if(agentSpeech==null)agentDetailStatus.Text="Experimental · "+(AgentTools.IsShortcut(detail)?"Manual handoff":active.Status)+" · "+(detail.AllowEdits?"Project edits allowed":"Review only")+" · "+ProjectName(detail.Workspace);}
            string output=active.Status=="Needs attention"?active.Summary+Environment.NewLine+active.Transcript:string.IsNullOrEmpty(active.Transcript)?(AgentTools.IsShortcut(detail)?"Write a prompt below. Copy it, then open your app to choose its destination.":"Ask about this project. For screen questions, use the camera to attach and review an image before Send. Screen access is not automatic."):active.Transcript;
            if(active.Diagnostics.Length==0)agentDiagnosticsShown=false;
            agentDetails.Visibility=active.Diagnostics.Length>0?Visibility.Visible:Visibility.Hidden;
            agentDetails.ToolTip=agentDiagnosticsShown?"Show agent response":"Show tool diagnostics";
            System.Windows.Automation.AutomationProperties.SetName(agentDetails,Convert.ToString(agentDetails.ToolTip));
            ((TextBlock)agentDetails.Content).Foreground=agentDiagnosticsShown?Ui.Green:Ui.Muted;
            if(agentDiagnosticsShown)output="Tool diagnostics · separate from the agent reply"+Environment.NewLine+Environment.NewLine+active.Diagnostics;
            if(agentResult.Text!=output){bool atEnd=agentResult.SelectionLength==0&&agentResult.VerticalOffset>=agentResult.ExtentHeight-agentResult.ViewportHeight-8;int start=agentResult.SelectionStart,length=agentResult.SelectionLength;double offset=agentResult.VerticalOffset;agentResult.Text=output;agentResult.Select(Math.Min(start,output.Length),Math.Min(length,Math.Max(0,output.Length-start)));if(atEnd)agentResult.ScrollToEnd();else agentResult.ScrollToVerticalOffset(offset);}
            agentSend.IsEnabled=!agentCapturing&&!active.Running&&agentSpeech==null&&!string.IsNullOrWhiteSpace(agentPrompt.Text);
            agentStop.IsEnabled=active.Running;
            agentVoice.IsEnabled=!active.Running&&!agentCapturing;
            RefreshAgentAttachmentChip();
            agentStop.Visibility=AgentTools.IsShortcut(detail)?Visibility.Collapsed:Visibility.Visible;
        }
        void UpdateAgentIndicator()
        {
            if(agentIndicator==null)return;
            int working=0,attention=0;
            if(agentHub!=null&&!gameModeActive&&Order().Contains("Agents"))foreach(var profile in state.Agents){var session=agentHub.Get(profile.Id);if(session.Running)working++;else if(session.Status=="Needs attention")attention++;}
            agentIndicator.Visibility=working+attention+agentUnread.Count>0?Visibility.Visible:Visibility.Collapsed;
            agentIndicator.Fill=attention>0?Ui.Hex("#FFCA70"):Ui.Green;
            if(compact!=null)compact.ToolTip=working+attention+agentUnread.Count>0?working+" agent(s) working · "+attention+" need attention · "+agentUnread.Count+" unread · Agents page":null;
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
            if(!this.open)TrackAgentContext();
            agentCaptureNote="";
            PreserveAgentDraft();CloseAgentPreview();agentUnread.Remove(id);CloseVolumeMenu();
            if(dictation!=null&&dictation.Active)dictation.Cancel();
            agentDiagnosticsShown=false;agentDetailId=id;state.SelectedAgent=id;Save();
            var body=new Grid();body.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});body.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});body.RowDefinitions.Add(new RowDefinition{Height=new GridLength(54)});body.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            body.RowDefinitions.Insert(1,new RowDefinition{Height=GridLength.Auto});var chip=BuildAgentAttachmentChip();Grid.SetRow(chip,1);body.Children.Add(chip);
            var top=new DockPanel();var back=Ui.Button("←",ExitInlineView);back.ToolTip="Back to Agents";back.Padding=new Thickness(7,2,7,2);back.MinHeight=24;DockPanel.SetDock(back,Dock.Left);top.Children.Add(back);
            agentPicker=AgentControls.Picker();agentPicker.ItemsSource=state.Agents.ToArray();agentPicker.SelectedItem=profile;agentPicker.Height=26;agentPicker.Margin=new Thickness(5,0,5,0);agentPicker.MaxWidth=230;System.Windows.Automation.AutomationProperties.SetName(agentPicker,"Prompt destination");
            var settings=Ui.IconButton("\uE713","Conversation actions",ShowAgentActions);settings.Height=26;DockPanel.SetDock(settings,Dock.Right);top.Children.Add(settings);agentCaptureButton=Ui.IconButton("\uE722","Attach a screen image",ShowAgentCaptureMenu);agentCaptureButton.Height=26;DockPanel.SetDock(agentCaptureButton,Dock.Right);top.Children.Add(agentCaptureButton);top.Children.Add(agentPicker);body.Children.Add(top);
            agentResult=Ui.Input("");agentResult.IsReadOnly=true;agentResult.AcceptsReturn=true;agentResult.TextWrapping=TextWrapping.Wrap;agentResult.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;agentResult.Background=Brushes.Transparent;agentResult.BorderThickness=new Thickness(0);agentResult.Padding=new Thickness(1,6,1,4);agentResult.FontSize=11.5;Grid.SetRow(agentResult,2);body.Children.Add(agentResult);System.Windows.Automation.AutomationProperties.SetName(agentResult,"Agent response");
            agentPrompt=Ui.Input(Agents.Get(id).Draft??"");agentPrompt.MaxLength=16000;agentPrompt.AcceptsReturn=true;agentPrompt.TextWrapping=TextWrapping.Wrap;agentPrompt.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;agentPrompt.FontSize=12;agentPrompt.ToolTip="Prompt for "+profile.Name+" · Ctrl+Enter sends";Grid.SetRow(agentPrompt,3);body.Children.Add(agentPrompt);System.Windows.Automation.AutomationProperties.SetName(agentPrompt,"Prompt for "+profile.Name);
            var footer=new DockPanel{Margin=new Thickness(0,5,0,0)};
            agentSend=Ui.Button(AgentTools.IsShortcut(profile)?"Copy prompt":"Send",SendAgentPrompt);agentSend.MinHeight=26;agentSend.Padding=new Thickness(10,3,10,3);DockPanel.SetDock(agentSend,Dock.Right);footer.Children.Add(agentSend);
            agentStop=Ui.Button("Stop",async delegate{await Agents.StopAsync(id);});agentStop.MinHeight=26;agentStop.Padding=new Thickness(8,3,8,3);DockPanel.SetDock(agentStop,Dock.Right);footer.Children.Add(agentStop);
            var open=Ui.IconButton("\uE8A7",AgentTools.IsShortcut(profile)?"Open agent app":"Open project folder",delegate{OpenAgentApp(profile);});open.Width=28;open.Height=26;DockPanel.SetDock(open,Dock.Left);footer.Children.Add(open);
            agentVoice=Ui.IconButton("\uE720",SpeechAssets.Ready?"Hold to dictate a prompt; release to review":"Install optional English speech in Settings → Dictation",delegate{});agentVoice.Width=28;agentVoice.Height=26;DockPanel.SetDock(agentVoice,Dock.Left);footer.Children.Add(agentVoice);
            WireAgentVoiceButton(agentVoice,id);
            agentDetails=Ui.IconButton("\uE946","Show tool diagnostics",delegate{agentDiagnosticsShown=!agentDiagnosticsShown;RefreshAgentViews();});agentDetails.Width=26;agentDetails.Height=26;DockPanel.SetDock(agentDetails,Dock.Left);footer.Children.Add(agentDetails);
            agentWave=new DictationWaveform{Width=100,Height=26,LayoutTransform=new ScaleTransform(.7,.7),Visibility=Visibility.Collapsed};DockPanel.SetDock(agentWave,Dock.Left);footer.Children.Add(agentWave);
            agentDetailStatus=Ui.Text("",10,Ui.Muted);agentDetailStatus.TextTrimming=TextTrimming.CharacterEllipsis;footer.Children.Add(agentDetailStatus);
            Grid.SetRow(footer,4);body.Children.Add(footer);
            var promptInput=agentPrompt;var destinationPicker=agentPicker;
            agentPrompt.TextChanged+=delegate{agentCaptureNote="";if(agentHub!=null)Agents.Get(id).Draft=promptInput.Text;RefreshAgentViews();};
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
            if(profile==null||agentPrompt==null||agentSpeech!=null||agentCapturing)return;
            string prompt=agentPrompt.Text.Trim();if(prompt.Length==0||Agents.Get(profile.Id).Running)return;
            agentCaptureNote="";
            if(AgentTools.IsShortcut(profile))
            {
                try{Clipboard.SetText(prompt);agentDetailStatus.Text="Copied · open your app to choose the session";}catch{agentDetailStatus.Text="Clipboard busy · try again";}return;
            }
            string problem=AgentTools.Validate(profile);if(!string.IsNullOrEmpty(problem)){agentDetailStatus.Text=problem;return;}
            string sentDraft=agentPrompt.Text;AgentAttachment attachment;agentAttachments.TryGetValue(profile.Id,out attachment);AgentAttachmentCopy runImage=null;
            try{if(attachment!=null){runImage=attachment.CopyForRun();agentRunImages[profile.Id]=runImage;}PasteTarget paste;agentReplyTargets[profile.Id]=agentAttachmentTargets.TryGetValue(profile.Id,out paste)?paste:agentPasteTarget;var run=Agents.SendAsync(profile,prompt,runImage==null?new string[0]:new[]{runImage.FilePath});if(runImage!=null)DisposeAgentImageAfterRun(run,runImage);RefreshAgentViews();await run;
                if(!stopping){var session=Agents.Get(profile.Id);if(session.Status=="Done"&&attachment!=null){AgentAttachment current;if(agentAttachments.TryGetValue(profile.Id,out current)&&current==attachment)ClearAgentAttachment(profile.Id);}if(session.Status=="Done"&&session.Draft==sentDraft){session.Draft="";if(agentDetailId==profile.Id&&agentPrompt!=null&&agentPrompt.Text==sentDraft)agentPrompt.Clear();}}
            }
            catch(Exception e){if(!stopping)Notify("Agent could not start: "+e.Message);}
            finally{if(runImage!=null){runImage.Dispose();AgentAttachmentCopy owned;if(agentRunImages.TryGetValue(profile.Id,out owned)&&owned==runImage)agentRunImages.Remove(profile.Id);}}
            RefreshAgentViews();
        }
        void ManageAgents()
        {
            var selected=SelectedAgent();if(selected==null){EditAgentConnection(null);return;}
            var menu=new ContextMenu{Background=Ui.Card,Foreground=Ui.White};
            AddMenu(menu,"Connect another agent…",delegate{EditAgentConnection(null);});
            AddMenu(menu,"New conversation",delegate{if(Agents.Get(selected.Id).Running){Notify("Stop this run before starting a new conversation.");return;}ClearAgentAttachment(selected.Id);Agents.Reset(selected.Id);RefreshAgentViews();});
            AddMenu(menu,"Edit "+selected.Name+"…",delegate{EditAgentConnection(selected);});
            AddMenu(menu,"Remove "+selected.Name,async delegate{
                await Agents.StopAsync(selected.Id);if(stopping)return;if(agentDetailId==selected.Id)ExitInlineIfNeeded();ClearAgentAttachment(selected.Id);agentUnread.Remove(selected.Id);state.Agents.Remove(selected);state.SelectedAgent=state.Agents.Count==0?"":state.Agents[0].Id;Save();RebuildCurrentPage(true);
            });
            AddMenu(menu,"Hide Agents page",HideAgentsPage);menu.Opened+=delegate{contextMenuOpen=true;};menu.Closed+=delegate{contextMenuOpen=false;};menu.IsOpen=true;
        }
        void EditAgentConnection(AgentProfile profile)
        {
            if(agentEditor!=null){agentEditor.Activate();return;}
            if(profile!=null&&Agents.Get(profile.Id).Running){Notify("Stop this agent before editing its connection.");return;}
            if(profile==null&&state.Agents.Count>=20){Notify("Up to 20 agent connections are supported.");return;}
            PreserveAgentDraft();
            var editor=new AgentConnectionWindow(profile){Owner=this,Topmost=true,ShowActivated=true};agentEditor=editor;
            editor.SaveError=delegate{
                if(profile!=null&&!state.Agents.Contains(profile))return "This connection was removed. Cancel to return to Agents.";
                if(profile!=null&&Agents.Get(profile.Id).Running)return "Stop this run before saving connection changes.";
                return "";
            };
            bool wasPinned=pinned;pinned=true;
            editor.Closed+=delegate
            {
                agentEditor=null;pinned=wasPinned;
                if(stopping||editor.Result==null)return;
                var result=editor.Result;int index=profile==null?-1:state.Agents.IndexOf(profile);
                if(index<0)state.Agents.Add(result);else state.Agents[index]=result;
                state.SelectedAgent=result.Id;Save();
                if(IsInlinePose(pose))ExitInlineView();page="Agents";SetOpen(true);RebuildCurrentPage(true);
            };
            try{editor.Show();editor.Activate();}
            catch(Exception e){agentEditor=null;pinned=wasPinned;editor.Close();Notify("Could not open agent setup: "+e.Message);}
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
        void CleanupAgents()
        {
            CancelAgentVoice();if(agentHub!=null){agentHub.Changed-=QueueAgentRefresh;agentHub.Dispose();agentHub=null;}CleanupAgentExperience();if(agentEditor!=null)agentEditor.Close();
        }
    }
}
