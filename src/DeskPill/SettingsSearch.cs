using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace DeskPill
{
    public sealed partial class SettingsWindow
    {
        sealed class SettingsSearchEntry
        {
            public string Page,Label;
            public FrameworkElement Target;
            public Button Navigation;
        }
        readonly Dictionary<FrameworkElement,Tuple<string,FrameworkElement>> settingsSearchPrerequisites=new Dictionary<FrameworkElement,Tuple<string,FrameworkElement>>();
        readonly List<SettingsSearchEntry> settingsSearchEntries=new List<SettingsSearchEntry>();
        readonly List<Button> settingsSearchResults=new List<Button>();
        ScrollViewer settingsScroll;
        TextBox settingsSearch;
        Button clearSettingsSearch,selectedSettingsPage,searchReturnPage;
        bool changingSettingsSearch;

        FrameworkElement BuildSettingsSearch()
        {
            var panel=new StackPanel {Margin=new Thickness(16,0,0,20)};
            var label=Ui.Text("Search settings",12,Ui.Muted);label.Margin=new Thickness(0,0,0,5);panel.Children.Add(label);
            var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
            settingsSearch=Ui.Input("");settingsSearch.Margin=new Thickness(0);
            settingsSearch.ToolTip="Find an option · Ctrl+F";
            AutomationProperties.SetName(settingsSearch,"Search settings");
            row.Children.Add(settingsSearch);
            clearSettingsSearch=Ui.Button("Clear",delegate {ClearSettingsSearch(true);settingsSearch.Focus();});
            clearSettingsSearch.Margin=new Thickness(6,0,0,0);clearSettingsSearch.IsEnabled=false;
            AutomationProperties.SetName(clearSettingsSearch,"Clear settings search");
            Grid.SetColumn(clearSettingsSearch,1);row.Children.Add(clearSettingsSearch);panel.Children.Add(row);
            settingsSearch.TextChanged+=delegate {if(!changingSettingsSearch)SearchSettings();};
            PreviewKeyDown+=delegate(object sender,KeyEventArgs e){
                if(e.Key==Key.F&&Keyboard.Modifiers==ModifierKeys.Control){settingsSearch.Focus();settingsSearch.SelectAll();e.Handled=true;}
                else if(e.Key==Key.Escape&&!string.IsNullOrEmpty(settingsSearch.Text)){ClearSettingsSearch(true);settingsSearch.Focus();e.Handled=true;}
                else if(e.Key==Key.Enter&&settingsSearch.IsKeyboardFocusWithin&&settingsSearchResults.Count>0){settingsSearchResults[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));e.Handled=true;}
            };
            return panel;
        }

        void IndexSettingsPage(string title,StackPanel panel,Button navigationButton)
        {
            settingsSearchEntries.Add(new SettingsSearchEntry {Page=title,Label=title,Target=panel,Navigation=navigationButton});
            var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase){title};
            Action<DependencyObject> visit=null;
            visit=delegate(DependencyObject node){
                // Search labels and explanations, never values users type (including keys and paths).
                if(node is TextBox||node is PasswordBox||node is ComboBox)return;
                var text=node as TextBlock;var control=node as ContentControl;
                var slider=node as Slider;
                string label=slider!=null?AutomationProperties.GetName(slider):text!=null?text.Text:control!=null?control.Content as string:null;
                if(slider!=null&&Convert.ToString(slider.Tag).StartsWith("Hover"))label="Hover · "+label;
                if(!string.IsNullOrWhiteSpace(label)&&seen.Add(label.Trim()))
                    settingsSearchEntries.Add(new SettingsSearchEntry {Page=title,Label=label.Trim(),Target=node as FrameworkElement,Navigation=navigationButton});
                if(slider!=null)return;
                foreach(object child in LogicalTreeHelper.GetChildren(node)){var d=child as DependencyObject;if(d!=null)visit(d);}
            };
            visit(panel);
        }

        static string SearchAliases(string text)
        {
            string value=text.ToLowerInvariant();
            if(value.Contains("overall notch size"))value+=" scale height dimensions";
            if(value.Contains("corner"))value+=" shape radius rounding";
            if(value.Contains("processing device"))value+=" gpu cpu vulkan automatic";
            if(value.Contains("opacity"))value+=" transparency translucent";
            if(value.Contains("dictation"))value+=" speech voice microphone";
            if(value.Contains("start with windows")||value.Contains("when i sign in"))value+=" startup launch login boot";
            if(value.Contains("hover"))value+=" delay timing speed";
            if(value.Contains("pages"))value+=" tabs visibility hide reorder";
            return value;
        }

        void SearchSettings()
        {
            string query=settingsSearch.Text.Trim();clearSettingsSearch.IsEnabled=settingsSearch.Text.Length>0;
            if(query.Length==0){ClearSettingsSearch(true);return;}
            if(searchReturnPage==null)searchReturnPage=selectedSettingsPage;
            string[] words=query.Split(new[]{' ','\t','\r','\n'},StringSplitOptions.RemoveEmptyEntries);
            var matches=settingsSearchEntries.Where(entry=>words.All(word=>SearchAliases(entry.Page+" "+entry.Label).IndexOf(word,StringComparison.OrdinalIgnoreCase)>=0))
                .OrderBy(entry=>string.Equals(entry.Label,query,StringComparison.OrdinalIgnoreCase)?0:entry.Label.StartsWith(query,StringComparison.OrdinalIgnoreCase)?1:entry.Label==entry.Page?3:2)
                .ThenBy(entry=>entry.Label.Length>90?1:0).Take(30).ToArray();
            content.Children.Clear();settingsSearchResults.Clear();
            var results=new StackPanel();results.Children.Add(Ui.Text("Search results",21,Ui.White));
            Note(results,matches.Length==0?"No matching settings. Try “dictation”, “opacity” or “startup”.":"Select an option to open its settings page. Your unsaved changes stay here.");
            foreach(var match in matches){
                var entry=match;
                var text=new StackPanel();text.Children.Add(Ui.Text(entry.Page,11,Ui.Muted));
                var label=Ui.Text(entry.Label,13,Ui.White);label.TextWrapping=TextWrapping.Wrap;label.MaxHeight=52;label.Margin=new Thickness(0,4,0,0);text.Children.Add(label);
                var prerequisite=SettingsSearchPrerequisite(entry);
                if(prerequisite!=null){var hint=Ui.Text(prerequisite.Item1,11,Ui.Muted);hint.TextWrapping=TextWrapping.Wrap;hint.Margin=new Thickness(0,6,0,0);text.Children.Add(hint);}
                var button=Ui.Button("",delegate {OpenSettingsSearchResult(entry);});button.Content=text;
                button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.Margin=new Thickness(0,0,0,8);button.Padding=new Thickness(12);
                button.ToolTip="Open "+entry.Page;AutomationProperties.SetName(button,entry.Page+": "+entry.Label);
                settingsSearchResults.Add(button);results.Children.Add(button);
            }
            content.Children.Add(results);settingsScroll.ScrollToTop();
            foreach(var b in navigation){b.Background=Ui.Hex("#151515");b.Foreground=Ui.Muted;}
        }

        Tuple<string,FrameworkElement> SettingsSearchPrerequisite(SettingsSearchEntry entry)
        {
            for(var current=entry.Target;current!=null;current=current.Parent as FrameworkElement){
                Tuple<string,FrameworkElement> prerequisite;
                if(current.Visibility!=Visibility.Visible&&settingsSearchPrerequisites.TryGetValue(current,out prerequisite))return prerequisite;
            }
            return null;
        }

        void OpenSettingsSearchResult(SettingsSearchEntry entry)
        {
            entry.Navigation.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(delegate {
                var prerequisite=SettingsSearchPrerequisite(entry);
                FrameworkElement target=prerequisite==null?entry.Target:prerequisite.Item2;
                while(target!=null&&!target.IsVisible)target=target.Parent as FrameworkElement;
                if(target!=null){target.BringIntoView();if(target.Focusable)target.Focus();}
            }));
        }

        void ClearSettingsSearch(bool restore)
        {
            if(settingsSearch==null)return;
            var previous=searchReturnPage;searchReturnPage=null;
            changingSettingsSearch=true;settingsSearch.Clear();changingSettingsSearch=false;
            clearSettingsSearch.IsEnabled=false;settingsSearchResults.Clear();
            if(restore&&previous!=null)previous.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
    }
}
