using System;
namespace DeskPill
{
    public sealed partial class IslandWindow
    {
        // Explicit opt-in enables the shortcut only; it never opens the microphone.
        bool SetDictationEnabled(bool enabled)
        {
            if(enabled&&!SpeechModels.IsReady(state.Settings.SpeechModel)){ShowSettings();if(settingsWindow!=null)settingsWindow.OpenSection("Dictation");return false;}
            bool previous=state.Settings.DictationEnabled;
            state.Settings.DictationEnabled=enabled;
            if(!Save()){state.Settings.DictationEnabled=previous;return false;}
            if(!enabled)CancelAgentDictation();
            if(dictation!=null)dictation.Configure();
            Notify(enabled?"Dictation enabled · hold Ctrl+Space to speak; release to insert.":"Dictation disabled · Ctrl+Space released to other apps.");
            return true;
        }
    }
}
