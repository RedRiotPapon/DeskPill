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
   Check(burst.Count<=10 && burst.Last().Key=="burst19","A burst is bounded and keeps the latest arrivals");
   Check(inbox.Observe(items,now.AddSeconds(31)).Count==0,"Overflow burst entries are also marked seen");
   var legacy=new AppState {Version=2};legacy.Settings.MirrorNotifications=false;legacy.Normalize(now.LocalDateTime);
   Check(legacy.Settings.MirrorNotifications,"Previous installs enable the requested notification feature on migration");
   legacy.Settings.MirrorNotifications=false;legacy.Normalize(now.LocalDateTime);
   Check(!legacy.Settings.MirrorNotifications,"A saved opt-out stays off after normalization");
   Console.WriteLine("Notification tests: "+checks+" passed");return 0;
  }catch(Exception e){Console.WriteLine("FAIL "+e.Message);return 1;}
 }
}
