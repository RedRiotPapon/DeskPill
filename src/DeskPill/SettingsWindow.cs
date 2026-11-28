using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace DeskPill
{
    public sealed class SettingsWindow : Window
    {
        readonly AppState state;
        readonly Action apply;
        readonly Action<string> previewTheme;
        readonly Action<Settings,bool> previewPreferences;
        StackPanel glassPanel;
        readonly string savedTheme;
        readonly Settings savedAppearance;
        readonly Dictionary<string, TextBox> customColors = new Dictionary<string, TextBox>();
        RadioButton customChoice;
        TextBlock customHint;
        bool themeCommitted;
        readonly Action<string> notify;
        readonly Settings draft;
        readonly List<Action> readers = new List<Action>();
        readonly StackPanel content = new StackPanel();
        readonly List<Button> navigation = new List<Button>();
        bool startupTouched, clearKey;
        CheckBox startupChoice;
        System.Threading.CancellationTokenSource speechInstall;
        PasswordBox apiKey;
        TextBlock keyStatus;
        ListBox pageList;
        public SettingsWindow(AppState state, Action apply, Action<string> notify, Action<string> previewTheme = null, IEnumerable<DesktopNotification> knownNotifications = null, Action<Settings,bool> previewPreferences = null)
        {
            this.state = state;
            this.apply = apply;
            this.notify = notify;
            draft = Clone(state.Settings);
            savedTheme = Themes.Normalize(draft.Theme);
            savedAppearance = Clone(state.Settings);
            Themes.ConfigureCustom(draft);
            this.previewTheme = previewTheme ?? Themes.Apply;
            this.previewPreferences=previewPreferences??delegate{};
            Closed += delegate { if (!themeCommitted) { Themes.ConfigureCustom(savedAppearance); this.previewTheme(savedTheme); this.previewPreferences(savedAppearance,false); } };
            Title = "DeskPill Settings";
            Width = 820;
            Height = 620;
            MinWidth = 720;
            MinHeight = 500;
            Background = Ui.Hex("#090909");
            Foreground = Ui.White;
            FontFamily = new FontFamily("Segoe UI");
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            var root = new Grid
            {
                Margin = new Thickness(22)
            };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var heading = Ui.Column(Ui.Text("DeskPill Settings", 25, Ui.White), Ui.Text("Make the island feel like yours.", 12, Ui.Muted));
            heading.Margin = new Thickness(0, 0, 0, 20);
            root.Children.Add(heading);
            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            body.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetRow(body, 1);
            root.Children.Add(body);
            var nav = new StackPanel
            {
                Margin = new Thickness(0, 0, 18, 0)
            };
            body.Children.Add(new ScrollViewer {Content=nav,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
            var scroll = new ScrollViewer
            {
                Content = content,
                Padding = new Thickness(4, 0, 12, 12)
            };
            Grid.SetColumn(scroll, 1);
            body.Children.Add(scroll);
            AddPage(nav, "General", General());
            AddPage(nav, "Appearance", Appearance());
            AddPage(nav, "Motion", MotionOptions());
            AddPage(nav, "Gaming", GamingOptions());
            AddPage(nav, "Pages", Pages());
            AddPage(nav, "Focus & water", FocusWater());
            AddPage(nav, "Clipboard", Clipboard());
            AddPage(nav, "Dictation", Dictation());
            AddPage(nav, "Notifications", NotificationOptions(knownNotifications ?? new DesktopNotification[0]));
            AddPage(nav, "Calendar", Calendar());
            AddPage(nav, "Ask & weather", Ask());
            AddPage(nav, "Prayer", Prayer());
            AddPage(nav, "About & shortcuts", About());
            var footer = new DockPanel
            {
                Margin = new Thickness(0, 16, 0, 0)
            };
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);
            var save = Ui.Button("Save changes", Save, true);
            DockPanel.SetDock(save, Dock.Right);
            footer.Children.Add(save);
            var cancel = Ui.Button("Cancel", delegate
            {
                Close();
            });
            DockPanel.SetDock(cancel, Dock.Right);
            footer.Children.Add(cancel);
            footer.Children.Add(Ui.Text("Preview changes here. Save keeps them; Cancel restores them.", 11, Ui.Muted));
            Content = root;
            navigation[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }

        static Settings Clone(Settings value)
        {
            var serializer = new DataContractJsonSerializer(typeof(Settings));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, value ?? new Settings());
                stream.Position = 0;
                return (Settings)serializer.ReadObject(stream);
            }
        }

        void AddPage(StackPanel nav, string title, StackPanel panel)
        {
            var button = Ui.Button(title, null);
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Margin = new Thickness(0, 0, 0, 3);
            button.Padding = new Thickness(12, 6, 8, 6);
            button.Click += delegate
            {
                content.Children.Clear();
                content.Children.Add(panel);
                foreach (var b in navigation)
                {
                    b.Background = Ui.Hex("#151515");
                    b.Foreground = Ui.Muted;
                }

                button.Background = Ui.Hex("#173422");
                button.Foreground = Ui.Green;
            };
            navigation.Add(button);
            nav.Children.Add(button);
        }

        StackPanel Page(string title, string description)
        {
            var p = new StackPanel();
            p.Children.Add(Ui.Text(title, 21, Ui.White));
            Note(p, description);
            return p;
        }

        void Note(StackPanel p, string text)
        {
            var t = Ui.Text(text, 12, Ui.Muted);
            t.TextWrapping = TextWrapping.Wrap;
            t.TextTrimming = TextTrimming.None;
            t.Margin = new Thickness(0, 8, 0, 16);
            p.Children.Add(t);
        }

        void Label(StackPanel p, string text)
        {
            var t = Ui.Text(text, 12, Ui.White);
            t.Margin = new Thickness(0, 12, 0, 6);
            p.Children.Add(t);
        }

        void Check(StackPanel p, string label, string field)
        {
            var f = typeof(Settings).GetField(field);
            var c = new CheckBox
            {
                Content = label,
                IsChecked = (bool)f.GetValue(draft)
            };
            p.Children.Add(c);
            readers.Add(delegate
            {
                f.SetValue(draft, c.IsChecked == true);
            });
            if (field == "StartWithWindows")
            {
                startupChoice=c;
                c.Checked += delegate
                {
                    startupTouched = true;
                };
                c.Unchecked += delegate
                {
                    startupTouched = true;
                };
            }
        }

        TextBox Text(StackPanel p, string label, string field)
        {
            Label(p, label);
            var f = typeof(Settings).GetField(field);
            var t = Ui.Input(Convert.ToString(f.GetValue(draft), CultureInfo.InvariantCulture));
            p.Children.Add(t);
            readers.Add(delegate
            {
                f.SetValue(draft, t.Text.Trim());
            });
            return t;
        }

        void Number(StackPanel p, string label, string field, double min, double max)
        {
            Label(p, label);
            var f = typeof(Settings).GetField(field);
            var t = Ui.Input(Convert.ToString(f.GetValue(draft), CultureInfo.InvariantCulture));
            p.Children.Add(t);
            readers.Add(delegate
            {
                double v;
                if (!double.TryParse(t.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out v) || double.IsNaN(v) || double.IsInfinity(v) || v < min || v > max || (f.FieldType == typeof(int) && v != Math.Truncate(v)))
                    throw new ArgumentException(label + " must be " + min + " to " + max + ".");
                if (f.FieldType == typeof(int))
                    f.SetValue(draft, (int)v);
                else
                    f.SetValue(draft, v);
            });
        }

        void Choice(StackPanel p, string label, string field, string[] options)
        {
            Label(p, label);
            var f = typeof(Settings).GetField(field);
            var c = new ComboBox
            {
                ItemsSource = options,
                SelectedItem = (string)f.GetValue(draft),
                MinHeight = 32,
                FontSize = 13
            };
            if (c.SelectedIndex < 0)
                c.SelectedIndex = 0;
            p.Children.Add(c);
            readers.Add(delegate
            {
                f.SetValue(draft, (string)c.SelectedItem);
            });
        }

        StackPanel Appearance()
        {
            var p = Page("Appearance", "Select a skin to try it on the notch immediately. Save keeps it; Cancel restores your saved look.");
            var choices = new List<RadioButton>();
            for (int i = 0; i < Themes.Names.Length; i++)
            {
                string name = Themes.Names[i];
                var palettePills = Ui.Row(
                    new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = Themes.ShellSwatch(i), BorderBrush = Ui.Line, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 4, 0) },
                    new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = Themes.Swatch(i), Margin = new Thickness(0, 0, 4, 0) },
                    new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = Themes.SecondarySwatch(i), Margin = new Thickness(0, 0, 12, 0) }
                );
                var description = Ui.Text(Themes.Descriptions[i], 11, Ui.Muted);
                description.TextWrapping = TextWrapping.Wrap;
                var titleText = Ui.Text(name, 14, Ui.White);
                titleText.FontWeight = FontWeights.SemiBold;
                var label = Ui.Column(titleText, description);
                var row = new DockPanel();
                DockPanel.SetDock(palettePills, Dock.Left);
                row.Children.Add(palettePills);
                row.Children.Add(label);
                var choice = new RadioButton { Content = row, GroupName = "Theme", Tag = name, IsChecked = draft.Theme == name, Foreground = Ui.White, Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 4, 0, 4), HorizontalContentAlignment = HorizontalAlignment.Stretch };
                System.Windows.Automation.AutomationProperties.SetName(choice, name + " theme");
                choice.Checked += delegate
                {
                    draft.Theme = name;
                    if(glassPanel!=null)glassPanel.Visibility=name=="Glass"?Visibility.Visible:Visibility.Collapsed;
                    Themes.ConfigureCustom(draft);
                    this.previewTheme(name);
                };
                if (name == "Custom") customChoice = choice;
                choices.Add(choice);
                p.Children.Add(choice);
                if(name=="Glass")
                {
                    glassPanel=Page("Glass finish","Tint the surface while lettering stays opaque. This is translucent glass with a highlight, without background blur.");
                    glassPanel.Margin=new Thickness(22,0,0,12);glassPanel.Visibility=draft.Theme=="Glass"?Visibility.Visible:Visibility.Collapsed;
                    Action glassPreview=delegate{Themes.ConfigureCustom(draft);this.previewTheme(draft.Theme);};
                    PreferenceSlider(glassPanel,"Surface opacity","GlassOpacity",35,95,"%",glassPreview);
                    PreferenceSlider(glassPanel,"Highlight","GlassHighlight",0,80,"%",glassPreview);
                    Note(glassPanel,"Lower opacity reveals more of the app behind the notch. Values below 60% can reduce text contrast over bright content.");
                    p.Children.Add(glassPanel);
                }
            }
            readers.Add(delegate { foreach (var choice in choices) if (choice.IsChecked == true) draft.Theme = (string)choice.Tag; });
            Label(p, "Your palette");
            Note(p, "Edit a hex color or open the color picker. Editing selects Custom automatically. Changes stay in this Settings window until saved.");
            CustomColor(p, "Shell", "CustomShell"); CustomColor(p, "Surface", "CustomSurface");
            CustomColor(p, "Accent", "CustomAccent"); CustomColor(p, "Secondary", "CustomSecondary");
            CustomColor(p, "Text", "CustomText"); CustomColor(p, "Muted text", "CustomMuted");
            Label(p, "Surface finish");
            var finishRow = Ui.Row(); var finishes = new List<Button>();
            foreach (string value in new[] { "Matte", "Satin" })
            {
                string selectedFinish = value;
                var button = Ui.Button(value, delegate
                {
                    draft.CustomFinish = selectedFinish;
                    foreach (var option in finishes) { bool active = (string)option.Tag == selectedFinish; option.Background = active ? Ui.DockActive : Ui.Card; option.Foreground = active ? Ui.Green : Ui.Muted; }
                    PreviewCustom();
                });
                button.Tag = value; button.Width = 100;
                button.Background = draft.CustomFinish == value ? Ui.DockActive : Ui.Card;
                button.Foreground = draft.CustomFinish == value ? Ui.Green : Ui.Muted;
                System.Windows.Automation.AutomationProperties.SetName(button, value + " surface finish"); finishes.Add(button); finishRow.Children.Add(button);
            }
            p.Children.Add(finishRow);
            Label(p, "Outline strength");
            var outline = new Slider { Minimum = 0, Maximum = 60, Value = draft.CustomOutline, TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(0, 4, 0, 8) };
            outline.Tag="CustomOutline";
            outline.Template = Ui.SliderTemplate();
            var amount = Ui.Text(draft.CustomOutline + "%", 11, Ui.Muted);
            outline.ValueChanged += delegate { draft.CustomOutline = (int)Math.Round(outline.Value); amount.Text = draft.CustomOutline + "%"; PreviewCustom(); };
            System.Windows.Automation.AutomationProperties.SetName(outline, "Custom outline strength"); p.Children.Add(outline); p.Children.Add(amount);
            customHint = Ui.Text("Live preview · Save to keep your palette.", 11, Ui.Muted); customHint.TextWrapping = TextWrapping.Wrap; customHint.Margin = new Thickness(0,12,0,0); p.Children.Add(customHint);
            return p;
        }

        sealed class ColorPickerOwner : System.Windows.Forms.IWin32Window
        {
            public IntPtr Handle { get; private set; }
            public ColorPickerOwner(IntPtr handle) { Handle = handle; }
        }

        void CustomColor(StackPanel panel, string label, string field)
        {
            var property = typeof(Settings).GetField(field);
            var value = (string)property.GetValue(draft);
            var input = Ui.Input(value); input.Width = 112; input.MaxLength = 7;
            System.Windows.Automation.AutomationProperties.SetName(input, label + " color");
            var swatch = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)) };
            var choose = Ui.Button("", delegate
            {
                using (var picker = new System.Windows.Forms.ColorDialog())
                {
                    picker.FullOpen = true; picker.Color = System.Drawing.ColorTranslator.FromHtml((string)property.GetValue(draft));
                    if (picker.ShowDialog(new ColorPickerOwner(new System.Windows.Interop.WindowInteropHelper(this).Handle)) == System.Windows.Forms.DialogResult.OK)
                        input.Text = "#" + picker.Color.R.ToString("X2") + picker.Color.G.ToString("X2") + picker.Color.B.ToString("X2");
                }
            });
            choose.Content = swatch; choose.Width = 38; choose.Margin = new Thickness(8,0,0,0); choose.ToolTip = "Choose " + label.ToLowerInvariant() + " color";
            System.Windows.Automation.AutomationProperties.SetName(choose, (string)choose.ToolTip);
            var title = Ui.Text(label, 12, Ui.White); title.Width = 100;
            var row = Ui.Row(title, input, choose); row.Margin = new Thickness(0,3,0,3); panel.Children.Add(row);
            customColors.Add(field,input);
            input.TextChanged += delegate
            {
                string color = input.Text.Trim(); bool valid = Settings.IsHexColor(color);
                input.BorderBrush = valid ? Ui.Line : Ui.Hex("#FF375F");
                if (valid) { property.SetValue(draft,color.ToUpperInvariant()); swatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); }
                PreviewCustom();
            };
            readers.Add(delegate { if (draft.Theme == "Custom" && !Settings.IsHexColor(input.Text.Trim())) throw new ArgumentException(label + " color must use #RRGGBB."); });
        }

        void PreviewCustom()
        {
            if (customColors.Values.Any(t => !Settings.IsHexColor(t.Text.Trim())))
            {
                if (customHint != null) customHint.Text = "Use six hex digits after #. The last valid preview stays visible.";
                return;
            }
            draft.Theme = "Custom"; Themes.ConfigureCustom(draft);
            customChoice.IsChecked = true;
            this.previewTheme("Custom");
            if (customHint != null)
            {
                var shell = (Color)ColorConverter.ConvertFromString(draft.CustomShell);
                var surface = (Color)ColorConverter.ConvertFromString(draft.CustomSurface);
                var text = (Color)ColorConverter.ConvertFromString(draft.CustomText);
                var muted = (Color)ColorConverter.ConvertFromString(draft.CustomMuted);
                double ratio = Math.Min(Math.Min(Themes.ContrastRatio(shell,text),Themes.ContrastRatio(surface,text)), Math.Min(Themes.ContrastRatio(shell,muted),Themes.ContrastRatio(surface,muted)));
                customHint.Text = ratio < 4.5 ? "Low text contrast (" + ratio.ToString("0.0",CultureInfo.InvariantCulture) + ":1). Adjust text or surface colors for easier reading." : "Live preview · Text contrast " + ratio.ToString("0.0",CultureInfo.InvariantCulture) + ":1 · Save to keep.";
            }
        }

        StackPanel General()
        {
            var p = Page("General", "Appearance, placement and everyday behavior.");
            Check(p, "Start DeskPill when I sign in", "StartWithWindows");
            Note(p, "Startup is opt-in. Saving a changed checkbox updates only DeskPill's current-user Windows Run entry.");
            var screens = System.Windows.Forms.Screen.AllScreens.Select(delegate (System.Windows.Forms.Screen s)
            {
                return s.DeviceName;
            }).ToList();
            screens.Insert(0, "");
            Choice(p, "Display (blank = primary)", "Monitor", screens.ToArray());
            Label(p, "Scale");
            var scale = new ComboBox
            {
                ItemsSource = new[]
                {
                    "100%",
                    "115%",
                    "130%",
                    "150%"
                },
                SelectedIndex = Array.IndexOf(new[] { 1.0, 1.15, 1.3, 1.5 }, draft.Scale),
                MinHeight = 32
            };
            if (scale.SelectedIndex < 0)
                scale.SelectedIndex = 0;
            p.Children.Add(scale);
            readers.Add(delegate
            {
                draft.Scale = new[]
                {
                    1.0,
                    1.15,
                    1.3,
                    1.5
                }[scale.SelectedIndex];
            });
            Number(p, "Top offset (pixels)", "OffsetY", 0, 1000);
            Check(p, "Show clock", "ShowClock");
            Check(p, "Show idle mascot in the notch", "ShowMascot");
            Check(p, "Shrink to tiny pill when app windows overlap the notch", "AutoShrinkOnTabs");
            Check(p, "Minimal notch (always tiny when idle)", "MinimalNotch");
            Check(p, "Mirror new Windows notifications in the notch", "MirrorNotifications");
            p.Children.Add(Ui.Button("Notification access…", async delegate
            {
                using (var listener = new NotificationService())
                    MessageBox.Show(this, await listener.RequestAccessAsync(), "DeskPill notification access");
            }));
            Check(p, "Show sleek HUD pills for volume, brightness & status", "ShowHudPopups");
            Choice(p, "Open island on", "OpenMode", new[] { "Hover", "Click" });
            Choice(p, "Island style", "Style", new[] { "Classic", "Floating" });
            Check(p, "Hide while a fullscreen app is active", "HideFullscreen");
            Check(p, "Play reminder sounds", "Sound");
            Check(p, "Use fast animations", "FastAnimations");
            Choice(p, "Status ring", "StatusRing", new[] { "Water", "Tasks", "Battery", "Focus", "None" });
            return p;
        }

        Slider PreferenceSlider(StackPanel page, string label, string field, int min, int max, string unit, Action changed)
        {
            var info = typeof(Settings).GetField(field);
            var row = new DockPanel
            {
                Margin = new Thickness(0, 8, 0, 2)
            };
            var labelText = Ui.Text(label, 12, Ui.White);
            var value = Ui.Text(Convert.ToString(info.GetValue(draft)) + unit, 12, Ui.Muted);
            value.HorizontalAlignment = HorizontalAlignment.Right;
            var slider = new Slider
            {
                Minimum = min,
                Maximum = max,
                Value = (int)info.GetValue(draft),
                SmallChange = unit == " ms" ? 10 : field == "MotionPace" ? 5 : 1,
                LargeChange = unit == " ms" ? 50 : 10,
                IsSnapToTickEnabled = true,
                TickFrequency = unit == " ms" ? 10 : field == "MotionPace" ? 5 : 1,
                Foreground = Ui.Green,
                Margin = new Thickness(0, 0, 0, 10),
                Tag = field
            };
            slider.Template = Ui.SliderTemplate();
            System.Windows.Automation.AutomationProperties.SetName(slider, label);
            slider.ValueChanged += delegate
            {
                info.SetValue(draft, (int)Math.Round(slider.Value));
                value.Text = ((int)Math.Round(slider.Value)) + unit;
                changed();
            };
            DockPanel.SetDock(value, Dock.Right);
            row.Children.Add(value);
            row.Children.Add(labelText);
            page.Children.Add(row);
            page.Children.Add(slider);
            readers.Add(delegate
            {
                info.SetValue(draft, (int)Math.Round(slider.Value));
            });
            return slider;
        }

        StackPanel MotionOptions()
        {
            var p = Page("Motion", "Adjust when the notch responds, then choose how quickly it moves. Changes preview immediately; Cancel restores your saved settings.");
            Action update = delegate
            {
                previewPreferences(draft, false);
            };
            var opening = PreferenceSlider(p, "Open after", "HoverOpenMs", 0, 600, " ms", update);
            var options = PreferenceSlider(p, "Options after", "HoverOptionsMs", 0, 600, " ms", update);
            var closing = PreferenceSlider(p, "Close after", "HoverCloseMs", 150, 1200, " ms", update);
            var pace = PreferenceSlider(p, "Animation pace", "MotionPace", 70, 130, "%", update);
            Note(p, "100% keeps the default spring. Lower values move more slowly; higher values move faster. Hover delays do not affect click actions. Windows reduced motion and Fast animations take priority.");
            p.Children.Add(Ui.Row(Ui.Button("Try motion", delegate
            {
                previewPreferences(draft, true);
            }), Ui.Button("Reset motion", delegate
            {
                opening.Value = 110;
                options.Value = 130;
                closing.Value = 360;
                pace.Value = 100;
                update();
            })));
            return p;
        }

        StackPanel GamingOptions()
        {
            var p = Page("Gaming", "Enable a subtle FPS bar for games, or leave it off to tuck the notch away.");
            Check(p, "Game mode · translucent FPS bar", "GameOverlay");
            Check(p, "Detect fullscreen 3D games", "DetectFullscreenGames");
            Note(p, "You can also switch Game mode from the tray icon’s right-click menu. Its glass surface uses 22% opacity and leaves text readable. Detection is a best effort. Add game executable names below for reliable matching, including windowed games. Browsers and common desktop apps are excluded from automatic detection.");
            Label(p, "Your games · one executable name per line");
            var games = Ui.Input(string.Join("\n", draft.GameApps ?? new string[0]));
            games.Background = Ui.Card;
            games.AcceptsReturn = true;
            games.TextWrapping = TextWrapping.Wrap;
            games.Height = 90;
            games.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            p.Children.Add(games);
            p.Children.Add(Ui.Button("Add game…", delegate
            {
                var picker = new OpenFileDialog
                {
                    Filter = "Game executable (*.exe)|*.exe"
                };
                if (picker.ShowDialog(this) == true)
                {
                    string name = Path.GetFileName(picker.FileName);
                    games.Text = games.Text.Trim() + (games.Text.Trim().Length > 0 ? "\n" : "") + name;
                }
            }));
            readers.Add(delegate
            {
                draft.GameApps = games.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray();
            });
            var status = Ui.Text(PresentMonAssets.Ready ? (PresentMonAssets.TracePermission ? "FPS component is ready." : "FPS component installed · Windows trace access is needed.") : "Install the optional FPS component to measure game presents.", 12, Ui.Muted);
            status.TextWrapping = TextWrapping.Wrap;
            status.Margin = new Thickness(0, 15, 0, 8);
            p.Children.Add(status);
            Button install = null;
            install = Ui.Button("Install FPS component · 1 MB", async delegate
            {
                install.IsEnabled = false;
                try
                {
                    await PresentMonAssets.InstallAsync();
                    status.Text = PresentMonAssets.TracePermission ? "FPS component is ready." : "Installed · Windows trace access is needed.";
                }
                catch (Exception e)
                {
                    status.Text = "Could not install FPS: " + e.Message;
                }
                finally
                {
                    install.IsEnabled = true;
                }
            });
            p.Children.Add(install);
            p.Children.Add(Ui.Button("About FPS access…", delegate
            {
                MessageBox.Show(this, "Windows allows FPS tracing for administrators and members of Performance Log Users.\n\nAn administrator can add your Windows account to the local Performance Log Users group in Computer Management → Local Users and Groups → Groups. Sign out and back in for the new permission to take effect.\n\nDeskPill does not change account permissions or request elevation automatically. Game glass still works without FPS access.", "FPS access", MessageBoxButton.OK, MessageBoxImage.Information);
            }));
            Note(p, "Uses PresentMon’s Windows presentation trace, without injecting into games. Permissions, protected games and exclusive fullscreen can limit measurement or overlay visibility. If unavailable, the bar shows FPS — rather than an estimated number. Your saved theme returns when the game loses focus.");
            return p;
        }

        StackPanel Dictation()
        {
            var p=Page("Dictation", "English speech to text, privately on your PC. Optional and off by default.");
            Check(p,"Click to start and stop agent dictation (hold by default)","AgentVoiceToggle");
            Check(p,"Enable English dictation · Win+Space","DictationEnabled");
            Note(p,"Hold Win+Space while speaking; release to insert. The microphone waveform appears inside the notch. Esc cancels. While enabled, this replaces Windows’ Win+Space language switch. Disabling restores it.");
            Note(p,"Install the English speech pack once (about 54 MB download). Recognition runs on-device in a separate worker, which exits after each transcription to release its model memory. No microphone access while idle. Recordings are limited to 90 seconds.");
            var status=Ui.Text(SpeechAssets.Ready?"English speech pack is ready.":"English speech pack is not installed.",12,Ui.Muted);status.TextWrapping=TextWrapping.Wrap;status.Margin=new Thickness(0,12,0,8);p.Children.Add(status);
            Button install=null;install=Ui.Button("Install / repair English speech",async delegate
            {
                install.IsEnabled=false;speechInstall=new System.Threading.CancellationTokenSource();
                try{await SpeechAssets.InstallAsync(delegate(string message){Dispatcher.BeginInvoke(new Action(delegate{status.Text=message;}));},speechInstall.Token,true);}
                catch(OperationCanceledException){status.Text="Installation cancelled.";}
                catch(Exception e){status.Text=speechInstall.IsCancellationRequested?"Installation cancelled.":"Installation failed: "+e.Message;}
                finally{install.IsEnabled=true;speechInstall.Dispose();speechInstall=null;}
            });p.Children.Add(install);
            Closed+=delegate{if(speechInstall!=null)speechInstall.Cancel();};
            Note(p,"Speech recognition uses Vosk and its small US English model (Apache 2.0). It may miss names, accents and punctuation. Review is offered if you switch apps before the result is ready. No speech account or API key is needed.");
            return p;
        }

        StackPanel Pages()
        {
            var p = Page("Pages", "Reorder pages, then choose which ones appear in navigation. Hidden pages keep their data.");
            pageList = new ListBox
            {
                Height = 220,
                Background = Ui.Card,
                Foreground = Ui.White,
                FontSize = 15,
                BorderBrush = Ui.Line
            };
            foreach (var name in new Settings().PageOrder.OrderBy(delegate (string s)
            {
                int i = Array.IndexOf(draft.PageOrder ?? new string[0], s);
                return i < 0 ? 99 : i;
            }))
                pageList.Items.Add(name);
            pageList.SelectedIndex = 0;
            p.Children.Add(pageList);
            p.Children.Add(Ui.Row(Ui.Button("Move up", delegate
            {
                MovePage(-1);
            }), Ui.Button("Move down", delegate
            {
                MovePage(1);
            })));
            readers.Add(delegate
            {
                var order = pageList.Items.Cast<string>().ToArray();
                var known = new Settings().PageOrder;
                if (order.Length != known.Length || order.Distinct().Count() != known.Length || order.Any(x=>!known.Contains(x)))
                    throw new ArgumentException("Page order must contain each page once.");
                draft.PageOrder = order;
            });
            Label(p,"Visible pages");
            var visible=new Dictionary<string,CheckBox>();
            foreach (string name in new Settings().PageOrder)
            {
                var choice=new CheckBox {Content=name,IsChecked=!(draft.HiddenPages??new string[0]).Contains(name)};
                System.Windows.Automation.AutomationProperties.SetName(choice,"Show "+name+" page"); visible[name]=choice;p.Children.Add(choice);
            }
            readers.Add(delegate {
                if(!visible.Values.Any(c=>c.IsChecked==true))throw new ArgumentException("Keep at least one page visible.");
                draft.HiddenPages=visible.Where(c=>c.Value.IsChecked!=true).Select(c=>c.Key).ToArray();
            });
            return p;
        }

        void MovePage(int step)
        {
            int i = pageList.SelectedIndex, j = i + step;
            if (i < 0 || j < 0 || j >= pageList.Items.Count)
                return;
            var item = pageList.Items[i];
            pageList.Items.RemoveAt(i);
            pageList.Items.Insert(j, item);
            pageList.SelectedIndex = j;
        }

        internal void OpenStartupOptions()
        {
            OpenSection("General");
            Dispatcher.BeginInvoke(new Action(delegate{if(startupChoice!=null){startupChoice.BringIntoView();startupChoice.Focus();}}));
        }

        internal void OpenSection(string name){var item=navigation.FirstOrDefault(b=>Convert.ToString(b.Content)==name);if(item!=null)item.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}

        StackPanel NotificationOptions(IEnumerable<DesktopNotification> known)
        {
            var p=Page("Notifications","Quiet controls for new arrivals. Recent notifications stay in memory until you clear them or close DeskPill.");
            Check(p,"Quiet agent completion previews","AgentCompletionPeeks");
            Number(p,"Peek duration (seconds)","NotificationSeconds",2,15);
            Choice(p,"During focus sessions","FocusNotifications",new[]{"History only","Allow peeks"});
            Choice(p,"During fullscreen apps","FullscreenNotifications",new[]{"History only","After fullscreen"});
            Note(p,"History only keeps arrivals in Recent without a peek. After fullscreen shows only the newest eligible pending arrival when you leave fullscreen. Peeks wait while you use the notch.");
            Label(p,"Apps allowed to show peeks");
            var muted=new HashSet<string>(draft.MutedNotificationApps??new string[0],StringComparer.OrdinalIgnoreCase);
            var remembered=muted.Select(key=>new DesktopNotification {AppId=key.StartsWith("name:",StringComparison.Ordinal)?"":key,AppName=key.StartsWith("name:",StringComparison.Ordinal)?key.Substring(5):key});
            var apps=known.Concat(remembered).GroupBy(NotificationHistory.AppKey,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).OrderBy(n=>n.AppName).ToArray();
            if(apps.Length==0)Note(p,"Apps appear here after a notification arrives. Mirroring and Windows notification access are in General.");
            foreach(var app in apps)
            {
                string key=NotificationHistory.AppKey(app);
                var check=new CheckBox {Content=app.AppName,IsChecked=!muted.Contains(key)};
                System.Windows.Automation.AutomationProperties.SetName(check,"Allow peeks from "+app.AppName);p.Children.Add(check);
                readers.Add(delegate {if(check.IsChecked==true)muted.Remove(key);else muted.Add(key);draft.MutedNotificationApps=muted.ToArray();});
            }
            return p;
        }

        StackPanel FocusWater()
        {
            var p = Page("Focus & water", "Timers and gentle reminders for your day.");
            Number(p, "Focus duration (minutes)", "FocusMinutes", 1, 240);
            Number(p, "Short break (minutes)", "ShortBreakMinutes", 1, 120);
            Number(p, "Long break (minutes)", "LongBreakMinutes", 1, 240);
            Number(p, "Daily water goal (ml)", "WaterGoalMl", 1, 20000);
            Number(p, "Bottle / serving size (ml)", "BottleMl", 1, 5000);
            Number(p, "Active start hour (0–23)", "ActiveStart", 0, 23);
            Number(p, "Active end hour (1–24)", "ActiveEnd", 1, 24);
            Number(p, "Water reminder interval (minutes)", "ReminderMinutes", 1, 1440);
            readers.Add(delegate
            {
                if (draft.ActiveEnd <= draft.ActiveStart)
                    throw new ArgumentException("Active end must be later than active start.");
            });
            return p;
        }

        StackPanel Clipboard()
        {
            var p = Page("Clipboard", "Clipboard history is captured locally while DeskPill is running. Ctrl+Alt+V opens search, selection and plain-text paste back to the app you came from.");
            Check(p, "Enable clipboard history", "ClipboardEnabled");
            Check(p, "Keep clipboard history after closing DeskPill", "KeepClipboard");
            Note(p, "Retention is limited to the latest 50 text entries. With persistence off, history stays in memory and is excluded from the saved state. Sensitive copied text may appear here; disable capture when needed.");
            return p;
        }

        StackPanel Calendar()
        {
            var p = Page("Calendar", "Read events from a local calendar export or an HTTPS ICS feed.");
            var source = Text(p, "ICS file path or HTTPS URL", "CalendarSource");
            p.Children.Add(Ui.Button("Choose ICS file", delegate
            {
                var dialog = new OpenFileDialog
                {
                    Filter = "iCalendar (*.ics)|*.ics|All files (*.*)|*.*"
                };
                if (dialog.ShowDialog(this) == true)
                    source.Text = dialog.FileName;
            }));
            readers.Add(delegate
            {
                Uri uri;
                if (draft.CalendarSource.Length > 0 && Uri.TryCreate(draft.CalendarSource, UriKind.Absolute, out uri) && !uri.IsFile && uri.Scheme != "https")
                    throw new ArgumentException("Calendar feeds must use HTTPS.");
            });
            Note(p, "Read-only import; no calendar account sign-in or event editing. Recurring events may be unsupported. Only the source you supply is read.");
            return p;
        }

        StackPanel Ask()
        {
            var p = Page("Ask & weather", "Ask uses your Gemini API key. Prompts are sent to Google when you submit a request.");
            Text(p, "Gemini model", "GeminiModel");
            Label(p, "New API key (leave blank to keep saved key)");
            apiKey = new PasswordBox
            {
                Background = Ui.Hex("#222222"),
                Foreground = Ui.White,
                Padding = new Thickness(10),
                BorderBrush = Ui.Line
            };
            p.Children.Add(apiKey);
            keyStatus = Ui.Text(string.IsNullOrEmpty(draft.ProtectedApiKey) ? "No API key saved" : "API key saved for this Windows user", 12, Ui.Muted);
            keyStatus.Margin = new Thickness(0, 8, 0, 8);
            p.Children.Add(keyStatus);
            p.Children.Add(Ui.Button("Clear saved key", delegate
            {
                clearKey = true;
                apiKey.Clear();
                keyStatus.Text = "Key will be cleared when saved";
            }));
            Note(p, "Saved keys use Windows DPAPI protection for the current user. API usage may incur charges from your provider. Weather uses MET Norway; coordinates are sent when requested.");
            Text(p, "Weather location label", "WeatherLocation");
            return p;
        }

        StackPanel Prayer()
        {
            var p = Page("Prayer", "Calculated locally using your coordinates and the computer's local time zone.");
            Number(p, "Latitude", "Latitude", -90, 90);
            Number(p, "Longitude", "Longitude", -180, 180);
            Choice(p, "Calculation method", "PrayerMethod", new[] { "MWL", "ISNA", "Karachi", "UmmAlQura" });
            Check(p, "Enable prayer reminders", "PrayerReminder");
            Note(p, "Times depend on the selected calculation method. High-latitude dates can have unavailable times. Weather shares these coordinates.");
            return p;
        }

        StackPanel About()
        {
            var p = Page("About & shortcuts", "DeskPill · a native Windows desktop island");
            Note(p, "Media controls use available Windows media sessions. Brightness requires supported display hardware; volume and battery depend on system capabilities. Screen OCR uses Windows recognition support. Unsupported controls report their availability.");
            Note(p, "Clipboard capture runs only when enabled. Screen capture and selected files are used only for the action you request. Calendar feeds, lyrics, weather and Ask require network access when used.");
            Note(p, "Keyboard: Tab moves between controls; Enter activates the focused button; Escape closes settings without saving. Ctrl+Alt+V opens clipboard search and paste. Esc dismisses notification previews while you work in another app. See the island tooltips for other registered global shortcuts.");
            return p;
        }

        protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                Close();
                e.Handled = true;
            }

            base.OnKeyDown(e);
        }

        void Save()
        {
            try
            {
                foreach (var read in readers)
                    read();
                if (draft.DictationEnabled && !SpeechAssets.Ready) throw new InvalidOperationException("Install the English speech pack in Dictation before enabling it.");
                if (!string.IsNullOrWhiteSpace(apiKey.Password))
                    draft.ProtectedApiKey = AskService.ProtectKey(apiKey.Password.Trim());
                else if (clearKey)
                    draft.ProtectedApiKey = "";
                if (startupTouched)
                {
                    using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                    {
                        if (draft.StartWithWindows)
                        {
                            string executable = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                            key.SetValue("DeskPill", "\"" + executable + "\"");
                        }
                        else
                            key.DeleteValue("DeskPill", false);
                    }
                }

                foreach (var f in typeof(Settings).GetFields(BindingFlags.Public | BindingFlags.Instance))
                    f.SetValue(state.Settings, f.GetValue(draft));
                state.Normalize(DateTime.Now);
                apply();
                themeCommitted = true;
                if (notify != null)
                    notify("Settings saved");
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Could not save settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
