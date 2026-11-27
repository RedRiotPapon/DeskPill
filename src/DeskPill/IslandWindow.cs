using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace DeskPill
{
    public sealed class IslandWindow : Window
    {
        const double HoverOpenDelayMs = 110, HoverOptionsDelayMs = 130;
        const double ExpandedWidthClassic = 396.0;
        const double ExpandedWidthFloating = 380.0;
        const double ExpandedOffsetX = (IslandMotion.CanvasWidth - ExpandedWidthClassic) * 0.5;
        const double PageWidth = ExpandedWidthClassic;
        const double PageHeight = 114.0;
        const double PageTopY = 40.0;

        readonly AppState state;
        readonly StateStore store;
        readonly FocusClock focus;
        readonly MediaService media = new MediaService();
        readonly NotificationService notifications = new NotificationService();
        readonly DispatcherTimer notificationTimer = new DispatcherTimer();
        bool notificationEscapeDown;
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int key);
        readonly NotificationHistory notificationHistory = new NotificationHistory();
        NotificationHistoryWindow historyWindow;
        ClipboardPalette clipboardPalette;
        DesktopNotification currentNotification;
        bool notificationLaunching;
        DictationController dictation;
        internal bool RuntimeProbeMode { get; set; }
        Border notificationCard;
        readonly DispatcherTimer heartbeat = new DispatcherTimer(), closeTimer = new DispatcherTimer(), hoverTimer = new DispatcherTimer(), hudTimer = new DispatcherTimer(), tabWatchTimer = new DispatcherTimer();
        readonly bool preview, capture;

        readonly Grid root = new Grid();
        readonly Canvas design = new Canvas
        {
            Width = IslandMotion.CanvasWidth,
            Height = IslandMotion.CanvasHeight
        };
        readonly Canvas contentHost = new Canvas
        {
            Width = IslandMotion.CanvasWidth,
            Height = IslandMotion.CanvasHeight
        };

        // Permanent layers: never torn down across open/close/tiny/hud/page transitions.
        readonly Canvas tinyLayer = new Canvas
        {
            Width = IslandMotion.CanvasWidth,
            Height = IslandMotion.CanvasHeight
        };
        Border tinyBar, tinyCatcher;

        readonly Canvas compactLayer = new Canvas
        {
            Width = IslandMotion.CanvasWidth,
            Height = IslandMotion.CanvasHeight,
            RenderTransformOrigin = new Point(0.5, 0.06)
        };
        readonly ScaleTransform compactScale = new ScaleTransform(1.0, 1.0);

        readonly Canvas hudLayer = new Canvas
        {
            Width = IslandMotion.CanvasWidth,
            Height = IslandMotion.CanvasHeight,
            IsHitTestVisible = false
        };
        Border hudCard;
        TextBlock hudIcon, hudTitle, hudValue;
        ProgressBar hudBar;
        readonly TranslateTransform hudTranslate = new TranslateTransform(0.0, 0.0);

        readonly Canvas expandedLayer = new Canvas
        {
            Width = IslandMotion.CanvasWidth,
            Height = IslandMotion.CanvasHeight,
            RenderTransformOrigin = new Point(0.5, 0.0)
        };
        readonly ScaleTransform expandedScale = new ScaleTransform(1.0, 1.0);
        readonly TranslateTransform expandedTranslate = new TranslateTransform(0.0, 0.0);
        readonly BitmapCache animCache = new BitmapCache { EnableClearType = false, RenderAtScale = 1.0 };

        readonly Canvas pagerHost = new Canvas
        {
            Width = IslandMotion.CanvasWidth,
            Height = IslandMotion.CanvasHeight
        };
        readonly Canvas pageViewport = new Canvas
        {
            Width = PageWidth,
            Height = PageHeight,
            ClipToBounds = false,
            Clip = new RectangleGeometry(new Rect(0, -20, PageWidth, PageHeight + 20))
        };
        Canvas currentPageVisual, outgoingPageVisual;
        TranslateTransform currentPageTransform = new TranslateTransform(), outgoingPageTransform = new TranslateTransform();
        int slideDirection = 1;

        readonly Border inlineHost = new Border
        {
            Visibility = Visibility.Collapsed,
            Background = Brushes.Transparent
        };

        readonly IslandMotion motion;
        bool shellShadowDisabled;
        readonly System.Windows.Shapes.Path shell = new System.Windows.Shapes.Path
        {
            Fill = Ui.Hex("#000000"),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, BlurRadius = 26, ShadowDepth = 7, Opacity = 0.26,
                RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance
            }
        };
        readonly System.Windows.Shapes.Path rim = new System.Windows.Shapes.Path
        {
            Stroke = Ui.Hex("#30FFFFFF"),
            StrokeThickness = 1,
            IsHitTestVisible = false
        };

        // Compact bar elements
        Grid compact;
        TextBlock compactText;
        Ring compactRing;
        EqBars compactEq;
        NotchMascot compactMascot;

        // Expanded persistent chrome elements
        Grid headerRow;
        Run titleFirstRun, titleSecondRun, dateRun, timeRun;
        Ring expandedRing;
        EqBars expandedEq;
        StackPanel dots;
        Border dotsStrip, dockTray;
        Button prevBtn, nextBtn, settingsBtn;
        readonly Dictionary<string, Button> dockPageButtons = new Dictionary<string, Button>();

        // Active page dynamic value refs
        TextBlock largeValue, smallValue, mediaElapsed, mediaRemaining, focusStateText;
        ProgressBar focusProgress, waterProgress;
        Ring pageRing;
        FrameworkElement pageContent;
        ProgressBar mediaProgress;

        // Inline contextual view state
        TextBox inlineAskInput, inlineAskOutput;
        bool inlineAskBusy, inlineTranslateMode;
        ComboBox inlineTranslateLang;
        Button inlineAskModeBtn;
        List<LyricLine> inlineLyricsLines;
        List<Run> inlineLyricsRuns;
        TextBlock inlineLyricsPlain;
        int inlineLastActiveLyric = -2;

        string page = "Music", shelfTab = "Files", selectedTask = "", lastMedia = "", toast = "", lastLanguage = "", prayerKey = "";
        DateTime toastUntil, waterReminder;
        DateTime hoverIntentSince, dockIntentSince;
        bool hoverBlockedUntilExit;
        bool open = true, pinned, stopping, polling, clipboardBusy, contextMenuOpen, dragHovering, tabOverlapping, hotkeyConflict;
        NotchPose pose = NotchPose.Expanded;
        double currentScale = 1.0, currentDpiX = 96.0, currentDpiY = 96.0;
        internal bool SuppressCursorWatch { get; set; }

        List<PrayerTime> prayers = new List<PrayerTime>();
        readonly HashSet<string> prayed = new HashSet<string>();
        int ticks, tabClearTicks, lastBattery = -2, lastBrightness = -2;
        float lastVolume = -2;
        bool lastMuted;
        bool lastCaps;
        bool hudRollsPercent;
        string hudRollSuffix = "%";
        VolumeWatcher volumeWatcher;
        Forms.NotifyIcon tray;
        HwndSource source;
        SettingsWindow settingsWindow;
        IntPtr hwnd;

        readonly Dictionary<string, string> icons = new Dictionary<string, string>
        {
            { "Music", "\uE8D6" },
            { "Shelf", "\uE8B7" },
            { "Focus", "\uE916" },
            { "Water", "\uEB42" },
            { "Tasks", "\uE73A" }
        };

        public IslandWindow(AppState state, StateStore store, bool preview, bool capture)
        {
            this.state = state;
            ApplyTheme();
            this.store = store;
            this.preview = preview;
            this.capture = capture;
            Ui.MotionEnabled = !capture && !state.Settings.FastAnimations;

            motion = new IslandMotion(shell, rim, contentHost, OnMotionFrame);
            focus = new FocusClock(state.Settings);
            focus.RestoreFrom(state, DateTime.UtcNow);
            waterReminder = DateTime.Now.AddMinutes(state.Settings.ReminderMinutes);
            lastCaps = NativeMethods.CapsLock();
            lastLanguage = NativeMethods.InputLanguage();

            Title = "DeskPill";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = false;
            FontFamily = new FontFamily("Segoe UI");
            Width = IslandMotion.CanvasWidth;
            Height = IslandMotion.CanvasHeight;

            Content = root;
            root.Children.Add(design);
            TextOptions.SetTextRenderingMode(design, TextRenderingMode.Grayscale);
            TextOptions.SetTextFormattingMode(design, TextFormattingMode.Ideal);

            compactLayer.RenderTransform = compactScale;
            hudLayer.RenderTransform = hudTranslate;
            var expandedGroup = new TransformGroup();
            expandedGroup.Children.Add(expandedScale);
            expandedGroup.Children.Add(expandedTranslate);
            expandedLayer.RenderTransform = expandedGroup;

            // Mount shell, permanent content layers, and subtle rim.
            design.Children.Add(shell);
            design.Children.Add(contentHost);
            design.Children.Add(rim);
            contentHost.Children.Add(tinyLayer);
            contentHost.Children.Add(compactLayer);
            contentHost.Children.Add(hudLayer);
            contentHost.Children.Add(expandedLayer);

            BuildTinyLayer();
            BuildCompactBar();
            BuildHudLayer();
            BuildExpandedChrome();

            MouseEnter += delegate
            {
                EnsureHoverWatch();
                PollHoverState();
                if (compactMascot != null)
                    compactMascot.Wake(0);
            };
            MouseMove += delegate (object sender, MouseEventArgs e)
            {
                EnsureHoverWatch();
                PollHoverState();
                if (!open && pose == NotchPose.Compact && compactMascot != null)
                    compactMascot.NudgeLook((e.GetPosition(compactLayer).X - IslandMotion.CanvasWidth * 0.5) / 86.0);
            };
            MouseLeave += delegate { PollHoverState(); };
            ContextMenuOpening += delegate
            {
                contextMenuOpen = true;
                closeTimer.Stop();
            };
            ContextMenuClosing += delegate
            {
                contextMenuOpen = false;
                if (CanAutoClose() && !IsCursorInsideNotch(12))
                    closeTimer.Start();
            };
            PreviewMouseWheel += delegate (object s, MouseWheelEventArgs e)
            {
                var element = e.OriginalSource as DependencyObject;
                while (element != null)
                {
                    var scroll = element as ScrollViewer;
                    if (scroll != null && scroll.ScrollableHeight > 0)
                        return;
                    element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
                }

                if (!open)
                {
                    SetOpen(true);
                    return;
                }

                if (IsInlinePose(pose))
                    return;
                ChangePage(e.Delta < 0 ? 1 : -1);
                e.Handled = true;
            };
            PreviewKeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.Key == Key.Escape)
                {
                    if (pose == NotchPose.NotificationPeek) { DismissNotification(); e.Handled = true; return; }
                    if (IsInlinePose(pose))
                    {
                        ExitInlineView();
                        e.Handled = true;
                        return;
                    }

                    pinned = false;
                    SetOpen(false);
                    e.Handled = true;
                }
                else if (e.OriginalSource is TextBox)
                {
                }
                else if (!IsInlinePose(pose) && e.Key == Key.Right)
                {
                    ChangePage(1);
                    e.Handled = true;
                }
                else if (!IsInlinePose(pose) && e.Key == Key.Left)
                {
                    ChangePage(-1);
                    e.Handled = true;
                }
            };

            AllowDrop = true;
            DragEnter += delegate (object s, DragEventArgs e)
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    dragHovering = true;
                    closeTimer.Stop();
                    if (IsInlinePose(pose))
                        ExitInlineView();
                    page = "Shelf";
                    shelfTab = "Files";
                    SetOpen(true);
                    SetPose(NotchPose.DropTarget);
                    RebuildCurrentPage(true);
                    e.Effects = DragDropEffects.Link;
                    e.Handled = true;
                }
            };
            DragLeave += delegate
            {
                if (dragHovering)
                {
                    dragHovering = false;
                    if (open && !IsInlinePose(pose))
                        SetPose(NotchPose.Expanded);
                }
            };
            Drop += delegate (object s, DragEventArgs e)
            {
                dragHovering = false;
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    AddFiles((string[])e.Data.GetData(DataFormats.FileDrop));
                    e.Handled = true;
                }
            };

            closeTimer.Interval = TimeSpan.FromMilliseconds(360);
            closeTimer.Tick += delegate
            {
                closeTimer.Stop();
                closeTimer.Interval = TimeSpan.FromMilliseconds(360);
                if (CanAutoClose() && !IsCursorInsideNotch(12))
                    SetOpen(false);
            };
            hoverTimer.Interval = TimeSpan.FromMilliseconds(16);
            hoverTimer.Tick += delegate
            {
                PollHoverState();
            };
            hudTimer.Interval = TimeSpan.FromMilliseconds(2200);
            hudTimer.Tick += delegate
            {
                OnHudTimeout();
            };
            tabWatchTimer.Interval = TimeSpan.FromMilliseconds(500);
            tabWatchTimer.Tick += delegate
            {
                PollTabOverlap();
            };

            SourceInitialized += delegate
            {
                hwnd = new WindowInteropHelper(this).Handle;
                source = HwndSource.FromHwnd(hwnd);
                NativeMethods.SetToolWindow(hwnd);
                if (!capture && !preview)
                {
                    NativeMethods.EnsureTopmost(hwnd);
                    source.AddHook(WindowMessage);
                    NativeMethods.EnableClipboardListener(hwnd);
                    bool ask = NativeMethods.RegisterHotKey(hwnd, 1, 0x4003, 0x20), ocr = NativeMethods.RegisterHotKey(hwnd, 2, 0x4003, 0x54), clipboard = NativeMethods.RegisterHotKey(hwnd, 3, 0x4003, 0x56);
                    hotkeyConflict = !ask || !ocr || !clipboard;
                }
            };

            Loaded += async delegate
            {
                Place();
                motion.SetStyle(IsFloating);
                SnapPoseImmediate();
                if (RuntimeProbeMode) return;
                if (capture)
                {
                    await RefreshMedia();
                    await CapturePages();
                    return;
                }

                if (!preview) {dictation=new DictationController(this,state.Settings,Notify,delegate {if(pose==NotchPose.NotificationPeek)DismissNotification();SetOpen(false);});dictation.Configure();}
                SetupTray();
                lastVolume = SystemService.GetVolume();
                lastMuted = SystemService.GetMuted();
                if (!preview)
                {
                    volumeWatcher = new VolumeWatcher(delegate (float vol, bool muted)
                    {
                        if (stopping) return;
                        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(delegate
                        {
                            HandleLiveVolume(vol, muted);
                        }));
                    });
                }
                heartbeat.Interval = TimeSpan.FromSeconds(1);
                heartbeat.Tick += async delegate
                {
                    await Tick();
                };
                heartbeat.Start();
                notificationTimer.Interval = TimeSpan.FromMilliseconds(750);
                notificationTimer.Tick += async delegate { await PollNotifications(); };
                await PollNotifications();
                notificationTimer.Start();
                if (!preview)
                    tabWatchTimer.Start();
                await RefreshMedia();
                RebuildCurrentPage(true);
                UpdateValues();
                if (!string.IsNullOrEmpty(store.LastError))
                    Notify("Recovered saved state. Details are available in the data folder.");
                if (!preview) NativeMethods.EnsureTopmost(hwnd);
                closeTimer.Interval = TimeSpan.FromSeconds(2.5);
                closeTimer.Start();
            };

            IsVisibleChanged += delegate
            {
                if (!IsVisible)
                {
                    hoverTimer.Stop();
                    hudTimer.Stop();
                    tabWatchTimer.Stop();
                    ClearOutgoingPage();
                    inlineAskInput = null;
                    inlineAskOutput = null;
                    pose = open ? NotchPose.Expanded : RestingClosedPose();
                    HideInlineHost();
                    SnapPoseImmediate();
                    Place();
                }
                else
                {
                    Place();
                    if (!preview && !capture)
                    {
                        NativeMethods.EnsureTopmost(hwnd);
                        if (!tabWatchTimer.IsEnabled)
                            tabWatchTimer.Start();
                    }
                }
            };

            Closed += delegate
            {
                Cleanup();
                if (Application.Current != null)
                    Application.Current.Shutdown();
            };

            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
            ContextMenu = BuildMenu();

            // Build initial active page synchronously so logical tree queries work immediately (UiTests).
            motion.SetStyle(IsFloating);
            SnapPoseImmediate();
            RebuildCurrentPage(true);
            UpdateValues();
        }

        static bool IsInlinePose(NotchPose p)
        {
            return p == NotchPose.InlineAsk || p == NotchPose.InlineToday || p == NotchPose.InlineLyrics;
        }

        bool CanAutoClose()
        {
            if (pinned || contextMenuOpen || dragHovering)
                return false;
            if (IsInlinePose(pose))
            {
                if (inlineAskBusy)
                    return false;
                if (inlineAskInput != null && inlineAskInput.IsKeyboardFocusWithin)
                    return false;
            }

            return true;
        }

        IntPtr WindowMessage(IntPtr window, int message, IntPtr w, IntPtr l, ref bool handled)
        {
            if (message == 0x0312)
            {
                if (w.ToInt32() == 1)
                    ShowInlineAsk();
                else if (w.ToInt32() == 2)
                    StartOcr();
                else if (w.ToInt32() == 3)
                    ShowClipboardPalette();
                handled = true;
            }
            else if (message == 0x031D)
                CaptureClipboard();
            else if (message == 0x02E0)
                Dispatcher.BeginInvoke(new Action(Place));
            return IntPtr.Zero;
        }

        void DisplayChanged(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(Place));
        }

        void SetupTray()
        {
            tray = new Forms.NotifyIcon
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetEntryAssembly().Location),
                Text = "DeskPill · a living notch for Windows",
                Visible = true
            };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Open DeskPill", null, delegate
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    pinned = true;
                    SyncDockStates();
                    SetOpen(true);
                    Show();
                }));
            });
            menu.Items.Add("Settings…", null, delegate
            {
                Dispatcher.BeginInvoke(new Action(ShowSettings));
            });
            menu.Items.Add("Ask DeskPill", null, delegate
            {
                Dispatcher.BeginInvoke(new Action(ShowInlineAsk));
            });
            menu.Items.Add("Copy text from screen", null, delegate
            {
                Dispatcher.BeginInvoke(new Action(StartOcr));
            });
            menu.Items.Add("Dictation (Win+Space, optional)", null, delegate { Dispatcher.BeginInvoke(new Action(delegate {if(dictation!=null)dictation.Toggle();})); });
            menu.Items.Add("Clipboard palette", null, delegate { Dispatcher.BeginInvoke(new Action(ShowClipboardPalette)); });
            menu.Items.Add("Recent notifications", null, delegate { Dispatcher.BeginInvoke(new Action(ShowRecentNotifications)); });
            menu.Items.Add("Exit DeskPill", null, delegate
            {
                Dispatcher.BeginInvoke(new Action(Exit));
            });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    pinned = !pinned;
                    SyncDockStates();
                    SetOpen(true);
                }));
            };
        }

        ContextMenu BuildMenu()
        {
            var menu = new ContextMenu
            {
                Background = Ui.Hex("#202020"),
                Foreground = Ui.White,
                BorderBrush = Ui.Line
            };
            foreach (string name in Order())
            {
                string selected = name;
                var item = new MenuItem { Header = name };
                item.Click += delegate
                {
                    ExitInlineIfNeeded();
                    page = selected;
                    SetOpen(true);
                    RebuildCurrentPage(true);
                };
                menu.Items.Add(item);
            }

            menu.Items.Add(new Separator());
            AddMenu(menu, "Today & calendar", ShowInlineToday);
            AddMenu(menu, "Ask DeskPill    Ctrl+Alt+Space", ShowInlineAsk);
            AddMenu(menu, "Copy text from screen    Ctrl+Alt+T", StartOcr);
            AddMenu(menu, "Dictation    Win+Space (optional)", delegate {if(dictation!=null)dictation.Toggle();});
            AddMenu(menu, "Clipboard palette    Ctrl+Alt+V", ShowClipboardPalette);
            AddMenu(menu, "Recent notifications", ShowRecentNotifications);
            AddMenu(menu, "Notification access…", async delegate { await RequestNotificationAccess(); });
            AddMenu(menu, "Settings…", ShowSettings);
            AddMenu(menu, "Pin open / unpin", delegate
            {
                pinned = !pinned;
                SyncDockStates();
                SetOpen(true);
            });
            AddMenu(menu, "Exit DeskPill", Exit);
            return menu;
        }

        void AddMenu(ContextMenu menu, string name, Action action)
        {
            var item = new MenuItem { Header = name };
            item.Click += delegate { action(); };
            menu.Items.Add(item);
        }

        public void Notify(string message)
        {
            toast = message;
            toastUntil = DateTime.Now.AddSeconds(5);
            SetOpen(true);
            UpdateValues();
            if (!pinned)
                closeTimer.Start();
        }

        public void ReportError(Exception ex)
        {
            Notify("Couldn't complete that action: " + ex.Message);
        }

        bool Save(bool report = true)
        {
            if (capture)
                return true;
            try
            {
                focus.SaveTo(state, DateTime.UtcNow);
                store.Save(state);
                return true;
            }
            catch (Exception ex)
            {
                if (report)
                    Notify("Couldn't save changes: " + ex.Message);
                return false;
            }
        }

        bool IsFloating
        {
            get
            {
                return state.Settings.Style == "Floating" || preview;
            }
        }

        bool HasRichCompactContent()
        {
            return DateTime.Now < toastUntil || focus.Running || media.Current.Playing;
        }

        double DesiredCompactWidth()
        {

            return 150.0;
        }

        NotchPose RestingClosedPose()
        {
            return (state.Settings.MinimalNotch || tabOverlapping) ? NotchPose.Tiny : NotchPose.Compact;
        }

        void TargetDimensions(NotchPose targetPose, out double w, out double h, out bool showDock, out bool showHud)
        {
            bool floating = IsFloating;
            double baseW = floating ? ExpandedWidthFloating : ExpandedWidthClassic;
            showHud = false;
            if (targetPose == NotchPose.Tiny)
            {
                w = IslandMotion.TinyWidth;
                h = IslandMotion.TinyHeight;
                showDock = false;
            }
            else if (targetPose == NotchPose.NotificationPeek)
            {
                w = 360.0;
                h = 106.0;
                showDock = false;
                showHud = true;
            }
            else if (targetPose == NotchPose.HudPill)
            {
                w = IslandMotion.HudPillWidth;
                h = IslandMotion.HudPillHeight;
                showDock = false;
                showHud = true;
            }
            else if (targetPose == NotchPose.Compact)
            {
                w = DesiredCompactWidth();
                h = IslandMotion.CompactHeight;
                showDock = false;
            }
            else if (targetPose == NotchPose.OptionsDock)
            {
                w = baseW;
                h = IslandMotion.OptionsDockHeight;
                showDock = true;
            }
            else if (targetPose == NotchPose.DropTarget)
            {
                w = 432.0;
                h = IslandMotion.DropTargetHeight;
                showDock = false;
            }
            else if (targetPose == NotchPose.InlineAsk)
            {
                w = 460.0;
                h = IslandMotion.InlineAskHeight;
                showDock = false;
            }
            else if (targetPose == NotchPose.InlineToday)
            {
                w = 484.0;
                h = IslandMotion.InlineTodayHeight;
                showDock = false;
            }
            else if (targetPose == NotchPose.InlineLyrics)
            {
                w = 484.0;
                h = IslandMotion.InlineLyricsHeight;
                showDock = false;
            }
            else
            {
                w = baseW;
                h = IslandMotion.ExpandedHeight;
                showDock = false;
            }
        }

        void SnapPoseImmediate()
        {
            double w, h;
            bool showDock, showHud;
            TargetDimensions(pose, out w, out h, out showDock, out showHud);
            motion.SetStyle(IsFloating);
            LayoutLayersTopOffset();
            motion.SnapPose(w, h, showDock, showHud);
        }

        void Place()
        {
            var screens = Forms.Screen.AllScreens;
            var screen = screens.FirstOrDefault(s => s.DeviceName == state.Settings.Monitor) ?? Forms.Screen.PrimaryScreen;
            double dpiX = 96.0, dpiY = 96.0;
            try
            {
                var visual = PresentationSource.FromVisual(this);
                if (visual != null && visual.CompositionTarget != null)
                {
                    dpiX = 96.0 * visual.CompositionTarget.TransformToDevice.M11;
                    dpiY = 96.0 * visual.CompositionTarget.TransformToDevice.M22;
                }
            }
            catch
            {
            }

            currentDpiX = dpiX;
            currentDpiY = dpiY;
            double screenLeftDip = screen.Bounds.Left * 96.0 / dpiX;
            double screenWidthDip = screen.Bounds.Width * 96.0 / dpiX;
            double screenTopDip = screen.Bounds.Top * 96.0 / dpiY;
            double workBottomDip = screen.WorkingArea.Bottom * 96.0 / dpiY;
            currentScale = Math.Min(state.Settings.Scale, screenWidthDip / IslandMotion.CanvasWidth);
            design.LayoutTransform = new ScaleTransform(currentScale, currentScale);
            Width = IslandMotion.CanvasWidth * currentScale;
            Height = IslandMotion.CanvasHeight * currentScale;
            Left = Math.Round(screenLeftDip + (screenWidthDip - Width) * 0.5);
            double desiredTop = screenTopDip + state.Settings.OffsetY + (preview ? 210.0 : 0.0);
            Top = Math.Round(Math.Min(desiredTop, workBottomDip - Height));
            motion.SetStyle(IsFloating);
            LayoutLayersTopOffset();
            if (!preview && !capture && IsVisible)
                NativeMethods.EnsureTopmost(hwnd);
        }

        void LayoutLayersTopOffset()
        {
            double yOff = motion.TopOffset;
            if (tinyBar != null)
                Canvas.SetTop(tinyBar, yOff + 2.0);
            if (tinyCatcher != null)
                Canvas.SetTop(tinyCatcher, yOff);
            if (compact != null)
                Canvas.SetTop(compact, yOff);
            if (hudCard != null)
                Canvas.SetTop(hudCard, yOff + 2.0);
            if (notificationCard != null)
                Canvas.SetTop(notificationCard, yOff + 6.0);
            if (headerRow != null)
                Canvas.SetTop(headerRow, yOff + 18.0);
            Canvas.SetTop(pageViewport, yOff + PageTopY);
            if (prevBtn != null)
                Canvas.SetTop(prevBtn, yOff + 83.0);
            if (nextBtn != null)
                Canvas.SetTop(nextBtn, yOff + 83.0);
            if (dotsStrip != null)
                Canvas.SetTop(dotsStrip, yOff + 150.0);
            if (dockTray != null)
                Canvas.SetTop(dockTray, yOff + 168.0);
            if (inlineHost != null && inlineHost.Visibility == Visibility.Visible)
                Canvas.SetTop(inlineHost, yOff + 42.0);
        }

        bool overlapPolling;
        async void PollTabOverlap()
        {
            if (capture || preview || SuppressCursorWatch || !IsLoaded || !IsVisible || overlapPolling)
                return;

            bool detected = false;
            if (state.Settings.AutoShrinkOnTabs && hwnd != IntPtr.Zero)
            {
                // WPF performs the actual per-monitor DPI and layout transform mapping.
                double width = DesiredCompactWidth();
                Point start = design.PointToScreen(new Point((IslandMotion.CanvasWidth - width) * 0.5, motion.TopOffset));
                Point end = design.PointToScreen(new Point((IslandMotion.CanvasWidth + width) * 0.5, motion.TopOffset + 30.0));
                overlapPolling = true;
                try
                {
                    detected = await Task.Run(() => NativeMethods.IsForegroundOverlappingTabs(hwnd, (int)Math.Round((start.X + end.X) * 0.5),
                        (int)Math.Floor(start.Y), (int)Math.Ceiling(end.X - start.X), (int)Math.Ceiling(end.Y - start.Y)));
                }
                catch { return; }
                finally { overlapPolling = false; }
                if (!IsLoaded || !state.Settings.AutoShrinkOnTabs) return;
            }

            if (detected)
            {
                tabClearTicks = 0;
                if (!tabOverlapping)
                {
                    tabOverlapping = true;
                    if (!open && pose != NotchPose.HudPill && pose != NotchPose.NotificationPeek)
                        SetPose(RestingClosedPose());
                }
            }
            else if (tabOverlapping)
            {
                tabClearTicks++;
                if (tabClearTicks >= 2)
                {
                    tabOverlapping = false;
                    if (!open && pose != NotchPose.HudPill && pose != NotchPose.NotificationPeek)
                        SetPose(RestingClosedPose());
                }
            }
        }

        internal void SetTabOverlapForProbe(bool overlapping)
        {
            tabOverlapping = overlapping;
            tabClearTicks = 0;
            if (!open && pose != NotchPose.HudPill && pose != NotchPose.NotificationPeek)
                SetPose(RestingClosedPose());
        }

        bool TryGetLogicalCursor(out double lx, out double ly)
        {
            lx = ly = -1000;
            if (!IsLoaded || !IsVisible)
                return false;
            int sx, sy;
            if (!NativeMethods.TryGetCursorPos(out sx, out sy))
                return false;
            Point logical = design.PointFromScreen(new Point(sx, sy));
            lx = logical.X;
            ly = logical.Y;
            return true;
        }

        Rect PointerBounds(double pad)
        {
            double w, h;
            bool dock, hud;
            TargetDimensions(pose, out w, out h, out dock, out hud);
            if (open)
            {
                // The target remains a stable hover region while the outline catches up.
                w = Math.Max(w, motion.CurrentWidth);
                h = Math.Max(h, motion.CurrentHeight);
            }
            if (pose == NotchPose.Tiny) { w = 64; h = 12; }
            return new Rect((IslandMotion.CanvasWidth - w) * 0.5 - pad,
                Math.Max(0, motion.TopOffset - pad), w + pad * 2, h + pad * 2);
        }

        bool IsCursorInsideNotch(double pad)
        {
            double x, y;
            return TryGetLogicalCursor(out x, out y) && PointerBounds(pad).Contains(x, y);
        }

        void EnsureHoverWatch()
        {
            if (!capture && !SuppressCursorWatch && IsLoaded && IsVisible && !hoverTimer.IsEnabled)
                hoverTimer.Start();
        }

        void PollHoverState()
        {
            if (!IsLoaded || !IsVisible || SuppressCursorWatch)
            {
                hoverTimer.Stop();
                return;
            }
            ProcessNotificationEscape((GetAsyncKeyState(0x1B) & 0x8000) != 0);
            double x, y;
            if (TryGetLogicalCursor(out x, out y))
                ProcessPointer(x, y, DateTime.UtcNow);
        }

        void ProcessPointer(double x, double y, DateTime now)
        {
            if(dictation!=null && dictation.Active)return;
            bool inside = PointerBounds(open ? 12.0 : 0.0).Contains(x, y);
            if (pose == NotchPose.NotificationPeek)
            {
                if (inside) hudTimer.Stop();
                else if (!hudTimer.IsEnabled && !capture) hudTimer.Start();
                return;
            }
            if (open)
            {
                hoverIntentSince = DateTime.MinValue;
                if (inside)
                {
                    closeTimer.Stop();
                    closeTimer.Interval = TimeSpan.FromMilliseconds(360);
                    double relY = y - motion.TopOffset;
                    bool dockZone = !dragHovering && !IsInlinePose(pose)
                        && relY >= 152 && relY <= 194 && Math.Abs(x - IslandMotion.CanvasWidth * 0.5) <= 98;
                    if (pose == NotchPose.OptionsDock)
                    {
                        if (relY < 145) { dockIntentSince = DateTime.MinValue; SetPose(NotchPose.Expanded); }
                    }
                    else if (dockZone)
                    {
                        if (dockIntentSince == DateTime.MinValue) dockIntentSince = now;
                        if ((now - dockIntentSince).TotalMilliseconds >= HoverOptionsDelayMs) SetPose(NotchPose.OptionsDock);
                    }
                    else dockIntentSince = DateTime.MinValue;
                }
                else
                {
                    dockIntentSince = DateTime.MinValue;
                    // Keep the dock shape during the leave grace period; shrinking it here
                    // generates another hit-test enter/leave at the stationary pointer.
                    if (CanAutoClose() && !closeTimer.IsEnabled) closeTimer.Start();
                }
            }
            else
            {
                dockIntentSince = DateTime.MinValue;
                if (!inside)
                {
                    hoverIntentSince = DateTime.MinValue;
                    hoverBlockedUntilExit = false;
                    if (!motion.IsAnimating && !closeTimer.IsEnabled) hoverTimer.Stop();
                }
                else if (!hoverBlockedUntilExit && state.Settings.OpenMode == "Hover")
                {
                    if (hoverIntentSince == DateTime.MinValue) hoverIntentSince = now;
                    if ((now - hoverIntentSince).TotalMilliseconds >= HoverOpenDelayMs)
                    {
                        closeTimer.Stop();
                        hudTimer.Stop();
                        SetOpen(true);
                    }
                }
            }
        }

        void OnMotionFrame()
        {
            var shadow = shell.Effect as System.Windows.Media.Effects.DropShadowEffect;
            if (shadow != null) shadow.Opacity = (shellShadowDisabled ? 0 : 0.26) * Math.Max(0, Math.Min(1, (motion.CurrentHeight - 30) / 142));
            SyncVisualsFromMotion();
        }

        void SyncVisualsFromMotion()
        {
            double tinyRev = motion.TinyIndicatorReveal;
            tinyLayer.Opacity = tinyRev;
            tinyLayer.IsHitTestVisible = !open && pose == NotchPose.Tiny;
            if (tinyCatcher != null)
            {
                tinyCatcher.Opacity = tinyRev;
                tinyCatcher.IsHitTestVisible = !open && pose == NotchPose.Tiny;
            }

            double compactRev = motion.CompactReveal;
            compactLayer.Opacity = compactRev;
            compactLayer.IsHitTestVisible = !open && compactRev > 0.35 && pose != NotchPose.HudPill;
            double cs = 0.90 + 0.10 * compactRev;
            compactScale.ScaleX = cs;
            compactScale.ScaleY = cs;

            double hudRev = motion.HudReveal;
            hudLayer.Opacity = hudRev;
            hudLayer.IsHitTestVisible = !open && (pose == NotchPose.HudPill || pose == NotchPose.NotificationPeek) && hudRev > 0.35;
            hudCard.Visibility = pose == NotchPose.NotificationPeek ? Visibility.Collapsed : Visibility.Visible;
            if (notificationCard != null) notificationCard.Visibility = pose == NotchPose.NotificationPeek ? Visibility.Visible : Visibility.Collapsed;
            hudTranslate.Y = (1.0 - hudRev) * -6.0;
            if (hudBar != null && hudRollsPercent)
            {
                double m = Math.Max(0.0, Math.Min(1.0, motion.HudMeter));
                hudBar.Value = m;
                if (hudValue != null && !string.IsNullOrEmpty(hudRollSuffix))
                    hudValue.Text = ((int)Math.Round(m * 100.0)) + hudRollSuffix;
            }

            if (compactMascot != null)
            {
                bool mascotActive = !open && pose == NotchPose.Compact && compactRev > 0.35 && !media.Current.Playing && state.Settings.ShowMascot && IsVisible && !capture;
                compactMascot.SetActive(mascotActive);
            }

            double expandedRev = motion.ExpandedReveal;
            expandedLayer.Opacity = expandedRev;
            expandedLayer.IsHitTestVisible = open && expandedRev > 0.35;
            double es = motion.ExpandedScale;
            expandedScale.ScaleX = es;
            expandedScale.ScaleY = es;
            expandedTranslate.Y = motion.ExpandedTranslateY;

            double dock = motion.DockReveal;
            if (dockTray != null)
            {
                dockTray.Opacity = dock;
                dockTray.IsHitTestVisible = dock > 0.25 && !IsInlinePose(pose);
                var tt = dockTray.RenderTransform as TranslateTransform;
                if (tt != null)
                    tt.Y = (1.0 - dock) * -6.0;
            }

            if (dotsStrip != null)
            {
                double dotsOpacity = IsInlinePose(pose) ? 0.0 : Math.Max(0.0, 1.0 - dock * 2.6);
                dotsStrip.Opacity = dotsOpacity;
                dotsStrip.IsHitTestVisible = !IsInlinePose(pose) && dock < 0.20;
            }

            if (currentPageVisual != null)
            {
                double sx = motion.PageSlideX;
                if (outgoingPageVisual != null && Math.Abs(sx) > 0.3)
                {
                    if (!capture && Ui.AnimationsAllowed)
                    {
                        if (outgoingPageVisual.CacheMode == null)
                            outgoingPageVisual.CacheMode = animCache;
                        if (currentPageVisual.CacheMode == null)
                            currentPageVisual.CacheMode = animCache;
                    }

                    double prog = motion.PageSlideProgress;
                    currentPageTransform.X = sx;
                    currentPageVisual.Opacity = 0.25 + 0.75 * prog;
                    outgoingPageTransform.X = sx - slideDirection * 110.0;
                    outgoingPageVisual.Opacity = Math.Max(0.0, 1.0 - prog * 1.15);
                }
                else
                {
                    currentPageTransform.X = 0.0;
                    currentPageVisual.Opacity = 1.0;
                    if (currentPageVisual.CacheMode != null)
                        currentPageVisual.CacheMode = null;
                    if (outgoingPageVisual != null)
                        ClearOutgoingPage();
                }
            }
        }

        void ClearOutgoingPage()
        {
            if (outgoingPageVisual != null)
            {
                outgoingPageVisual.CacheMode = null;
                pageViewport.Children.Remove(outgoingPageVisual);
                outgoingPageVisual = null;
            }
        }

        static bool IsClosedPose(NotchPose p)
        {
            return p == NotchPose.Tiny || p == NotchPose.Compact || p == NotchPose.HudPill || p == NotchPose.NotificationPeek;
        }

        void SetPose(NotchPose next)
        {
            if (pose == next)
            {
                double expectedWidth, expectedHeight;
                bool expectedDock, expectedHud;
                TargetDimensions(next, out expectedWidth, out expectedHeight, out expectedDock, out expectedHud);
                if (Math.Abs(motion.TargetWidth - expectedWidth) < 0.1 && Math.Abs(motion.TargetHeight - expectedHeight) < 0.1) return;
            }
            bool targetOpen = !IsClosedPose(next);
            if (targetOpen != open)
            {
                SetOpen(targetOpen);
                if (next != NotchPose.Expanded && !IsClosedPose(next))
                    SetPose(next);
                return;
            }

            bool closingToCompactOrTiny = next == NotchPose.Compact && !IsClosedPose(pose);
            bool shrinkingToTiny = next == NotchPose.Tiny && pose != NotchPose.Tiny;
            bool opening = !IsClosedPose(next) && IsClosedPose(pose);
            pose = next;
            if (next != NotchPose.HudPill)
                hudTimer.Stop();
            UpdateHeaderAndPagerVisibility();

            double w, h;
            bool showDock, showHud;
            TargetDimensions(next, out w, out h, out showDock, out showHud);
            if (!CanAnimate)
            {
                motion.SnapPose(w, h, showDock, showHud);
                return;
            }

            motion.MoveToPose(w, h, showDock, showHud, opening, closingToCompactOrTiny, shrinkingToTiny, false, null);
            EnsureHoverWatch();
        }

        void SetOpen(bool value)
        {
            if (open == value)
            {
                if (value && IsLoaded && !IsVisible)
                {
                    Show();
                    if (!preview && !capture)
                        NativeMethods.EnsureTopmost(hwnd);
                }
                else if (value && IsClosedPose(pose))
                {
                    SetPose(NotchPose.Expanded);
                }

                return;
            }

            bool animate = CanAnimate;
            open = value;
            hoverIntentSince = dockIntentSince = DateTime.MinValue;
            hudTimer.Stop();
            if (!value)
            {
                dragHovering = false;
                HideInlineHost();
            }

            pose = value ? NotchPose.Expanded : RestingClosedPose();
            hoverBlockedUntilExit = !value && IsCursorInsideNotch(0);
            bool shrinkingToTiny = !value && pose == NotchPose.Tiny;
            UpdateHeaderAndPagerVisibility();
            UpdateValues();

            double w, h;
            bool showDock, showHud;
            TargetDimensions(pose, out w, out h, out showDock, out showHud);
            if (animate)
            {
                motion.MoveToPose(w, h, showDock, showHud, value, !value && !shrinkingToTiny, shrinkingToTiny, false, null);
                EnsureHoverWatch();
            }
            else
            {
                motion.SnapPose(w, h, showDock, showHud);
            }
        }

        bool CanAnimate
        {
            get
            {
                return !capture && IsLoaded && IsVisible && !state.Settings.FastAnimations && SystemParameters.ClientAreaAnimation;
            }
        }

        string[] Order()
        {
            var valid = state.Settings.VisiblePages().Where(icons.ContainsKey).Distinct().ToArray();
            return valid.Length > 0 ? valid : new[] { "Music" };
        }

        void ChangePage(int direction)
        {
            ExitInlineIfNeeded();
            var order = Order();
            int i = Array.IndexOf(order, page);
            page = order[(i + direction + order.Length) % order.Length];
            SwitchPageVisual(direction);
        }

        void SelectPage(string selected)
        {
            ExitInlineIfNeeded();
            if (selected == page)
                return;
            var order = Order();
            int direction = Array.IndexOf(order, selected) > Array.IndexOf(order, page) ? 1 : -1;
            page = selected;
            SwitchPageVisual(direction);
        }

        void SwitchPageVisual(int direction)
        {
            UpdateHeaderTitle();
            RebuildDots();
            SyncDockStates();

            if (!IsLoaded || !IsVisible || !CanAnimate || !open)
            {
                ClearOutgoingPage();
                motion.ClearPageSlide();
                RebuildCurrentPage(true);
                return;
            }

            ClearOutgoingPage();
            Canvas previous = currentPageVisual;
            if (previous != null)
            {
                outgoingPageVisual = previous;
                outgoingPageVisual.IsHitTestVisible = false;
                outgoingPageTransform = new TranslateTransform();
                outgoingPageVisual.RenderTransform = outgoingPageTransform;
                if (!capture && Ui.AnimationsAllowed)
                    outgoingPageVisual.CacheMode = animCache;
            }

            slideDirection = Math.Sign(direction);
            if (slideDirection == 0)
                slideDirection = 1;

            RebuildCurrentPage(false);
            motion.KickPageSlide(slideDirection * 110.0);
        }

        void BuildTinyLayer()
        {
            tinyBar = new Border
            {
                Width = 28,
                Height = 2.5,
                CornerRadius = new CornerRadius(1.25),
                Background = Ui.Hex("#4DFFFFFF"),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(tinyBar, (IslandMotion.CanvasWidth - 28.0) * 0.5);
            Canvas.SetTop(tinyBar, motion.TopOffset + 2.0);
            tinyLayer.Children.Add(tinyBar);

            tinyCatcher = new Border
            {
                Width = 64,
                Height = 12,
                Background = Ui.Hex("#01000000"),
                Cursor = Cursors.Hand,
                ToolTip = "DeskPill · hover or click to expand"
            };
            tinyCatcher.MouseEnter += delegate { EnsureHoverWatch(); PollHoverState(); };
            tinyCatcher.MouseLeftButtonUp += delegate
            {
                pinned = state.Settings.OpenMode == "Click";
                SyncDockStates();
                SetOpen(true);
            };
            Canvas.SetLeft(tinyCatcher, (IslandMotion.CanvasWidth - 64.0) * 0.5);
            Canvas.SetTop(tinyCatcher, motion.TopOffset);
            design.Children.Add(tinyCatcher); // Keep the deliberate catch area outside the animated content clip.
        }

        void BuildHudLayer()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(5) });

            hudIcon = Ui.Icon("\uE767", 13, Ui.Green);
            hudIcon.HorizontalAlignment = HorizontalAlignment.Center;
            hudIcon.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRowSpan(hudIcon, 2);
            grid.Children.Add(hudIcon);

            hudTitle = Ui.Text("Volume", 11.5, Ui.White);
            hudTitle.FontWeight = FontWeights.SemiBold;
            hudTitle.Margin = new Thickness(4, 0, 8, 0);
            Grid.SetColumn(hudTitle, 1);
            grid.Children.Add(hudTitle);

            hudValue = Ui.Text("65%", 11.5, Ui.Green);
            hudValue.FontWeight = FontWeights.SemiBold;
            hudValue.Margin = new Thickness(0, 0, 2, 0);
            Grid.SetColumn(hudValue, 2);
            grid.Children.Add(hudValue);

            hudBar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 1,
                Value = 0.65,
                Height = 3,
                Background = Ui.Hex("#262626"),
                Foreground = Ui.Green,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(4, 1, 2, 0)
            };
            Grid.SetRow(hudBar, 1);
            Grid.SetColumn(hudBar, 1);
            Grid.SetColumnSpan(hudBar, 2);
            grid.Children.Add(hudBar);

            hudCard = new Border
            {
                Width = 276,
                Height = 34,
                Padding = new Thickness(10, 4, 10, 4),
                Background = Brushes.Transparent,
                Child = grid
            };
            Canvas.SetLeft(hudCard, (IslandMotion.CanvasWidth - 276.0) * 0.5);
            Canvas.SetTop(hudCard, motion.TopOffset + 2.0);
            hudLayer.Children.Add(hudCard);
        }

        void HandleLiveVolume(float volume, bool muted)
        {
            if (stopping || capture)
                return;
            float prevVol = lastVolume;
            bool prevMuted = lastMuted;
            lastVolume = volume;
            lastMuted = muted;
            if (prevVol < 0f)
                return;
            if (Math.Abs(volume - prevVol) > 0.004f || muted != prevMuted)
            {
                double targetRatio = muted ? 0.0 : Math.Max(0.0, Math.Min(1.0, (double)volume));
                double startFrom = (pose == NotchPose.HudPill && hudRollsPercent) ? -1.0 : (prevMuted ? 0.0 : Math.Max(0.0, Math.Min(1.0, (double)prevVol)));
                ShowRollingHud(
                    muted ? "\uE74F" : "\uE767",
                    muted ? "Muted" : "Volume",
                    targetRatio,
                    startFrom,
                    muted ? "" : "%",
                    2000);
            }
        }

        void ShowRollingHud(string iconGlyph, string label, double targetRatio, double startFrom, string suffix, int holdMs)
        {
            if (hudIcon == null)
                return;
            if (pose == NotchPose.NotificationPeek) return;
            double clamped = Math.Max(0.0, Math.Min(1.0, targetRatio));
            string finalText = string.IsNullOrEmpty(suffix) ? "" : ((int)Math.Round(clamped * 100.0)) + suffix;
            if (!state.Settings.ShowHudPopups || open || dragHovering)
            {
                if (open)
                {
                    toast = label + (string.IsNullOrEmpty(finalText) ? "" : " · " + finalText);
                    toastUntil = DateTime.Now.AddMilliseconds(holdMs);
                    UpdateValues();
                }

                return;
            }

            hudRollsPercent = true;
            hudRollSuffix = suffix ?? "";
            hudIcon.Text = iconGlyph;
            hudTitle.Text = label;
            hudBar.Visibility = Visibility.Visible;
            motion.SetHudMeterTarget(clamped, startFrom, capture || !CanAnimate);
            double displayRatio = Math.Max(0.0, Math.Min(1.0, motion.HudMeter));
            hudBar.Value = displayRatio;
            hudValue.Text = string.IsNullOrEmpty(hudRollSuffix) ? "" : ((int)Math.Round(displayRatio * 100.0)) + hudRollSuffix;

            SetPose(NotchPose.HudPill);
            if (!capture)
            {
                hudTimer.Stop();
                hudTimer.Interval = TimeSpan.FromMilliseconds(holdMs);
                hudTimer.Start();
            }
        }

        void ShowHud(string iconGlyph, string label, string valueText, double meterRatio, int holdMs)
        {
            if (hudIcon == null)
                return;
            if (pose == NotchPose.NotificationPeek) return;
            if (!state.Settings.ShowHudPopups || open || dragHovering)
            {
                if (open)
                {
                    toast = label + (string.IsNullOrEmpty(valueText) ? "" : " · " + valueText);
                    toastUntil = DateTime.Now.AddMilliseconds(holdMs);
                    UpdateValues();
                }

                return;
            }

            hudRollsPercent = false;
            hudRollSuffix = "";
            hudIcon.Text = iconGlyph;
            hudTitle.Text = label;
            hudValue.Text = valueText ?? "";
            bool hasMeter = !double.IsNaN(meterRatio) && meterRatio >= 0.0;
            hudBar.Visibility = hasMeter ? Visibility.Visible : Visibility.Collapsed;
            if (hasMeter)
            {
                double clamped = Math.Max(0.0, Math.Min(1.0, meterRatio));
                motion.SetHudMeterTarget(clamped, clamped, true);
                hudBar.Value = clamped;
            }

            SetPose(NotchPose.HudPill);
            if (!capture)
            {
                hudTimer.Stop();
                hudTimer.Interval = TimeSpan.FromMilliseconds(holdMs);
                hudTimer.Start();
            }
        }

        async Task PollNotifications()
        {
            if (stopping || capture) return;
            bool enabled=state.Settings.MirrorNotifications;
            var arrivals = await Task.Run(()=>notifications.PollAsync(enabled));
            if (stopping) return;
            if (!state.Settings.MirrorNotifications)
            {
                notificationHistory.ClearPending();
                if (pose == NotchPose.NotificationPeek) DismissNotification();
                return;
            }
            bool fullscreen = NativeMethods.IsForegroundFullscreen(hwnd);
            foreach (var arrival in arrivals)
                notificationHistory.Receive(arrival,state.Settings,focus.Running && focus.Phase == "Focus",fullscreen);
            TryShowNextNotification();
        }

        async Task RequestNotificationAccess()
        {
            string status = await notifications.RequestAccessAsync();
            MessageBox.Show(this, status + "\n\nNew notifications appear in the notch. Existing history is not replayed. You can turn mirroring off in Settings.", "DeskPill notification access");
        }

        void TryShowNextNotification()
        {
            if (NativeMethods.IsForegroundFullscreen(hwnd))
            {
                if (state.Settings.FullscreenNotifications == "History only") notificationHistory.ClearPending();
                return;
            }
            if (open || dragHovering || contextMenuOpen || !IsVisible || pose == NotchPose.NotificationPeek || clipboardPalette != null || historyWindow != null || settingsWindow != null || (dictation!=null && dictation.Active) || notificationLaunching) return;
            var next = notificationHistory.Take(state.Settings,focus.Running && focus.Phase == "Focus");
            if (next != null) ShowNotification(next);
        }

        void ShowRecentNotifications()
        {
            notificationHistory.ClearPending();
            if (pose == NotchPose.NotificationPeek) DismissNotification();
            if (historyWindow != null) { historyWindow.Activate(); return; }
            historyWindow = new NotificationHistoryWindow(notificationHistory,OpenNotificationApp,Copy);
            historyWindow.Closed += delegate { historyWindow=null; notificationHistory.ClearPending(); };
            historyWindow.Show(); historyWindow.Activate();
        }

        async void OpenNotificationApp(DesktopNotification item)
        {
            if(notificationLaunching)return;
            notificationLaunching=true;
            if (pose == NotchPose.NotificationPeek) DismissNotification();
            try {if(!await NotificationLauncher.OpenAsync(item)) Notify("Could not open the source app. Open it from Windows instead.");}
            finally {notificationLaunching=false;}
        }

        void ShowClipboardPalette()
        {
            if (clipboardPalette != null) { clipboardPalette.Activate(); return; }
            var target = PasteTarget.Capture();
            clipboardPalette = new ClipboardPalette(state.Clipboard,target != null,target == null ? "" : target.Name,async delegate(string text,bool paste)
            {
                try { Clipboard.SetText(text,TextDataFormat.UnicodeText); }
                catch { Notify("Clipboard is busy. Try again."); return; }
                var palette=clipboardPalette; palette.Close();
                if (paste && (target == null || !await target.TryPasteAsync())) Notify("Copied. Paste manually: Windows could not focus the target app.");
            });
            clipboardPalette.Closed += delegate { clipboardPalette=null; };
            clipboardPalette.Show(); clipboardPalette.Activate();
        }

        void ProcessNotificationEscape(bool down)
        {
            bool pressed = down && !notificationEscapeDown;
            notificationEscapeDown = down;
            if (pressed && pose == NotchPose.NotificationPeek) DismissNotification();
        }

        void OnHudTimeout()
        {
            hudTimer.Stop();
            if (pose == NotchPose.NotificationPeek) { DismissNotification(); return; }
            if (!open && pose == NotchPose.HudPill) SetPose(RestingClosedPose());
        }

        void DismissNotification()
        {
            hudTimer.Stop();
            if (pose == NotchPose.NotificationPeek) SetPose(RestingClosedPose());
            currentNotification=null;
            if(notificationCard!=null){hudLayer.Children.Remove(notificationCard);notificationCard=null;}
            TryShowNextNotification();
        }

        void ShowNotification(DesktopNotification item)
        {
            if (open || dragHovering) return;
            currentNotification = item;
            notificationEscapeDown = (GetAsyncKeyState(0x1B) & 0x8000) != 0;
            if (notificationCard != null) hudLayer.Children.Remove(notificationCard);
            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            body.ColumnDefinitions.Add(new ColumnDefinition());
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            var icon = Ui.Icon("\uE7F4", 18, Ui.Hex("#9BBAFF"));
            icon.VerticalAlignment = VerticalAlignment.Top; icon.Margin = new Thickness(2, 10, 0, 0); body.Children.Add(icon);
            var lines = new StackPanel { Margin = new Thickness(2, 0, 4, 0) };
            var app = Ui.Text(item.AppName, 10, Ui.Muted); app.Margin = new Thickness(0, 0, 0, 3); lines.Children.Add(app);
            var title = Ui.Text(item.Title, 12, Ui.White); title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(0, 0, 0, 3); lines.Children.Add(title);
            lines.Children.Add(new TextBlock { Text = item.Body, Foreground = Ui.Hex("#CCCCCC"), FontSize = 11, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, LineHeight = 15, MaxHeight = 15 });
            var launch = Ui.Button("Open app",delegate { OpenNotificationApp(item); });
            launch.IsEnabled = NotificationLauncher.CanOpen(item.AppId);
            var copy = Ui.Button("Copy",delegate { Copy(item.Title + "\n" + item.Body); });
            var recent = Ui.Button("Recent",ShowRecentNotifications);
            foreach (var action in new[] { launch,copy,recent }) { action.FontSize=10; action.MinHeight=22; action.Padding=new Thickness(7,2,7,2); action.Margin=new Thickness(0,3,4,0); }
            lines.Children.Add(Ui.Row(launch,copy,recent));
            Grid.SetColumn(lines, 1); body.Children.Add(lines);
            var close = Ui.IconButton("\uE711", "Dismiss this DeskPill preview", DismissNotification);
            close.Width = close.Height = close.MinHeight = 22; close.Padding = new Thickness(0); close.Margin = new Thickness(0); close.Background = Brushes.Transparent; close.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(close, 2); body.Children.Add(close);
            notificationCard = new Border { Width = 330, Height = 90, Padding = new Thickness(2, 6, 2, 4), Child = body };
            Canvas.SetLeft(notificationCard, (IslandMotion.CanvasWidth - 330) * 0.5);
            Canvas.SetTop(notificationCard, motion.TopOffset + 6);
            hudLayer.Children.Add(notificationCard);
            SetPose(NotchPose.NotificationPeek);
            hudTimer.Stop(); hudTimer.Interval = TimeSpan.FromSeconds(state.Settings.NotificationSeconds);
            if (!capture) hudTimer.Start();
            EnsureHoverWatch();
        }

        void BuildCompactBar()
        {
            double compactW = DesiredCompactWidth() - 18.0;
            compact = new Grid
            {
                Width = compactW,
                Height = IslandMotion.CompactHeight,
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent
            };
            compact.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            compact.ColumnDefinitions.Add(new ColumnDefinition());
            compact.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });

            compactRing = new Ring
            {
                Width = 15,
                Height = 15,
                Stroke = 2,
                Accent = Ui.Hex("#379CF8"),
                IconGlyph = "\uEB42",
                IconSize = 7.0,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            compact.Children.Add(compactRing);

            compactText = Ui.Text(DateTime.Now.ToString("h:mm tt"), 11.5, Ui.White);
            compactText.FontWeight = FontWeights.SemiBold;
            compactText.HorizontalAlignment = HorizontalAlignment.Center;
            compactText.Margin = new Thickness(3, 0, 3, 0);
            Grid.SetColumn(compactText, 1);
            compact.Children.Add(compactText);

            var rightSlot = new Grid
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            compactEq = new EqBars
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            compactMascot = new NotchMascot
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            rightSlot.Children.Add(compactEq);
            rightSlot.Children.Add(compactMascot);
            Grid.SetColumn(rightSlot, 2);
            compact.Children.Add(rightSlot);

            compact.MouseLeftButtonUp += delegate
            {
                pinned = state.Settings.OpenMode == "Click";
                SyncDockStates();
                SetOpen(true);
            };

            Canvas.SetLeft(compact, (IslandMotion.CanvasWidth - compactW) * 0.5);
            Canvas.SetTop(compact, motion.TopOffset);
            compactLayer.Children.Add(compact);
        }

        void BuildExpandedChrome()
        {
            double yOff = motion.TopOffset;

            // Header bar (340px wide inside 396px classic notch)
            headerRow = new Grid
            {
                Width = 340,
                Height = 26
            };
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition());
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });

            var title = new TextBlock
            {
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Ui.White,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip = "DeskPill · click for Today, right-click for more"
            };
            titleFirstRun = new Run();
            titleSecondRun = new Run { Foreground = Ui.Green };
            title.Inlines.Add(titleFirstRun);
            title.Inlines.Add(titleSecondRun);
            title.MouseLeftButtonUp += delegate
            {
                if (pose == NotchPose.InlineToday)
                    ExitInlineView();
                else
                    ShowInlineToday();
            };
            headerRow.Children.Add(title);

            expandedRing = new Ring
            {
                Width = 16,
                Height = 16,
                Stroke = 2,
                Accent = Ui.Hex("#379CF8"),
                IconGlyph = "\uEB42",
                IconSize = 7.5
            };
            Grid.SetColumn(expandedRing, 1);
            headerRow.Children.Add(expandedRing);

            var clockBlock = new TextBlock
            {
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 10.5,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(6, 0, 4, 0)
            };
            dateRun = new Run { Foreground = Ui.Hex("#B5B5B5") };
            timeRun = new Run { Foreground = Ui.White, FontWeight = FontWeights.SemiBold };
            clockBlock.Inlines.Add(dateRun);
            clockBlock.Inlines.Add(timeRun);
            Grid.SetColumn(clockBlock, 2);
            headerRow.Children.Add(clockBlock);

            expandedEq = new EqBars
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var eqWrap = new Border
            {
                Width = 24,
                Height = 22,
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = "Music player",
                Child = expandedEq
            };
            eqWrap.MouseLeftButtonUp += delegate
            {
                SelectPage("Music");
            };
            Grid.SetColumn(eqWrap, 3);
            headerRow.Children.Add(eqWrap);

            settingsBtn = Ui.IconButton("\uE713", "DeskPill settings", ShowSettings);
            settingsBtn.Width = 24;
            settingsBtn.Height = 24;
            settingsBtn.MinHeight = 24;
            settingsBtn.Padding = new Thickness(0);
            settingsBtn.Margin = new Thickness(0);
            settingsBtn.Background = Brushes.Transparent;
            settingsBtn.Opacity = 0.72;
            settingsBtn.MouseEnter += delegate { settingsBtn.Opacity = 1.0; };
            settingsBtn.MouseLeave += delegate { settingsBtn.Opacity = 0.72; };
            Grid.SetColumn(settingsBtn, 4);
            settingsBtn.Visibility = Visibility.Collapsed;
            headerRow.ColumnDefinitions[4].Width = new GridLength(0);
            headerRow.Children.Add(settingsBtn);

            Canvas.SetLeft(headerRow, ExpandedOffsetX + 28.0);
            Canvas.SetTop(headerRow, yOff + 18.0);
            expandedLayer.Children.Add(headerRow);

            // Viewport + Pager chevrons + Bottom Dots + Dock inside pagerHost
            Canvas.SetLeft(pageViewport, ExpandedOffsetX);
            Canvas.SetTop(pageViewport, yOff + PageTopY);
            pagerHost.Children.Add(pageViewport);

            prevBtn = Ui.IconButton("\uE76B", "Previous page", delegate { ChangePage(-1); });
            prevBtn.Width = 20;
            prevBtn.Height = 28;
            prevBtn.Padding = new Thickness(0);
            prevBtn.Margin = new Thickness(0);
            prevBtn.Background = Brushes.Transparent;
            prevBtn.Opacity = 0.75;
            prevBtn.MouseEnter += delegate { prevBtn.Opacity = 1.0; };
            prevBtn.MouseLeave += delegate { prevBtn.Opacity = 0.75; };
            Canvas.SetLeft(prevBtn, ExpandedOffsetX + 8.0);
            Canvas.SetTop(prevBtn, yOff + 83.0);
            pagerHost.Children.Add(prevBtn);

            nextBtn = Ui.IconButton("\uE76C", "Next page", delegate { ChangePage(1); });
            nextBtn.Width = 20;
            nextBtn.Height = 28;
            nextBtn.Padding = new Thickness(0);
            nextBtn.Margin = new Thickness(0);
            nextBtn.Background = Brushes.Transparent;
            nextBtn.Opacity = 0.75;
            nextBtn.MouseEnter += delegate { nextBtn.Opacity = 1.0; };
            nextBtn.MouseLeave += delegate { nextBtn.Opacity = 0.75; };
            Canvas.SetLeft(nextBtn, ExpandedOffsetX + PageWidth - 28.0);
            Canvas.SetTop(nextBtn, yOff + 83.0);
            pagerHost.Children.Add(nextBtn);

            dots = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            dotsStrip = new Border
            {
                Width = 160,
                Height = 20,
                Background = Brushes.Transparent,
                Child = dots,
                Cursor = Cursors.Hand
            };
            dotsStrip.MouseEnter += delegate
            {
                EnsureHoverWatch();
                PollHoverState();
            };
            Canvas.SetLeft(dotsStrip, (IslandMotion.CanvasWidth - 160.0) * 0.5);
            Canvas.SetTop(dotsStrip, yOff + 150.0);
            pagerHost.Children.Add(dotsStrip);

            BuildDockTray(yOff);
            expandedLayer.Children.Add(pagerHost);

            // Inline contextual panel host (Ask / Today / Lyrics)
            Canvas.SetLeft(inlineHost, (IslandMotion.CanvasWidth - 460.0) * 0.5);
            Canvas.SetTop(inlineHost, yOff + 42.0);
            expandedLayer.Children.Add(inlineHost);

            UpdateHeaderTitle();
            RebuildDots();
            SyncDockStates();
        }

        static Brush PageAccent(string name)
        {
            return name == "Music" ? Ui.Hex("#FF375F") : name == "Focus" ? Ui.Hex("#9160FF")
                : name == "Water" ? Ui.Hex("#279DFF") : name == "Shelf" ? Ui.Hex("#E7BE54") : Ui.Hex("#56DE9A");
        }

        Button NavigationButton(string glyph, string label, Action action)
        {
            var button = Ui.DockIconButton(glyph, label, false, action);
            button.Width = 28; button.Height = button.MinHeight = 24;
            button.Margin = new Thickness(0); button.Background = Brushes.Transparent;
            var icon = button.Content as TextBlock;
            if (icon != null) { icon.FontSize = 12; icon.Foreground = Ui.Hex("#929292"); }
            return button;
        }

        void BuildDockTray(double yOff)
        {
            if (dockTray != null) pagerHost.Children.Remove(dockTray);
            dockPageButtons.Clear();
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (string name in Order())
            {
                string selected = name;
                var button = NavigationButton(icons[name], name + " page", delegate { SelectPage(selected); });
                dockPageButtons[name] = button;
                row.Children.Add(button);
            }
            row.Children.Add(new Border { Width = 1, Height = 12, Margin = new Thickness(3, 0, 4, 0), Background = Ui.Hex("#333333"), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(NavigationButton("\uE713", "Open settings", ShowSettings));
            dockTray = new Border
            {
                Width = Order().Length * 28 + 44, Height = 24, CornerRadius = new CornerRadius(12), Background = Ui.Hex("#141414"),
                Child = row, RenderTransform = new TranslateTransform(0, -6)
            };
            Canvas.SetLeft(dockTray, (IslandMotion.CanvasWidth - dockTray.Width) * 0.5);
            Canvas.SetTop(dockTray, yOff + 168.0);
            pagerHost.Children.Add(dockTray);
        }

        void SyncDockStates()
        {
            foreach (var item in dockPageButtons)
            {
                item.Value.Background = Brushes.Transparent;
                var icon = item.Value.Content as TextBlock;
                if (icon != null) icon.Foreground = item.Key == page ? PageAccent(page) : Ui.Hex("#929292");
            }
        }

        void UpdateHeaderTitle()
        {
            if (titleFirstRun == null || titleSecondRun == null)
                return;
            if (pose == NotchPose.InlineAsk)
            {
                titleFirstRun.Text = "Ask";
                titleSecondRun.Text = "DeskPill";
            }
            else if (pose == NotchPose.InlineToday)
            {
                titleFirstRun.Text = "To";
                titleSecondRun.Text = "day";
            }
            else if (pose == NotchPose.InlineLyrics)
            {
                titleFirstRun.Text = "Track";
                titleSecondRun.Text = "Lyrics";
            }
            else
            {
                titleFirstRun.Text = page == "Tasks" ? "Todo" : page == "Music" ? "" : page == "Shelf" ? "Shelf" : page == "Focus" ? "Pomodoro" : "Water";
                titleSecondRun.Text = page == "Tasks" ? "List" : "";
            }
            if (headerRow != null && !IsInlinePose(pose))
            {
                bool music = page == "Music";
                headerRow.Width = music ? 264 : 340;
                Canvas.SetLeft(headerRow, ExpandedOffsetX + (music ? 110 : 32));
                ((FrameworkElement)headerRow.Children[0]).Visibility = music ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        void RebuildDots()
        {
            if (dots == null)
                return;
            dots.Children.Clear();
            foreach (string name in Order())
            {
                string selected = name;
                var dot = new Button
                {
                    Width = name == page ? 17 : 8,
                    MinHeight = 12,
                    Height = 12,
                    Padding = new Thickness(0),
                    Margin = new Thickness(0, 0, 1, 0),
                    Background = Brushes.Transparent,
                    ToolTip = name
                };
                dot.Content = new Border
                {
                    Width = name == page ? 14 : 3,
                    Height = 3,
                    CornerRadius = new CornerRadius(2),
                    Background = name == page ? Ui.White : Ui.Hex("#555555")
                };
                dot.Click += delegate
                {
                    SelectPage(selected);
                };
                System.Windows.Automation.AutomationProperties.SetName(dot, name + " page");
                dots.Children.Add(dot);
            }
        }

        void RebuildCurrentPage(bool preserveDock)
        {
            if (!Order().Contains(page)) page=Order()[0];
            if (currentPageVisual != null)
            {
                pageViewport.Children.Remove(currentPageVisual);
                currentPageVisual = null;
            }

            currentPageTransform = new TranslateTransform();
            currentPageVisual = new Canvas
            {
                Width = PageWidth,
                Height = PageHeight,
                RenderTransform = currentPageTransform
            };
            pageViewport.Children.Add(currentPageVisual);

            largeValue = null;
            smallValue = null;
            pageRing = null;
            pageContent = null;
            mediaProgress = null;
            mediaElapsed = mediaRemaining = focusStateText = null;
            focusProgress = waterProgress = null;

            if (page == "Music")
                BuildMusic(currentPageVisual);
            else if (page == "Shelf")
                BuildShelf(currentPageVisual);
            else if (page == "Focus")
                BuildFocus(currentPageVisual);
            else if (page == "Water")
                BuildWater(currentPageVisual);
            else
                BuildTasks(currentPageVisual);

            UpdateHeaderTitle();
            RebuildDots();
            SyncDockStates();
            if (prevBtn != null) prevBtn.Visibility = Order().Length > 1 ? Visibility.Visible : Visibility.Collapsed;
            if (nextBtn != null) nextBtn.Visibility = Order().Length > 1 ? Visibility.Visible : Visibility.Collapsed;
            UpdateValues();
            if (!preserveDock && !IsInlinePose(pose) && pose != NotchPose.Compact)
                pose = NotchPose.Expanded;
        }

        static void Put(Canvas target, UIElement element, double x, double y)
        {
            Canvas.SetLeft(element, x);
            Canvas.SetTop(element, y);
            target.Children.Add(element);
        }

        void LeftMetric(Canvas target, string value, string caption)
        {
            largeValue = Ui.Text(value, 34, Ui.White);
            largeValue.FontWeight = FontWeights.Bold;
            largeValue.Width = 148;
            Put(target, largeValue, 34, 6);
            smallValue = Ui.Text(caption, 10, Ui.Muted);
            smallValue.Width = 148;
            Put(target, smallValue, 34, 52);
        }

        void Card(Canvas target, UIElement child)
        {
            var card = Ui.Panel(child);
            card.Width = 172;
            card.Height = 94;
            card.Padding = new Thickness(11);
            Put(target, card, 190, 12);
            pageContent = card;
        }

        void Buttons(Canvas target, params UIElement[] buttons)
        {
            var row = Ui.Row(buttons);
            Put(target, row, 34, 74);
        }

        Button TransportButton(string glyph, string label, Func<Task> action, double size)
        {
            var button = Ui.IconButton(glyph, label, async delegate { await Transport(action); });
            button.Width = 38; button.Height = button.MinHeight = 32; button.Margin = new Thickness(0);
            button.Background = Brushes.Transparent;
            ((TextBlock)button.Content).FontSize = size;
            return button;
        }

        void BuildMusic(Canvas target)
        {
            var current = media.Current;
            var art = new Border { Width = 62, Height = 62, CornerRadius = new CornerRadius(16), Background = Ui.Card, ClipToBounds = true };
            art.Child = current.Artwork != null ? (UIElement)new Image { Source = current.Artwork, Stretch = Stretch.UniformToFill }
                : Ui.Icon("\uE8D6", 28, Ui.Hex("#555555"));
            Put(target, art, 32, -16);
            var title = Ui.Text(current.HasMedia ? current.Title : "Not Playing", 14, Ui.White);
            title.FontWeight = FontWeights.SemiBold; title.Width = 250;
            Put(target, title, 110, 5);
            var artist = Ui.Text(current.HasMedia ? current.Artist : "Play music in a media app", 10, Ui.Muted);
            artist.Width = 250; Put(target, artist, 110, 25);
            mediaElapsed = Ui.Text("0:00", 15, Ui.White); mediaElapsed.Width = 48; Put(target, mediaElapsed, 32, 58);
            mediaRemaining = Ui.Text("−0:00", 15, Ui.White); mediaRemaining.Width = 58; mediaRemaining.TextAlignment = TextAlignment.Right;
            Put(target, mediaRemaining, 306, 58);
            var bar = new ProgressBar { Width = 220, Height = 3, Maximum = 1, Foreground = Ui.White, Background = Ui.Hex("#424242"), BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
            mediaProgress = bar; Put(target, bar, 84, 65);
            bar.MouseLeftButtonDown += async delegate (object sender, MouseButtonEventArgs e)
            {
                if (media.Current.DurationSeconds <= 0) return;
                try { await media.SeekAsync(Math.Max(0, Math.Min(1, e.GetPosition(bar).X / bar.ActualWidth)) * media.Current.DurationSeconds); UpdateValues(); }
                catch (Exception ex) { ReportError(ex); }
            };
            var lyrics = Ui.IconButton("\uE8F2", "Lyrics", ShowInlineLyrics); lyrics.Background = Brushes.Transparent;
            Put(target, lyrics, 46, 82);
            Put(target, TransportButton("\uE892", "Previous track", media.PreviousAsync, 19), 132, 80);
            Put(target, TransportButton(current.Playing ? "\uE769" : "\uE768", "Play or pause", media.PlayPauseAsync, 21), 180, 80);
            Put(target, TransportButton("\uE893", "Next track", media.NextAsync, 19), 228, 80);
            Button volume = null;
            volume = Ui.IconButton("\uE767", "Volume", delegate { OpenVolumeMenu(volume); });
            volume.Background = Brushes.Transparent;
            Put(target, volume, 338, 82);
        }

        void OpenVolumeMenu(Button anchor)
        {
            float current = SystemService.GetVolume();
            if (current < 0) { Notify("Volume unavailable"); return; }
            var slider = new Slider { Minimum = 0, Maximum = 100, Value = current * 100, Width = 160, Margin = new Thickness(8), ToolTip = "System volume" };
            slider.ValueChanged += delegate
            {
                try { SystemService.SetVolume((float)(slider.Value / 100)); }
                catch (Exception ex) { ReportError(ex); }
            };
            var menu = new ContextMenu { PlacementTarget = anchor };
            menu.Items.Add(new MenuItem { Header = slider, StaysOpenOnClick = true });
            AddMenu(menu, "Mute / unmute", delegate
            {
                try { SystemService.ToggleMute(); }
                catch (Exception ex) { ReportError(ex); }
            });
            anchor.ContextMenu = menu;
            menu.IsOpen = true;
        }

        async Task Transport(Func<Task> action)
        {
            try
            {
                await action();
                await RefreshMedia();
                RebuildCurrentPage(true);
            }
            catch (Exception ex)
            {
                ReportError(ex);
            }
        }

        void BuildFocus(Canvas target)
        {
            smallValue = Ui.Text("Focus · 1 of 4", 10, Ui.Muted); Put(target, smallValue, 32, 10);
            largeValue = Ui.Text("25:00", 28, Ui.White); largeValue.FontWeight = FontWeights.Bold; Put(target, largeValue, 32, 28);
            focusStateText = Ui.Text("25 min · Ready", 12, PageAccent("Focus")); Put(target, focusStateText, 32, 70);
            focusProgress = new ProgressBar { Width = 128, Height = 3, Maximum = 1, Foreground = PageAccent("Focus"), Background = Ui.Line, BorderThickness = new Thickness(0) };
            Put(target, focusProgress, 32, 94);
            Put(target, new Border { Width = 1, Height = 88, Background = Ui.Line }, 180, 14);
            Put(target, Ui.Text("This round", 10, Ui.Muted), 194, 16);
            var start = Ui.Button(focus.Running ? "\u23F8 Pause" : "\u25B6 Start", delegate
            {
                if (focus.Running) focus.Pause(DateTime.UtcNow); else focus.Start(DateTime.UtcNow);
                Save(); RebuildCurrentPage(true);
            });
            start.Width = 60; start.MinHeight = start.Height = 22; start.Padding = new Thickness(4, 0, 4, 0);
            start.Background = PageAccent("Focus"); start.Foreground = Ui.Hex("#FDFDFD"); Put(target, start, 278, 14);
            var reset = Ui.IconButton("\uE777", "Reset focus timer", delegate { focus.Reset(); Save(); RebuildCurrentPage(true); });
            reset.Width = reset.Height = reset.MinHeight = 22; reset.Padding = new Thickness(0); Put(target, reset, 342, 14);
            for (int i = 0; i < 5; i++)
            {
                bool completed = i < focus.Round - 1;
                Put(target, new Border { Width = 30, Height = 6, CornerRadius = new CornerRadius(3), Background = completed ? PageAccent("Focus") : Ui.Line }, 194 + i * 34, 55);
                var label = Ui.Text(i == 4 ? "Long" : (i + 1).ToString(), 9, i == focus.Round - 1 ? Ui.White : Ui.Muted);
                label.Width = 30; label.TextAlignment = TextAlignment.Center; Put(target, label, 194 + i * 34, 70);
            }
            var skip = Ui.Button("Skip phase →", delegate { focus.Skip(); Save(); RebuildCurrentPage(true); });
            skip.ToolTip = "Skip this phase";
            skip.Background = Brushes.Transparent; skip.Foreground = Ui.Muted; skip.Padding = new Thickness(0); skip.MinHeight = 20;
            Put(target, skip, 194, 90);
        }

        void BuildWater(Canvas target)
        {
            largeValue = Ui.Text("0.0", 28, Ui.White); largeValue.FontWeight = FontWeights.Bold; largeValue.Width = 88;
            Put(target, largeValue, 32, 10);
            var goal = Ui.Text("/ " + (state.Settings.WaterGoalMl / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " L", 12, Ui.Muted);
            Put(target, goal, 95, 18);
            smallValue = Ui.Text("Today · 0%", 11, Ui.Muted); smallValue.Width = 154; Put(target, smallValue, 32, 45);
            waterProgress = new ProgressBar { Width = 150, Height = 4, Maximum = 1, Foreground = PageAccent("Water"), Background = Ui.Line, BorderThickness = new Thickness(0) };
            Put(target, waterProgress, 32, 65);
            Put(target, new Border { Width = 1, Height = 88, Background = Ui.Line }, 196, 18);
            pageRing = new Ring { Width = 76, Height = 76, Stroke = 6, Accent = PageAccent("Water"), Center = "0%" };
            Put(target, pageRing, 248, 25);
            var drink = Ui.Button("+ Drink", delegate
            {
                state.AddWater(state.Settings.BottleMl, DateTime.Now);
                waterReminder = DateTime.Now.AddMinutes(state.Settings.ReminderMinutes);
                Save(); UpdateValues();
            });
            drink.Background = Ui.Hex("#124B7C"); drink.Padding = new Thickness(10, 3, 10, 3); drink.MinHeight = 26;
            var settings = Ui.Button("Settings", ShowSettings); settings.Padding = new Thickness(8, 3, 8, 3); settings.MinHeight = 26;
            var snooze = Ui.IconButton("\uE708", "Snooze water reminder", delegate { waterReminder = DateTime.Now.AddMinutes(30); Notify("Water reminder snoozed for 30 minutes"); });
            snooze.Width = 26; snooze.MinHeight = 26;
            Put(target, Ui.Row(drink, settings, snooze), 32, 82);
        }

        void BuildTasks(Canvas target)
        {
            int done = state.Tasks.Count(t => t.Done);
            LeftMetric(target, done + "/" + state.Tasks.Count, "Task Done Today");

            var calBadge = Ui.DockIconButton("\uE787", "Open Today view", false, ShowInlineToday);
            calBadge.Width = 26;
            calBadge.Height = 26;
            calBadge.MinHeight = 26;
            Put(target, calBadge, 148, 22);

            var list = new StackPanel();
            if (state.Tasks.Count == 0)
            {
                var empty = Ui.Column(Ui.Icon("\uE9D5", 29, Ui.Hex("#414141")), Ui.Text("No tasks.", 11, Ui.Muted));
                foreach (FrameworkElement e in empty.Children)
                    e.HorizontalAlignment = HorizontalAlignment.Center;
                empty.Children[0].SetValue(FrameworkElement.MarginProperty, new Thickness(0, 2, 0, 6));
                Card(target, empty);
            }
            else
            {
                foreach (var task in state.Tasks.OrderBy(t => t.Done))
                {
                    var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
                    row.ColumnDefinitions.Add(new ColumnDefinition());
                    var check = new CheckBox
                    {
                        IsChecked = task.Done,
                        Margin = new Thickness(0),
                        VerticalAlignment = VerticalAlignment.Center,
                        ToolTip = "Complete task"
                    };
                    TodoItem item = task;
                    check.Click += delegate
                    {
                        item.Done = check.IsChecked == true;
                        item.CompletedAt = item.Done ? DateTime.Now : new DateTime(0, DateTimeKind.Utc);
                        Save();
                        RebuildCurrentPage(true);
                    };
                    row.Children.Add(check);
                    var label = Ui.Text(task.Title, 11, task.Done ? Ui.Muted : Ui.White);
                    if (task.Done)
                        label.TextDecorations = TextDecorations.Strikethrough;
                    label.ToolTip = task.Title;
                    label.Cursor = Cursors.Hand;
                    Grid.SetColumn(label, 1);
                    row.Children.Add(label);
                    label.MouseLeftButtonDown += delegate
                    {
                        selectedTask = item.Id;
                        EditTask(item);
                    };
                    list.Children.Add(row);
                }

                Card(target, new ScrollViewer { Content = list });
            }

            var add = Ui.Button("⊕ Add Task", delegate { EditTask(null); });
            add.Padding = new Thickness(8, 5, 8, 5);
            var edit = Ui.Button("\uE70F Edit Task", delegate
            {
                EditTask(state.Tasks.FirstOrDefault(t => t.Id == selectedTask) ?? state.Tasks.FirstOrDefault());
            });
            edit.IsEnabled = state.Tasks.Count > 0;
            edit.Content = Ui.Row(Ui.Icon("\uE70F", 10, Ui.White), Ui.Text(" Edit Task", 10, Ui.White));
            edit.Padding = new Thickness(8, 5, 8, 5);
            Buttons(target, add, edit);
        }

        void EditTask(TodoItem task)
        {
            pinned = true;
            var dialog = new TaskEditor(task);
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                if (dialog.DeleteRequested && task != null)
                    state.Tasks.Remove(task);
                else if (task == null)
                    state.Tasks.Add(dialog.Result);
                else
                {
                    int index = state.Tasks.IndexOf(task);
                    if (index >= 0)
                        state.Tasks[index] = dialog.Result;
                }

                Save();
                RebuildCurrentPage(true);
            }

            pinned = false;
            SyncDockStates();
            closeTimer.Start();
        }

        void BuildShelf(Canvas target)
        {
            if (shelfTab == "Files" && state.Shelf.Count == 0)
            {
                var drop = new Grid { Width = 336, Height = 92, Cursor = Cursors.Hand, ToolTip = "Drop files here · click to add · right-click for clipboard" };
                var outline = new System.Windows.Shapes.Rectangle { RadiusX = 18, RadiusY = 18, Stroke = Ui.Hex("#414141"), StrokeThickness = 1.2, StrokeDashArray = new DoubleCollection { 3, 3 }, Fill = Ui.Hex("#090909") };
                drop.Children.Add(outline);
                var label = Ui.Column(Ui.Icon("\uE896", 28, Ui.Hex("#858585")), Ui.Text("Drop files here", 13, Ui.White), Ui.Text("Drag out anytime · click to open or copy", 9, Ui.Muted));
                label.VerticalAlignment = VerticalAlignment.Center;
                foreach (FrameworkElement child in label.Children) { child.HorizontalAlignment = HorizontalAlignment.Center; child.Margin = new Thickness(0, 2, 0, 0); }
                drop.Children.Add(label);
                drop.MouseLeftButtonUp += delegate { PickFiles(); };
                var menu = new ContextMenu();
                AddMenu(menu, "Add files…", PickFiles);
                AddMenu(menu, "Clipboard history", delegate { shelfTab = "Clipboard"; RebuildCurrentPage(true); });
                drop.ContextMenu = menu;
                Put(target, drop, 32, 16);
                return;
            }
            var files = Ui.Button("Files", delegate
            {
                shelfTab = "Files";
                RebuildCurrentPage(true);
            });
            var clipboard = Ui.Button("Clipboard", delegate
            {
                shelfTab = "Clipboard";
                RebuildCurrentPage(true);
            });
            files.Foreground = shelfTab == "Files" ? Ui.Green : Ui.Muted;
            clipboard.Foreground = shelfTab == "Clipboard" ? Ui.Green : Ui.Muted;
            Put(target, Ui.Row(files, clipboard), 32, 10);

            var count = Ui.Text((shelfTab == "Files" ? state.Shelf.Count : state.Clipboard.Count) + " saved", 11, Ui.Muted);
            Put(target, count, 34, 46);

            Buttons(target, Ui.Button(shelfTab == "Files" ? "+ Add files" : "Clear history", delegate
            {
                if (shelfTab == "Files")
                    PickFiles();
                else
                {
                    if (MessageBox.Show("Clear DeskPill's clipboard history?", "DeskPill", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    {
                        state.Clipboard.Clear();
                        Save();
                        RebuildCurrentPage(true);
                    }
                }
            }));

            if (shelfTab == "Clipboard")
            {
                var search = Ui.Button("Search & paste",ShowClipboardPalette); search.FontSize=10; search.MinHeight=22; search.Padding=new Thickness(5,2,5,2); Put(target,search,244,12);
            }
            var items = shelfTab == "Files" ? state.Shelf : state.Clipboard;
            var list = new StackPanel();
            if (items.Count == 0)
            {
                var empty = Ui.Column(Ui.Icon(shelfTab == "Files" ? "\uE8B7" : "\uE77F", 26, Ui.Hex("#454545")), Ui.Text(shelfTab == "Files" ? "Drop files here" : state.Settings.ClipboardEnabled ? "Copy text to save it" : "Clipboard history is off", 11, Ui.Muted));
                foreach (FrameworkElement e in empty.Children)
                    e.HorizontalAlignment = HorizontalAlignment.Center;
                Card(target, empty);
            }
            else
            {
                foreach (string item in items.Take(50))
                {
                    string content = item;
                    var b = Ui.Button(shelfTab == "Files" ? System.IO.Path.GetFileName(item) : item.Replace("\r", " ").Replace("\n", " "), delegate
                    {
                        if (shelfTab == "Files")
                            OpenFile(content);
                        else
                            Copy(content);
                    });
                    b.HorizontalContentAlignment = HorizontalAlignment.Left;
                    b.MaxWidth = 146;
                    b.MinHeight = 25;
                    b.Padding = new Thickness(5, 3, 5, 3);
                    b.Margin = new Thickness(0, 1, 0, 1);
                    b.ToolTip = shelfTab == "Files" ? content + "\nDrag to another application. Right-click for options." : content;
                    var menu = new ContextMenu();
                    AddMenu(menu, "Copy" + (shelfTab == "Files" ? " path" : ""), delegate { Copy(content); });
                    AddMenu(menu, "Remove from shelf", delegate
                    {
                        if (shelfTab == "Files")
                            state.Shelf.Remove(content);
                        else
                            state.Clipboard.Remove(content);
                        Save();
                        RebuildCurrentPage(true);
                    });
                    b.ContextMenu = menu;
                    if (shelfTab == "Files")
                    {
                        Point down = new Point();
                        b.PreviewMouseLeftButtonDown += delegate (object sender, MouseButtonEventArgs e)
                        {
                            down = e.GetPosition(b);
                        };
                        b.PreviewMouseMove += delegate (object sender, MouseEventArgs e)
                        {
                            if (e.LeftButton == MouseButtonState.Pressed && (e.GetPosition(b) - down).Length > 6 && (File.Exists(content) || Directory.Exists(content)))
                            {
                                var data = new DataObject(DataFormats.FileDrop, new[] { content });
                                DragDrop.DoDragDrop(b, data, DragDropEffects.Copy | DragDropEffects.Link);
                            }
                        };
                    }

                    list.Children.Add(b);
                }

                Card(target, new ScrollViewer { Content = list });
            }
        }

        // Inline contextual views inside the notch (no jarring popup windows needed)
        void UpdateHeaderAndPagerVisibility()
        {
            bool inlineMode = IsInlinePose(pose);
            pagerHost.Visibility = inlineMode ? Visibility.Collapsed : Visibility.Visible;
            pagerHost.IsHitTestVisible = !inlineMode;
            inlineHost.Visibility = inlineMode ? Visibility.Visible : Visibility.Collapsed;
            inlineHost.IsHitTestVisible = inlineMode;
            if (headerRow != null)
            {
                double hw = pose == NotchPose.InlineAsk ? 412.0 : (pose == NotchPose.InlineToday || pose == NotchPose.InlineLyrics) ? 436.0 : 340.0;
                headerRow.Width = hw;
                Canvas.SetLeft(headerRow, (IslandMotion.CanvasWidth - hw) * 0.5);
            }

            UpdateHeaderTitle();
        }

        void HideInlineHost()
        {
            inlineAskInput = null;
            inlineAskOutput = null;
            inlineTranslateLang = null;
            inlineAskModeBtn = null;
            inlineLyricsLines = null;
            inlineLyricsRuns = null;
            inlineLyricsPlain = null;
            inlineLastActiveLyric = -2;
            inlineHost.Child = null;
            inlineHost.Visibility = Visibility.Collapsed;
            pagerHost.Visibility = Visibility.Visible;
            pagerHost.IsHitTestVisible = true;
        }

        void ExitInlineIfNeeded()
        {
            if (IsInlinePose(pose))
                ExitInlineView();
        }

        void ExitInlineView()
        {
            HideInlineHost();
            if (open)
                SetPose(NotchPose.Expanded);
            else
                UpdateHeaderTitle();
        }

        void ShowInlineView(NotchPose targetPose, double width, UIElement body)
        {
            double panelW = width - 48.0;
            double h = targetPose == NotchPose.InlineAsk ? IslandMotion.InlineAskHeight : targetPose == NotchPose.InlineToday ? IslandMotion.InlineTodayHeight : IslandMotion.InlineLyricsHeight;
            inlineHost.Width = panelW;
            inlineHost.Height = h - 56.0;
            inlineHost.Child = body;
            Canvas.SetLeft(inlineHost, (IslandMotion.CanvasWidth - panelW) * 0.5);
            Canvas.SetTop(inlineHost, motion.TopOffset + 42.0);
            if (!open)
                SetOpen(true);
            SetPose(targetPose);
        }

        void ShowInlineAsk()
        {
            inlineTranslateMode = false;
            var stack = new DockPanel();
            var topBar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var back = Ui.Button("← Back", ExitInlineView);
            back.Padding = new Thickness(8, 3, 8, 3);
            back.MinHeight = 24;
            var popOut = Ui.Button("Pop out", delegate
            {
                ExitInlineView();
                new AskWindow(state.Settings, Notify).Show();
            });
            popOut.Padding = new Thickness(8, 3, 8, 3);
            popOut.MinHeight = 24;

            inlineTranslateLang = new ComboBox
            {
                ItemsSource = new[] { "English", "Indonesian", "Bengali", "Spanish", "French", "Japanese", "Arabic", "German" },
                SelectedIndex = 0,
                MinHeight = 22,
                FontSize = 10.5,
                Margin = new Thickness(4, 0, 4, 0),
                Visibility = Visibility.Collapsed,
                VerticalAlignment = VerticalAlignment.Center
            };
            var subtitle = Ui.Text("Math offline · Currency online", 10.5, Ui.Muted);
            subtitle.HorizontalAlignment = HorizontalAlignment.Center;
            subtitle.Margin = new Thickness(6, 0, 6, 0);

            inlineAskModeBtn = Ui.Button("Mode: Ask", null);
            inlineAskModeBtn.Padding = new Thickness(8, 3, 8, 3);
            inlineAskModeBtn.MinHeight = 24;
            Action syncMode = delegate
            {
                if (inlineAskModeBtn != null)
                {
                    inlineAskModeBtn.Content = inlineTranslateMode ? "Mode: Translate" : "Mode: Ask";
                    inlineAskModeBtn.Background = inlineTranslateMode ? Ui.DockActive : Ui.Hex("#1D1D1D");
                    inlineAskModeBtn.Foreground = inlineTranslateMode ? Ui.Green : Ui.White;
                }
                if (inlineTranslateLang != null)
                    inlineTranslateLang.Visibility = inlineTranslateMode ? Visibility.Visible : Visibility.Collapsed;
                if (subtitle != null)
                    subtitle.Text = inlineTranslateMode ? "Choose a language · Enter translates" : "Math offline · Currency online";
                if (inlineAskInput != null)
                    inlineAskInput.Focus();
            };
            inlineAskModeBtn.Click += delegate
            {
                inlineTranslateMode = !inlineTranslateMode;
                syncMode();
            };

            DockPanel.SetDock(popOut, Dock.Right);
            DockPanel.SetDock(inlineTranslateLang, Dock.Right);
            DockPanel.SetDock(back, Dock.Left);
            DockPanel.SetDock(inlineAskModeBtn, Dock.Left);
            topBar.Children.Add(back);
            topBar.Children.Add(inlineAskModeBtn);
            topBar.Children.Add(popOut);
            topBar.Children.Add(inlineTranslateLang);
            topBar.Children.Add(subtitle);
            DockPanel.SetDock(topBar, Dock.Top);
            stack.Children.Add(topBar);

            inlineAskInput = Ui.Input("");
            inlineAskInput.Height = 32;
            inlineAskInput.FontSize = 12.5;
            inlineAskInput.ToolTip = "Try 24*60=, 100 usd to bdt, or ask anything · Use Mode to translate";
            inlineAskInput.PreviewMouseLeftButtonDown += delegate
            {
                if (!capture && !IsActive)
                    Activate();
                inlineAskInput.Focus();
            };

            inlineAskOutput = UtilityUi.Output("Ask anything, math (24*60), currency (100 usd to bdt), or choose Translate");
            inlineAskOutput.Background = Ui.Hex("#141414");
            inlineAskOutput.BorderBrush = Ui.Line;
            inlineAskOutput.FontSize = 12;

            Button sendBtn = null;
            sendBtn = Ui.Button("Send", delegate { RunInlineAsk(sendBtn); }, true);
            sendBtn.MinHeight = 30;
            var clearBtn = Ui.Button("Clear", delegate
            {
                if (!inlineAskBusy && inlineAskInput != null && inlineAskOutput != null)
                {
                    inlineAskInput.Clear();
                    inlineAskOutput.Text = "";
                    inlineAskInput.Focus();
                }
            });
            clearBtn.MinHeight = 30;

            var inputRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            inputRow.ColumnDefinitions.Add(new ColumnDefinition());
            inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            inputRow.Children.Add(inlineAskInput);
            var actRow = Ui.Row(sendBtn, clearBtn);
            actRow.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(actRow, 1);
            inputRow.Children.Add(actRow);
            DockPanel.SetDock(inputRow, Dock.Top);
            stack.Children.Add(inputRow);
            stack.Children.Add(inlineAskOutput);

            inlineAskInput.PreviewKeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    e.Handled = true;
                    RunInlineAsk(sendBtn);
                }
                else if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    ExitInlineView();
                }
            };

            ShowInlineView(NotchPose.InlineAsk, 460.0, stack);
            if (!capture && !preview)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (!IsActive)
                        Activate();
                    if (inlineAskInput != null)
                        inlineAskInput.Focus();
                }));
            }
        }

        async void RunInlineAsk(Button sendBtn)
        {
            if (inlineAskBusy || inlineAskInput == null || inlineAskOutput == null || string.IsNullOrWhiteSpace(inlineAskInput.Text))
                return;
            TextBox resultTarget = inlineAskOutput;
            inlineAskBusy = true;
            if (sendBtn != null)
                sendBtn.IsEnabled = false;
            inlineAskOutput.Text = inlineTranslateMode ? "Translating…" : "Thinking…";
            string prompt = inlineAskInput.Text.Trim();
            bool translate = inlineTranslateMode;
            string lang = inlineTranslateLang != null ? Convert.ToString(inlineTranslateLang.SelectedItem) : "English";
            if (!translate && prompt.EndsWith("=") && !CurrencyService.IsCurrencyQuery(prompt))
                prompt = prompt.Substring(0, prompt.Length - 1).TrimEnd();
            try
            {
                string answer = translate
                    ? await new AskService().TranslateAsync(prompt, lang, state.Settings)
                    : await new AskService().AskAsync(prompt, state.Settings);
                if (inlineAskOutput == resultTarget)
                    resultTarget.Text = answer;
            }
            catch (Exception ex)
            {
                if (inlineAskOutput == resultTarget)
                    resultTarget.Text = "Unavailable: " + ex.Message;
            }
            finally
            {
                inlineAskBusy = false;
                if (sendBtn != null)
                    sendBtn.IsEnabled = true;
            }
        }

        static string MoonPhaseSummary(DateTime utcNow)
        {
            var epoch = new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);
            double synodic = 29.53058867;
            double age = (utcNow - epoch).TotalDays % synodic;
            if (age < 0)
                age += synodic;
            double frac = age / synodic;
            int illum = (int)Math.Round(0.5 * (1.0 - Math.Cos(frac * Math.PI * 2.0)) * 100.0);
            string name = frac < 0.0625 || frac >= 0.9375 ? "New Moon"
                : frac < 0.1875 ? "Waxing Crescent"
                : frac < 0.3125 ? "First Quarter"
                : frac < 0.4375 ? "Waxing Gibbous"
                : frac < 0.5625 ? "Full Moon"
                : frac < 0.6875 ? "Waning Gibbous"
                : frac < 0.8125 ? "Last Quarter"
                : "Waning Crescent";
            return name + " · " + illum + "% lit";
        }

        async void ShowInlineToday()
        {
            var stack = new DockPanel();
            var topBar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var back = Ui.Button("← Back", ExitInlineView);
            back.Padding = new Thickness(8, 3, 8, 3);
            back.MinHeight = 24;
            var popOut = Ui.Button("Pop out", delegate
            {
                ExitInlineView();
                new TodayWindow(state, Notify).Show();
            });
            popOut.Padding = new Thickness(8, 3, 8, 3);
            popOut.MinHeight = 24;
            var titleInfo = Ui.Text(DateTime.Now.ToString("dddd, dd MMMM yyyy"), 11, Ui.Muted);
            DockPanel.SetDock(popOut, Dock.Right);
            DockPanel.SetDock(back, Dock.Left);
            topBar.Children.Add(back);
            topBar.Children.Add(popOut);
            topBar.Children.Add(titleInfo);
            DockPanel.SetDock(topBar, Dock.Top);
            stack.Children.Add(topBar);

            var cols = new Grid();
            cols.ColumnDefinitions.Add(new ColumnDefinition());
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            cols.ColumnDefinitions.Add(new ColumnDefinition());

            int done = state.Tasks.Count(t => t.Done);
            int battery = SystemService.BatteryPercent();
            string batLine = battery >= 0 ? ("BATTERY · " + battery + "%\n") : "";
            var todayTasks = state.Tasks.Where(t => !t.Done).Take(3).Select(t => "• " + t.Title).ToList();
            string leftText = "TASKS · " + done + "/" + state.Tasks.Count + "\n"
                + (todayTasks.Count == 0 ? "All caught up." : string.Join("\n", todayTasks))
                + "\n\nHYDRATION · " + state.WaterMl + " / " + state.Settings.WaterGoalMl + " ml\n"
                + "FOCUS · " + state.CompletedFocus + " rounds completed\n"
                + batLine
                + "MOON · " + MoonPhaseSummary(DateTime.UtcNow);
            var leftBox = UtilityUi.Output(leftText);
            leftBox.Background = Ui.Hex("#141414");
            leftBox.BorderBrush = Ui.Line;
            leftBox.FontSize = 11.2;
            cols.Children.Add(leftBox);

            var prayersNow = PrayerService.Calculate(DateTime.Now.Date, state.Settings.Latitude, state.Settings.Longitude, state.Settings.PrayerMethod);
            var nextPrayer = prayersNow.FirstOrDefault(p => p.Time >= DateTime.Now);
            string prayerBlock = nextPrayer != null ? ("\nNEXT PRAYER\n" + nextPrayer.Name + " · " + nextPrayer.Time.ToString("HH:mm")) : "";
            var rightBox = UtilityUi.Output("");
            rightBox.Background = Ui.Hex("#141414");
            rightBox.BorderBrush = Ui.Line;
            rightBox.FontSize = 11.2;
            Grid.SetColumn(rightBox, 2);
            cols.Children.Add(rightBox);
            stack.Children.Add(cols);
            ShowInlineView(NotchPose.InlineToday, 484.0, stack);

            string calendarSource = state.Settings.CalendarSource;
            string calendarText = string.IsNullOrWhiteSpace(calendarSource) ? "None configured" : "Loading…";
            string weatherText = "Loading…";
            Action refresh = delegate
            {
                if (pose == NotchPose.InlineToday && inlineHost.Child == stack)
                    rightBox.Text = "WEATHER\n" + weatherText + "\n\nCALENDAR\n" + calendarText + prayerBlock;
            };
            refresh();
            if (capture) return;
            // Each section completes independently; one service failure preserves the others.
            Task calendarTask = LoadInlineCalendar(calendarSource, delegate (string text) { calendarText = text; refresh(); });
            Task weatherTask = LoadInlineWeather(state.Settings.Latitude, state.Settings.Longitude,
                delegate (string text) { weatherText = text; refresh(); });
            await Task.WhenAll(calendarTask, weatherTask);
        }

        async Task LoadInlineCalendar(string source, Action<string> display)
        {
            if (string.IsNullOrWhiteSpace(source)) return;
            try
            {
                var events = await new CalendarService().LoadAsync(source);
                if (state.Settings.CalendarSource != source) { display("Source changed · reopen Today to refresh"); return; }
                var upcoming = events.Where(ev => ev.StartsAt >= DateTime.Today).Take(3).ToList();
                display(upcoming.Count == 0 ? "No upcoming events" : string.Join("\n", upcoming.Select(ev => ev.StartsAt.ToString("ddd HH:mm") + " " + ev.Title)));
            }
            catch (Exception ex) { display("Unavailable: " + ex.Message); }
        }

        async Task LoadInlineWeather(double latitude, double longitude, Action<string> display)
        {
            try { display(await new AskService().WeatherAsync(latitude, longitude)); }
            catch (Exception ex) { display("Unavailable: " + ex.Message); }
        }

        async void ShowInlineLyrics()
        {
            if (!media.Current.HasMedia)
            {
                Notify("Play a track to find its lyrics.");
                return;
            }

            string title = media.Current.Title, artist = media.Current.Artist;
            var stack = new DockPanel();
            var topBar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var back = Ui.Button("← Back", ExitInlineView);
            back.Padding = new Thickness(8, 3, 8, 3);
            back.MinHeight = 24;
            string popText = "Finding lyrics from LRCLIB for " + title + "…";
            var popOut = Ui.Button("Pop out", delegate
            {
                string currentText = inlineLyricsLines != null && inlineLyricsLines.Count > 0
                    ? string.Join("\n", inlineLyricsLines.Select(l => l.Text))
                    : (inlineLyricsPlain != null ? inlineLyricsPlain.Text : popText);
                ExitInlineView();
                new TextResultWindow("Lyrics", title + "\n" + artist + "\n\n" + currentText).Show();
            });
            popOut.Padding = new Thickness(8, 3, 8, 3);
            popOut.MinHeight = 24;
            var trackInfo = Ui.Text(title + (string.IsNullOrEmpty(artist) ? "" : " · " + artist), 11, Ui.Muted);
            DockPanel.SetDock(popOut, Dock.Right);
            DockPanel.SetDock(back, Dock.Left);
            topBar.Children.Add(back);
            topBar.Children.Add(popOut);
            topBar.Children.Add(trackInfo);
            DockPanel.SetDock(topBar, Dock.Top);
            stack.Children.Add(topBar);

            inlineLyricsPlain = new TextBlock
            {
                Text = popText,
                Foreground = Ui.White,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20
            };
            var card = new Border
            {
                Background = Ui.Hex("#141414"),
                BorderBrush = Ui.Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12, 8, 12, 8),
                Child = new ScrollViewer
                {
                    Content = inlineLyricsPlain,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                }
            };
            stack.Children.Add(card);

            ShowInlineView(NotchPose.InlineLyrics, 484.0, stack);
            try
            {
                string rawLyrics = await new LyricsService().LoadAsync(title, artist, media.Current.DurationSeconds);
                if (pose == NotchPose.InlineLyrics && inlineHost.Child == stack)
                {
                    var parsed = LyricsService.Parse(rawLyrics);
                    bool hasTimestamps = parsed.Any(l => l.Seconds >= 0);
                    if (parsed.Count > 0 && hasTimestamps)
                    {
                        inlineLyricsLines = parsed.Where(l => !string.IsNullOrWhiteSpace(l.Text)).ToList();
                        inlineLyricsRuns = new List<Run>();
                        inlineLyricsPlain.Text = "";
                        inlineLyricsPlain.Inlines.Clear();
                        for (int i = 0; i < inlineLyricsLines.Count; i++)
                        {
                            var run = new Run(inlineLyricsLines[i].Text + (i + 1 < inlineLyricsLines.Count ? "\n" : ""))
                            {
                                Foreground = Ui.Muted
                            };
                            inlineLyricsRuns.Add(run);
                            inlineLyricsPlain.Inlines.Add(run);
                        }

                        inlineLastActiveLyric = -2;
                        if (mediaElapsed != null)
            {
                mediaElapsed.Text = ((int)Math.Max(0, media.Current.PositionSeconds) / 60) + ":" + ((int)Math.Max(0, media.Current.PositionSeconds) % 60).ToString("00");
                double secondsLeft = Math.Max(0, media.Current.DurationSeconds - media.Current.PositionSeconds);
                mediaRemaining.Text = "−" + ((int)secondsLeft / 60) + ":" + ((int)secondsLeft % 60).ToString("00");
            }
            UpdateLyricsHighlight();
                    }
                    else
                    {
                        inlineLyricsPlain.Text = rawLyrics + "\n\nLyrics provided by LRCLIB";
                    }
                }
            }
            catch (Exception ex)
            {
                if (pose == NotchPose.InlineLyrics && inlineHost.Child == stack && inlineLyricsPlain != null)
                    inlineLyricsPlain.Text = "Lyrics unavailable: " + ex.Message;
            }
        }

        void UpdateLyricsHighlight()
        {
            if (pose != NotchPose.InlineLyrics || inlineLyricsLines == null || inlineLyricsRuns == null || inlineLyricsLines.Count != inlineLyricsRuns.Count)
                return;
            double pos = media.Current.PositionSeconds;
            int active = -1;
            for (int i = 0; i < inlineLyricsLines.Count; i++)
            {
                if (inlineLyricsLines[i].Seconds >= 0 && inlineLyricsLines[i].Seconds <= pos + 0.3)
                    active = i;
            }

            if (active == inlineLastActiveLyric)
                return;
            inlineLastActiveLyric = active;
            for (int i = 0; i < inlineLyricsRuns.Count; i++)
            {
                bool current = i == active;
                inlineLyricsRuns[i].Foreground = current ? Ui.Green : (i < active ? Ui.Hex("#787878") : Ui.Hex("#C8C8C8"));
                inlineLyricsRuns[i].FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }

        void AddFiles(string[] files)
        {
            foreach (var file in files)
                if ((File.Exists(file) || Directory.Exists(file)) && !state.Shelf.Contains(file) && state.Shelf.Count < 200)
                    state.Shelf.Add(file);
            page = "Shelf";
            shelfTab = "Files";
            Save();
            SetOpen(true);
            RebuildCurrentPage(true);
        }

        void PickFiles()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true,
                Title = "Add files to DeskPill shelf"
            };
            if (dialog.ShowDialog(this) == true)
                AddFiles(dialog.FileNames);
        }

        void OpenFile(string file)
        {
            try
            {
                if (!File.Exists(file) && !Directory.Exists(file))
                {
                    Notify("This file has moved. Remove its shelf reference or add it again.");
                    return;
                }

                Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ReportError(ex);
            }
        }

        void Copy(string text)
        {
            try
            {
                Clipboard.SetText(text);
                Notify("Copied to clipboard");
            }
            catch (ExternalException)
            {
                Notify("Clipboard is busy. Try again.");
            }
        }

        void CaptureClipboard()
        {
            if (!state.Settings.ClipboardEnabled || clipboardBusy || capture)
                return;
            clipboardBusy = true;
            try
            {
                if (Clipboard.ContainsText())
                {
                    string text = Clipboard.GetText();
                    if (!string.IsNullOrWhiteSpace(text) && text.Length <= 20000)
                    {
                        state.Clipboard.Remove(text);
                        state.Clipboard.Insert(0, text);
                        if (state.Clipboard.Count > 50)
                            state.Clipboard.RemoveRange(50, state.Clipboard.Count - 50);
                        if (state.Settings.KeepClipboard)
                            Save();
                        if (open && page == "Shelf" && shelfTab == "Clipboard" && clipboardPalette == null)
                            RebuildCurrentPage(true);
                    }
                }
            }
            catch (ExternalException)
            {
            }
            finally
            {
                clipboardBusy = false;
            }
        }

        void UpdateValues()
        {
            var now = DateTime.Now;
            double progress = state.Settings.StatusRing == "Tasks" ? (state.Tasks.Count == 0 ? 0 : (double)state.Tasks.Count(t => t.Done) / state.Tasks.Count) : state.Settings.StatusRing == "Battery" ? Math.Max(0, SystemService.BatteryPercent()) / 100.0 : state.Settings.StatusRing == "None" ? 0 : state.Settings.StatusRing == "Focus" ? 1 - focus.Remaining(DateTime.UtcNow).TotalMinutes / (focus.Phase == "Focus" ? state.Settings.FocusMinutes : focus.Phase == "Short break" ? state.Settings.ShortBreakMinutes : state.Settings.LongBreakMinutes) : (double)state.WaterMl / state.Settings.WaterGoalMl;

            string sourceName = state.Settings.StatusRing;
            string glyph = sourceName == "Tasks" ? "\uE73E" : sourceName == "Battery" ? "\uE83F" : sourceName == "Focus" ? "\uE916" : "\uEB42";
            Brush accent = sourceName == "Tasks" ? PageAccent("Tasks") : sourceName == "Focus" ? PageAccent("Focus") : sourceName == "Battery" ? Ui.Green : Ui.Hex("#379CF8");
            foreach (var ring in new[] { compactRing, expandedRing })
            {
                if (ring == null) continue;
                ring.Visibility = sourceName == "None" ? Visibility.Hidden : Visibility.Visible;
                ring.IconGlyph = glyph;
                if(!object.ReferenceEquals(ring.Accent,accent)){ring.Accent=accent;ring.InvalidateVisual();}
                ring.Progress = progress;
                ring.ToolTip = sourceName + " · " + (Math.Max(0, Math.Min(1, progress))*100).ToString("0") + "%";
            }

            bool isPlaying = media.Current.Playing;
            if (compactEq != null)
                compactEq.Playing = isPlaying;
            if (expandedEq != null)
                expandedEq.Playing = isPlaying;

            bool showMascot = !isPlaying && state.Settings.ShowMascot;
            if (compactMascot != null && compactEq != null)
            {
                compactMascot.Visibility = showMascot ? Visibility.Visible : Visibility.Collapsed;
                compactEq.Visibility = showMascot ? Visibility.Collapsed : Visibility.Visible;
                compactMascot.SetActive(!open && pose == NotchPose.Compact && showMascot && IsVisible && !capture);
            }

            if (compactText != null)
            {
                if (now < toastUntil)
                    compactText.Text = toast;
                else if (focus.Running)
                    compactText.Text = FormatTime(focus.Remaining(DateTime.UtcNow));
                else if (isPlaying)
                    compactText.Text = state.Settings.ShowClock ? now.ToString("h:mm tt") : "Playing";
                else
                    compactText.Text = state.Settings.ShowClock ? now.ToString("h:mm tt") : "DeskPill";

                double desiredCompactW = DesiredCompactWidth() - 18.0;
                if (compact != null && Math.Abs(compact.Width - desiredCompactW) > 0.5)
                {
                    compact.Width = desiredCompactW;
                    Canvas.SetLeft(compact, (IslandMotion.CanvasWidth - desiredCompactW) * 0.5);
                    if (!open && pose == NotchPose.Compact)
                        SetPose(NotchPose.Compact);
                }
            }

            if (mediaElapsed != null)
            {
                mediaElapsed.Text = ((int)Math.Max(0, media.Current.PositionSeconds) / 60) + ":" + ((int)Math.Max(0, media.Current.PositionSeconds) % 60).ToString("00");
                double secondsLeft = Math.Max(0, media.Current.DurationSeconds - media.Current.PositionSeconds);
                mediaRemaining.Text = "−" + ((int)secondsLeft / 60) + ":" + ((int)secondsLeft % 60).ToString("00");
            }
            UpdateLyricsHighlight();

            if (dateRun != null && timeRun != null)
            {
                if (now < toastUntil)
                {
                    dateRun.Text = "";
                    timeRun.Text = toast;
                    timeRun.Foreground = Ui.Green;
                }
                else if (state.Settings.ShowClock)
                {
                    dateRun.Text = now.ToString("ddd d MMM yyyy  ");
                    timeRun.Text = now.ToString("h:mm tt");
                    timeRun.Foreground = Ui.White;
                }
                else
                {
                    dateRun.Text = "";
                    timeRun.Text = "";
                }
            }

            if (page == "Music" && mediaProgress != null)
                mediaProgress.Value = media.Current.DurationSeconds > 0 ? Math.Min(1, Math.Max(0, media.Current.PositionSeconds / media.Current.DurationSeconds)) : 0;

            if (page == "Focus" && largeValue != null)
            {
                var remaining = focus.Remaining(DateTime.UtcNow);
                largeValue.Text = FormatTime(remaining);
                smallValue.Text = focus.Phase + " · " + focus.Round + " of 4";
                int total = focus.Phase == "Focus" ? state.Settings.FocusMinutes : focus.Phase == "Short break" ? state.Settings.ShortBreakMinutes : state.Settings.LongBreakMinutes;
                if (focusProgress != null) focusProgress.Value = Math.Max(0, Math.Min(1, 1 - remaining.TotalMinutes / total));
                if (focusStateText != null) focusStateText.Text = total + " min · " + (focus.Running ? "Running" : "Ready");
            }

            if (page == "Water" && largeValue != null)
            {
                largeValue.Text = (state.WaterMl / 1000.0).ToString("0.0", CultureInfo.InvariantCulture);
                smallValue.Text = "Today · " + (int)(100.0 * state.WaterMl / state.Settings.WaterGoalMl) + "% · " + state.Settings.BottleMl + " ml / drink";
                if (waterProgress != null) waterProgress.Value = Math.Min(1, (double)state.WaterMl / state.Settings.WaterGoalMl);
                pageRing.Center = Math.Min(999, (int)(100.0 * state.WaterMl / state.Settings.WaterGoalMl)) + "%";
                pageRing.Progress = (double)state.WaterMl / state.Settings.WaterGoalMl;
            }
        }

        static string FormatTime(TimeSpan time)
        {
            return ((int)time.TotalMinutes).ToString("00") + ":" + time.Seconds.ToString("00");
        }

        async Task Tick()
        {
            if (stopping)
                return;
            ticks++;
            string date = state.WaterDate;
            state.Normalize(DateTime.Now);
            if (date != state.WaterDate)
            {
                waterReminder = DateTime.Now.AddMinutes(state.Settings.ReminderMinutes);
                Save();
            }

            if (focus.Tick(DateTime.UtcNow))
            {
                state.CompletedFocus = focus.Completed;
                Save();
                Notify("Time for " + focus.Phase.ToLowerInvariant() + ". Start when you're ready.");
                Chime();
                RebuildCurrentPage(true);
            }

            foreach (var task in state.Tasks.Where(t => !t.Done))
            {
                if (task.StartsAt.HasValue && !task.StartNotified && DateTime.Now >= task.StartsAt.Value)
                {
                    task.StartNotified = true;
                    RemindTask(task, "Starting now");
                    Save();
                }

                if (task.EndsAt.HasValue && !task.EndNotified && DateTime.Now >= task.EndsAt.Value)
                {
                    task.EndNotified = true;
                    RemindTask(task, "Time is up");
                    Save();
                }
            }

            int hour = DateTime.Now.Hour;
            bool active = state.Settings.ActiveStart <= state.Settings.ActiveEnd ? hour >= state.Settings.ActiveStart && hour < state.Settings.ActiveEnd : hour >= state.Settings.ActiveStart || hour < state.Settings.ActiveEnd;
            if (DateTime.Now >= waterReminder)
            {
                waterReminder = DateTime.Now.AddMinutes(state.Settings.ReminderMinutes);
                if (active && state.WaterMl < state.Settings.WaterGoalMl)
                {
                    Notify("Time for a little water");
                    Chime();
                }
            }

            if (state.Settings.PrayerReminder)
            {
                string key = DateTime.Now.ToString("yyyy-MM-dd") + state.Settings.PrayerMethod + state.Settings.Latitude.ToString(CultureInfo.InvariantCulture) + state.Settings.Longitude.ToString(CultureInfo.InvariantCulture);
                if (key != prayerKey)
                {
                    prayerKey = key;
                    prayers = PrayerService.Calculate(DateTime.Now.Date, state.Settings.Latitude, state.Settings.Longitude, state.Settings.PrayerMethod);
                    prayed.Clear();
                }

                foreach (var prayer in prayers)
                {
                    double seconds = (DateTime.Now - prayer.Time).TotalSeconds;
                    if (seconds >= 0 && seconds < 60 && prayed.Add(prayer.Name))
                    {
                        Notify(prayer.Name + " · prayer time");
                        Chime();
                    }
                }
            }

            if (ticks % 2 == 0)
                await RefreshMedia();
            if (ticks % 3 == 0)
            {
                bool hide = state.Settings.HideFullscreen && !preview && NativeMethods.IsForegroundFullscreen(hwnd);
                if (hide && IsVisible)
                    Hide();
                else if (!hide && !IsVisible)
                {
                    Show();
                    if (!preview && !capture)
                        NativeMethods.EnsureTopmost(hwnd);
                }
            }

            float volume = SystemService.GetVolume();
            bool muted = SystemService.GetMuted();
            int battery = SystemService.BatteryPercent();
            int brightness = await Task.Run(() => SystemService.GetBrightness());
            bool caps = NativeMethods.CapsLock();
            if (lastVolume >= 0 && volume >= 0 && (Math.Abs(volume - lastVolume) > .015f || muted != lastMuted))
            {
                double startFrom = (pose == NotchPose.HudPill && hudRollsPercent) ? -1.0 : (lastMuted ? 0.0 : lastVolume);
                ShowRollingHud(muted ? "\uE74F" : "\uE767", muted ? "Muted" : "Volume", muted ? 0.0 : volume, startFrom, muted ? "" : "%", 2000);
            }
            if (lastBrightness >= 0 && brightness >= 0 && Math.Abs(brightness - lastBrightness) >= 2)
            {
                double startB = (pose == NotchPose.HudPill && hudRollsPercent) ? -1.0 : (lastBrightness / 100.0);
                ShowRollingHud("\uE706", "Brightness", brightness / 100.0, startB, "%", 2000);
            }
            if (lastBattery >= 0 && battery >= 0 && battery <= 20 && battery != lastBattery)
                ShowHud("\uE83F", "Low battery", battery + "%", battery / 100.0, 2600);
            if (caps != lastCaps)
                ShowHud("\uE751", "Caps Lock", caps ? "On" : "Off", -1.0, 1600);
            lastVolume = volume;
            lastMuted = muted;
            lastBrightness = brightness;
            lastBattery = battery;
            lastCaps = caps;

            string language = NativeMethods.InputLanguage();
            if (!string.IsNullOrEmpty(language) && language != lastLanguage)
            {
                if (!string.IsNullOrEmpty(lastLanguage))
                    ShowHud("\uE775", "Input language", language, -1.0, 1600);
                lastLanguage = language;
            }

            UpdateValues();
        }

        void RemindTask(TodoItem task, string label)
        {
            Notify(label + " · " + task.Title);
            Chime();
            if (tray != null)
            {
                tray.BalloonTipTitle = "DeskPill · " + label;
                tray.BalloonTipText = task.Title;
                tray.ShowBalloonTip(5000);
            }
        }

        void Chime()
        {
            if (state.Settings.Sound)
                System.Media.SystemSounds.Asterisk.Play();
        }

        async Task RefreshMedia()
        {
            if (polling)
                return;
            polling = true;
            try
            {
                await Task.Run(() => media.RefreshAsync());
                string identity = media.Current.Title + media.Current.Artist + media.Current.Playing;
                if (identity != lastMedia)
                {
                    lastMedia = identity;
                    if (page == "Music" && !IsInlinePose(pose))
                        RebuildCurrentPage(true);
                    else
                        UpdateValues();
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                polling = false;
            }
        }

        void ApplyTheme() { Themes.ConfigureCustom(state.Settings); ApplyTheme(state.Settings.Theme); }

        void ApplyTheme(string name)
        {
            Themes.Apply(name);
            shell.Fill = Themes.ShellFill;
            bool isClassic = Themes.Current == "Classic";
            rim.StrokeThickness = 1.0;
            // The themed outline supplies separation. Colored shadows create a halo
            // that grows with hover expansion; only Classic retains its original depth.
            shellShadowDisabled = !isClassic;
            var shadow = shell.Effect as System.Windows.Media.Effects.DropShadowEffect;
            if (shadow != null)
            {
                shadow.Color = Colors.Black;
                shadow.BlurRadius = 26;
                shadow.ShadowDepth = 7;
                shadow.Opacity = isClassic ? 0.26 * (motion == null ? 1 : Math.Max(0, Math.Min(1, (motion.CurrentHeight - 30) / 142))) : 0;
            }
        }

        void ShowSettings()
        {
            if(dictation!=null && dictation.Active)dictation.Cancel();
            closeTimer.Stop();
            if (settingsWindow != null)
            {
                settingsWindow.Activate();
                return;
            }

            int previousFocus = state.Settings.FocusMinutes;
            settingsWindow = new SettingsWindow(state, delegate
            {
                Ui.MotionEnabled = !capture && !state.Settings.FastAnimations;
                state.Normalize(DateTime.Now);
                ApplyTheme();
                if (!focus.Running && focus.Phase == "Focus" && focus.Remaining(DateTime.UtcNow) == TimeSpan.FromMinutes(previousFocus))
                    focus.Reset();
                focus.SaveTo(state, DateTime.UtcNow);
                store.Save(state);
                waterReminder = DateTime.Now.AddMinutes(state.Settings.ReminderMinutes);
                Place();
                RebuildCurrentPage(true);
                BuildDockTray(motion.TopOffset);
                SyncDockStates();
                SnapPoseImmediate();
                ContextMenu=BuildMenu();
                if(dictation!=null)dictation.Configure();
                if (pose == NotchPose.NotificationPeek && (currentNotification == null || !NotificationHistory.Allowed(currentNotification,state.Settings,focus.Running && focus.Phase == "Focus"))) DismissNotification();
            }, Notify, ApplyTheme, notificationHistory.Items);
            settingsWindow.Closed += delegate { settingsWindow = null; };
            settingsWindow.Show();
            settingsWindow.Activate();
        }

        async void StartOcr()
        {
            try
            {
                pinned = true;
                SyncDockStates();
                var selector = new RegionSelector();
                selector.Show();
                var bitmap = await selector.Completion;
                if (bitmap == null)
                    return;
                using (bitmap)
                {
                    string text = await OcrService.RecognizeAsync(bitmap);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        Notify("No text was recognized in that area.");
                        return;
                    }

                    Copy(text);
                    new TextResultWindow("Text from screen", text).Show();
                }
            }
            catch (Exception ex)
            {
                ReportError(ex);
            }
            finally
            {
                pinned = false;
                SyncDockStates();
            }
        }

        void SaveShot(string folder, string fileName)
        {
            Place();
            UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(ActualWidth), (int)Math.Ceiling(ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(System.IO.Path.Combine(folder, fileName)))
                encoder.Save(file);
        }

        async Task CapturePages()
        {
            string folder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "docs", "screenshots");
            Directory.CreateDirectory(folder);
            foreach (string name in Order())
            {
                page = name;
                open = true;
                pose = NotchPose.Expanded;
                HideInlineHost();
                UpdateHeaderAndPagerVisibility();
                RebuildCurrentPage(false);
                SnapPoseImmediate();
                await Task.Delay(70);
                SaveShot(folder, name.ToLowerInvariant() + ".png");
            }

            page = "Music";
            open = true;
            pose = NotchPose.OptionsDock;
            HideInlineHost();
            UpdateHeaderAndPagerVisibility();
            RebuildCurrentPage(true);
            SnapPoseImmediate();
            await Task.Delay(50);
            SaveShot(folder, "options-dock.png");

            ShowInlineAsk();
            SnapPoseImmediate();
            await Task.Delay(50);
            SaveShot(folder, "inline-ask.png");

            ShowInlineToday();
            SnapPoseImmediate();
            await Task.Delay(50);
            SaveShot(folder, "inline-today.png");

            HideInlineHost();
            open = false;
            ShowHud("\uE767", "Volume", "72%", 0.72, 2000);
            SnapPoseImmediate();
            await Task.Delay(40);
            SaveShot(folder, "hud.png");

            hudTimer.Stop();
            pose = NotchPose.Compact;
            UpdateHeaderAndPagerVisibility();
            UpdateValues();
            SnapPoseImmediate();
            await Task.Delay(40);
            SaveShot(folder, "compact.png");

            pose = NotchPose.Tiny;
            UpdateHeaderAndPagerVisibility();
            SnapPoseImmediate();
            await Task.Delay(40);
            SaveShot(folder, "tiny.png");
            Exit();
        }

        public void Exit()
        {
            if (!Save() && MessageBox.Show("Changes could not be saved. Exit DeskPill anyway?", "DeskPill", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            stopping = true;
            Cleanup();
            Application.Current.Shutdown();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!stopping && !capture)
            {
                if (!Save(false))
                {
                    e.Cancel = true;
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        Notify("Couldn't save changes: " + store.LastError);
                    }));
                    return;
                }

                stopping = true;
            }

            base.OnClosing(e);
        }

        void Cleanup()
        {
            if(dictation!=null){dictation.Dispose();dictation=null;}
            ClearOutgoingPage();
            hoverTimer.Stop();
            hudTimer.Stop();
            tabWatchTimer.Stop();
            motion.Stop();
            notificationTimer.Stop();
            if (clipboardPalette != null) clipboardPalette.Close();
            if (historyWindow != null) historyWindow.Close();
            notifications.Dispose();
            notificationHistory.ClearPending();
            heartbeat.Stop();
            if (volumeWatcher != null)
            {
                volumeWatcher.Dispose();
                volumeWatcher = null;
            }
            closeTimer.Stop();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            if (hwnd != IntPtr.Zero && !capture && !preview)
            {
                NativeMethods.UnregisterHotKey(hwnd, 1);
                NativeMethods.UnregisterHotKey(hwnd, 2);
                NativeMethods.UnregisterHotKey(hwnd, 3);
                NativeMethods.DisableClipboardListener(hwnd);
            }

            if (source != null)
                source.RemoveHook(WindowMessage);
            if (tray != null)
            {
                tray.Visible = false;
                tray.Dispose();
                tray = null;
            }
        }
    }
}
