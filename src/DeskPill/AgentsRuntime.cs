using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32.SafeHandles;

namespace DeskPill
{
    // Runtime state is deliberately never serialized into AppState.
    public sealed class AgentSession
    {
        internal readonly object Gate = new object();
        internal string status = "Ready", summary = "Ready to send a prompt.", output = "", sessionId = "";
        internal bool running;
        internal string identity = "", diagnostics = "";
        internal string transcript="", lastPrompt="";
        internal DateTime startedUtc;
        public string Transcript { get { lock(Gate) return transcript; } }
        public string LastPrompt { get { lock(Gate) return lastPrompt; } }
        public DateTime StartedUtc { get { lock(Gate) return startedUtc; } }
        public string Draft = "";
        public string Status { get { lock(Gate) return status; } }
        public string Summary { get { lock(Gate) return summary; } }
        public string Output { get { lock(Gate) return output; } }
        public string Diagnostics { get { lock(Gate) return diagnostics; } }
        internal void Diagnostic(string value) { diagnostics=Tail(diagnostics+value,16384); }
        public string SessionId { get { lock(Gate) return sessionId; } }
        public bool Running { get { lock(Gate) return running; } }
        internal void Append(string value) { if(string.IsNullOrEmpty(value))return; output = Tail(output + value,16384); transcript=Tail(transcript+value,65536); }
        internal static string Tail(string value,int max) { return value.Length <= max ? value : value.Substring(value.Length-max); }
        internal void Attention(string value) { status="Needs attention";summary=Tail(value ?? "The tool needs attention.",1000); }
    }

