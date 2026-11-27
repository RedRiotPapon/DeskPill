using System;
namespace DeskPill
{
    public sealed partial class IslandWindow
    {
        // Explicit opt-in enables the shortcut only; it never opens the microphone.
        bool SetDictationEnabled(bool enabled)
        {
            if(enabled&&!SpeechAssets.Ready){ShowSettings();if(settingsWindow!=null)settingsWindow.OpenSection("Dictation");return false;}
            bool previous=state.Settings.DictationEnabled;
            state.Settings.DictationEnabled=enabled;
            if(!Save()){state.Settings.DictationEnabled=previous;return false;}
            if(!enabled)CancelAgentDictation();
            if(dictation!=null)dictation.Configure();
            Notify(enabled?"Dictation enabled · hold Win+Space to speak; release to insert.":"Dictation disabled · Windows language shortcut restored.");
            return true;
        }
    }
}
