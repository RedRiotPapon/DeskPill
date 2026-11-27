using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DeskPill
{
    public sealed class ClipboardPalette : Window
    {
        readonly string[] items;
        readonly TextBox search;
        readonly ListBox results;
        readonly TextBlock status;
        readonly Action<string, bool> accept;
        readonly bool canPaste;
        public ClipboardPalette(IEnumerable<string> history, bool canPaste, string target, Action<string, bool> accept)
        {
            this.items = history.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().Take(50).ToArray();
            this.accept = accept;
            this.canPaste = canPaste;
            Title = "DeskPill Clipboard";
            Width = 480;
            Height = 440;
            MinWidth = 380;
            MinHeight = 300;
            Background = Ui.Hex("#090909");
            Foreground = Ui.White;
            FontFamily = new FontFamily("Segoe UI");
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            var root = new DockPanel
            {
                Margin = new Thickness(20)
            };
            var header = Ui.Column(Ui.Text("Clipboard", 23, Ui.White), Ui.Text(canPaste ? "Enter pastes into " + target + " · Ctrl+Enter copies" : "Copy only · Use Ctrl+Alt+V from your target app to paste", 11, Ui.Muted));
            search = Ui.Input("");
            search.Margin = new Thickness(0, 14, 0, 12);
            header.Children.Add(search);
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);
            System.Windows.Automation.AutomationProperties.SetName(search, "Search clipboard");
            var footer = Ui.Column();
            status = Ui.Text("", 11, Ui.Muted);
            status.TextWrapping = TextWrapping.Wrap;
            footer.Children.Add(status);
            var copy = Ui.Button("Copy", () => Choose(false));
            var paste = Ui.Button("Paste", () => Choose(true), true);
            paste.IsEnabled = canPaste;
            footer.Children.Add(Ui.Row(paste, copy, Ui.Button("Cancel", Close)));
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);
            results = new ListBox
            {
                Background = Brushes.Transparent,
                Foreground = Ui.White,
                BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            results.SelectionChanged += delegate
            {
                bool selected = results.SelectedItem != null;
                copy.IsEnabled = selected;
                paste.IsEnabled = selected && canPaste;
            };
            results.MouseDoubleClick += delegate (object sender, MouseButtonEventArgs e)
            {
                if (ItemsControl.ContainerFromElement(results, e.OriginalSource as DependencyObject) is ListBoxItem)
                    Choose(canPaste);
            };
            root.Children.Add(results);
            Content = root;
            search.TextChanged += delegate
            {
                Filter();
            };
            PreviewKeyDown += delegate (object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape)
                {
                    Close();
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter)
                {
                    Choose(canPaste && (Keyboard.Modifiers & ModifierKeys.Control) == 0);
                    e.Handled = true;
                }
                else if (e.Key == Key.Down || e.Key == Key.Up)
                {
                    int step = e.Key == Key.Down ? 1 : -1;
                    if (results.Items.Count == 0)
                    {
                        e.Handled = true;
                        return;
                    }

                    results.SelectedIndex = Math.Max(0, Math.Min(results.Items.Count - 1, results.SelectedIndex + step));
                    results.ScrollIntoView(results.SelectedItem);
                    e.Handled = true;
                }
            };
            Loaded += delegate
            {
                search.Focus();
            };
            Filter();
        }

        static readonly ControlTemplate ItemTemplate = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ListBoxItem'><Border x:Name='row' CornerRadius='8' BorderThickness='1' BorderBrush='Transparent' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}'><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property='IsSelected' Value='True'><Setter TargetName='row' Property='BorderBrush' Value='{DynamicResource DeskPillAccent}'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='row' Property='BorderBrush' Value='{DynamicResource DeskPillAccent}'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
        void Filter()
        {
            results.Items.Clear();
            foreach (string value in ClipboardSearch.Find(items, search.Text))
            {
                var text = Ui.Text(value.Replace("\r", " ").Replace("\n", " "), 13, Ui.White);
                text.TextWrapping = TextWrapping.Wrap;
                text.MaxHeight = 42;
                results.Items.Add(new ListBoxItem { Template = ItemTemplate, Content = text, Tag = value, Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 5), Background = Ui.Card, BorderThickness = new Thickness(0) });
            }

            results.SelectedIndex = results.Items.Count > 0 ? 0 : -1;
            status.Text = results.Items.Count == 0 ? (items.Length == 0 ? "Copy some text to start your history." : "No matches. Try another search.") : results.Items.Count + " items · newest first";
        }

        void Choose(bool paste)
        {
            var item = results.SelectedItem as ListBoxItem;
            if (item == null)
                return;
            if (paste && !canPaste)
                return;
            accept((string)item.Tag, paste);
        }
    }

    public sealed class NotificationHistoryWindow : Window
    {
        readonly NotificationHistory history;
        readonly StackPanel rows = new StackPanel();
        readonly Action<DesktopNotification> open;
        readonly Action<string> copy;
        public NotificationHistoryWindow(NotificationHistory history, Action<DesktopNotification> open, Action<string> copy)
        {
            this.history = history;
            this.open = open;
            this.copy = copy;
            Title = "DeskPill Recent Notifications";
            Width = 470;
            Height = 540;
            MinWidth = 360;
            MinHeight = 300;
            Background = Ui.Hex("#090909");
            Foreground = Ui.White;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            var root = new DockPanel
            {
                Margin = new Thickness(20)
            };
            var header = Ui.Column(Ui.Text("Recent notifications", 23, Ui.White), Ui.Text("Last 50 arrivals · kept only while DeskPill is running", 11, Ui.Muted));
            header.Margin = new Thickness(0, 0, 0, 14);
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);
            var clear = Ui.Button("Clear recent", () => history.Clear());
            DockPanel.SetDock(clear, Dock.Bottom);
            root.Children.Add(clear);
            root.Children.Add(new ScrollViewer { Content = rows });
            Content = root;
            history.Changed += Refresh;
            Closed += delegate
            {
                history.Changed -= Refresh;
            };
            PreviewKeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.Key == Key.Escape)
                    Close();
            };
            Refresh();
        }

        void Refresh()
        {
            rows.Children.Clear();
            if (history.Items.Length == 0)
            {
                rows.Children.Add(Ui.Text("New notifications will appear here.", 13, Ui.Muted));
                return;
            }

            foreach (var item in history.Items)
            {
                var current = item;
                var title = Ui.Text(item.Title, 14, Ui.White);
                title.TextWrapping = TextWrapping.Wrap;
                var body = Ui.Text(item.Body, 12, Ui.Muted);
                body.TextWrapping = TextWrapping.Wrap;
                body.TextTrimming = TextTrimming.None;
                var content = Ui.Column(Ui.Text(item.AppName + " · " + item.CreatedAt.ToLocalTime().ToString("t"), 11, Ui.Muted), title, body);
                var launch = Ui.Button("Open app", () => open(current));
                launch.IsEnabled = NotificationLauncher.CanOpen(item.AppId);
                content.Children.Add(Ui.Row(launch, Ui.Button("Copy", () => copy(current.Title + "\n" + current.Body)), Ui.Button("Remove", () => history.Remove(current))));
                rows.Children.Add(new Border { Background = Ui.Card, Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 10), CornerRadius = new CornerRadius(12), Child = content });
            }
        }
    }
}
