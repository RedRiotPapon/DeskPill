using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace DeskPill
{
    public sealed class AgentLaunchPlan
    {
        public string Executable { get; private set; }
        public string PrefixArguments { get; private set; }
        public string Script { get; private set; }
        internal AgentLaunchPlan(string executable,string script) { Executable=executable;Script=script;PrefixArguments=script.Length==0?"":AgentTools.Quote(script)+" "; }
    }
    // Never interprets a batch file. Only package metadata from these known CLI packages is accepted.
    public static class AgentLaunch
    {
        public static AgentLaunchPlan Resolve(AgentProfile profile)
        {
            string path=profile.Executable ?? "";
            if(!Absolute(path)||!File.Exists(path))throw new ArgumentException("Choose an existing native .exe or a recognized npm CLI .cmd.");
            if(string.Equals(Path.GetExtension(path),".exe",StringComparison.OrdinalIgnoreCase))return new AgentLaunchPlan(Path.GetFullPath(path),"");
            string command=profile.Provider=="Codex CLI"?"codex":profile.Provider=="Claude Code"?"claude":profile.Provider=="Gemini CLI"?"gemini":"";
            if(command.Length==0||!string.Equals(Path.GetFileName(path),command+".cmd",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("This script is not a recognized npm CLI installation. Choose its native .exe or install the official CLI separately.");
            string package=command=="codex"?"@openai/codex":command=="claude"?"@anthropic-ai/claude-code":"@google/gemini-cli";
            string bin=command=="codex"?"bin/codex.js":command=="claude"?"cli.js":"dist/index.js";
            string root=Path.Combine(Path.GetDirectoryName(path),"node_modules",package.Replace('/',Path.DirectorySeparatorChar));
            string metadata=Path.Combine(root,"package.json"), script=Path.Combine(root,bin.Replace('/',Path.DirectorySeparatorChar));
            if(!File.Exists(metadata)||!File.Exists(script)||new FileInfo(metadata).Length>1048576)throw new ArgumentException("The official npm package or CLI entry point is missing beside this shim.");
            var data=new JavaScriptSerializer{MaxJsonLength=1048576}.DeserializeObject(File.ReadAllText(metadata)) as IDictionary<string,object>;
            object name,entry; if(data==null||!data.TryGetValue("name",out name)||!object.Equals(name,package)||!data.TryGetValue("bin",out entry))throw new ArgumentException("The npm package metadata does not match this provider.");
            string target=entry as string; var bins=entry as IDictionary<string,object>;object value;if(bins!=null&&bins.TryGetValue(command,out value))target=value as string;
            if((target??"").Replace('\\','/')!=bin)throw new ArgumentException("The npm package has an unrecognized CLI entry point.");
            var dirs=new List<string>();dirs.Add(Path.GetDirectoryName(path));dirs.AddRange((Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator));
            foreach(string dir in dirs)try{if(string.IsNullOrWhiteSpace(dir))continue;string node=Path.GetFullPath(Path.Combine(dir.Trim().Trim('"'),"node.exe"));if(File.Exists(node))return new AgentLaunchPlan(node,Path.GetFullPath(script));}catch(ArgumentException){}catch(NotSupportedException){}
            throw new ArgumentException("Node.js node.exe was not found. Install Node.js separately or choose a native CLI.");
        }
        static bool Absolute(string path) { return !string.IsNullOrWhiteSpace(path)&&Path.IsPathRooted(path)&&((path.Length>=3&&char.IsLetter(path[0])&&path[1]==':'&&(path[2]=='\\'||path[2]=='/'))||path.StartsWith("\\\\",StringComparison.Ordinal)); }
    }
}
