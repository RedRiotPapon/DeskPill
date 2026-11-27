using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Threading.Tasks;
using System.Windows.Threading;
using DeskPill;
class InteractionTests {
 static int passed,failed;
 static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
 static object Field(object o,string name) { return o.GetType().GetField(name,Private).GetValue(o); }
 static void Call(object o,string name,params object[] args) { o.GetType().GetMethods(Private).Single(m=>m.Name==name && m.GetParameters().Length==args.Length).Invoke(o,args); }
 static IEnumerable<T> Controls<T>(DependencyObject p) where T:DependencyObject {
  foreach(object o in LogicalTreeHelper.GetChildren(p)) { var d=o as DependencyObject; if(d==null)continue; if(d is T)yield return (T)d; foreach(var t in Controls<T>(d))yield return t; }
 }
 static void Check(bool ok,string message) { if(!ok)throw new Exception(message); }
 static void Test(string name,Action test) { try {test();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+(e.InnerException??e).Message);} }
 static IslandWindow Island() {
  var state=new AppState();state.Settings.Sound=false;state.Settings.ClipboardEnabled=false;state.Settings.FastAnimations=true;
  return new IslandWindow(state,new StateStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"fixture")),true,true);
 }
 [STAThread] static int Main() {
  var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};Ui.Init(app);
  Test("Live Settings preview updates the island and cancels without resetting its content",delegate {
   var w=Island();var state=(AppState)Field(w,"state");var page=Field(w,"currentPageVisual");var focus=Field(w,"focus");
   Call(w,"ShowSettings");var settings=(SettingsWindow)Field(w,"settingsWindow");
   try {
    Controls<Button>(settings).Single(b=>Convert.ToString(b.Content)=="Appearance").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Controls<RadioButton>(settings).Single(b=>(string)b.Tag=="Sakura Shrine").IsChecked=true;
    var shadow=((System.Windows.Shapes.Path)Field(w,"shell")).Effect as System.Windows.Media.Effects.DropShadowEffect;
    Check(Themes.Current=="Sakura Shrine" && shadow.Opacity==0,"Island preview kept the colored halo");
    Check(state.Settings.Theme=="Classic" && object.ReferenceEquals(page,Field(w,"currentPageVisual")) && object.ReferenceEquals(focus,Field(w,"focus")),"Preview changed state or rebuilt live content");
    Call(w,"SetPose",NotchPose.OptionsDock);Check(shadow.Opacity==0,"Hover expansion restored the halo");
    settings.Close();Check(Themes.Current=="Classic" && shadow.Color==Colors.Black && shadow.Opacity==.26,"Cancel did not restore Classic depth");
   } finally {settings.Close();Call(w,"Cleanup");}
  });
  Test("Every non-Classic skin stays free of outer glow through expansion and collapse",delegate {
   var w=Island();
   try {foreach(var theme in Themes.Names.Where(t=>t!="Classic")) {
    Call(w,"ApplyTheme",theme);var shadow=((System.Windows.Shapes.Path)Field(w,"shell")).Effect as System.Windows.Media.Effects.DropShadowEffect;
    foreach(var pose in new[]{NotchPose.Compact,NotchPose.Expanded,NotchPose.OptionsDock,NotchPose.Compact}){Call(w,"SetPose",pose);Check(shadow.Opacity==0 && shadow.Color==Colors.Black,"Halo in "+theme+" "+pose);}
   }} finally {Call(w,"ApplyTheme","Classic");Call(w,"Cleanup");}
  });
  Test("Hidden pages disappear from carousel, dots and dock",delegate {
   var state=new AppState();state.Settings.HiddenPages=new[]{"Music","Water","Focus","Shelf"};state.Settings.FastAnimations=true;
   var w=new IslandWindow(state,new StateStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"hidden-fixture")),true,true);
   try {
    Check(Convert.ToString(Field(w,"page"))=="Tasks","Hidden startup page stayed selected");
    var order=(string[])typeof(IslandWindow).GetMethod("Order",Private).Invoke(w,null);Check(order.SequenceEqual(new[]{"Tasks"}),"Hidden pages remain in navigation");
    Check(((System.Collections.IDictionary)Field(w,"dockPageButtons")).Count==1,"Dock still exposes hidden pages");
    Check(((FrameworkElement)Field(w,"prevBtn")).Visibility==Visibility.Collapsed,"Single page kept redundant arrows");
    Call(w,"ChangePage",1);Check(Convert.ToString(Field(w,"page"))=="Tasks","Single-page navigation escaped visible set");
   } finally {Call(w,"Cleanup");}
  });
  Test("Saving page visibility keeps an open dock aligned and selected",delegate {
   var w=Island();Call(w,"SetPose",NotchPose.OptionsDock);Call(w,"ShowSettings");var settings=(SettingsWindow)Field(w,"settingsWindow");
   try {
    Controls<Button>(settings).Single(b=>Convert.ToString(b.Content)=="Pages").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Controls<CheckBox>(settings).Single(c=>Convert.ToString(c.Content)=="Water").IsChecked=false;
    Controls<Button>(settings).Single(b=>Convert.ToString(b.Content)=="Save changes").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    var dock=(Border)Field(w,"dockTray");Check(dock.Width==156 && Math.Abs(((TranslateTransform)dock.RenderTransform).Y)<.01 && dock.Opacity==1,"Rebuilt dock was offset or hidden after Save");
    Check(Convert.ToString(Field(w,"pose"))=="OptionsDock","Settings collapsed the open dock");
   } finally {settings.Close();Call(w,"Cleanup");}
  });
  Test("Inline Ask Send evaluates arithmetic",delegate {
   var w=Island();Call(w,"ShowInlineAsk");
   var input=Controls<TextBox>(w).Single(t=>!t.IsReadOnly);var output=Controls<TextBox>(w).Single(t=>t.IsReadOnly);
   input.Text="24*60";Controls<Button>(w).Single(b=>Convert.ToString(b.Content)=="Send").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   Check(output.Text=="1440","Expected 1440, got: "+output.Text);Call(w,"Cleanup");
  });
  Test("Inline Ask pointer entry keeps live controls",delegate {
   var w=Island();Call(w,"ShowInlineAsk");var input=Controls<TextBox>(w).Single(t=>!t.IsReadOnly);
   input.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseDownEvent});Call(w,"Cleanup");
  });
  Test("Geometry MouseEnter cannot reopen without physical pointer intent",delegate {
   var w=Island();Call(w,"SetOpen",false);
   w.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseEnterEvent});
   Check(!(bool)Field(w,"open"),"Synthetic layout enter reopened closed notch");Call(w,"Cleanup");
  });
  Test("Crossing page dots does not immediately replace navigation",delegate {
   var w=Island();var dots=(FrameworkElement)Field(w,"dotsStrip");
   dots.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseEnterEvent});
   Check(Convert.ToString(Field(w,"pose"))=="Expanded","Dock opened before hover intent");Call(w,"Cleanup");
  });
  Test("Solid ordinary window overlap is detected without foreground or pixel edges",delegate {
   var fixture=new Window {Width=600,Height=220,Left=-4000,Top=320,WindowStyle=WindowStyle.None,ShowActivated=false,ShowInTaskbar=false,Background=Brushes.Black};
   fixture.Show();fixture.UpdateLayout();var origin=fixture.PointToScreen(new Point(0,0));
   bool overlaps=NativeMethods.IsForegroundOverlappingTabs(IntPtr.Zero,(int)origin.X+300,(int)origin.Y+50,180,30);
   fixture.Hide();fixture.Close();Check(overlaps,"Visible solid overlap was missed");
  });
  Test("Empty custom title area stays compact but text and buttons require tiny",delegate {
   var canvas=new Canvas();var button=new Button {Content="Control",Width=80,Height=25};Canvas.SetLeft(button,15);Canvas.SetTop(button,55);canvas.Children.Add(button);
   var fixture=new Window {Width=600,Height=200,Left=400,Top=300,WindowStyle=WindowStyle.None,ShowActivated=false,ShowInTaskbar=false,Content=canvas};
   fixture.Show();fixture.UpdateLayout();var handle=new WindowInteropHelper(fixture).Handle;var point=fixture.PointToScreen(new Point(250,55));
   Func<bool> occupied=delegate {
    var task=Task.Run(()=> (bool)typeof(NativeMethods).GetMethod("OccupiesNotch",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{handle,"Chrome_WidgetWin_1",(int)point.X,(int)point.Y,150,30}));
    var frame=new DispatcherFrame();var watch=System.Diagnostics.Stopwatch.StartNew();var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(10)};
    timer.Tick+=delegate {if(task.IsCompleted || watch.ElapsedMilliseconds>3000)frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);timer.Stop();Check(task.IsCompleted,"Accessibility check timed out");return task.Result;
   };
   try {
    Check(!occupied(),"Blank title area was treated as occupied");
    Canvas.SetLeft(button,270);fixture.UpdateLayout();Check(occupied(),"Button under notch was missed");
    canvas.Children.Clear();var text=new TextBlock {Text="Title under notch",FontSize=14};Canvas.SetLeft(text,260);Canvas.SetTop(text,55);canvas.Children.Add(text);fixture.UpdateLayout();Check(occupied(),"Text under notch was missed");
   } finally {fixture.Close();}
  });
  Test("Closing outline cannot reopen from its expanded footprint",delegate {
   var w=Island();Call(w,"SetOpen",false);var now=DateTime.UtcNow;
   Call(w,"ProcessPointer",100.0,160.0,now);Call(w,"ProcessPointer",100.0,160.0,now.AddSeconds(1));
   Check(!(bool)Field(w,"open"),"Pointer outside compact region reopened notch");Call(w,"Cleanup");
  });
  Test("Tiny opens only after deliberate dwell and stays open at dock edge",delegate {
   var w=Island();Call(w,"SetOpen",false);Call(w,"SetTabOverlapForProbe",true);var now=DateTime.UtcNow;
   Call(w,"ProcessPointer",0.0,250.0,now);Call(w,"ProcessPointer",270.0,12.0,now.AddSeconds(1));
   Check(!(bool)Field(w,"open"),"Pass-through hover opened tiny immediately");
   Call(w,"ProcessPointer",270.0,12.0,now.AddMilliseconds(1150));Check((bool)Field(w,"open"),"Tiny failed to open after dwell");
   Call(w,"ProcessPointer",270.0,172.0,now.AddSeconds(2));Call(w,"ProcessPointer",270.0,172.0,now.AddMilliseconds(2200));
   for(int i=0;i<100;i++) Call(w,"ProcessPointer",80.0,190.0,now.AddMilliseconds(2300+i*16));
   Check(Convert.ToString(Field(w,"pose"))=="OptionsDock","Stationary dock-edge pointer caused shape oscillation");Call(w,"Cleanup");
  });
  Test("Leaving dock starts grace period without collapsing navigation under pointer",delegate {
   var w=Island();Call(w,"SetPose",NotchPose.OptionsDock);Call(w,"ProcessPointer",0.0,250.0,DateTime.UtcNow);
   Check(Convert.ToString(Field(w,"pose"))=="OptionsDock","Dock collapsed before close grace period");Call(w,"Cleanup");
  });
  Test("Inline Ask Tab preserves mode and keyboard traversal",delegate {
   var w=Island();Call(w,"ShowInlineAsk");var input=Controls<TextBox>(w).Single(t=>!t.IsReadOnly);
   var key=new KeyEventArgs(Keyboard.PrimaryDevice,new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("DeskPill keyboard fixture") {Width=1,Height=1,WindowStyle=0}),0,Key.Tab) {RoutedEvent=Keyboard.PreviewKeyDownEvent};
   try { input.RaiseEvent(key);Check(!key.Handled && !(bool)Field(w,"inlineTranslateMode"),"Tab was consumed as mode change"); }
   finally { ((System.Windows.Interop.HwndSource)key.InputSource).Dispose();Call(w,"Cleanup"); }
  });
  Test("Overlap ignores own window and hidden windows",delegate {
   var fixture=new Window {Width=600,Height=220,Left=-4000,Top=320,WindowStyle=WindowStyle.None,ShowActivated=false,ShowInTaskbar=false,Background=Brushes.Black};
   fixture.Show();fixture.UpdateLayout();var origin=fixture.PointToScreen(new Point(0,0));var handle=new WindowInteropHelper(fixture).Handle;
   Check(!NativeMethods.IsForegroundOverlappingTabs(handle,(int)origin.X+300,(int)origin.Y+50,180,30),"Own island was considered an overlap");
   fixture.Hide();Check(!NativeMethods.IsForegroundOverlappingTabs(IntPtr.Zero,(int)origin.X+300,(int)origin.Y+50,180,30),"Hidden window was considered an overlap");fixture.Close();
  });
  Test("Hover options preserve page width and expose just pages plus settings",delegate {
   var w=Island();double width=((System.Windows.Shapes.Path)Field(w,"shell")).Data.Bounds.Width;
   Call(w,"SetPose",NotchPose.OptionsDock);double dockWidth=((System.Windows.Shapes.Path)Field(w,"shell")).Data.Bounds.Width;
   Check(Math.Abs(width-dockWidth)<0.1,"Options moved the page edges sideways");
   Check(Controls<Button>((DependencyObject)Field(w,"dockTray")).Count()==6,"Expected five page icons and Settings");Call(w,"Cleanup");
  });
  Test("Hover options respond promptly to deliberate hover",delegate {
   var w=Island();var now=DateTime.UtcNow;
   Call(w,"ProcessPointer",270.0,162.0,now);Call(w,"ProcessPointer",270.0,162.0,now.AddMilliseconds(80));
   Check(Convert.ToString(Field(w,"pose"))=="OptionsDock","Options still waiting after a deliberate hover");Call(w,"Cleanup");
  });
  Test("Opening content fades progressively without a blank width gate",delegate {
   var w=Island();object motion=Field(w,"motion");Type type=motion.GetType();
   type.GetMethod("Snap").Invoke(motion,new object[]{172.0,30.0,false});
   type.GetMethod("MoveTo").Invoke(motion,new object[]{396.0,172.0,false,true,false,false,null});
   type.GetMethod("Stop").Invoke(motion,null);Call(motion,"StepPhysics",0.08);
   double opacity=(double)type.GetProperty("ExpandedReveal").GetValue(motion,null);
   Check(opacity>0.05 && opacity<0.9,"Expected a progressive fade at 80ms, got "+opacity);Call(w,"Cleanup");
  });
  Test("Regular compact notch stays small during playback and focus",delegate {
   var w=Island();var media=(MediaService)Field(w,"media");media.Current=new MediaSnapshot {Playing=true,Title="A very long track title",Source="Fixture"};
   Call(w,"SetOpen",false);var bounds=((System.Windows.Shapes.Path)Field(w,"shell")).Data.Bounds;
   Check(bounds.Width<=174 && bounds.Height==30,"Playback enlarged the regular notch to "+bounds.Width);
   var timer=(FocusClock)Field(w,"focus");timer.Start(DateTime.UtcNow);media.Current.Playing=false;Call(w,"UpdateValues");
   Check(((System.Windows.Shapes.Path)Field(w,"shell")).Data.Bounds.Width<=174,"Focus enlarged the regular notch");Call(w,"Cleanup");
  });
  Test("Escape dismisses a preview while another app has focus and ignores a held key",delegate {
   var w=Island();Call(w,"SetOpen",false);var item=new DesktopNotification {Title="Preview",CreatedAt=DateTimeOffset.UtcNow};
   Call(w,"ShowNotification",item);Call(w,"ProcessNotificationEscape",false);Call(w,"ProcessNotificationEscape",true);
   Check(Convert.ToString(Field(w,"pose"))!="NotificationPeek" && Field(w,"notificationCard")==null,"Escape left the preview behind");
   Call(w,"ShowNotification",item);w.GetType().GetField("notificationEscapeDown",Private).SetValue(w,true);Call(w,"ProcessNotificationEscape",true);
   Check(Convert.ToString(Field(w,"pose"))=="NotificationPeek","Held Escape dismissed a new preview");
   Call(w,"ProcessNotificationEscape",false);Call(w,"ProcessNotificationEscape",true);
   Check(Field(w,"currentNotification")==null,"Fresh Escape did not dismiss");Call(w,"Cleanup");
  });
  Test("Notification timeout releases the preview and hover pauses its countdown",delegate {
   var w=Island();Call(w,"SetOpen",false);Call(w,"ShowNotification",new DesktopNotification {Title="Preview",CreatedAt=DateTimeOffset.UtcNow});
   var timer=(DispatcherTimer)Field(w,"hudTimer");timer.Start();
   Call(w,"ProcessPointer",270.0,12.0,DateTime.UtcNow);
   Check(!timer.IsEnabled,"Hover did not pause expiry");
   Call(w,"OnHudTimeout");Check(Field(w,"notificationCard")==null && Field(w,"currentNotification")==null && Convert.ToString(Field(w,"pose"))!="NotificationPeek","Timeout retained preview state");Call(w,"Cleanup");
  });
  Test("Notification peek shows source title and body without opening a main page",delegate {
   var w=Island();Call(w,"SetOpen",false);
   Call(w,"ShowNotification",new DesktopNotification {AppName="Fixture app",Title="New message",Body="Message preview body",CreatedAt=DateTimeOffset.UtcNow});
   Check(Convert.ToString(Field(w,"pose"))=="NotificationPeek" && !(bool)Field(w,"open"),"Notification did not enter the dedicated peek");
   var texts=Controls<TextBlock>((DependencyObject)Field(w,"notificationCard")).Select(t=>t.Text).ToArray();
   Call(w,"SetTabOverlapForProbe",true); Check(Convert.ToString(Field(w,"pose"))=="NotificationPeek","Overlap polling dismissed a live notification");
   Check(texts.Contains("Fixture app") && texts.Contains("New message") && texts.Contains("Message preview body"),"Notification content was missing");Call(w,"Cleanup");
  });
  Test("Notification hover keeps its preview instead of opening Music",delegate {
   var w=Island();Call(w,"SetOpen",false);Call(w,"ShowNotification",new DesktopNotification {AppName="Fixture",Title="Preview",CreatedAt=DateTimeOffset.UtcNow});
   var now=DateTime.UtcNow;Call(w,"ProcessPointer",270.0,30.0,now);Call(w,"ProcessPointer",270.0,30.0,now.AddSeconds(2));
   Check(Convert.ToString(Field(w,"pose"))=="NotificationPeek" && !(bool)Field(w,"open"),"Hover replaced notification with main page");Call(w,"Cleanup");
  });
  Test("Dismissing a notification restores the small regular notch",delegate {
   var w=Island();Call(w,"SetOpen",false);Call(w,"ShowNotification",new DesktopNotification {Title="Preview",CreatedAt=DateTimeOffset.UtcNow});
   Call(w,"DismissNotification");var bounds=((System.Windows.Shapes.Path)Field(w,"shell")).Data.Bounds;
   Check(Convert.ToString(Field(w,"pose"))=="Compact" && bounds.Width==150 && bounds.Height==30,"Dismiss did not restore 150x30 compact notch");Call(w,"Cleanup");
  });
  Test("Notifications do not interrupt an open inline editor",delegate {
   var w=Island();Call(w,"ShowInlineAsk");Call(w,"ShowNotification",new DesktopNotification {Title="Preview",CreatedAt=DateTimeOffset.UtcNow});
   Check(Convert.ToString(Field(w,"pose"))=="InlineAsk" && (bool)Field(w,"open"),"Notification interrupted Ask");Call(w,"Cleanup");
  });
  app.Shutdown();Console.WriteLine("Interaction regressions: "+passed+" passed, "+failed+" failed");return failed==0?0:1;
 }
}
