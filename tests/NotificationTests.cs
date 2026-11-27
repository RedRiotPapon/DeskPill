using System;
using System.Linq;
using DeskPill;
class NotificationTests {
 static int checks;
 static void Check(bool value,string name) { if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name); }
 static DesktopNotification Item(string key,DateTimeOffset at) { return new DesktopNotification {Key=key,CreatedAt=at,AppName="Fixture",Title="A title",Body="A body"}; }
 static int Main() {
  try {
   var now=new DateTimeOffset(2026,11,27,0,0,0,TimeSpan.Zero);var inbox=new NotificationInbox();
   Check(inbox.Observe(new[]{Item("old",now.AddMinutes(-5))},now).Count==0,"Existing notification history stays silent at startup");
   var n=Item("new",now.AddSeconds(1));var current=new[]{Item("old",now.AddMinutes(-5)),n};
   Check(inbox.Observe(current,now.AddSeconds(2)).Single().Title=="A title","New arrival carries title and body");
   Check(inbox.Observe(current,now.AddSeconds(3)).Count==0,"Repeated polls do not replay the same toast");
   Check(inbox.Observe(new[]{Item("late-history",now.AddMinutes(-2))},now.AddSeconds(4)).Count==0,"Late historical entries are ignored");
   Check(inbox.Observe(new[]{n},now.AddSeconds(5)).Count==0,"Removed then restored notification stays deduplicated");
   var reused=Item("same-id-new-time",now.AddSeconds(6));
   Check(inbox.Observe(new[]{reused},now.AddSeconds(7)).Count==1,"A newly created toast with reused platform ID can arrive");
   inbox.Reset();Check(inbox.Observe(new[]{reused},now.AddSeconds(8)).Count==0,"Re-enabling access establishes a fresh baseline");
   var items=Enumerable.Range(0,20).Select(i=>Item("burst"+i,now.AddSeconds(10+i))).ToArray();
   var burst=inbox.Observe(items,now.AddSeconds(30));
   Check(burst.Count==20 && burst.Last().Key=="burst19","A burst retains arrivals for the bounded Recent history");
   Check(inbox.Observe(items,now.AddSeconds(31)).Count==0,"Overflow burst entries are also marked seen");
   var legacy=new AppState {Version=2};legacy.Settings.MirrorNotifications=false;legacy.Normalize(now.LocalDateTime);
   Check(legacy.Settings.MirrorNotifications,"Previous installs enable the requested notification feature on migration");
   legacy.Settings.MirrorNotifications=false;legacy.Normalize(now.LocalDateTime);
   Check(!legacy.Settings.MirrorNotifications,"A saved opt-out stays off after normalization");
   var history=new NotificationHistory();var settings=new Settings();
   var queued=Item("queued",now.AddMinutes(-10));queued.AppId="demo.app";queued.Title="Retained";
   history.Receive(queued,settings,false,false);
   Check(history.Take(settings,false)==queued && history.Items.Length==1,"pending arrival survives without a freshness expiry");
   settings.MutedNotificationApps=new[]{"DEMO.APP"};var muted=Item("muted",now);muted.AppId="demo.app";history.Receive(muted,settings,false,false);
   Check(history.Pending==null && history.Items.Length==2,"muted app is retained in Recent without peeking");
   settings.MutedNotificationApps=new string[0];var focused=Item("focus",now);history.Receive(focused,settings,true,false);
   Check(history.Pending==null && history.Items.Length==3,"focus quiet mode retains arrivals without a peek");
   settings.FocusNotifications="Allow peeks";history.Receive(Item("focus-allowed",now),settings,true,false);
   Check(history.Take(settings,true)!=null,"focus policy can allow peeks");
   history.Receive(Item("fullscreen",now),settings,false,true);
   Check(history.Pending==null,"fullscreen quiet mode stores history only");
   settings.FullscreenNotifications="After fullscreen";history.Receive(Item("deferred1",now),settings,false,true);history.Receive(Item("deferred2",now),settings,false,true);
   Check(history.Take(settings,false).Key=="deferred2" && history.Take(settings,false)==null,"resume shows the newest eligible arrival without a backlog barrage");
   history.Receive(queued,settings,false,false);Check(history.Items.Count(entry=>entry.Key=="queued")==1,"Recent deduplicates arrival identity");
   for(int i=0;i<70;i++)history.Receive(Item("cap"+i,now),settings,false,false);
   Check(history.Items.Length==50 && history.Items[0].Key=="cap69","Recent history is bounded newest-first");
   history.Clear();Check(history.Items.Length==0 && history.Pending==null,"clearing Recent removes pending content");
   Check(!NotificationLauncher.CanOpen("bad\" /inject") && !NotificationLauncher.CanOpen("bad\napp") && NotificationLauncher.CanOpen("Vendor.App_abc!App"),"source app identifiers cannot inject explorer arguments");
   Check(!PasteTarget.CanInject(IntPtr.Zero,123,123,true,false) && !PasteTarget.CanInject(new IntPtr(1),123,124,true,false) && !PasteTarget.CanInject(new IntPtr(1),123,123,false,false) && !PasteTarget.CanInject(new IntPtr(1),123,123,true,true),"paste guards reject missing, reused, unfocused or modifier-held targets");
   Console.WriteLine("Notification tests: "+checks+" passed");return 0;
  }catch(Exception e){Console.WriteLine("FAIL "+e.Message);return 1;}
 }
}
