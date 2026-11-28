using System;
using System.Collections.Generic;
using System.Linq;

namespace DeskPill
{
    public sealed partial class SettingsWindow
    {
        static readonly HashSet<string> LivePreviewFields=new HashSet<string>(new[]{
            "Monitor","OffsetY","ShowClock","ShowMascot","AutoShrinkOnTabs","MinimalNotch","ShowHudPopups","OpenMode","FastAnimations","StatusRing","Sound","HideFullscreen",
            "FocusMinutes","ShortBreakMinutes","LongBreakMinutes","BottleMl","WaterGoalMl"
        });
        void PreviewField(string field){if(LivePreviewFields.Contains(field))this.previewPreferences(draft,false);}
    }

    public sealed partial class IslandWindow
    {
        string previewContentIdentity;
        string previewOriginalPage;
        double motionOffsetPreview;
        string motionMonitorPreview;
        bool PreviewTimerChanged {get{return preferencesPreview!=null&&(MotionPreferences.FocusMinutes!=state.Settings.FocusMinutes||MotionPreferences.ShortBreakMinutes!=state.Settings.ShortBreakMinutes||MotionPreferences.LongBreakMinutes!=state.Settings.LongBreakMinutes);}}
        TimeSpan DisplayFocusRemaining
        {
            get{
                var remaining=focus.Remaining(DateTime.UtcNow);
                if(preferencesPreview!=null&&!focus.Running&&focus.Phase=="Focus"&&remaining==TimeSpan.FromMinutes(state.Settings.FocusMinutes))
                    return TimeSpan.FromMinutes(MotionPreferences.FocusMinutes);
                return remaining;
            }
        }
        void PreviewPresentation(Settings settings,bool restoring)
        {
            Ui.MotionEnabled=!capture&&!settings.FastAnimations;
            string identity=settings.ShowClock+"|"+settings.ShowMascot+"|"+settings.StatusRing+"|"+settings.WaterGoalMl+"|"+settings.BottleMl+"|"+settings.FocusMinutes+"|"+settings.ShortBreakMinutes+"|"+settings.LongBreakMinutes+"|"+string.Join(",",settings.PageOrder)+"|"+string.Join(",",settings.HiddenPages);
            string oldPage=page;
            if(!restoring&&previewOriginalPage==null)previewOriginalPage=page;
            if(restoring&&previewOriginalPage!=null&&Order().Contains(previewOriginalPage))page=previewOriginalPage;
            if(restoring)previewOriginalPage=null;
            if(!Order().Contains(page))page=Order()[0];
            if(identity!=previewContentIdentity||page!=oldPage){previewContentIdentity=identity;if(!IsInlinePose(pose))RebuildCurrentPage(true);BuildDockTray(motion.TopOffset);SyncDockStates();OnMotionFrame();}
            if(!settings.AutoShrinkOnTabs)tabOverlapping=false;
            if(!open&&pose!=NotchPose.NotificationPeek&&pose!=NotchPose.HudPill&&pose!=NotchPose.Dictation){pose=RestingClosedPose();SnapPoseImmediate();}
            UpdateValues();
        }
    }
}