    public static class AgentTools
    {
        public static bool IsShortcut(AgentProfile p) { return p != null && p.Provider == "App shortcut"; }
        public static string Validate(AgentProfile p)
        {
            try { return ValidatePath(p); }
            catch(ArgumentException) { return "Choose a valid executable path and workspace folder."; }
            catch(InvalidOperationException) { return "The npm package metadata is invalid."; }
            catch(NotSupportedException) { return "Choose a valid executable path and workspace folder."; }
            catch(PathTooLongException) { return "The executable or workspace path is too long."; }
        }
        static string ValidatePath(AgentProfile p)
        {
            if(p==null)return "Choose a connection.";
            if(p.Provider!="Codex CLI"&&p.Provider!="Claude Code"&&p.Provider!="Gemini CLI"&&!IsShortcut(p))return "Choose a supported tool.";
            try { AgentLaunch.Resolve(p); } catch(ArgumentException e) { return e.Message; } catch(IOException) { return "Could not read the CLI installation."; } catch(UnauthorizedAccessException) { return "Cannot read the CLI installation."; }
            if(!IsShortcut(p)&&(!Absolute(p.Workspace)||!Directory.Exists(p.Workspace)))return "Choose an existing workspace folder.";
            if(IsShortcut(p)&&!string.IsNullOrWhiteSpace(p.Workspace)&&(!Absolute(p.Workspace)||!Directory.Exists(p.Workspace)))return "Choose an existing project folder, or clear it for this app shortcut.";
            return "";
        }
        static bool Absolute(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path) &&
                ((path.Length>=3&&char.IsLetter(path[0])&&path[1]==':'&&(path[2]=='\\'||path[2]=='/')) || path.StartsWith("\\\\",StringComparison.Ordinal));
        }
        public static string FindExecutable(string provider)
        {
            string name=provider=="Codex CLI"?"codex.exe":provider=="Claude Code"?"claude.exe":provider=="Gemini CLI"?"gemini.exe":"";
            if(name.Length==0)return "";
            var dirs=new List<string>((Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator));
            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"npm"));
            string user=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            dirs.Add(Path.Combine(user,".local","bin"));
            if(provider=="Codex CLI")try{string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenAI","Codex","bin");if(Directory.Exists(root))dirs.AddRange(Directory.GetDirectories(root).OrderByDescending(Directory.GetLastWriteTimeUtc));}catch(IOException){}catch(UnauthorizedAccessException){}
            foreach(string d in dirs)try{if(string.IsNullOrWhiteSpace(d))continue;string file=Path.GetFullPath(Path.Combine(d.Trim().Trim('"'),name));if(File.Exists(file))return file;string shim=Path.ChangeExtension(file,".cmd");if(File.Exists(shim)){try{AgentLaunch.Resolve(new AgentProfile{Provider=provider,Executable=shim});return shim;}catch(ArgumentException){}catch(InvalidOperationException){}}}catch(ArgumentException){}catch(NotSupportedException){}catch(IOException){}catch(UnauthorizedAccessException){}
            return "";
        }
        // CommandLineToArgvW / CRT argument escaping; never passed through a shell.
        public static string Quote(string value)
        {
            var b=new StringBuilder("\"");int slashes=0;
            foreach(char c in value??""){if(c=='\\'){slashes++;continue;}if(c=='"'){b.Append('\\',slashes*2+1);b.Append(c);}else{b.Append('\\',slashes);b.Append(c);}slashes=0;}
            b.Append('\\',slashes*2);return b.Append('"').ToString();
        }
        public static bool SupportsImages(string provider) { return provider=="Codex CLI"; }
        public static string ValidateImages(AgentProfile p,string[] images)
        {
            if(images==null||images.Length==0)return "";
            if(!SupportsImages(p.Provider))return "Screen attachments are unavailable for this provider. Choose Codex CLI.";
            if(images.Length>4)return "Attach at most four reviewed images.";
            foreach(string image in images)try{if(!Absolute(image)||!File.Exists(image))return "A reviewed attachment is missing. Capture or select it again.";}catch(ArgumentException){return "Choose a valid reviewed attachment.";}catch(NotSupportedException){return "Choose a valid reviewed attachment.";}
            return "";
        }
        public static string BuildArguments(AgentProfile p,string prompt,string sessionId) { return BuildArguments(p,prompt,sessionId,null); }
        public static string BuildArguments(AgentProfile p,string prompt,string sessionId,string[] images)
        {
            string problem=ValidateImages(p,images);if(problem.Length>0)throw new ArgumentException(problem);
            string attachments=images==null?"":string.Concat(images.Select(image=>"--image "+Quote(image)+" "));
            Guid parsed; if(!string.IsNullOrEmpty(sessionId)&&!Guid.TryParse(sessionId,out parsed))throw new ArgumentException("Only this connection's UUID can be resumed.");
            if(p.Provider=="Codex CLI")return "exec --json --sandbox "+(p.AllowEdits?"workspace-write":"read-only")+" -c "+Quote("approval_policy=\"never\"")+" --skip-git-repo-check "+(string.IsNullOrEmpty(sessionId)?"":"resume "+Quote(sessionId)+" ")+attachments+"-";
            if(p.Provider=="Claude Code")return "-p --verbose --output-format stream-json --permission-mode plan"+(string.IsNullOrEmpty(sessionId)?"":" --resume "+Quote(sessionId));
            if(p.Provider=="Gemini CLI")return "--output-format stream-json --approval-mode plan"+(string.IsNullOrEmpty(sessionId)?"":" --resume "+Quote(sessionId))+" -p "+Quote(prompt);
            throw new ArgumentException("App shortcuts use manual copy and open.");
        }
        static IDictionary<string,object> Obj(object o){return o as IDictionary<string,object>;}
        static object Value(IDictionary<string,object> d,string key){object o;return d!=null&&d.TryGetValue(key,out o)?o:null;}
        static string Str(IDictionary<string,object> d,string key){return Value(d,key) as string ?? "";}
        static string Message(object o){var d=Obj(o);return o as string ?? (d==null?"":Str(d,"message"));}
        static bool Any(object o){var e=o as ICollection;return e!=null&&e.Count>0;}
        public static void ApplyEvent(AgentSession s,string provider,string line)
        {
            if(string.IsNullOrWhiteSpace(line)||line.Length>65536)return;
            IDictionary<string,object> d;
            try{d=Obj(new JavaScriptSerializer{MaxJsonLength=65536,RecursionLimit=32}.DeserializeObject(line));}catch(ArgumentException){return;}catch(InvalidOperationException){return;}
            if(d==null)return;
            lock(s.Gate){
                string type=Str(d,"type"), id=Str(d,"session_id");
                if(provider=="Codex CLI"&&type=="thread.started")id=Str(d,"thread_id");
                Guid uuid;if(Guid.TryParse(id,out uuid)&&s.sessionId.Length==0)s.sessionId=id;
                if(type=="error"||type=="turn.failed"||object.Equals(Value(d,"is_error"),true)){
                    string error=Message(Value(d,"error"));if(error.Length==0)error=Str(d,"message");if(error.Length==0)error=Str(d,"result");if(error.Length==0)error="The tool reported an error. Check its sign-in and workspace.";s.Attention(error);s.Append(error+Environment.NewLine);
                }
                if(type=="permission_request"||type=="permission_denied"||type=="approval_required"||Any(Value(d,"permission_denials")))s.Attention("A tool permission was requested or denied. Review the request in the tool; DeskPill does not approve it.");
                if(s.status!="Needs attention"){
                    string activity="";
                    if(provider=="Codex CLI"&&(type=="item.started"||type=="item.updated"||type=="item.completed")){
                        var item=Obj(Value(d,"item"));string kind=Str(item,"type"), detail=Str(item,"command");
                        if(kind=="command_execution")activity=(type=="item.completed"?"Command finished: ":"Running command: ")+detail;
                        else if(kind=="mcp_tool_call")activity=(type=="item.completed"?"Tool finished: ":"Using tool: ")+Str(item,"server")+" / "+Str(item,"tool");
                        else if(kind=="file_change")activity=type=="item.completed"?"File changes reported by the tool.":"Tool is changing files.";
                        else if(kind=="web_search")activity="Tool reported a web search: "+Str(item,"query");
                    }
                    if(provider=="Claude Code"&&type=="assistant"){
                        var message=Obj(Value(d,"message"));var blocks=Value(message,"content") as IEnumerable;
                        if(blocks!=null)foreach(object block in blocks){var b=Obj(block);if(Str(b,"type")=="tool_use")activity="Using tool: "+Str(b,"name");}
                    }
                    if(provider=="Gemini CLI"&&(type=="tool_use"||type=="tool_result"))activity=(type=="tool_use"?"Using tool: ":"Tool result: ")+Str(d,"tool_name");
                    if(activity.Length>0)s.summary=AgentSession.Tail(activity,1000);
                }
                string text="";
                if(provider=="Codex CLI"&&type=="item.completed"){var item=Obj(Value(d,"item"));if(Str(item,"type")=="agent_message")text=Str(item,"text");}
                if(provider=="Claude Code"){
                    if(type=="assistant"){
                        var message=Obj(Value(d,"message"));var blocks=Value(message,"content") as IEnumerable;
                        if(blocks!=null)foreach(object block in blocks){var b=Obj(block);if(Str(b,"type")=="text")text+=Str(b,"text");}
                    }
                    if(type=="result"&&s.output.Length==0)text=Str(d,"result");
                    if(type=="result"&&Str(d,"subtype").StartsWith("error",StringComparison.Ordinal))s.Attention(Str(d,"result").Length>0?Str(d,"result"):"Claude reported an unsuccessful result.");
                }
                if(provider=="Gemini CLI"){
                    if(type=="message"&&Str(d,"role")=="assistant")text=Str(d,"content");
                    if(type=="result"&&(Str(d,"status")=="error"||Value(d,"error")!=null))s.Attention(Message(Value(d,"error")));
                }
                if(text.Length>0){bool delta=provider=="Gemini CLI"&&type=="message"&&object.Equals(Value(d,"delta"),true);s.Append(text+(delta?"":Environment.NewLine));if(s.status!="Needs attention")s.summary=AgentSession.Tail(delta?s.output:text,1000);}
            }
        }
    }

    public sealed class AgentHub : IDisposable
    {
        readonly object gate=new object();
        readonly Dictionary<string,AgentSession> sessions=new Dictionary<string,AgentSession>();
        readonly Dictionary<string,Run> runs=new Dictionary<string,Run>();
        bool disposed;
        public event Action Changed;
        sealed class Run { internal bool Cancelled;internal OwnedAgentProcess Process;internal Task Task; }
        public AgentSession Get(string id){lock(gate){AgentSession s;if(!sessions.TryGetValue(id??"",out s)){s=new AgentSession();sessions.Add(id??"",s);}return s;}}
        public bool Reset(string id){lock(gate){if(disposed||runs.ContainsKey(id))return false;var session=Get(id);lock(session.Gate){session.sessionId="";session.transcript="";session.lastPrompt="";session.startedUtc=default(DateTime);session.output="";session.diagnostics="";session.status="Ready";session.summary="Ready to send a prompt.";}return true;}}
        void Notify(){var handler=Changed;if(handler!=null)try{handler();}catch(ObjectDisposedException){}catch(InvalidOperationException){}}
        public Task SendAsync(AgentProfile profile,string prompt) { return SendAsync(profile,prompt,null); }
        public Task SendAsync(AgentProfile profile,string prompt,string[] images)
        {
            string[] imageCopy=images==null?new string[0]:(string[])images.Clone();
            if(profile==null)return Task.FromResult(0);
            var copy=new AgentProfile{Id=profile.Id,Name=profile.Name,Provider=profile.Provider,Executable=profile.Executable,Workspace=profile.Workspace,AllowEdits=profile.AllowEdits};
            Task result;
            lock(gate){
                var s=Get(copy.Id);
                if(disposed||runs.ContainsKey(copy.Id))return Task.FromResult(0);
                string error=AgentTools.Validate(copy);
                if(error.Length==0)error=AgentTools.ValidateImages(copy,imageCopy);
                if(AgentTools.IsShortcut(copy))error="Copy your prompt and open the app to continue manually.";
                if(string.IsNullOrWhiteSpace(prompt))error="Write a prompt before sending.";
                if((prompt??"").Length>16000)error="Keep the prompt under 16,000 characters.";
                if(error.Length>0){lock(s.Gate)s.Attention(error);result=Task.FromResult(0);}
                else{
                    var run=new Run();runs.Add(copy.Id,run);
                    lock(s.Gate){string identity=copy.Provider+"\n"+copy.Executable+"\n"+copy.Workspace+"\n"+copy.AllowEdits;if(s.identity!=identity){s.sessionId="";s.transcript="";}s.identity=identity;s.lastPrompt=prompt;s.startedUtc=DateTime.UtcNow;s.transcript=AgentSession.Tail(s.transcript+ (s.transcript.Length>0?Environment.NewLine+Environment.NewLine:"")+"You:"+Environment.NewLine+prompt+Environment.NewLine+Environment.NewLine+"Agent:"+Environment.NewLine,65536);s.running=true;s.status="Working";s.summary="Waiting for the tool's response…";s.output="";s.diagnostics="";}
                    run.Task=Task.Run(()=>Execute(copy,prompt,imageCopy,s,run));result=run.Task;
                }
            }
            Notify();return result;
        }
        void Execute(AgentProfile p,string prompt,string[] images,AgentSession s,Run run)
        {
            OwnedAgentProcess child=null;
            try{
                lock(gate){if(run.Cancelled)return;var launch=AgentLaunch.Resolve(p);child=OwnedAgentProcess.Start(launch.Executable,launch.PrefixArguments+AgentTools.BuildArguments(p,prompt,s.SessionId,images),p.Workspace);run.Process=child;}
                var stdout=Task.Run(()=>Read(child.Output,s,p.Provider,false));
                var stderr=Task.Run(()=>Read(child.Error,s,p.Provider,true));
                var input=Task.Run(()=>{try{using(var writer=child.Input){if(p.Provider!="Gemini CLI")writer.Write(images.Length==0?prompt:"DeskPill screen context: Use the attached image as the visual context for this request. It is a user-reviewed still image, not a live screen stream. Do not attempt another screen capture to read it. If a newer image is needed, ask the user to attach one with the camera button.\n\nUser request:\n"+prompt);}}catch(IOException){}catch(ObjectDisposedException){}});
                bool finished=child.Wait(30*60*1000);
                child.Stop(); // Also release pipes inherited by any remaining descendants.
                Task.WaitAll(new[]{stdout,stderr,input},10000);
                lock(s.Gate){if(!finished)s.Attention("The run reached the 30 minute limit and was stopped.");else if(!run.Cancelled&&child.ExitCode!=0&&s.status!="Needs attention")s.Attention("The tool exited with code "+child.ExitCode+". Check its sign-in, version and workspace.");}
            }catch(Exception e){lock(s.Gate)s.Attention("Could not run the tool: "+e.Message);}
            finally{
                lock(gate){if(child!=null)child.Dispose();runs.Remove(p.Id);lock(s.Gate){s.running=false;if(run.Cancelled){s.status="Stopped";s.summary="Stopped this DeskPill run.";}else if(s.status!="Needs attention"){s.status="Done";if(s.output.Length==0)s.summary="The tool finished without an assistant response.";}}}
                Notify();
            }
        }
        void Read(StreamReader reader,AgentSession s,string provider,bool error)
        {
            try{
                var line=new StringBuilder();var buffer=new char[2048];bool overflow=false;int n;
                while((n=reader.Read(buffer,0,buffer.Length))>0){
                    for(int i=0;i<n;i++){
                        char c=buffer[i];if(c=='\n'){
                            if(!overflow){string value=line.ToString().TrimEnd('\r');if(error){lock(s.Gate)s.Diagnostic(value+Environment.NewLine);}else AgentTools.ApplyEvent(s,provider,value);Notify();}
                            line.Length=0;overflow=false;
                        }else if(!overflow){if(line.Length<65536)line.Append(c);else{overflow=true;line.Length=0;}}
                    }
                }
                if(!overflow&&line.Length>0){if(error){lock(s.Gate)s.Diagnostic(line.ToString());}else AgentTools.ApplyEvent(s,provider,line.ToString());Notify();}
            }catch(IOException){}catch(ObjectDisposedException){}
        }
        public Task StopAsync(string id){lock(gate){Run r;if(!runs.TryGetValue(id??"",out r))return Task.FromResult(0);r.Cancelled=true;if(r.Process!=null)r.Process.Stop();return r.Task??Task.FromResult(0);}}
        public void Dispose(){lock(gate){if(disposed)return;disposed=true;foreach(var r in runs.Values){r.Cancelled=true;if(r.Process!=null)r.Process.Stop();}}}
    }

    // Create suspended, place in kill-on-close job, then resume: no uncontained startup window.
    internal sealed class OwnedAgentProcess : IDisposable
    {
        static readonly object launchGate=new object();
        IntPtr job,process;
        internal StreamReader Output,Error;
        internal StreamWriter Input;
        internal int ExitCode {get{uint n;return GetExitCodeProcess(process,out n)?unchecked((int)n):-1;}}
        internal bool Wait(int milliseconds){return WaitForSingleObject(process,(uint)milliseconds)==0;}
        internal void Stop(){if(job!=IntPtr.Zero)TerminateJobObject(job,1);}
        internal static OwnedAgentProcess Start(string exe,string arguments,string cwd)
        {
            lock(launchGate){
                var owner=new OwnedAgentProcess();IntPtr ir=IntPtr.Zero,iw=IntPtr.Zero,or=IntPtr.Zero,ow=IntPtr.Zero,er=IntPtr.Zero,ew=IntPtr.Zero;var pi=new PROCESS_INFORMATION();
                try{
                    owner.job=CreateJobObject(IntPtr.Zero,null);if(owner.job==IntPtr.Zero)throw new Win32Exception();
                    var limits=new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();limits.BasicLimitInformation.LimitFlags=0x2000;
                    int size=Marshal.SizeOf(limits);IntPtr mem=Marshal.AllocHGlobal(size);try{Marshal.StructureToPtr(limits,mem,false);if(!SetInformationJobObject(owner.job,9,mem,(uint)size))throw new Win32Exception();}finally{Marshal.FreeHGlobal(mem);}
                    var sa=new SECURITY_ATTRIBUTES{nLength=Marshal.SizeOf(typeof(SECURITY_ATTRIBUTES)),bInheritHandle=1};
                    if(!CreatePipe(out ir,out iw,ref sa,0)||!CreatePipe(out or,out ow,ref sa,0)||!CreatePipe(out er,out ew,ref sa,0))throw new Win32Exception();
                    if(!SetHandleInformation(iw,1,0)||!SetHandleInformation(or,1,0)||!SetHandleInformation(er,1,0))throw new Win32Exception();
                    var si=new STARTUPINFO{cb=Marshal.SizeOf(typeof(STARTUPINFO)),dwFlags=0x100,hStdInput=ir,hStdOutput=ow,hStdError=ew};
                    if(!CreateProcess(exe,new StringBuilder(AgentTools.Quote(exe)+" "+arguments),IntPtr.Zero,IntPtr.Zero,true,0x08000004,IntPtr.Zero,cwd,ref si,out pi))throw new Win32Exception();
                    owner.process=pi.hProcess;
                    if(!AssignProcessToJobObject(owner.job,pi.hProcess))throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot safely contain the tool in a Windows Job Object.");
                    owner.Output=new StreamReader(new FileStream(new SafeFileHandle(or,true),FileAccess.Read),Encoding.UTF8,true,2048);or=IntPtr.Zero;
                    owner.Error=new StreamReader(new FileStream(new SafeFileHandle(er,true),FileAccess.Read),Encoding.UTF8,true,2048);er=IntPtr.Zero;
                    owner.Input=new StreamWriter(new FileStream(new SafeFileHandle(iw,true),FileAccess.Write),new UTF8Encoding(false));iw=IntPtr.Zero;
                    if(ResumeThread(pi.hThread)==uint.MaxValue)throw new Win32Exception();
                    return owner;
                }catch{if(pi.hProcess!=IntPtr.Zero)TerminateProcess(pi.hProcess,1);owner.Dispose();throw;}
                finally{Close(ir);Close(iw);Close(or);Close(ow);Close(er);Close(ew);Close(pi.hThread);}
            }
        }
        static void Close(IntPtr h){if(h!=IntPtr.Zero)CloseHandle(h);}
        public void Dispose(){Stop();Close(job);job=IntPtr.Zero;Close(process);process=IntPtr.Zero;if(Output!=null)Output.Dispose();if(Error!=null)Error.Dispose();if(Input!=null)try{Input.Dispose();}catch(IOException){}}
        [StructLayout(LayoutKind.Sequential)]struct SECURITY_ATTRIBUTES{public int nLength;public IntPtr lpSecurityDescriptor;public int bInheritHandle;}
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct STARTUPINFO{public int cb;public string lpReserved,lpDesktop,lpTitle;public int dwX,dwY,dwXSize,dwYSize,dwXCountChars,dwYCountChars,dwFillAttribute,dwFlags;public short wShowWindow,cbReserved2;public IntPtr lpReserved2,hStdInput,hStdOutput,hStdError;}
        [StructLayout(LayoutKind.Sequential)]struct PROCESS_INFORMATION{public IntPtr hProcess,hThread;public uint dwProcessId,dwThreadId;}
        [StructLayout(LayoutKind.Sequential)]struct JOBOBJECT_BASIC_LIMIT_INFORMATION{public long PerProcessUserTimeLimit,PerJobUserTimeLimit;public uint LimitFlags;public UIntPtr MinimumWorkingSetSize,MaximumWorkingSetSize;public uint ActiveProcessLimit;public UIntPtr Affinity;public uint PriorityClass,SchedulingClass;}
        [StructLayout(LayoutKind.Sequential)]struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION{public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;public ulong ReadOperationCount,WriteOperationCount,OtherOperationCount,ReadTransferCount,WriteTransferCount,OtherTransferCount;public UIntPtr ProcessMemoryLimit,JobMemoryLimit,PeakProcessMemoryUsed,PeakJobMemoryUsed;}
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr CreateJobObject(IntPtr attributes,string name);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetInformationJobObject(IntPtr job,int info,IntPtr value,uint length);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool TerminateJobObject(IntPtr job,uint code);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool CreatePipe(out IntPtr read,out IntPtr write,ref SECURITY_ATTRIBUTES sa,uint size);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetHandleInformation(IntPtr h,uint mask,uint flags);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true,EntryPoint="CreateProcessW")]static extern bool CreateProcess(string application,StringBuilder command,IntPtr pa,IntPtr ta,bool inherit,uint flags,IntPtr environment,string cwd,ref STARTUPINFO startup,out PROCESS_INFORMATION process);
        [DllImport("kernel32.dll",SetLastError=true)]static extern uint ResumeThread(IntPtr thread);
        [DllImport("kernel32.dll")]static extern bool TerminateProcess(IntPtr process,uint code);
        [DllImport("kernel32.dll")]static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
        [DllImport("kernel32.dll")]static extern bool GetExitCodeProcess(IntPtr process,out uint code);
        [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
    }
}
