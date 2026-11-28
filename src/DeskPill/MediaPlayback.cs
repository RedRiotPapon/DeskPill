using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace DeskPill
{
    // Optimistic UI is bounded and local; stale acknowledgements cannot overwrite a newer click.
    public sealed class MediaPlaybackFeedback
    {
        int revision;
        string source;
        bool? desired;
        public bool Effective(string currentSource,bool observed)
        {
            if(desired.HasValue&&source!=currentSource)Cancel();
            return desired??observed;
        }
        public int Begin(string currentSource,bool observed)
        {
            bool next=!Effective(currentSource,observed);
            source=currentSource;desired=next;return ++revision;
        }
        public bool IsCurrent(int token){return token==revision&&desired.HasValue;}
        public bool Resolve(int token){if(!IsCurrent(token))return false;desired=null;return true;}
        public void Cancel(){desired=null;revision++;}
    }

    public sealed partial class IslandWindow
    {
        readonly MediaPlaybackFeedback playbackFeedback=new MediaPlaybackFeedback();
        Button musicPlayButton;
        bool? musicIconPlaying;
        bool MusicPlaying {get{return playbackFeedback.Effective(media.Current.Source,media.Current.Playing);}}
        void UpdateMusicPlayback()
        {
            if(musicPlayButton==null)return;
            bool playing=MusicPlaying;
            musicPlayButton.IsEnabled=media.Current.HasMedia;
            if(musicIconPlaying==playing)return;
            var icon=musicPlayButton.Content as TextBlock;
            if(icon==null)return;
            bool animate=musicIconPlaying.HasValue&&icon.IsLoaded&&Ui.AnimationsAllowed;
            musicIconPlaying=playing;
            icon.Text=playing?"\uE769":"\uE768";
            // Change on the click frame, then ease the new glyph into full contrast.
            icon.BeginAnimation(OpacityProperty,null);icon.Opacity=1;
            if(animate)icon.BeginAnimation(OpacityProperty,new DoubleAnimation(.65,1,TimeSpan.FromMilliseconds(100)){FillBehavior=FillBehavior.Stop});
            System.Windows.Automation.AutomationProperties.SetName(musicPlayButton,playing?"Pause":"Play");
        }
        Task ToggleMusicPlayback(){return RequestMusicPlayback(media.SetPlayingAsync);}
        static void ObservePlaybackFailure(Task task){task.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);}
        async Task RequestMusicPlayback(Func<bool,string,Task<bool?>> command)
        {
            var current=media.Current;
            if(!current.HasMedia)return;
            int token=playbackFeedback.Begin(current.Source,current.Playing);
            bool desired=MusicPlaying;
            UpdateValues();
            try{
                var task=command(desired,current.Source);
                if(await Task.WhenAny(task,Task.Delay(3000))!=task){
                    ObservePlaybackFailure(task);
                    if(playbackFeedback.Resolve(token)){UpdateValues();Notify("The player is taking too long to respond.");}
                    return;
                }
                bool? observed=await task;
                if(!playbackFeedback.IsCurrent(token))return;
                var latest=media.Current;
                if(observed.HasValue&&latest.Source==current.Source)latest.Playing=observed.Value;
                playbackFeedback.Resolve(token);UpdateValues();
                if(!observed.HasValue||observed.Value!=desired)Notify("The player didn't confirm the playback change.");
            }catch(Exception ex){if(playbackFeedback.Resolve(token)){UpdateValues();ReportError(ex);}}
        }
    }
}
