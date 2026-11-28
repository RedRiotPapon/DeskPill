using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace DeskPill
{
    // This screen explains an opt-in. It never downloads, enables speech or opens a microphone.
    public sealed class DictationWelcomeWindow : Window
    {
        readonly Func<bool,bool> choose;
        readonly TextBlock error;
        bool completed;
        public DictationWelcomeWindow(Func<bool,bool> choose)
        {
            this.choose=choose;
            Title="Welcome to DeskPill";
            Width=550;Height=600;MinWidth=460;MinHeight=480;
            MaxHeight=Math.Max(480,SystemParameters.WorkArea.Height);
            WindowStartupLocation=WindowStartupLocation.CenterScreen;
            Background=Ui.Hex("#090909");Foreground=Ui.White;
            FontFamily=new FontFamily("Segoe UI");ShowInTaskbar=true;
            var body=new StackPanel {Margin=new Thickness(32,26,32,26)};
            var heading=Copy("Your voice, one\nshortcut away.",30,Ui.White);
            heading.FontWeight=FontWeights.SemiBold;heading.LineHeight=37;
            body.Children.Add(heading);
            var intro=Copy("Add optional English dictation to DeskPill. Speak into any text field, with recognition running on your PC.",14,Ui.Muted);
            intro.Margin=new Thickness(0,12,0,24);body.Children.Add(intro);

            var sample=new Grid {Margin=new Thickness(0,0,0,24)};
            sample.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
            sample.ColumnDefinitions.Add(new ColumnDefinition());
            var keys=Ui.Text("Ctrl + Space",16,Ui.White);keys.FontWeight=FontWeights.SemiBold;
            sample.Children.Add(keys);
            var wave=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center};
            foreach(double h in new[]{6.0,13,23,32,20,10,26,36,22,12,6})
                wave.Children.Add(new Rectangle {Width=3,Height=h,RadiusX=1.5,RadiusY=1.5,Fill=Ui.Green,Margin=new Thickness(2,0,2,0),VerticalAlignment=VerticalAlignment.Center});
            Grid.SetColumn(wave,1);sample.Children.Add(wave);
            body.Children.Add(new Border {Background=Ui.Card,BorderBrush=Ui.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(14),Padding=new Thickness(18,18,18,0),Child=sample});
            var instruction=Copy("Hold to speak. Release to insert. Esc cancels.",12,Ui.Muted);
            instruction.Margin=new Thickness(0,10,0,22);body.Children.Add(instruction);
            var guidance=Copy("Choose a lightweight model or optional GPU processing. You’ll see download sizes and requirements before installing anything.",13,Ui.White);
            body.Children.Add(guidance);
            var later=Copy("You can always set this up later in Settings → Dictation.",12,Ui.Muted);
            later.Margin=new Thickness(0,8,0,20);body.Children.Add(later);
            error=Copy("",12,Ui.White);error.Visibility=Visibility.Collapsed;
            var actions=new StackPanel {Margin=new Thickness(32,8,32,26)};
            actions.Children.Add(error);
            var setup=Ui.Button("Set up dictation",delegate {Complete(true);},true);
            setup.FontSize=14;setup.MinHeight=40;setup.Margin=new Thickness(0,0,0,8);setup.IsDefault=true;
            actions.Children.Add(setup);
            var skip=Ui.Button("Continue without dictation",delegate {Complete(false);});
            skip.FontSize=13;skip.MinHeight=36;skip.Margin=new Thickness(0);actions.Children.Add(skip);
            var root=new Grid();root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
            root.Children.Add(new ScrollViewer {Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
            Grid.SetRow(actions,1);root.Children.Add(actions);Content=root;
            PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Escape){Complete(false);e.Handled=true;}};
            Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(!completed&&!Accept(false))e.Cancel=true;};
            Loaded+=delegate {if(Ui.AnimationsAllowed)body.BeginAnimation(OpacityProperty,new DoubleAnimation(.65,1,TimeSpan.FromMilliseconds(180)) {FillBehavior=FillBehavior.Stop});};
        }
        static TextBlock Copy(string text,double size,Brush color)
        {
            var block=Ui.Text(text,size,color);block.TextWrapping=TextWrapping.Wrap;block.TextTrimming=TextTrimming.None;return block;
        }
        bool Accept(bool setup)
        {
            if(choose(setup)){completed=true;return true;}
            error.Text="Couldn’t save your choice. Please try again.";error.Visibility=Visibility.Visible;error.Margin=new Thickness(0,0,0,12);return false;
        }
        void Complete(bool setup){if(!completed&&Accept(setup))Close();}
    }

    public sealed partial class IslandWindow
    {
        DictationWelcomeWindow dictationWelcome;
        void MaybeShowDictationWelcome()
        {
            if(preview||capture||stopping||state.DictationWelcomeCompleted||dictationWelcome!=null||settingsWindow!=null||gameDetected)return;
            // Preserve an already-enabled profile, including a manually prepared fresh profile.
            if(state.Settings.DictationEnabled){state.DictationWelcomeCompleted=true;if(!Save(false))state.DictationWelcomeCompleted=false;return;}
            bool setupRequested=false;
            dictationWelcome=new DictationWelcomeWindow(delegate(bool setup){
                if(stopping)return true;
                state.DictationWelcomeCompleted=true;
                if(!Save(false)){state.DictationWelcomeCompleted=false;return false;}
                setupRequested=setup;return true;
            });
            dictationWelcome.Closed+=delegate{
                dictationWelcome=null;
                if(setupRequested&&!stopping){ShowSettings();if(settingsWindow!=null)settingsWindow.OpenSection("Dictation");}
            };
            dictationWelcome.Show();
        }
    }
}
