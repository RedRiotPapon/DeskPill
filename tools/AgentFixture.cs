using System;
using System.Diagnostics;
using System.Threading;
class AgentFixture {
 static int Main(string[] args){
 if(args.Length>0&&args[0]=="child"){Thread.Sleep(60000);return 0;}
 string prompt=Console.In.ReadToEnd();
 for(int i=0;i<args.Length-1;i++)if(args[i]=="-p"&&args[i+1].StartsWith("fixture argument")){
 if(args[i+1]!="fixture argument \"quoted\" trailing\\ & | >\nবাংলা")return 3;prompt=args[i+1];}
 if(prompt=="slow"){
 Console.WriteLine("{\"type\":\"item.started\",\"item\":{\"type\":\"command_execution\",\"command\":\"fixture checking files\"}}");
 Thread.Sleep(450);}
 if(prompt.EndsWith("check-image",StringComparison.Ordinal)){
 if(!prompt.Contains("Use the attached image"))return 7;
 bool found=false;for(int i=0;i<args.Length-1;i++)if(args[i]=="--image"&&System.IO.File.Exists(args[i+1]))found=true;
 if(!found)return 6;
 }
 if(prompt=="hold"){Thread.Sleep(60000);return 0;}
 if(prompt=="tree"||prompt=="treeexit"){
 var p=Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,"child"){UseShellExecute=false,CreateNoWindow=true});
 Console.WriteLine("{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\",\"text\":\"childpid="+p.Id+"\"}}");
 if(prompt=="tree")Thread.Sleep(60000);return 0;
 }
 if(prompt=="diagnostic-flood")for(int n=0;n<100;n++)Console.Error.WriteLine(new string('d',1000));
 if(prompt=="unterminated-diagnostic")Console.Error.Write("Tool diagnostic without newline");
 if(prompt=="mcp-warning")Console.Error.WriteLine("ERROR rmcp::transport::worker: AuthRequired invalid_token for optional connector");
 if(prompt=="fail"){Console.Error.WriteLine("Fixture authentication failed");return 2;}
 if(prompt=="flood"){Console.Write(new string('x',2000000));Console.WriteLine();Console.Error.WriteLine(new string('e',2000000));}
 Console.WriteLine("{\"type\":\"thread.started\",\"thread_id\":\"12345678-1234-1234-1234-123456789abc\"}");
 Console.WriteLine("{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\",\"text\":\"fixture answer\"}}");return 0;}
}
