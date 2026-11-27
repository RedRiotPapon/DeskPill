using System;
using System.Diagnostics;
using System.Threading;
class AgentFixture {
 static int Main(string[] args){
 if(args.Length>0&&args[0]=="child"){Thread.Sleep(60000);return 0;}
 string prompt=Console.In.ReadToEnd();
 for(int i=0;i<args.Length-1;i++)if(args[i]=="-p"&&args[i+1].StartsWith("fixture argument")){
 if(args[i+1]!="fixture argument \"quoted\" trailing\\ & | >\nবাংলা")return 3;prompt=args[i+1];}
 if(prompt=="slow")Thread.Sleep(450);
 if(prompt=="hold"){Thread.Sleep(60000);return 0;}
 if(prompt=="tree"||prompt=="treeexit"){
 var p=Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,"child"){UseShellExecute=false,CreateNoWindow=true});
 Console.WriteLine("{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\",\"text\":\"childpid="+p.Id+"\"}}");
 if(prompt=="tree")Thread.Sleep(60000);return 0;
 }
 if(prompt=="fail"){Console.Error.WriteLine("Fixture authentication failed");return 2;}
 if(prompt=="flood"){Console.Write(new string('x',2000000));Console.WriteLine();Console.Error.WriteLine(new string('e',2000000));}
 Console.WriteLine("{\"type\":\"thread.started\",\"thread_id\":\"12345678-1234-1234-1234-123456789abc\"}");
 Console.WriteLine("{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\",\"text\":\"fixture answer\"}}");return 0;}
}
