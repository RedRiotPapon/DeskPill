using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace DeskPill
{
    public sealed partial class IslandWindow
    {
        bool agentVoiceFinishPending, agentVoiceToggleSession;

        internal bool AgentVoiceActive { get { return agentSpeech != null; } }

        // Returning true also consumes a refused start: never paste into another app
        // when the user asked to dictate into the visible agent draft.
        internal bool TryBeginAgentDictation()
        {
            if (pose != NotchPose.InlineAgents) return false;
            if (!AgentVoiceActive)
            {
                agentVoiceToggleSession = false;
                agentVoiceHeld = true;
                BeginAgentVoice(agentDetailId);
            }
            return true;
        }
        internal void BeginAgentVoiceFromHome(string id)
        {
            if (stopping || agentCapturing || !state.Agents.Any(a => a.Id == id)) return;
            ShowAgentConversation(id);
            agentVoiceToggleSession = true;
            agentVoiceHeld = false;
            BeginAgentVoice(id);
        }
        internal void FinishAgentDictation() { FinishAgentVoice(); }
        internal void CancelAgentDictation() { CancelAgentVoice(); }

        void WireAgentVoiceButton(Button button, string id)
        {
            Action describe = delegate
            {
                string help = !SpeechAssets.Ready ? "Set up optional on-device English speech" :
                    state.Settings.AgentVoiceToggle ? "Click to dictate; click again to review · Esc cancels · 90-second limit" :
                    "Hold mouse, Space or Enter to dictate; release to review · Esc cancels";
                button.ToolTip = help + " · Right-click for voice mode";
                AutomationProperties.SetName(button, "Dictate agent prompt");
                AutomationProperties.SetHelpText(button, Convert.ToString(button.ToolTip));
            };
            Action toggle = delegate
            {
                if (AgentVoiceActive) FinishAgentVoice();
                else
                {
                    agentVoiceToggleSession = true;
                    agentVoiceHeld = false;
                    BeginAgentVoice(id);
                }
            };
            var menu = new ContextMenu { Background = Ui.Card, Foreground = Ui.White };
            var hold = new MenuItem { Header = "Hold to dictate", IsCheckable = true };
            var click = new MenuItem { Header = "Click to start and stop", IsCheckable = true };
            hold.Click += delegate { CancelAgentVoice(); state.Settings.AgentVoiceToggle = false; Save(); describe(); };
            click.Click += delegate { CancelAgentVoice(); state.Settings.AgentVoiceToggle = true; Save(); describe(); };
            menu.Items.Add(hold); menu.Items.Add(click);
            menu.Closed += delegate { contextMenuOpen=false; };
            menu.Opened += delegate { contextMenuOpen=true; hold.IsChecked = !state.Settings.AgentVoiceToggle; click.IsChecked = state.Settings.AgentVoiceToggle; };
            button.ContextMenu = menu;
            button.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                e.Handled = true;
                button.Focus();
                if (state.Settings.AgentVoiceToggle) { toggle(); return; }
                if (AgentVoiceActive) return;
                agentVoiceToggleSession = false; agentVoiceHeld = true;
                if (!button.CaptureMouse()) { agentVoiceHeld = false; return; }
                BeginAgentVoice(id);
            };
            button.PreviewMouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (!agentVoiceToggleSession && agentVoiceHeld) FinishAgentVoice();
                if (button.IsMouseCaptured) button.ReleaseMouseCapture();
            };
            button.LostMouseCapture += delegate { if (!agentVoiceToggleSession && agentVoiceHeld) FinishAgentVoice(); };
            button.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape && AgentVoiceActive) { CancelAgentVoice(); e.Handled = true; return; }
                if (e.Key != Key.Space && e.Key != Key.Enter) return;
                e.Handled = true;
                if (e.IsRepeat) return;
                if (state.Settings.AgentVoiceToggle) { toggle(); return; }
                if (AgentVoiceActive) return;
                agentVoiceToggleSession = false; agentVoiceHeld = true; BeginAgentVoice(id);
            };
            button.PreviewKeyUp += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Space && e.Key != Key.Enter) return;
                e.Handled = true;
                if (!agentVoiceToggleSession && agentVoiceHeld) FinishAgentVoice();
            };
            button.LostKeyboardFocus += delegate { if (!agentVoiceToggleSession && agentVoiceHeld) FinishAgentVoice(); };
            // Automation invocation has no held input; treat its explicit activation as a toggle.
            button.Click += delegate { toggle(); };
            describe();
        }

        async void BeginAgentVoice(string id)
        {
            if (AgentVoiceActive || agentCapturing || stopping || pose != NotchPose.InlineAgents || agentPrompt == null ||
                id != agentDetailId || state.SelectedAgent != id || !state.Agents.Any(a => a.Id == id))
            { if (!AgentVoiceActive) ReleaseAgentVoiceHold(); return; }
            if (Agents.Get(id).Running) { ReleaseAgentVoiceHold(); return; }
            if (!SpeechAssets.Ready) { ReleaseAgentVoiceHold(); ShowAgentSpeechSetup(); return; }
            agentSpeech = new CancellationTokenSource();
            var token = agentSpeech;
            agentVoiceTarget = id;
            int revision = ++agentVoiceRevision;
            agentVoiceFinishPending = false;
            RefreshAgentViews();
            agentWave.Visibility = Visibility.Visible; agentWave.Processing = false;
            agentDetailStatus.Text = "Preparing microphone…";
            try
            {
                var recorder = await Task.Run(() => new DictationRecorder());
                if (token.IsCancellationRequested || stopping || revision != agentVoiceRevision)
                { await Task.Run((Action)recorder.Dispose); return; }
                agentRecording = recorder; agentVoiceStarted = DateTime.UtcNow;
                agentVoiceTimer.Tick -= AgentVoiceTick; agentVoiceTimer.Tick += AgentVoiceTick; agentVoiceTimer.Start();
                agentDetailStatus.Text = agentVoiceToggleSession ? "Listening · click mic to review · Esc cancels" : "Listening · release to review · Esc cancels";
                if (agentVoiceFinishPending || (!agentVoiceToggleSession && !agentVoiceHeld)) FinishAgentVoice();
            }
            catch (Exception e)
            {
                if (!token.IsCancellationRequested && revision == agentVoiceRevision)
                { CancelAgentVoice(); if (agentDetailStatus != null) agentDetailStatus.Text = e.Message; }
            }
        }
        void AgentVoiceTick(object sender, EventArgs e)
        {
            if (agentRecording == null) return;
            if (pose != NotchPose.InlineAgents || agentDetailId != agentVoiceTarget || state.SelectedAgent != agentVoiceTarget)
            { CancelAgentVoice(); return; }
            if (agentWave != null) agentWave.Level = agentRecording.Level;
            if ((!agentVoiceToggleSession && !agentVoiceHeld) || agentRecording.Failed || DateTime.UtcNow - agentVoiceStarted >= TimeSpan.FromSeconds(90)) FinishAgentVoice();
        }
        async void FinishAgentVoice()
        {
            if (!AgentVoiceActive) { ReleaseAgentVoiceHold(); return; }
            agentVoiceFinishPending = true;
            ReleaseAgentVoiceHold();
            if (agentRecording == null) return;
            var recorder = agentRecording; agentRecording = null;
            var token = agentSpeech; string id = agentVoiceTarget; int revision = agentVoiceRevision;
            agentVoiceTimer.Stop();
            if (agentWave != null) { agentWave.Processing = true; agentWave.Level = 0; }
            if (agentDetailStatus != null) agentDetailStatus.Text = "Turning speech into a draft…";
            try
            {
                byte[] wave = await Task.Run(delegate { try { return recorder.Finish(); } finally { recorder.Dispose(); } });
                token.Token.ThrowIfCancellationRequested();
                string text = await SpeechTranscriber.TranscribeAsync(wave, token.Token);
                if (token.IsCancellationRequested || stopping || revision != agentVoiceRevision ||
                    pose != NotchPose.InlineAgents || agentDetailId != id || state.SelectedAgent != id || agentPrompt == null)
                { if (!token.IsCancellationRequested && revision == agentVoiceRevision) CancelAgentVoice(); return; }
                var profile = state.Agents.FirstOrDefault(a => a.Id == id);
                if (profile == null) { CancelAgentVoice(); return; }
                if (!string.IsNullOrWhiteSpace(text))
                {
                    string draft = (agentPrompt.Text.Length == 0 ? "" : agentPrompt.Text + " ") + text;
                    agentPrompt.Text = draft.Substring(0, Math.Min(agentPrompt.MaxLength > 0 ? agentPrompt.MaxLength : 16000, draft.Length));
                    agentPrompt.CaretIndex = agentPrompt.Text.Length;
                }
                CancelAgentVoice();
                agentDetailStatus.Text = string.IsNullOrWhiteSpace(text) ? "No speech detected · try again" : "Review your words, then Send · " + profile.Name;
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                if (!token.IsCancellationRequested && revision == agentVoiceRevision)
                { CancelAgentVoice(); if (agentDetailStatus != null) agentDetailStatus.Text = e.Message; }
            }
        }
        void ReleaseAgentVoiceHold()
        {
            agentVoiceHeld = false;
            if (agentVoice != null && agentVoice.IsMouseCaptured) agentVoice.ReleaseMouseCapture();
        }
        void CancelAgentVoice()
        {
            ++agentVoiceRevision; agentVoiceFinishPending = false;
            agentVoiceTimer.Stop();
            var token = agentSpeech; agentSpeech = null;
            if (token != null) token.Cancel();
            var recorder = agentRecording; agentRecording = null;
            if (recorder != null) Task.Run((Action)recorder.Dispose);
            ReleaseAgentVoiceHold();
            if (agentWave != null) { agentWave.Visibility = Visibility.Collapsed; agentWave.Processing = false; agentWave.Level = 0; }
            RefreshAgentViews();
        }
    }
}
