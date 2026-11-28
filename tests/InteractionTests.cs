using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;
using System.Windows.Threading;
using DeskPill;
class InteractionTests {
 [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="GetWindowLongW")]static extern int WindowLong(IntPtr window,int index);
 static int GameWindowStyle(IntPtr window){return WindowLong(window,-20);}
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
 static int InkPixels(FrameworkElement element) {
  element.Measure(new Size(100,42));element.Arrange(new Rect(0,0,100,42));element.UpdateLayout();
  var image=new RenderTargetBitmap(100,42,96,96,PixelFormats.Pbgra32);image.Render(element);var pixels=new byte[100*42*4];image.CopyPixels(pixels,400,0);
  int count=0;for(int i=3;i<pixels.Length;i+=4)if(pixels[i]>30)count++;return count;
 }
 static void PumpUntil(Task task) {
  var frame=new DispatcherFrame();var limit=new DispatcherTimer{Interval=TimeSpan.FromSeconds(4)};
  limit.Tick+=delegate{frame.Continue=false;};limit.Start();
  task.ContinueWith(t=>Application.Current.Dispatcher.BeginInvoke(new Action(delegate{frame.Continue=false;})));
  Dispatcher.PushFrame(frame);limit.Stop();Check(task.IsCompleted,"Native UI Automation did not finish within its fixture deadline");task.GetAwaiter().GetResult();
 }
 [STAThread] static int Main() {
  var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};Ui.Init(app);System.Threading.SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
  Test("Explicit dictation opt-in persists and can be disabled without starting the microphone",delegate {
   var w=Island();try{var state=(AppState)Field(w,"state");Check(!state.Settings.DictationEnabled,"Dictation started enabled");var method=w.GetType().GetMethod("SetDictationEnabled",Private);Check(method!=null,"No explicit tray enable path exists");if(SpeechAssets.Ready){method.Invoke(w,new object[]{true});Check(state.Settings.DictationEnabled&&Field(w,"dictation")==null,"Enabling starts microphone or fails to update preference");method.Invoke(w,new object[]{false});Check(!state.Settings.DictationEnabled,"Disabling failed");}}finally{Call(w,"Cleanup");}
  });
  Test("Regular users never initialize agent services from housekeeping",delegate {
   var w=Island();try{Call(w,"TrackAgentContext");Call(w,"ObserveAgentTransitions");Check(Field(w,"agentHub")==null&&Field(w,"agentScreenTarget")==null&&Field(w,"agentSpeech")==null,"Agent housekeeping activated without a connection");}finally{Call(w,"Cleanup");}
  });
  Test("Playback feedback is immediate and rolls back a refused command without rebuilding Music",delegate {
   var w=Island();try{var media=(MediaService)Field(w,"media");media.Current=new MediaSnapshot {Source="fixture",Title="Sample",Playing=true};Call(w,"RebuildCurrentPage",true);
    var button=(Button)Field(w,"musicPlayButton");var page=Field(w,"currentPageVisual");var result=new TaskCompletionSource<bool?>();
    var method=w.GetType().GetMethod("RequestMusicPlayback",Private);var task=(Task)method.Invoke(w,new object[]{new Func<bool,string,Task<bool?>>((desired,source)=>{Check(!desired&&source=="fixture","Wrong playback intent");return result.Task;})});
    Check(((TextBlock)button.Content).Text=="\uE768"&&!task.IsCompleted,"Pause click waited for the media provider");
    Call(w,"UpdateValues");Check(((TextBlock)button.Content).Text=="\uE768","Polling replaced pending playback feedback");
    result.SetResult(true);var frame=new DispatcherFrame();var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(10)};int ticks=0;timer.Tick+=delegate{if(task.IsCompleted||++ticks>100)frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);timer.Stop();
    Check(task.IsCompleted&&((TextBlock)button.Content).Text=="\uE769","Refused playback did not restore the real state");
    Check(object.ReferenceEquals(page,Field(w,"currentPageVisual"))&&object.ReferenceEquals(button,Field(w,"musicPlayButton")),"Play/pause rebuilt the page");
   }finally{Call(w,"Cleanup");}
  });
  Test("Startup tray preference synchronizes an open draft without writing registration",delegate {
   var state=new AppState();var w=new SettingsWindow(state,delegate{},delegate{});try{Call(w,"SyncStartupPreference",true);Check(((Settings)Field(w,"draft")).StartWithWindows&&!(bool)Field(w,"startupTouched"),"Tray startup change was left stale in Settings");Check(!state.Settings.StartWithWindows,"Sync wrote the real preference without a tray action");}finally{w.Close();}
  });
  Test("General, pages and valid numbers preview live and Cancel restores saved presentation",delegate {
   var state=new AppState();state.Settings.FastAnimations=true;var w=new IslandWindow(state,new StateStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"live-settings-fixture")),true,true);
   try{Call(w,"SetOpen",false);Call(w,"ShowSettings");var settings=(SettingsWindow)Field(w,"settingsWindow");
    Call(settings,"OpenSection","General");Controls<CheckBox>(settings).Single(c=>Convert.ToString(c.Content)=="Show clock").IsChecked=false;
    Check(((Run)Field(w,"timeRun")).Text==""&&state.Settings.ShowClock,"Clock edit is blind or changed saved settings");
    Controls<ComboBox>(settings).Single(c=>c.Items.Contains("Battery")&&c.Items.Contains("Water")).SelectedItem="None";
    Check(((Ring)Field(w,"compactRing")).Visibility!=Visibility.Visible&&state.Settings.StatusRing=="Water","Ring choice is blind or stored early");
    Call(settings,"OpenSection","Pages");Controls<CheckBox>(settings).Single(c=>Convert.ToString(c.Content)=="Water").IsChecked=false;
    Check(!((string[])w.GetType().GetMethod("Order",Private).Invoke(w,null)).Contains("Water")&&state.Settings.VisiblePages().Contains("Water"),"Hidden page did not preview independently");
    Check(!((Dictionary<string,Button>)Field(w,"dockPageButtons")).ContainsKey("Water"),"Hidden page stayed in the hover dock");
    Call(settings,"OpenSection","Focus & water");Controls<TextBox>(settings).Single(t=>System.Windows.Automation.AutomationProperties.GetName(t)=="Daily water goal (ml)").Text="3000";
    Check(((Settings)Field(w,"preferencesPreview")).WaterGoalMl==3000&&state.Settings.WaterGoalMl==2000,"Numeric goal did not preview");
    Controls<Button>(settings).Single(b=>Convert.ToString(b.Content)=="Cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Check(Field(w,"preferencesPreview")==null&&((Run)Field(w,"timeRun")).Text!=""&&((Ring)Field(w,"compactRing")).Visibility==Visibility.Visible,"Cancel did not restore runtime presentation");
    Check(((Dictionary<string,Button>)Field(w,"dockPageButtons")).ContainsKey("Water"),"Cancel did not restore the hover dock");
   }finally{Call(w,"Cleanup");}
  });
  Test("Hydration actions cannot persist an unsaved preview amount",delegate {
   var w=Island();try{var state=(AppState)Field(w,"state");Call(w,"SelectPage","Water");var preview=new Settings {BottleMl=1000};Call(w,"PreviewPreferences",preview,false);
    var drink=Controls<Button>(w).Single(b=>Convert.ToString(b.Content)=="+ Drink");Check(!drink.IsEnabled,"Uncommitted drink amount can be logged");
    int before=state.WaterMl;drink.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(state.WaterMl==before,"Preview amount changed persistent hydration data");
    Call(w,"PreviewPreferences",state.Settings,false);Check(Controls<Button>(w).Single(b=>Convert.ToString(b.Content)=="+ Drink").IsEnabled,"Cancel left hydration controls disabled");
   }finally{Call(w,"Cleanup");}
  });
  Test("Size and shape preview stays staged, resets, and adapts the notch",delegate {
   var state=new AppState();int previews=0;var w=new SettingsWindow(state,delegate{},delegate{},null,null,delegate(Settings value,bool demo){previews++;});
   try{Call(w,"OpenSection","Size & shape");var size=Controls<Slider>(w).Single(x=>System.Windows.Automation.AutomationProperties.GetName(x)=="Overall notch size");
    size.Value=135;Call(w,"OpenSection","General");
    var combo=Controls<ComboBox>(w).Single(c=>c.Items.Contains("135%"));Check(Convert.ToString(combo.SelectedItem)=="135%","Size slider did not sync General picker");combo.SelectedItem="130%";Check(size.Value==130,"General picker did not sync size slider");
    Call(w,"OpenSection","Size & shape");size.Value=135;Controls<Slider>(w).Single(x=>Convert.ToString(x.Tag)=="CompactWidth").Value=220;
    Controls<RadioButton>(w).Single(x=>Convert.ToString(x.Tag)=="Rounded").IsChecked=true;
    Check(((Settings)Field(w,"draft")).Scale==1.35&&((Settings)Field(w,"draft")).CompactWidth==220&&previews>=3,"Geometry controls did not preview");
    Check(state.Settings.Scale==1&&state.Settings.CompactWidth==150&&state.Settings.NotchShape=="Original","Preview changed saved geometry");
    Controls<Button>(w).Single(x=>Convert.ToString(x.Content)=="Reset size & shape").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Check(size.Value==100&&((Settings)Field(w,"draft")).CompactWidth==150&&((Settings)Field(w,"draft")).NotchShape=="Original","Reset left customized geometry");
   }finally{w.Close();}
   var island=Island();try{var settings=new Settings {CompactWidth=240,NotchShape="Soft square",Scale=1.6};Call(island,"PreviewPreferences",settings,false);
    var shape=(System.Windows.Shapes.Path)Field(island,"shell");Check(shape.Data.Bounds.Width>=240,"Wide preview did not reach shell");
    Check(((Grid)Field(island,"compact")).Width==222,"Wide preview did not center compact content");
    Call(island,"PreviewPreferences",new Settings(),false);Check(((Grid)Field(island,"compact")).Width==132,"Cancel did not restore compact layout");
   }finally{Call(island,"Cleanup");}
  });
  Test("Settings search finds options and opens their page without losing drafts",delegate {
   var state=new AppState();var w=new SettingsWindow(state,delegate{},delegate{});
   try{var search=(TextBox)Field(w,"settingsSearch");Call(w,"OpenSection","General");
    var box=Controls<CheckBox>(w).First(b=>Convert.ToString(b.Content).Contains("clock"));bool original=box.IsChecked==true;box.IsChecked=!original;
    search.Text="GPU";Check(Controls<TextBlock>((StackPanel)Field(w,"content")).Any(t=>t.Text=="Search results"),"Search results not shown");
    var results=(List<Button>)Field(w,"settingsSearchResults");Check(results.Any(b=>Convert.ToString(b.ToolTip)=="Open Dictation"),"GPU search missed dictation");
    results.First(b=>Convert.ToString(b.ToolTip)=="Open Dictation").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Check(search.Text==""&&Convert.ToString(((Button)Field(w,"selectedSettingsPage")).Content)=="Dictation","Result did not navigate");
    Call(w,"OpenSection","General");Check(box.IsChecked==!original&&state.Settings.ShowClock==original,"Searching changed or discarded a staged option");
    search.Text="transparency";Check(((List<Button>)Field(w,"settingsSearchResults")).Any(b=>Convert.ToString(b.ToolTip)=="Open Appearance"),"Transparency alias missed opacity");
    search.Text="Surface opacity";Check(Controls<TextBlock>((StackPanel)Field(w,"content")).Any(t=>t.Text.Contains("Choose the Glass theme")),"Hidden opacity lacks prerequisite guidance");
    ((List<Button>)Field(w,"settingsSearchResults")).First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(((Settings)Field(w,"draft")).Theme=="Classic","Search changed the selected theme");
    search.Text="Parakeet processing device";Check(Controls<TextBlock>((StackPanel)Field(w,"content")).Any(t=>t.Text.Contains("Choose Parakeet Unified English")),"Hidden device option lacks prerequisite guidance");
    ((List<Button>)Field(w,"settingsSearchResults")).First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(((Settings)Field(w,"draft")).SpeechModel=="Vosk","Search changed the speech model");
    Call(w,"OpenSection","General");
    search.Text="startup";Check(((List<Button>)Field(w,"settingsSearchResults")).Any(b=>Convert.ToString(b.ToolTip)=="Open General"),"Startup alias missed start with Windows");
    search.Text="no_such_option_4829";Check(Controls<TextBlock>((StackPanel)Field(w,"content")).Any(t=>t.Text.Contains("No matching settings")),"Empty search lacks recovery guidance");
    Call(w,"ClearSettingsSearch",true);Check(Convert.ToString(((Button)Field(w,"selectedSettingsPage")).Content)=="General","Clear did not restore page");
   }finally{w.Close();}
  });
  Test("Preview and capture do not present dictation onboarding",delegate {
   var w=Island();try{Call(w,"MaybeShowDictationWelcome");Check(Field(w,"dictationWelcome")==null,"Preview opened first-run setup");Check(!((AppState)Field(w,"state")).DictationWelcomeCompleted,"Preview consumed first-run choice");}finally{Call(w,"Cleanup");}
  });
  Test("Dictation welcome offers an explicit setup or skip without enabling speech",delegate {
   var type=typeof(SettingsWindow).Assembly.GetType("DeskPill.DictationWelcomeWindow");Check(type!=null,"Welcome screen is missing");
   bool? selected=null;var w=(Window)Activator.CreateInstance(type,new object[]{new Func<bool,bool>(setup=>{selected=setup;return true;})});
   try{Check(Controls<TextBlock>(w).Any(t=>t.Text.Contains("Ctrl")&&t.Text.Contains("Space")),"Shortcut is not explained");
    var buttons=Controls<Button>(w).ToArray();Check(buttons.Any(b=>Convert.ToString(b.Content)=="Continue without dictation"),"Skip action missing");
    buttons.Single(b=>Convert.ToString(b.Content)=="Set up dictation").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(selected==true,"Setup action did not route to model selection");
   }finally{w.Close();}
   selected=null;w=(Window)Activator.CreateInstance(type,new object[]{new Func<bool,bool>(setup=>{selected=setup;return true;})});
   Controls<Button>(w).Single(b=>Convert.ToString(b.Content)=="Continue without dictation").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(selected==false,"Skip did not persist the choice");
  });
  Test("Welcome remains recoverable if saving the choice fails",delegate {
   var type=typeof(SettingsWindow).Assembly.GetType("DeskPill.DictationWelcomeWindow");Check(type!=null,"Welcome screen missing");bool allow=false;int attempts=0;
   var w=(Window)Activator.CreateInstance(type,new object[]{new Func<bool,bool>(setup=>{attempts++;return allow;})});
   try{Controls<Button>(w).Single(b=>Convert.ToString(b.Content)=="Set up dictation").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(attempts==1&&Controls<TextBlock>(w).Any(t=>t.Text.Contains("try again")),"Failed save was silently accepted");allow=true;}finally{w.Close();}
  });
  Test("Agents page clearly labels experimental status and screen sharing",delegate {
   var w=Island();try{Call(w,"SelectPage","Agents");Check(Controls<TextBlock>(w).Any(t=>t.Text.Contains("Experimental")),"Empty Agents page lacks experimental label");var state=(AppState)Field(w,"state");var p=new AgentProfile();state.Agents.Add(p);Call(w,"RebuildCurrentPage",true);Check(((TextBlock)Field(w,"agentPageStatus")).Text.Contains("Experimental"),"Connected Agents lacks experimental label");Call(w,"ShowAgentConversation",p.Id);Check(((TextBox)Field(w,"agentResult")).Text.Contains("camera"),"Screen questions lack explicit attachment guidance");}finally{Call(w,"Cleanup");}
  });
  Test("Connected Agents offers direct prompt and voice entry",delegate {
   var w=Island();try{var state=(AppState)Field(w,"state");state.Agents.Add(new AgentProfile{Name="My project"});Call(w,"SelectPage","Agents");Check(Controls<Button>(w).Any(b=>Convert.ToString(b.Content)=="Ask your agent…")&&Controls<Button>(w).Any(b=>Convert.ToString(b.ToolTip)=="Dictate a prompt"),"Agent home still requires navigating to a separate open-conversation action");}finally{Call(w,"Cleanup");}
  });
  Test("Screen attachments stay with their draft and cleanup removes private files",delegate {
   var w=Island();string file=null;try{var state=(AppState)Field(w,"state");var a=new AgentProfile{Name="A"};var b=new AgentProfile{Name="B"};state.Agents.Add(a);state.Agents.Add(b);Call(w,"SelectPage","Agents");Call(w,"ShowAgentConversation",a.Id);
    using(var bitmap=new System.Drawing.Bitmap(40,30)){using(var g=System.Drawing.Graphics.FromImage(bitmap))g.Clear(System.Drawing.Color.CornflowerBlue);var image=(AgentAttachment)typeof(AgentVision).GetMethod("Save",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bitmap,"Synthetic fixture"});file=image.FilePath;((Dictionary<string,AgentAttachment>)Field(w,"agentAttachments"))[a.Id]=image;Call(w,"RefreshAgentViews");Check(((Border)Field(w,"agentAttachmentChip")).Visibility==Visibility.Visible,"Attached review chip missing");}
    Call(w,"ShowAgentConversation",b.Id);Check(((Border)Field(w,"agentAttachmentChip")).Visibility==Visibility.Collapsed,"Attachment crossed agent destinations");Call(w,"ShowAgentConversation",a.Id);Check(((Border)Field(w,"agentAttachmentChip")).Visibility==Visibility.Visible&&File.Exists(file),"Switching discarded draft attachment");Call(w,"ClearAgentAttachment",a.Id);Check(!File.Exists(file),"Removing image left private capture behind");
   }finally{Call(w,"Cleanup");}
  });
  Test("Screen capture errors surface inline on the agent status line without closing",delegate {
   var w=Island();try{var state=(AppState)Field(w,"state");var p=new AgentProfile{Name="Screen check"};state.Agents.Add(p);Call(w,"ShowAgentConversation",p.Id);
    Call(w,"CaptureAgentContext","window");var pump=new DispatcherFrame();var wait=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};int ticks=0;
    wait.Tick+=delegate{if(!(bool)Field(w,"agentCapturing")||++ticks>50)pump.Continue=false;};wait.Start();Dispatcher.PushFrame(pump);wait.Stop();
    Check(!(bool)Field(w,"agentCapturing")&&Convert.ToString(Field(w,"pose"))=="InlineAgents"&&((TextBlock)Field(w,"agentDetailStatus")).Text.StartsWith("Screen:"),"Capture failure was hidden instead of updating inline status");
    Check(!((DispatcherTimer)Field(w,"closeTimer")).IsEnabled&&w.Opacity>=0.99,"Capture error started close timer or left notch hidden");
   }finally{Call(w,"Cleanup");}
  });
  Test("Running work may collapse but capture protects interaction and newer drafts",delegate {
   var w=Island();try{var state=(AppState)Field(w,"state");var p=new AgentProfile();state.Agents.Add(p);Call(w,"ShowAgentConversation",p.Id);((TextBox)Field(w,"agentPrompt")).Text="Keep draft";var session=((AgentHub)Field(w,"agentHub")).Get(p.Id);session.GetType().GetField("running",Private).SetValue(session,true);Check((bool)w.GetType().GetMethod("CanAutoClose",Private).Invoke(w,null),"Running work forces notch to stay open");w.GetType().GetField("agentCapturing",Private).SetValue(w,true);Check(!(bool)w.GetType().GetMethod("CanAutoClose",Private).Invoke(w,null),"Capture can collapse during selection");w.GetType().GetField("agentCapturing",Private).SetValue(w,false);Call(w,"SetOpen",false);Check(session.Draft=="Keep draft","Auto-collapse lost draft");}finally{Call(w,"Cleanup");}
  });
  Test("Attention during a run produces one unread terminal notice without opening",delegate {
   var w=Island();try{var state=(AppState)Field(w,"state");var p=new AgentProfile();state.Agents.Add(p);Call(w,"SelectPage","Agents");Call(w,"SetOpen",false);var session=((AgentHub)Field(w,"agentHub")).Get(p.Id);var t=session.GetType();t.GetField("running",Private).SetValue(session,true);t.GetField("status",Private).SetValue(session,"Needs attention");Call(w,"ObserveAgentTransitions");t.GetField("running",Private).SetValue(session,false);Call(w,"ObserveAgentTransitions");Check(((HashSet<string>)Field(w,"agentUnread")).Contains(p.Id)&&!(bool)Field(w,"open"),"Terminal failure was lost or opened notch");Call(w,"ShowAgentConversation",p.Id);Check(!((HashSet<string>)Field(w,"agentUnread")).Contains(p.Id),"Reading conversation left unread notice");}finally{Call(w,"Cleanup");}
  });
  Test("Conversation streaming preserves selected text and failed-turn history",delegate {
   var w=Island();try{var state=(AppState)Field(w,"state");var p=new AgentProfile();state.Agents.Add(p);Call(w,"ShowAgentConversation",p.Id);var session=((AgentHub)Field(w,"agentHub")).Get(p.Id);var t=session.GetType();t.GetField("transcript",Private).SetValue(session,"Earlier answer and next turn");Call(w,"RefreshAgentViews");var text=(TextBox)Field(w,"agentResult");text.Select(0,7);t.GetField("transcript",Private).SetValue(session,"Earlier answer and next turn appended");Call(w,"RefreshAgentViews");Check(text.SelectedText=="Earlier","Streaming cleared selection");t.GetField("status",Private).SetValue(session,"Needs attention");t.GetField("summary",Private).SetValue(session,"Try sign-in");Call(w,"RefreshAgentViews");Check(text.Text.Contains("Earlier answer")&&text.Text.Contains("Try sign-in"),"Failure hid retained conversation");}finally{Call(w,"Cleanup");}
  });
  Test("Connect opens a visible owned editor without blocking the notch and Cancel restores interaction",delegate {
   var w=Island();w.GetType().GetProperty("RuntimeProbeMode",Private).SetValue(w,true,null);w.Show();Call(w,"SelectPage","Agents");
   Exception failure=null;bool inspected=false;var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};
   timer.Tick+=delegate{timer.Stop();var editor=(Window)Field(w,"agentEditor");try{Check(editor!=null&&editor.IsVisible,"Connect editor was not shown");Check(editor.Owner==w&&editor.Topmost,"Editor can fall behind the topmost notch or another app");Check(w.IsEnabled,"Connect disabled the notch in a modal loop");inspected=true;}catch(Exception e){failure=e;}finally{if(editor!=null)editor.Close();}};timer.Start();
   try{Controls<Button>(w).Single(b=>Convert.ToString(b.Content)=="Connect agent").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    if(!inspected&&failure==null){var frame=new DispatcherFrame();w.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(delegate{frame.Continue=false;}));Dispatcher.PushFrame(frame);}
    timer.Stop();var editor=(Window)Field(w,"agentEditor");if(editor!=null){Check(editor.Owner==w&&editor.Topmost&&w.IsEnabled,"Connection editor is unowned or modal");editor.Close();inspected=true;}
    if(failure!=null)throw failure;Check(inspected&&Field(w,"agentEditor")==null&&w.IsEnabled&&!(bool)Field(w,"pinned"),"Cancel did not return to usable notch");
   }finally{timer.Stop();var editor=(Window)Field(w,"agentEditor");if(editor!=null)editor.Close();Call(w,"Cleanup");w.Hide();}
  });
  Test("Connection editor saves once without a modal loop and repeated Connect reuses it",delegate {
   var w=Island();w.GetType().GetProperty("RuntimeProbeMode",Private).SetValue(w,true,null);w.Show();Call(w,"SelectPage","Agents");
   try{Call(w,"EditAgentConnection",new object[]{null});var editor=(Window)Field(w,"agentEditor");Check(editor!=null&&w.IsEnabled,"Connect blocked the app");Call(w,"EditAgentConnection",new object[]{null});Check(Field(w,"agentEditor")==editor,"Repeated Connect opened duplicate setup windows");
    ((TextBox)Field(editor,"name")).Text="Fixture connection";((TextBox)Field(editor,"executable")).Text=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"AgentFixture.exe");((TextBox)Field(editor,"workspace")).Text=AppDomain.CurrentDomain.BaseDirectory;
    Controls<Button>(editor).Single(b=>Convert.ToString(b.Content)=="Connect agent").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var state=(AppState)Field(w,"state");
    Check(Field(w,"agentEditor")==null&&state.Agents.Count==1&&state.SelectedAgent==state.Agents[0].Id&&w.IsEnabled,"Save failed to close and add one connection");
    Call(w,"EditAgentConnection",state.Agents[0]);var edit=(Window)Field(w,"agentEditor");((TextBox)Field(edit,"name")).Text="Discard this name";Controls<Button>(edit).Single(b=>Convert.ToString(b.Content)=="Cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Check(Field(w,"agentEditor")==null&&state.Agents.Count==1&&state.Agents[0].Name=="Fixture connection"&&w.IsEnabled,"Cancel changed connection or left the app disabled");
   }finally{var editor=(Window)Field(w,"agentEditor");if(editor!=null)editor.Close();Call(w,"Cleanup");w.Hide();}
  });
  Test("Modeless setup cannot change a newly running connection or resurrect a removed one",delegate {
   var w=Island();w.GetType().GetProperty("RuntimeProbeMode",Private).SetValue(w,true,null);w.Show();var state=(AppState)Field(w,"state");var a=new AgentProfile{Name="Original connection",Executable=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"AgentFixture.exe"),Workspace=AppDomain.CurrentDomain.BaseDirectory};state.Agents.Add(a);Call(w,"SelectPage","Agents");
   try{Call(w,"EditAgentConnection",a);var editor=(Window)Field(w,"agentEditor");((TextBox)Field(editor,"name")).Text="Pending changes";var hub=(AgentHub)Field(w,"agentHub");hub.SendAsync(a,"hold");
    Controls<Button>(editor).Single(b=>Convert.ToString(b.Content)=="Save connection").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(Field(w,"agentEditor")==editor&&state.Agents[0].Name=="Original connection"&&((TextBlock)Field(editor,"error")).Text.Contains("Stop"),"Setup changed a connection while its run was active");
    PumpUntil(hub.StopAsync(a.Id));state.Agents.Remove(a);Controls<Button>(editor).Single(b=>Convert.ToString(b.Content)=="Save connection").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(state.Agents.Count==0&&Field(w,"agentEditor")==editor&&((TextBlock)Field(editor,"error")).Text.Contains("removed"),"Setup resurrected a removed connection");
   }finally{var editor=(Window)Field(w,"agentEditor");if(editor!=null)editor.Close();Call(w,"Cleanup");w.Hide();}
  });
  Test("Agents is separate and creates no runtime while using Music",delegate {
   var w=Island();Check(Field(w,"agentHub")==null && Convert.ToString(Field(w,"page"))=="Music","Music started agent work");
   Call(w,"SelectPage","Agents");Check(Controls<Button>(w).Any(b=>Convert.ToString(b.Content)=="Connect agent"),"Agents onboarding has no connect action");
   Check(Field(w,"agentHub")==null,"Empty Agents page started a runtime");Call(w,"HideAgentsPage");
   var state=(AppState)Field(w,"state");Check(!state.Settings.VisiblePages().Contains("Agents"),"Hide left Agents in navigation");
   state.Settings.HiddenPages=new string[0];Call(w,"SelectPage","Agents");Check(Convert.ToString(Field(w,"page"))=="Agents","Agents could not be reenabled");Call(w,"Cleanup");
  });
  Test("Agent target changes preserve drafts and idle conversations can collapse",delegate {
   var w=Island();var state=(AppState)Field(w,"state");var a=new AgentProfile{Name="First project"};var b=new AgentProfile{Name="Second project"};state.Agents.Add(a);state.Agents.Add(b);
   Call(w,"SelectPage","Agents");Call(w,"ShowAgentConversation",a.Id);var first=(TextBox)Field(w,"agentPrompt");first.Text="Keep this draft";
   Check((bool)typeof(IslandWindow).GetMethod("CanAutoClose",Private).Invoke(w,null),"Idle conversation cannot collapse");
   ((ComboBox)Field(w,"agentPicker")).SelectedItem=b;((TextBox)Field(w,"agentPrompt")).Text="Second draft";Call(w,"ShowAgentConversation",a.Id);
   Check(((TextBox)Field(w,"agentPrompt")).Text=="Keep this draft","Switching target lost draft");first.Text="Old detached control";
   Check(((AgentHub)Field(w,"agentHub")).Get(b.Id).Draft=="Second draft","Detached input overwrote another target");
   Call(w,"SetOpen",false);Call(w,"ShowAgentConversation",b.Id);Check(((TextBox)Field(w,"agentPrompt")).Text=="Second draft","Closing lost draft");Call(w,"Cleanup");
  });
  Test("Invalid agent launch keeps draft and displays an actionable error without starting",delegate {
   var w=Island();var state=(AppState)Field(w,"state");var a=new AgentProfile{Name="Unavailable tool",Executable="C:\\missing.exe",Workspace="C:\\missing"};state.Agents.Add(a);
   Call(w,"ShowAgentConversation",a.Id);var input=(TextBox)Field(w,"agentPrompt");input.Text="Do not lose me";Call(w,"SendAgentPrompt");
   Check(input.Text=="Do not lose me" && !((AgentHub)Field(w,"agentHub")).Get(a.Id).Running,"Invalid launch cleared draft or ran a process");
   Check(((TextBlock)Field(w,"agentDetailStatus")).Text.Contains("existing"),"Invalid path has no useful error");Call(w,"Cleanup");
  });
  Test("Agent Send shows a real fixture response without moving the shell or replacing a newer draft",delegate {
   var w=Island();try{
    var state=(AppState)Field(w,"state");var a=new AgentProfile{Name="Fixture project",Executable=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"AgentFixture.exe"),Workspace=AppDomain.CurrentDomain.BaseDirectory};state.Agents.Add(a);
    Call(w,"SelectPage","Agents");Call(w,"ShowAgentConversation",a.Id);var input=(TextBox)Field(w,"agentPrompt");input.Text="hello fixture";Call(w,"SnapPoseImmediate");
    var bounds=((System.Windows.Shapes.Path)Field(w,"shell")).Data.Bounds;Controls<Button>(w).Single(b=>Convert.ToString(b.Content)=="Send").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));input.Text="My next question";
    var hub=(AgentHub)Field(w,"agentHub");var frame=new DispatcherFrame();var poll=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};int ticks=0;
    poll.Tick+=delegate{if(!hub.Get(a.Id).Running||++ticks>200)frame.Continue=false;};poll.Start();Dispatcher.PushFrame(frame);poll.Stop();Call(w,"RefreshAgentViews");
    Check(!hub.Get(a.Id).Running&&hub.Get(a.Id).Status=="Done"&&((TextBox)Field(w,"agentResult")).Text.Contains("fixture answer"),"Send did not surface fixture response");
    Check(input.Text=="My next question","Completion cleared a newer unsent draft");Call(w,"SnapPoseImmediate");Check(((System.Windows.Shapes.Path)Field(w,"shell")).Data.Bounds==bounds,"Streamed response resized the shell");
    input.Text="hold";Call(w,"SendAgentPrompt");Check(hub.Get(a.Id).Running,"Owned run failed to start");Call(w,"RefreshAgentViews");Check(((FrameworkElement)Field(w,"agentIndicator")).Visibility==Visibility.Visible,"Working agent has no ambient indicator");Call(w,"HideAgentsPage");Call(w,"UpdateAgentIndicator");Check(((FrameworkElement)Field(w,"agentIndicator")).Visibility==Visibility.Collapsed,"Hidden Agents left ambient UI behind");Check(hub.Get(a.Id).Running && state.Agents.Count==1,"Hiding killed work or deleted connection");PumpUntil(hub.StopAsync(a.Id));Check(hub.Get(a.Id).Status=="Stopped","Stop did not reach stopped state");
   }finally{Call(w,"Cleanup");}
  });
  Test("Connector authentication diagnostics stay outside replies and remain inspectable",delegate {
   var w=Island();try{var state=(AppState)Field(w,"state");var a=new AgentProfile{Name="Diagnostics fixture",Executable=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"AgentFixture.exe"),Workspace=AppDomain.CurrentDomain.BaseDirectory};state.Agents.Add(a);Call(w,"SelectPage","Agents");Call(w,"ShowAgentConversation",a.Id);((TextBox)Field(w,"agentPrompt")).Text="mcp-warning";Call(w,"SendAgentPrompt");var hub=(AgentHub)Field(w,"agentHub");
    var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};int ticks=0;timer.Tick+=delegate{if(!hub.Get(a.Id).Running||++ticks>200)frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);timer.Stop();Call(w,"RefreshAgentViews");
    var response=(TextBox)Field(w,"agentResult");Check(response.Text.Contains("fixture answer")&&!response.Text.Contains("AuthRequired"),"Diagnostic logs leaked into the agent response");var details=(Button)Field(w,"agentDetails");Check(details.Visibility==Visibility.Visible,"Diagnostics disappeared instead of remaining accessible");
    details.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(response.Text.Contains("AuthRequired")&&response.Text.Contains("Tool diagnostics"),"Details did not show connector diagnostics");details.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(response.Text.Contains("fixture answer")&&!response.Text.Contains("AuthRequired"),"Response toggle did not restore the clean reply");
   }finally{Call(w,"Cleanup");}
  });
  Test("Completing a detached conversation cannot erase its newer stored draft",delegate {
   var w=Island();try{
    var state=(AppState)Field(w,"state");var a=new AgentProfile{Name="First",Executable=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"AgentFixture.exe"),Workspace=AppDomain.CurrentDomain.BaseDirectory};var b=new AgentProfile{Name="Second"};state.Agents.Add(a);state.Agents.Add(b);Call(w,"SelectPage","Agents");Call(w,"ShowAgentConversation",a.Id);
    ((TextBox)Field(w,"agentPrompt")).Text="slow";Call(w,"SendAgentPrompt");Call(w,"ExitInlineView");Call(w,"ShowAgentConversation",a.Id);((TextBox)Field(w,"agentPrompt")).Text="A newer draft after reopening";Call(w,"ShowAgentConversation",b.Id);
    var hub=(AgentHub)Field(w,"agentHub");var frame=new DispatcherFrame();var poll=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};int ticks=0;poll.Tick+=delegate{if(!hub.Get(a.Id).Running||++ticks>200)frame.Continue=false;};poll.Start();Dispatcher.PushFrame(frame);poll.Stop();
    Check(!hub.Get(a.Id).Running&&hub.Get(a.Id).Draft=="A newer draft after reopening","Completion erased a newer detached draft");Call(w,"ShowAgentConversation",a.Id);Check(((TextBox)Field(w,"agentPrompt")).Text=="A newer draft after reopening","Reopened draft disappeared");
   }finally{Call(w,"Cleanup");}
  });
  Test("Agent keyboard selector has a themed popup and keeps the chosen target",delegate {
   var w=Island();try{var state=(AppState)Field(w,"state");var a=new AgentProfile{Name="One"};var b=new AgentProfile{Name="Two"};state.Agents.Add(a);state.Agents.Add(b);Call(w,"SelectPage","Agents");Call(w,"ShowAgentConversation",a.Id);
    var picker=(ComboBox)Field(w,"agentPicker");picker.ApplyTemplate();Check(picker.Template.FindName("PART_Popup",picker)!=null && picker.ItemContainerStyle!=null,"Connection picker lacks accessible dropdown structure");
    picker.SelectedIndex=1;Check(Convert.ToString(Field(w,"agentDetailId"))==b.Id,"Selector did not switch prompt destination");
   }finally{Call(w,"Cleanup");}
  });
  Test("Escape cancels agent speech without dispatching and then returns to the page",delegate {
   var w=Island();var source=new HwndSource(new HwndSourceParameters("DeskPill agent key fixture"){Width=1,Height=1,WindowStyle=0});try{
    var state=(AppState)Field(w,"state");var a=new AgentProfile{Name="Voice destination"};state.Agents.Add(a);Call(w,"SelectPage","Agents");Call(w,"ShowAgentConversation",a.Id);((TextBox)Field(w,"agentPrompt")).Text="Keep my draft";
    var token=new System.Threading.CancellationTokenSource();w.GetType().GetField("agentSpeech",Private).SetValue(w,token);
    w.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,source,0,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent});
    Check(token.IsCancellationRequested&&Field(w,"agentSpeech")==null&&Convert.ToString(Field(w,"pose"))=="InlineAgents","Escape did not cancel speech in place");
    Check(((TextBox)Field(w,"agentPrompt")).Text=="Keep my draft"&&!((AgentHub)Field(w,"agentHub")).Get(a.Id).Running,"Speech cancellation changed draft or dispatched");
    w.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,source,0,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent});Check(Convert.ToString(Field(w,"pose"))=="Expanded","Second Escape did not return to page");
   }finally{source.Dispose();Call(w,"Cleanup");}
  });
  Test("Agent home destination stays synchronized after returning from another conversation",delegate {
   var w=Island();try{var state=(AppState)Field(w,"state");var a=new AgentProfile{Name="First"};var b=new AgentProfile{Name="Second"};state.Agents.Add(a);state.Agents.Add(b);state.SelectedAgent=a.Id;Call(w,"SelectPage","Agents");Call(w,"ShowAgentConversation",a.Id);((ComboBox)Field(w,"agentPicker")).SelectedItem=b;Call(w,"ExitInlineView");
    Check(((ComboBox)Field(w,"agentHomePicker")).SelectedItem==b,"Home displays a different destination than its action");
   }finally{Call(w,"Cleanup");}
  });
  Test("Changing provider clears old executable and requires explicit discovery",delegate {
   var original=new AgentProfile{Name="Existing",Executable="C:\\old-provider.exe",AllowEdits=true};var type=typeof(IslandWindow).Assembly.GetType("DeskPill.AgentConnectionWindow");var editor=(Window)Activator.CreateInstance(type,new object[]{original});
   try{var provider=(ComboBox)Field(editor,"provider");provider.SelectedItem="App shortcut";Check(((TextBox)Field(editor,"executable")).Text=="" && ((CheckBox)Field(editor,"edits")).IsChecked==false,"Provider switch retained old executable or writes");provider.SelectedItem="Claude Code";Check(((TextBox)Field(editor,"executable")).Text=="", "Provider selection performed implicit discovery");}finally{editor.Close();}
  });
  Test("Agent refresh coalesces worker events before entering the dispatcher",delegate {
   var w=Island();for(int i=0;i<10000;i++)Call(w,"QueueAgentRefresh");
   Check((int)Field(w,"agentRefreshQueued")==1,"Worker events were not coalesced before dispatcher posting");Call(w,"Cleanup");
  });
  Test("Music source navigation has a dedicated clickable track control",delegate {
   var w=Island();var service=(MediaService)Field(w,"media");service.Current=new MediaSnapshot {Source="chrome.exe",Title="An example song",Artist="An artist"};Call(w,"RebuildCurrentPage",true);
   Check(Controls<Button>(w).Any(b=>Convert.ToString(b.ToolTip)=="Open playing app or browser tab"),"Track source has no keyboard-accessible action");Call(w,"Cleanup");
  });
  Test("Page arrows do not retain a border after click navigation and keep keyboard feedback",delegate {
   var w=Island();w.GetType().GetProperty("RuntimeProbeMode",Private).SetValue(w,true,null);
   try{w.Show();w.Activate();foreach(string field in new[]{"nextBtn","prevBtn"}){var button=(Button)Field(w,field);string before=Convert.ToString(Field(w,"page"));button.Focus();button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));w.UpdateLayout();Check(Convert.ToString(Field(w,"page"))!=before,"Arrow did not navigate");Check(button.IsKeyboardFocused,"Arrow focus fixture failed");var shell=(Border)button.Template.FindName("shell",button);var brush=shell.BorderBrush as SolidColorBrush;Check(brush!=null&&brush.Color.A==0,"Click left a persistent arrow border after page change");Check(button.FocusVisualStyle!=null,"Arrow lost keyboard-only focus feedback");}}
   finally{Call(w,"Cleanup");w.Hide();}
  });
  Test("Focusing the music source never leaves a persistent template border",delegate {
   var w=Island();w.GetType().GetProperty("RuntimeProbeMode",Private).SetValue(w,true,null);
   try {
    ((MediaService)Field(w,"media")).Current=new MediaSnapshot{Title="Example song",Source="Chrome"};Call(w,"RebuildCurrentPage",true);w.Show();w.Activate();
    var button=Controls<Button>(w).Single(b=>Convert.ToString(b.ToolTip)=="Open playing app or browser tab");button.Focus();w.UpdateLayout();
    Check(button.IsKeyboardFocused,"Focus fixture did not reach the track control");
    var outline=(Border)button.Template.FindName("outline",button);
    Check(outline.BorderThickness==new Thickness(0),"Mouse/programmatic focus left a permanent music border");
    Check(button.FocusVisualStyle!=null,"Keyboard navigation lost its focus feedback");
   }finally{Call(w,"Cleanup");w.Hide();}
  });
  Test("Volume flyout uses a continuous slider and closes after idle",delegate {
   var type=typeof(IslandWindow).Assembly.GetType("DeskPill.VolumeFlyout");Check(type!=null,"Volume still uses an untimed context menu");
   int closed=0;var flyout=Activator.CreateInstance(type,new object[]{.5f,false,new Action<float>(delegate{}),new Action(delegate{}),new Action(()=>closed++)});
   var root=(FrameworkElement)Field(flyout,"root");var slider=Controls<Slider>(root).Single();
   Check(slider.IsMoveToPointEnabled && !slider.IsSnapToTickEnabled && slider.Template!=null,"Volume slider is not continuous and directly seekable");
   type.GetField("opened",Private).SetValue(flyout,true);type.GetField("deadline",Private).SetValue(flyout,DateTime.UtcNow.AddSeconds(-1));Call(flyout,"OnIdle");
   Check(closed==1,"Idle volume flyout did not dismiss");((IDisposable)flyout).Dispose();
  });
  Test("Volume dismissal pauses through dragging and external changes never write back",delegate {
   var type=typeof(IslandWindow).Assembly.GetType("DeskPill.VolumeFlyout");int closed=0,writes=0;
   var flyout=Activator.CreateInstance(type,new object[]{.5f,false,new Action<float>(v=>System.Threading.Interlocked.Increment(ref writes)),new Action(delegate{}),new Action(()=>closed++)});
   type.GetField("opened",Private).SetValue(flyout,true);type.GetField("deadline",Private).SetValue(flyout,DateTime.UtcNow.AddSeconds(-1));type.GetField("dragging",Private).SetValue(flyout,true);Call(flyout,"OnIdle");Check(closed==0,"Flyout closed under an active drag");
   var slider=Controls<Slider>((FrameworkElement)Field(flyout,"root")).Single();
   type.GetMethod("Update").Invoke(flyout,new object[]{.2f,false});Check(Math.Abs(slider.Value-50)<.01,"A system callback pulled the thumb away from the pointer");
   type.GetField("dragging",Private).SetValue(flyout,false);type.GetMethod("Update").Invoke(flyout,new object[]{.2f,false});Check(Math.Abs(slider.Value-20)<.01 && writes==0,"External volume did not sync cleanly or wrote a feedback loop");
   Call(flyout,"OnIdle");Check(closed==1,"Flyout stayed open after the drag ended");((IDisposable)flyout).Dispose();
  });
  Test("Closing the notch dismisses its active volume flyout",delegate {
   var w=Island();var type=typeof(IslandWindow).Assembly.GetType("DeskPill.VolumeFlyout");int closed=0;
   var flyout=Activator.CreateInstance(type,new object[]{.5f,false,new Action<float>(delegate{}),new Action(delegate{}),new Action(()=>closed++)});type.GetField("opened",Private).SetValue(flyout,true);w.GetType().GetField("volumeFlyout",Private).SetValue(w,flyout);
   Check(!(bool)typeof(IslandWindow).GetMethod("CanAutoClose",Private).Invoke(w,null),"Notch can close during volume interaction");
   Call(w,"SetOpen",false);Check(closed==1 && Field(w,"volumeFlyout")==null,"Closing left an orphaned volume popup");Call(w,"Cleanup");
  });
  Test("Media tab lookup and selection work through native UI Automation on an isolated fixture",delegate {
   var tabs=new TabControl();tabs.Items.Add(new TabItem{Header="Unrelated tab",Content="Other content"});tabs.Items.Add(new TabItem{Header="Example song — Example artist",Content="Media content"});tabs.SelectedIndex=0;
   FrameworkElement chrome=tabs;for(int i=0;i<8;i++)chrome=new GroupBox{Content=chrome};
   var window=new Window{Content=chrome,Width=360,Height=130,ShowInTaskbar=false,ShowActivated=false,Title="DeskPill media navigation fixture"};
   try {
    window.Show();window.UpdateLayout();var launcher=typeof(MediaSourceLauncher);var nativeType=launcher.GetNestedType("AppWindow",BindingFlags.NonPublic);var native=Activator.CreateInstance(nativeType,true);
    nativeType.GetField("Handle").SetValue(native,new WindowInteropHelper(window).Handle);nativeType.GetField("Pid").SetValue(native,(uint)System.Diagnostics.Process.GetCurrentProcess().Id);
    var tabType=launcher.GetNestedType("Tab",BindingFlags.NonPublic);var list=(System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(tabType));
    var read=launcher.GetMethod("ReadTabs",BindingFlags.Static|BindingFlags.NonPublic);var cancel=new System.Threading.CancellationTokenSource(2500);
    PumpUntil(Task.Run(delegate{Check((bool)read.Invoke(null,new object[]{native,list,cancel.Token}),"A complete tab scan was not reported");}));
    var names=list.Cast<object>().Select(t=>(string)tabType.GetField("Name").GetValue(t)).ToArray();int best=MediaSourcePolicy.BestTab(new MediaSnapshot{Title="Example song",Artist="Example artist"},names);Check(best>=0,"Native browser-chrome traversal did not find the media tab");
    var element=tabType.GetField("Element").GetValue(list[best]);var patternType=element.GetType().Assembly.GetType("System.Windows.Automation.SelectionItemPattern");
    PumpUntil(Task.Run(delegate{var pattern=element.GetType().GetMethod("GetCurrentPattern").Invoke(element,new[]{patternType.GetField("Pattern").GetValue(null)});patternType.GetMethod("Select").Invoke(pattern,null);}));
    Check(tabs.SelectedIndex==1,"Native selection did not activate the matching tab");
    for(int i=0;i<165;i++)tabs.Items.Add(new TabItem{Header="Extra tab "+i});window.UpdateLayout();list.Clear();cancel=new System.Threading.CancellationTokenSource(3000);
    PumpUntil(Task.Run(delegate{Check(!(bool)read.Invoke(null,new object[]{native,list,cancel.Token}),"A truncated tab scan was incorrectly reported as complete");}));
   }finally{window.Close();}
  });
  Test("Reopening volume shares the same queue so stale writes cannot win",delegate {
   var entered=new System.Threading.ManualResetEventSlim();var release=new System.Threading.ManualResetEventSlim();float last=-1;
   var writer=new LatestVolumeWriter(v=>{entered.Set();release.Wait(2000);last=v;});var type=typeof(IslandWindow).Assembly.GetType("DeskPill.VolumeFlyout");Action empty=delegate{};
   var first=Activator.CreateInstance(type,new object[]{.5f,false,writer,empty,empty});type.GetField("opened",Private).SetValue(first,true);var slider=Controls<Slider>((FrameworkElement)Field(first,"root")).Single();slider.Value=20;
   Check(entered.Wait(1000),"Background audio fixture never started");slider.Value=40;((IDisposable)first).Dispose();
   var second=Activator.CreateInstance(type,new object[]{.5f,false,writer,empty,empty});type.GetField("opened",Private).SetValue(second,true);Controls<Slider>((FrameworkElement)Field(second,"root")).Single().Value=80;((IDisposable)second).Dispose();
   release.Set();writer.Completion.GetAwaiter().GetResult();Check(Math.Abs(last-.8f)<.001,"A previous flyout overwrote the newly requested volume");writer.Dispose();
  });
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
  Test("Saving Floating style realigns every content layer with the shell",delegate {
   var state=new AppState();state.Settings.FastAnimations=true;var w=new IslandWindow(state,new StateStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"style-fixture")),false,true);Call(w,"SetOpen",false);Call(w,"ShowSettings");var settings=(SettingsWindow)Field(w,"settingsWindow");
   Controls<Button>(settings).Single(b=>Convert.ToString(b.Content)=="Size & shape").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   Controls<ComboBox>(settings).Single(c=>c.Items.Contains("Floating") && c.Items.Contains("Classic")).SelectedItem="Floating";
   Controls<Button>(settings).Single(b=>Convert.ToString(b.Content)=="Save changes").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   Check(Canvas.GetTop((FrameworkElement)Field(w,"compact"))==10,"Compact content did not follow Floating shell");
   Check(Canvas.GetTop((FrameworkElement)Field(w,"headerRow"))==28,"Header did not follow Floating shell");
   Call(w,"Cleanup");
  });
  Test("Status ring source updates its icon and None hides both rings",delegate {
   var w=Island();var state=(AppState)Field(w,"state");var ring=(Ring)Field(w,"compactRing");
   state.Settings.StatusRing="Tasks";Call(w,"UpdateValues");Check(ring.IconGlyph!="\uEB42","Tasks still uses the water icon");
   state.Settings.StatusRing="None";Call(w,"UpdateValues");Check(ring.Visibility!=Visibility.Visible && ((Ring)Field(w,"expandedRing")).Visibility!=Visibility.Visible,"None still displays a ring");
   state.Settings.StatusRing="Focus";Call(w,"UpdateValues");Check(ring.Visibility==Visibility.Visible && ring.Progress>=0 && ring.Progress<=1,"Focus ring did not restore");Call(w,"Cleanup");
  });
  Test("Speech model cards show requirements and model selection stays staged",delegate {
   var w=Island();try{var state=(AppState)Field(w,"state");Call(w,"ShowSettings");var settings=(SettingsWindow)Field(w,"settingsWindow");Call(settings,"OpenSection","Dictation");
    var choices=Controls<RadioButton>(settings).Where(c=>c.GroupName=="SpeechModel").ToArray();Check(choices.Length==2,"Missing speech model choices");var parakeet=choices.Single(c=>Convert.ToString(c.Content).Contains("Parakeet"));
    Check(Controls<TextBlock>(settings).Any(t=>t.Text.Contains("731 MB"))&&Controls<TextBlock>(settings).Any(t=>t.Text.Contains("300 MB")),"Model requirements are missing");parakeet.IsChecked=true;var device=Controls<ComboBox>(settings).Single(c=>c.Items.Contains("Auto")&&c.Items.Contains("GPU"));device.SelectedItem="GPU";Check(state.Settings.SpeechBackend=="Auto","Device choice applied before Save");Check(state.Settings.SpeechModel=="Vosk"&&Field(settings,"speechInstall")==null,"Selection applied or downloaded without consent");settings.Close();Check(state.Settings.SpeechModel=="Vosk","Cancel changed the speech model");
    Call(w,"ShowSettings");settings=(SettingsWindow)Field(w,"settingsWindow");Call(settings,"OpenSection","Dictation");Controls<RadioButton>(settings).Single(c=>c.GroupName=="SpeechModel"&&Convert.ToString(c.Content).Contains("Parakeet")).IsChecked=true;Controls<ComboBox>(settings).Single(c=>c.Items.Contains("Auto")&&c.Items.Contains("GPU")).SelectedItem="GPU";
    Controls<Button>(settings).Single(b=>Convert.ToString(b.Content)=="Save changes").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(state.Settings.SpeechModel=="Parakeet"&&state.Settings.SpeechBackend=="GPU"&&!state.Settings.DictationEnabled,"Save lost model choice or enabled microphone");
   }finally{Call(w,"Cleanup");}
  });
  Test("Dictation preferences remain optional and staged until saved",delegate {
   var w=Island();var state=(AppState)Field(w,"state");Call(w,"ShowSettings");var settings=(SettingsWindow)Field(w,"settingsWindow");
   Controls<Button>(settings).Single(b=>Convert.ToString(b.Content)=="Dictation").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   var toggle=Controls<CheckBox>(settings).Single(c=>Convert.ToString(c.Content).StartsWith("Enable English dictation"));
   Check(toggle.IsChecked==false && !state.Settings.DictationEnabled,"Dictation was not optional");toggle.IsChecked=true;settings.Close();
   Check(!state.Settings.DictationEnabled,"Cancel enabled dictation");Call(w,"Cleanup");
  });
  Test("Motion preview is immediate, staged and reversible",delegate {
   var w=Island();var state=(AppState)Field(w,"state");Call(w,"ShowSettings");var settings=(SettingsWindow)Field(w,"settingsWindow");
   Controls<Button>(settings).Single(b=>Convert.ToString(b.Content)=="Motion").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   Controls<Slider>(settings).Single(x=>Convert.ToString(x.Tag)=="HoverOpenMs").Value=270;
   Controls<Slider>(settings).Single(x=>Convert.ToString(x.Tag)=="MotionPace").Value=80;
   var preview=(Settings)Field(w,"preferencesPreview");var motion=Field(w,"motion");
   Check(preview.HoverOpenMs==270 && (double)motion.GetType().GetField("TimeScale").GetValue(motion)==.8 && state.Settings.HoverOpenMs==110,"Motion did not preview without saving");
   settings.Close();Check((double)motion.GetType().GetField("TimeScale").GetValue(motion)==1 && Field(w,"preferencesPreview")==null,"Cancel did not restore motion");Call(w,"Cleanup");
  });
  Test("Glass preview keeps content opaque and Cancel restores Classic",delegate {
   var w=Island();Call(w,"ShowSettings");var settings=(SettingsWindow)Field(w,"settingsWindow");
   Controls<Button>(settings).Single(b=>Convert.ToString(b.Content)=="Appearance").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   Controls<RadioButton>(settings).Single(r=>Convert.ToString(r.Tag)=="Glass").IsChecked=true;
   Controls<Slider>(settings).Single(x=>Convert.ToString(x.Tag)=="GlassOpacity").Value=45;
   var fill=(GradientBrush)((System.Windows.Shapes.Path)Field(w,"shell")).Fill;
   Check(Themes.Current=="Glass" && fill.GradientStops.Last().Color.A<140 && ((SolidColorBrush)((TextBlock)Field(w,"compactText")).Foreground).Color.A==255,"Glass faded content or stayed opaque");
   settings.Close();Check(Themes.Current=="Classic" && ((GradientBrush)((System.Windows.Shapes.Path)Field(w,"shell")).Fill).GradientStops.Last().Color.A==255,"Cancel retained Glass alpha");Call(w,"Cleanup");
  });
  Test("Detected games tuck away with Game mode off and restore the saved theme",delegate {
   var w=Island();
   Call(w,"SetGameMode",new GameWindow {ProcessId=123,Matches=true});
   Check((bool)Field(w,"gameDetected") && !(bool)Field(w,"gameModeActive") && Convert.ToString(Field(w,"pose"))=="Tiny" && Themes.Current=="Classic","Disabled overlay failed to tuck or changed the theme");
   Call(w,"ToggleGameMode");
   Check(((AppState)Field(w,"state")).Settings.GameOverlay && (bool)Field(w,"gameModeActive") && Convert.ToString(Field(w,"pose"))=="Compact","Tray toggle did not enable the overlay");
   var fill=(LinearGradientBrush)Themes.ShellFill;
   Check(fill.GradientStops.Last().Color.A<=57,"Game glass was not low opacity");
   Call(w,"ToggleGameMode");Check(Convert.ToString(Field(w,"pose"))=="Tiny" && Themes.Current=="Classic","Switching game mode off did not restore the tuck");
   Call(w,"SetGameMode",new GameWindow());Check(Convert.ToString(Field(w,"pose"))=="Compact","Leaving the game did not restore the notch");Call(w,"Cleanup");
  });
  Test("Dictation stays inside the notch through listening processing review and dismissal",delegate {
   var w=Island();var windows=Application.Current.Windows.Count;
   var type=typeof(IslandWindow).Assembly.GetType("DeskPill.DictationView");
   Action cancel=delegate{};
   var view=(FrameworkElement)Activator.CreateInstance(type,new object[]{cancel,cancel});
   Call(w,"ShowDictation",view);
   Check(Application.Current.Windows.Count==windows && Convert.ToString(Field(w,"pose"))=="Dictation","Dictation created another window or failed to use the notch");
   Check(ReferenceEquals(view.Parent,Field(w,"hudLayer")),"Voice view is outside the notch");
   Call(w,"SetTabOverlapForProbe",true);Check(Convert.ToString(Field(w,"pose"))=="Dictation","An overlap update hid live dictation");
   Call(w,"SetTabOverlapForProbe",false);Check(Convert.ToString(Field(w,"pose"))=="Dictation","A cleared overlap hid live dictation");
   type.GetMethod("Listening").Invoke(view,new object[]{4,.6});
   Check(Controls<TextBlock>(view).Any(x=>x.Text.Contains("Release to insert")),"Hold-to-talk instructions missing");
   type.GetMethod("Processing").Invoke(view,null);
   Check(Controls<TextBlock>(view).Any(x=>x.Text.Contains("Turning speech")),"Processing state missing");
   Action<string,bool> accept=delegate{};
   type.GetMethod("Review").Invoke(view,new object[]{"A retained transcript",accept});
   Call(w,"SetPose",NotchPose.Dictation);
   Check(((FrameworkElement)Field(w,"expandedLayer")).Opacity==0,"Transcript review exposed an unrelated dashboard underneath");
   Check(Controls<TextBox>(view).Single().Text=="A retained transcript" && view.Height>100,"Inline fallback review lost the transcript");
   Call(w,"HideDictation",view);
   Check(view.Parent==null && Convert.ToString(Field(w,"pose"))=="Compact","Dictation dismissal failed to restore the notch");Call(w,"Cleanup");
  });
  Test("Reduced-motion voice display still responds to actual microphone levels",delegate {
   bool motion=Ui.MotionEnabled;Ui.MotionEnabled=false;
   try {
    var type=typeof(IslandWindow).Assembly.GetType("DeskPill.DictationWaveform");var wave=(FrameworkElement)Activator.CreateInstance(type);
    type.GetProperty("Level").SetValue(wave,0.0,null);int quiet=InkPixels(wave);
    type.GetProperty("Level").SetValue(wave,1.0,null);int loud=InkPixels(wave);
    Check(loud>quiet*2,"Voice display did not reflect microphone input with motion disabled");
    Check(!(bool)type.GetField("attached",Private).GetValue(wave),"Reduced motion attached a continuous render loop");
   }finally{Ui.MotionEnabled=motion;}
  });
  Test("Voice rendering detaches when the dictation view leaves the notch",delegate {
   var w=Island();w.GetType().GetProperty("RuntimeProbeMode",Private).SetValue(w,true,null);w.Show();bool motion=Ui.MotionEnabled;Ui.MotionEnabled=true;
   try {
    var type=typeof(IslandWindow).Assembly.GetType("DeskPill.DictationView");Action empty=delegate{};var view=(FrameworkElement)Activator.CreateInstance(type,new object[]{empty,empty});Call(w,"ShowDictation",view);
    var frame=new DispatcherFrame();w.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(delegate{frame.Continue=false;}));Dispatcher.PushFrame(frame);
    var wave=type.GetField("orb",Private).GetValue(view);
    Check((bool)wave.GetType().GetField("attached",Private).GetValue(wave)==Ui.AnimationsAllowed,"Visible voice rendering failed to respect motion settings");
    Call(w,"HideDictation",view);
    frame=new DispatcherFrame();w.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(delegate{frame.Continue=false;}));Dispatcher.PushFrame(frame);
    Check(!(bool)wave.GetType().GetField("attached",Private).GetValue(wave),"Dismissed dictation retained its frame callback");
   }finally{Ui.MotionEnabled=motion;Call(w,"Cleanup");w.Hide();}
  });
  Test("A release during microphone preparation is retained and cancellation clears it",delegate {
   var w=Island();var type=typeof(IslandWindow).Assembly.GetType("DeskPill.DictationController");
   var controller=Activator.CreateInstance(type,new object[]{w,new Settings(),new Action<string>(delegate{}),new Action(delegate{})});
   var token=new System.Threading.CancellationTokenSource();type.GetField("session",Private).SetValue(controller,token);
   type.GetMethod("Finish").Invoke(controller,null);
   Check((bool)type.GetField("finishPending",Private).GetValue(controller),"An early release was discarded");
   type.GetMethod("Cancel").Invoke(controller,null);
   Check(token.IsCancellationRequested && !(bool)type.GetField("finishPending",Private).GetValue(controller),"Cancellation left a pending recording");
   ((IDisposable)controller).Dispose();Call(w,"Cleanup");
  });
  Test("Game overlay temporarily uses Glass and restores saved theme and style",delegate {
   var w=Island();var state=(AppState)Field(w,"state");state.Settings.GameOverlay=true;state.WaterMl=1000;string saved=state.Settings.Theme;
   Call(w,"SetGameMode",new GameWindow {ProcessId=123,Executable="fixture.exe",Matches=true});
   Check(Themes.Current=="Glass" && state.Settings.Theme==saved && Convert.ToString(Field(w,"pose"))=="Compact" && ((TextBlock)Field(w,"compactText")).Text.Contains("FPS"),"Game overlay did not stay compact or changed saved theme");
   Check(((Ring)Field(w,"compactRing")).Progress==0,"Game icon retained an unrelated water progress arc");
   Call(w,"SetGameMode",new GameWindow());Check(Themes.Current==saved && !(bool)Field(w,"gameModeActive"),"Game exit did not restore saved appearance");Call(w,"Cleanup");
  });
  Test("Game overlay toggles native input pass-through and restores it",delegate {
   var w=Island();((AppState)Field(w,"state")).Settings.GameOverlay=true;w.GetType().GetProperty("RuntimeProbeMode",Private).SetValue(w,true,null);w.Show();
   var handle=new WindowInteropHelper(w).Handle;
   Call(w,"SetGameMode",new GameWindow {ProcessId=123,Matches=true});
   Check((GameWindowStyle(handle)&0x20)!=0,"Game overlay still intercepted mouse input");
   Call(w,"ToggleGameMode");Check((GameWindowStyle(handle)&0x20)!=0 && Convert.ToString(Field(w,"pose"))=="Tiny" && Field(w,"gameCapture")==null,"Disabled game mode intercepted input or kept FPS capture");
   Call(w,"SetGameMode",new GameWindow());Check((GameWindowStyle(handle)&0x20)==0,"Game exit left the notch unclickable");Call(w,"Cleanup");w.Hide();
  });
  Test("Motion reset restores defaults and Save persists a custom profile",delegate {
   var w=Island();var state=(AppState)Field(w,"state");Call(w,"ShowSettings");var settings=(SettingsWindow)Field(w,"settingsWindow");
   Controls<Button>(settings).Single(b=>Convert.ToString(b.Content)=="Motion").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   var delay=Controls<Slider>(settings).Single(x=>Convert.ToString(x.Tag)=="HoverOpenMs");delay.Value=300;
   Controls<Button>(settings).Single(b=>Convert.ToString(b.Content)=="Reset motion").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(delay.Value==110,"Reset did not restore the default delay");
   delay.Value=240;Controls<Slider>(settings).Single(x=>Convert.ToString(x.Tag)=="MotionPace").Value=85;
   Controls<Button>(settings).Single(b=>Convert.ToString(b.Content)=="Save changes").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   Check(state.Settings.HoverOpenMs==240 && state.Settings.MotionPace==85 && state.Settings.GlassOpacity==72,"Saving motion lost the profile or changed untouched Glass opacity");Call(w,"Cleanup");
  });
  Test("Explicit opening leaves game input pass-through before showing controls",delegate {
   var w=Island();Call(w,"SetGameMode",new GameWindow {ProcessId=123,Matches=true});Call(w,"SetOpen",true);
   Check(!(bool)Field(w,"gameModeActive") && (bool)Field(w,"open") && Themes.Current=="Classic","Explicit controls stayed in game mode");Call(w,"Cleanup");
  });
  Test("Hidden pages disappear from carousel, dots and dock",delegate {
   var state=new AppState();state.Settings.HiddenPages=new[]{"Music","Water","Focus","Shelf","Agents"};state.Settings.FastAnimations=true;
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
    var dock=(Border)Field(w,"dockTray");Check(dock.Width==184 && Math.Abs(((TranslateTransform)dock.RenderTransform).Y)<.01 && dock.Opacity==1,"Rebuilt dock was offset or hidden after Save");
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
   Check(Controls<Button>((DependencyObject)Field(w,"dockTray")).Count()==7,"Expected six page icons and Settings");Call(w,"Cleanup");
  });
  Test("Brief pointer passes do not open the notch; a relaxed dwell does",delegate {
   var w=Island();Call(w,"SetOpen",false);var now=DateTime.UtcNow;
   Call(w,"ProcessPointer",0.0,260.0,now);Call(w,"ProcessPointer",270.0,12.0,now.AddMilliseconds(10));
   Call(w,"ProcessPointer",270.0,12.0,now.AddMilliseconds(90));Check(!(bool)Field(w,"open"),"Brief hover opened the notch too eagerly");
   Call(w,"ProcessPointer",270.0,12.0,now.AddMilliseconds(130));Check((bool)Field(w,"open"),"Deliberate hover remained unresponsive");Call(w,"Cleanup");
  });
  Test("Options wait through a brief pass and open after a relaxed dwell",delegate {
   var w=Island();var now=DateTime.UtcNow;
   Call(w,"ProcessPointer",270.0,162.0,now);Call(w,"ProcessPointer",270.0,162.0,now.AddMilliseconds(80));
   Check(Convert.ToString(Field(w,"pose"))=="Expanded","Options opened too eagerly");
   Call(w,"ProcessPointer",270.0,162.0,now.AddMilliseconds(145));
   Check(Convert.ToString(Field(w,"pose"))=="OptionsDock","Deliberate options hover remained unresponsive");Call(w,"Cleanup");
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
