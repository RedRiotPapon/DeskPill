using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DeskPill;
class AgentTests {
 static int checks; static void Check(bool v,string name){if(!v)throw new Exception(name);checks++;}
 static AgentProfile Profile(string exe,string folder){return new AgentProfile{Executable=exe,Workspace=folder};}
 static void Main(string[] args){
 string exe=args[0], folder=Path.GetDirectoryName(exe); var p=Profile(exe,folder);
 Check(AgentTools.Quote("a b")=="\"a b\"","quote space");
 Check(AgentTools.Quote("a\\")=="\"a\\\\\"","quote trailing slash");
 Check(AgentTools.Validate(p)=="","valid fixture");p.Provider="App shortcut";p.Workspace="C:\\missing-project-fixture";Check(AgentTools.Validate(p)!="","shortcut rejects missing working directory");p.Workspace=folder;p.Provider="Codex CLI";p.Executable="cmd.cmd";Check(AgentTools.Validate(p)!="","reject scripts");p.Executable=exe;
 p.Executable="C:\\bad\0.exe";Check(AgentTools.Validate(p)!="","invalid path is a validation error");p.Executable=exe;
 string oldFolder=p.Workspace;p.Workspace="\\";Check(AgentTools.Validate(p)!="","drive-relative workspace rejected");p.Workspace=oldFolder;
 string a=AgentTools.BuildArguments(p,"hi","");Check(a.Contains("read-only")&&a.Contains("approval_policy")&&!a.Contains("bypass"),"safe codex");
 p.AllowEdits=true;Check(AgentTools.BuildArguments(p,"hi","").Contains("workspace-write"),"explicit writes");p.AllowEdits=false;
 string sid=Guid.NewGuid().ToString();a=AgentTools.BuildArguments(p,"hi",sid);Check(a.Contains("resume "+AgentTools.Quote(sid))&&a.Contains("read-only")&&!a.Contains("--last"),"exact safe resume");
 bool threw=false;try{AgentTools.BuildArguments(p,"hi","latest");}catch(ArgumentException){threw=true;}Check(threw,"reject nonuuid");
 var s=new AgentSession();AgentTools.ApplyEvent(s,p.Provider,"{\"type\":\"thread.started\",\"thread_id\":\""+sid+"\"}");Check(s.SessionId==sid,"thread id");
 AgentTools.ApplyEvent(s,p.Provider,"{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\",\"text\":\"hello\"}}");Check(s.Output.Contains("hello"),"codex message");
 AgentTools.ApplyEvent(s,p.Provider,"not json");AgentTools.ApplyEvent(s,p.Provider,"{\"type\":\"turn.failed\",\"error\":{\"message\":\"Sign in required\"}}");Check(s.Status=="Needs attention"&&s.Summary.Contains("Sign in"),"actionable error");
 s=new AgentSession();AgentTools.ApplyEvent(s,"Claude Code","{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"Claude answer\"}]}}");Check(s.Output.Contains("Claude answer"),"claude message");
 AgentTools.ApplyEvent(s,"Claude Code","{\"type\":\"result\",\"permission_denials\":[{\"tool_name\":\"Edit\"}]}");Check(s.Status=="Needs attention","permission denial");
 s=new AgentSession();AgentTools.ApplyEvent(s,"Gemini CLI","{\"type\":\"message\",\"role\":\"assistant\",\"content\":\"Gemini answer\"}");Check(s.Output.Contains("Gemini answer"),"gemini message");
 var chunks=new AgentSession();AgentTools.ApplyEvent(chunks,"Gemini CLI","{\"type\":\"message\",\"role\":\"assistant\",\"content\":\"Hello\",\"delta\":true}");AgentTools.ApplyEvent(chunks,"Gemini CLI","{\"type\":\"message\",\"role\":\"assistant\",\"content\":\" world\\n```cs\\n\",\"delta\":true}");AgentTools.ApplyEvent(chunks,"Gemini CLI","{\"type\":\"message\",\"role\":\"assistant\",\"content\":\"return 1;\\n```\",\"delta\":true}");Check(chunks.Output=="Hello world\n```cs\nreturn 1;\n```","gemini deltas preserve exact whitespace and code");
 for(int i=0;i<100;i++)AgentTools.ApplyEvent(s,"Gemini CLI","{\"type\":\"message\",\"role\":\"assistant\",\"content\":\""+new string('x',1000)+"\"}");Check(s.Output.Length<=16384,"bounded output");
 using(var hub=new AgentHub()){
 var t=hub.SendAsync(p,"hello fixture");t.Wait(10000);Check(t.IsCompleted&&hub.Get(p.Id).Status=="Done","fixture completes");Check(hub.Get(p.Id).Output.Contains("fixture answer"),"fixture output");Check(hub.Get(p.Id).SessionId=="12345678-1234-1234-1234-123456789abc","owned session");
 Check(hub.Reset(p.Id)&&hub.Get(p.Id).SessionId==""&&hub.Get(p.Id).Output=="","new conversation clears only owned context");
 t=hub.SendAsync(p,"hold");Thread.Sleep(300);Check(!hub.Reset(p.Id),"cannot reset active conversation");var duplicate=hub.SendAsync(p,"duplicate");Check(duplicate.IsCompleted,"duplicate refused");hub.StopAsync(p.Id).Wait(10000);Check(hub.Get(p.Id).Status=="Stopped"&&!hub.Get(p.Id).Running,"stop owned process");
 t=hub.SendAsync(p,"flood");Check(t.Wait(10000),"flood completes");Check(hub.Get(p.Id).Output.Length<=16384,"flood bounded");
 t=hub.SendAsync(p,"fail");Check(t.Wait(10000)&&hub.Get(p.Id).Status=="Needs attention","nonzero attention");
 t=hub.SendAsync(p,"tree");int child=Child(hub,p.Id);Check(child>0,"fixture child started");hub.StopAsync(p.Id).Wait(10000);Check(Exited(child),"stop kills descendants");
 t=hub.SendAsync(p,"treeexit");Check(t.Wait(10000),"parent exits with child");child=Child(hub,p.Id);Check(child>0&&Exited(child),"parent completion kills remaining descendants");
 p.Provider="Gemini CLI";t=hub.SendAsync(p,"fixture argument \"quoted\" trailing\\ & | >\nবাংলা");Check(t.Wait(10000)&&hub.Get(p.Id).Status=="Done","Gemini prompt survives native argument quoting");p.Provider="Codex CLI";
 using(var unrelated=Process.Start(new ProcessStartInfo(exe,"child"){UseShellExecute=false,CreateNoWindow=true})){
 try{t=hub.SendAsync(p,"hold");Thread.Sleep(100);hub.StopAsync(p.Id).Wait(10000);Check(!unrelated.HasExited,"stop leaves unrelated process alive");}finally{if(!unrelated.HasExited)unrelated.Kill();unrelated.WaitForExit();}}
 p.Provider="App shortcut";hub.SendAsync(p,"hello").Wait();Check(hub.Get(p.Id).Status=="Needs attention","shortcut manual only");
 }
 var closing=new AgentHub();p.Provider="Codex CLI";var active=closing.SendAsync(p,"tree");int closingChild=Child(closing,p.Id);closing.Dispose();Check(active.Wait(10000)&&Exited(closingChild),"dispose kills owned tree");
 for(int i=0;i<10;i++){using(var race=new AgentHub()){var started=race.SendAsync(p,"hold");race.StopAsync(p.Id).Wait(10000);Check(started.IsCompleted&&race.Get(p.Id).Status=="Stopped","startup cancellation");}}
 Console.WriteLine("Agents: "+checks+" checks passed.");
 }
 static int Child(AgentHub hub,string id){for(int i=0;i<100;i++){var m=System.Text.RegularExpressions.Regex.Match(hub.Get(id).Output,"childpid=(\\d+)");if(m.Success)return int.Parse(m.Groups[1].Value);Thread.Sleep(25);}return -1;}
 static bool Exited(int id){if(id<=0)return false;try{using(var p=Process.GetProcessById(id))return p.WaitForExit(5000);}catch(ArgumentException){return true;}}
}
