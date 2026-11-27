using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DeskPill
{
    public sealed partial class IslandWindow
    {
        bool agentCapturing;
        AgentScreenTarget agentScreenTarget;
        PasteTarget agentPasteTarget;
        readonly Dictionary<string,AgentAttachment> agentAttachments=new Dictionary<string,AgentAttachment>();
        readonly Dictionary<string,PasteTarget> agentReplyTargets=new Dictionary<string,PasteTarget>();
        readonly Dictionary<string,PasteTarget> agentAttachmentTargets=new Dictionary<string,PasteTarget>();
        readonly Dictionary<string,bool> agentObservedRunning=new Dictionary<string,bool>();
        readonly Dictionary<string,AgentAttachmentCopy> agentRunImages=new Dictionary<string,AgentAttachmentCopy>();
        readonly HashSet<string> agentUnread=new HashSet<string>();
        Border agentAttachmentChip;
        Button agentCaptureButton;
        Window agentAttachmentPreview;
        string agentNoticeId="";
        DateTime agentNoticeUntil;

        static void DisposeAgentImageAfterRun(Task task,AgentAttachmentCopy image)
        {task.ContinueWith(delegate{image.Dispose();},TaskScheduler.Default);}

        void TrackAgentContext()
        {
            if(state.Agents.Count==0){agentScreenTarget=null;agentPasteTarget=null;return;}
            if(preview||capture||stopping||agentCapturing||agentAttachmentPreview!=null)return;
            var target=AgentScreenTarget.CaptureForeground();
            if(target!=null){var paste=PasteTarget.Capture();if(paste!=null&&paste.Window==target.WindowHandle&&paste.ProcessId==target.ProcessId){agentScreenTarget=target;agentPasteTarget=paste;}}
        }
        bool AgentCanAutoClose()
        {
            return !AgentVoiceActive&&!agentCapturing&&agentAttachmentPreview==null&&agentEditor==null&&
                (agentPrompt==null||!agentPrompt.IsKeyboardFocusWithin)&&
                (agentResult==null||!agentResult.IsKeyboardFocusWithin)&&Mouse.Captured==null;
        }
        void ShowAgentSpeechSetup(){ShowSettings();if(settingsWindow!=null)settingsWindow.OpenSection("Dictation");}
        void ShowAgentActions()
        {
            var p=state.Agents.FirstOrDefault(a=>a.Id==agentDetailId);if(p==null)return;
            var menu=new ContextMenu{Background=Ui.Card,Foreground=Ui.White};
            AddMenu(menu,"Copy answer",delegate{CopyAgentText(Agents.Get(p.Id).Output);});
            AddMenu(menu,"Copy conversation",delegate{CopyAgentText(Agents.Get(p.Id).Transcript);});
            PasteTarget target;agentReplyTargets.TryGetValue(p.Id,out target);
            if(target!=null)AddMenu(menu,"Paste answer into "+target.Name,async delegate{
                string answer=Agents.Get(p.Id).Output;if(string.IsNullOrWhiteSpace(answer))return;
                try{Clipboard.SetText(answer);if(!await target.TryPasteAsync())Notify("Answer copied. The original app is unavailable; paste it where you choose.");}
                catch(Exception){Notify("Could not paste. Try Copy answer.");}
            });
            AddMenu(menu,"New conversation",delegate{if(Agents.Get(p.Id).Running)return;ClearAgentAttachment(p.Id);Agents.Reset(p.Id);agentUnread.Remove(p.Id);RefreshAgentViews();});
            AddMenu(menu,"Edit connection…",delegate{EditAgentConnection(p);});
            OpenAgentMenu(menu);
        }
        void CopyAgentText(string text)
        {
            if(string.IsNullOrWhiteSpace(text))return;
            try{Clipboard.SetText(text);}catch{Notify("Clipboard busy · try again.");}
        }
        void OpenAgentMenu(ContextMenu menu)
        {
            menu.Opened+=delegate{contextMenuOpen=true;};menu.Closed+=delegate{contextMenuOpen=false;};menu.IsOpen=true;
        }
        void ShowAgentCaptureMenu()
        {
            var p=state.Agents.FirstOrDefault(a=>a.Id==agentDetailId);
            if(p==null||!AgentTools.SupportsImages(p.Provider)||agentCapturing||Agents.Get(p.Id).Running||AgentVoiceActive)return;
            var menu=new ContextMenu{Background=Ui.Card,Foreground=Ui.White};
            AddMenu(menu,"Select screen area…",delegate{CaptureAgentContext("area");});
            if(agentScreenTarget!=null){
                AddMenu(menu,"Previous window: "+agentScreenTarget.Title,delegate{CaptureAgentContext("window");});
                AddMenu(menu,"Monitor containing that window…",delegate{CaptureAgentContext("monitor");});
            }
            if(agentAttachments.ContainsKey(p.Id))AddMenu(menu,"Remove attached image",delegate{ClearAgentAttachment(p.Id);RefreshAgentViews();});
            OpenAgentMenu(menu);
        }
        async void CaptureAgentContext(string kind)
        {
            string id=agentDetailId;var p=state.Agents.FirstOrDefault(a=>a.Id==id);
            if(p==null||agentCapturing||AgentVoiceActive||Agents.Get(id).Running||!AgentTools.SupportsImages(p.Provider))return;
            CloseAgentPreview();var target=agentScreenTarget;var paste=agentPasteTarget;
            double opacity=Opacity;bool hit=IsHitTestVisible;AgentAttachment result=null;
            agentCapturing=true;RefreshAgentViews();
            try{
                Opacity=0;IsHitTestVisible=false;
                await Dispatcher.InvokeAsync(delegate{},System.Windows.Threading.DispatcherPriority.Render);await Task.Delay(70);
                if(stopping)return;
                if(kind=="area")result=await AgentVision.CaptureAreaAsync(this);
                else if(target==null)throw new InvalidOperationException("Return to your app, then open Agents again to select it.");
                else if(kind=="window")result=await AgentVision.CaptureWindowAsync(target);
                else result=await AgentVision.CaptureMonitorAsync(target);
                if(result!=null&&!stopping&&agentDetailId==id&&pose==NotchPose.InlineAgents&&state.Agents.Any(a=>a.Id==id)){
                    ClearAgentAttachment(id);agentAttachments[id]=result;agentAttachmentTargets[id]=paste;result=null;
                }
            }catch(Exception e){if(!stopping)Notify("Screen attachment: "+e.Message);}
            finally{if(result!=null)result.Dispose();agentCapturing=false;if(!stopping){Opacity=opacity;IsHitTestVisible=hit;RefreshAgentViews();}}
        }
        Border BuildAgentAttachmentChip()
        {
            agentAttachmentChip=new Border{Margin=new Thickness(0,3,0,0),Padding=new Thickness(3),Background=Ui.Card,CornerRadius=new CornerRadius(6),Visibility=Visibility.Collapsed};return agentAttachmentChip;
        }
        void RefreshAgentAttachmentChip()
        {
            var p=state.Agents.FirstOrDefault(a=>a.Id==agentDetailId);if(p==null)return;
            if(agentCaptureButton!=null){agentCaptureButton.IsEnabled=AgentTools.SupportsImages(p.Provider)&&!Agents.Get(p.Id).Running&&!agentCapturing&&!AgentVoiceActive;agentCaptureButton.ToolTip=AgentTools.SupportsImages(p.Provider)?"Attach a screen image · review before Send":"Screen images are currently supported by Codex CLI";}
            if(agentAttachmentChip==null)return;AgentAttachment image;
            if(!agentAttachments.TryGetValue(p.Id,out image)){agentAttachmentChip.Visibility=Visibility.Collapsed;return;}
            var row=new DockPanel();var remove=Ui.Button("×",delegate{ClearAgentAttachment(p.Id);RefreshAgentViews();});remove.ToolTip="Remove screen attachment";remove.MinHeight=20;remove.Padding=new Thickness(6,0,6,0);DockPanel.SetDock(remove,Dock.Right);row.Children.Add(remove);
            var thumb=new Button{Content=new Image{Source=image.Preview,Width=34,Height=22,Stretch=Stretch.Uniform},Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(0),Margin=new Thickness(0,0,6,0),ToolTip="Review attached screen image"};thumb.Click+=delegate{PreviewAgentAttachment(image,p);};DockPanel.SetDock(thumb,Dock.Left);row.Children.Add(thumb);
            var label=Ui.Text(image.SourceLabel+" · "+image.CapturedUtc.ToLocalTime().ToString("HH:mm")+" · "+p.Provider,10,Ui.Muted);label.TextTrimming=TextTrimming.CharacterEllipsis;label.VerticalAlignment=VerticalAlignment.Center;label.ToolTip="Only this reviewed image will be sent with your next prompt.";row.Children.Add(label);agentAttachmentChip.Child=row;agentAttachmentChip.Visibility=Visibility.Visible;
        }
        void PreviewAgentAttachment(AgentAttachment image,AgentProfile profile)
        {
            CloseAgentPreview();
            var panel=new DockPanel{Margin=new Thickness(16)};var note=Ui.Text(image.SourceLabel+" · "+image.CapturedUtc.ToLocalTime().ToString("g")+" · "+profile.Provider,12,Ui.White);note.TextWrapping=TextWrapping.Wrap;DockPanel.SetDock(note,Dock.Top);panel.Children.Add(note);
            panel.Children.Add(new Image{Source=image.Preview,Stretch=Stretch.Uniform,Margin=new Thickness(0,10,0,0)});
            var window=new Window{Owner=this,Title="Review screen attachment",Width=640,Height=440,MinWidth=320,MinHeight=220,Background=Ui.Card,Content=panel,Topmost=true,ShowInTaskbar=false};agentAttachmentPreview=window;window.Closed+=delegate{if(agentAttachmentPreview==window)agentAttachmentPreview=null;};window.Show();
        }
        void CloseAgentPreview(){if(agentAttachmentPreview!=null){var w=agentAttachmentPreview;agentAttachmentPreview=null;w.Close();}}
        void ClearAgentAttachment(string id){CloseAgentPreview();AgentAttachment image;if(agentAttachments.TryGetValue(id,out image)){agentAttachments.Remove(id);image.Dispose();}agentAttachmentTargets.Remove(id);}
        void CleanupAgentExperience(){CloseAgentPreview();foreach(var image in agentAttachments.Values)image.Dispose();agentAttachments.Clear();foreach(var image in agentRunImages.Values)image.Dispose();agentRunImages.Clear();agentAttachmentTargets.Clear();agentReplyTargets.Clear();agentScreenTarget=null;agentPasteTarget=null;}
        void ObserveAgentTransitions()
        {
            if(agentHub==null||state.Agents.Count==0)return;
            foreach(var p in state.Agents){var session=agentHub.Get(p.Id);bool before;agentObservedRunning.TryGetValue(p.Id,out before);agentObservedRunning[p.Id]=session.Running;
                if(before&&!session.Running&&(session.Status=="Done"||session.Status=="Needs attention")){
                    if(open&&pose==NotchPose.InlineAgents&&agentDetailId==p.Id)continue;
                    agentUnread.Add(p.Id);
                    if(!preview&&!capture&&!open&&!pinned&&!gameDetected&&!tabOverlapping&&pose==NotchPose.Compact&&settingsWindow==null&&agentEditor==null&&!contextMenuOpen&&Order().Contains("Agents")&&state.Settings.AgentCompletionPeeks&&!(focus.Running&&focus.Phase=="Focus"&&state.Settings.FocusNotifications!="Allow peeks")&&!NativeMethods.IsForegroundFullscreen(hwnd)){
                        ShowHud("\uE946",p.Name,session.Status=="Done"?"Finished · view":"Needs attention",double.NaN,3000);agentNoticeId=p.Id;agentNoticeUntil=DateTime.UtcNow.AddSeconds(3);
                    }
                }
            }
        }
        bool TryOpenAgentNotice()
        {
            if(DateTime.UtcNow>agentNoticeUntil||string.IsNullOrEmpty(agentNoticeId)||!Order().Contains("Agents"))return false;
            string id=agentNoticeId;agentNoticeId="";if(!state.Agents.Any(a=>a.Id==id))return false;SelectPage("Agents");ShowAgentConversation(id);return true;
        }
    }
}

