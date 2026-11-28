using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace DeskPill
{
    public static class StartupRegistration
    {
        const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
        public static string Executable {get{return Process.GetCurrentProcess().MainModule.FileName;}}
        public static string Read()
        {
            using(var key=Registry.CurrentUser.OpenSubKey(RunKey))return key==null?null:key.GetValue("DeskPill") as string;
        }
        public static void Write(string command)
        {
            using(var key=Registry.CurrentUser.CreateSubKey(RunKey)){
                if(command==null)key.DeleteValue("DeskPill",false);else key.SetValue("DeskPill",command);
            }
        }
        public static void SetEnabled(bool enabled){Write(enabled?"\""+Executable+"\"":null);}
        // Persist and the OS registration form one change; preserve the old command on failure.
        public static void Change(Settings settings,bool enabled,string executable,Func<string> read,Action<string> write,Action persist)
        {
            string previousCommand=read();bool previous=settings.StartWithWindows;
            write(enabled?"\""+executable+"\"":null);
            settings.StartWithWindows=enabled;
            try{persist();}
            catch{
                settings.StartWithWindows=previous;
                write(previousCommand);
                throw;
            }
        }
    }

    public sealed partial class IslandWindow
    {
        void ToggleStartup()
        {
            try{
                StartupRegistration.Change(state.Settings,!state.Settings.StartWithWindows,StartupRegistration.Executable,StartupRegistration.Read,StartupRegistration.Write,delegate{store.Save(state);});
                if(settingsWindow!=null)settingsWindow.SyncStartupPreference(state.Settings.StartWithWindows);
            }catch(Exception ex){Notify("Couldn't change Windows startup: "+ex.Message);}
        }
    }
}
