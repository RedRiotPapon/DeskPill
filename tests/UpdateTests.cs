using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DeskPill;

class UpdateTests
{
    static int checks;
    static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
    sealed class Handler : HttpMessageHandler
    {
        public int Calls;
        public Func<HttpRequestMessage,HttpResponseMessage> Reply;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Calls++;return Task.FromResult(Reply(request));}
    }
    sealed class CancelStream : MemoryStream
    {
        readonly CancellationTokenSource cancellation;
        public CancelStream(byte[] data,CancellationTokenSource cancellation):base(data){this.cancellation=cancellation;}
        public override Task<int> ReadAsync(byte[] buffer,int offset,int count,CancellationToken token){int read=Read(buffer,offset,Math.Min(count,100));cancellation.Cancel();return Task.FromResult(read);}
    }
    sealed class StalledStream : MemoryStream
    {
        readonly TaskCompletionSource<int> read=new TaskCompletionSource<int>();
        public override Task<int> ReadAsync(byte[] buffer,int offset,int count,CancellationToken token){return read.Task;}
        protected override void Dispose(bool disposing){read.TrySetException(new ObjectDisposedException("stalled body"));base.Dispose(disposing);}
    }
    static bool Reject(Action action){try{action();return false;}catch{return true;}}
    static int Main()
    {
        string directory=Path.Combine(Path.GetTempPath(),"DeskPill-UpdateTests-"+Guid.NewGuid().ToString("N"));
        try{
            byte[] payload=new byte[10000];new Random(42).NextBytes(payload);string hash;
            using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(payload)).Replace("-","").ToLowerInvariant();
            string json="{\"tag_name\":\"v3.1.0\",\"draft\":false,\"prerelease\":false,\"assets\":[{\"name\":\"DeskPill-3.1.0-portable.zip\",\"size\":10000,\"digest\":\"sha256:"+hash+"\",\"browser_download_url\":\"https://github.com/RedRiotPapon/DeskPill/releases/download/v3.1.0/DeskPill-3.1.0-portable.zip\"}]}";
            var release=UpdateService.Parse(json,new Version(3,0,0,0));
            Check(release!=null&&release.Version==new Version(3,1,0),"A newer stable official Windows release is selected");
            Check(UpdateService.Parse(json,new Version(3,1,0,0))==null&&UpdateService.Parse(json,new Version(4,0,0,0))==null,"Equal or older releases never download");
            Check(Reject(()=>UpdateService.Parse(json.Replace("\"prerelease\":false","\"prerelease\":true"),new Version(1,0))),"Prereleases are excluded");
            Check(Reject(()=>UpdateService.Parse(json.Replace("https://github.com/","https://example.org/"),new Version(1,0))),"An external asset URL is rejected");
            Check(Reject(()=>UpdateService.Parse(json.Replace(hash,"missing"),new Version(1,0))),"An asset without a SHA-256 digest is rejected");
            Check(Reject(()=>UpdateService.Parse(json.Replace("10000","999999999"),new Version(1,0))),"Oversized update metadata is rejected");
            Check(Reject(()=>UpdateService.Parse(json.Replace("v3.1.0","../../bad"),new Version(1,0))),"Malformed release tags are rejected");
            var handler=new Handler {Reply=request=>new HttpResponseMessage(HttpStatusCode.OK){Content=request.RequestUri.Host=="api.github.com"?(HttpContent)new StringContent(json):new ByteArrayContent(payload)}};
            using(var client=new HttpClient(handler))using(var service=new UpdateService(client)){
                using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();Check(Reject(()=>service.CheckAsync(new Version(1,0),cancelled.Token).GetAwaiter().GetResult())&&handler.Calls==0,"Cancelled checks do not contact GitHub");}
                Check(service.CheckAsync(new Version(1,0),CancellationToken.None).GetAwaiter().GetResult()!=null,"Release metadata is read through the HTTP boundary");
                int progress=0;string file=service.DownloadAsync(release,directory,value=>progress=value,CancellationToken.None).GetAwaiter().GetResult();
                Check(File.ReadAllBytes(file).Length==10000&&progress==100&&Directory.GetFiles(directory,"*.part").Length==0,"Complete verified updates publish once and remove staging");
                int calls=handler.Calls;service.DownloadAsync(release,directory,delegate{},CancellationToken.None).GetAwaiter().GetResult();
                Check(handler.Calls==calls,"An already verified update is reused without another download");
            }
            var bad=UpdateService.Parse(json.Replace(hash,new string('0',64)),new Version(1,0));
            handler=new Handler {Reply=request=>new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(payload)}};
            using(var client=new HttpClient(handler))using(var service=new UpdateService(client)){
                Check(Reject(()=>service.DownloadAsync(bad,directory,delegate{},CancellationToken.None).GetAwaiter().GetResult()),"Wrong downloaded content never passes verification");
                Check(Directory.GetFiles(directory,"*.part").Length==0&&File.ReadAllBytes(Path.Combine(directory,release.Name))[0]==payload[0],"Verification failure preserves the existing update and cleans staging");
            }
            using(var cancellation=new CancellationTokenSource()){
                handler=new Handler {Reply=request=>new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(new CancelStream(payload,cancellation))}};
                using(var client=new HttpClient(handler))using(var service=new UpdateService(client))Check(Reject(()=>service.DownloadAsync(release,Path.Combine(directory,"cancelled"),delegate{},cancellation.Token).GetAwaiter().GetResult()),"Cancellation interrupts an in-progress update");
                Check(Directory.GetFiles(directory,"*.part",SearchOption.AllDirectories).Length==0&&!File.Exists(Path.Combine(directory,"cancelled",release.Name)),"Cancelled updates leave no partial or published ZIP");
            }
            foreach(bool metadata in new[]{true,false})using(var cancellation=new CancellationTokenSource()){
                handler=new Handler {Reply=request=>new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(new StalledStream())}};
                using(var client=new HttpClient(handler))using(var service=new UpdateService(client)){
                    Task pending=metadata?(Task)service.CheckAsync(new Version(1,0),cancellation.Token):service.DownloadAsync(release,Path.Combine(directory,"stalled"),delegate{},cancellation.Token);
                    cancellation.CancelAfter(100);
                    Check(Task.WhenAny(pending,Task.Delay(2000)).GetAwaiter().GetResult()==pending,"Cancellation aborts a stalled "+(metadata?"metadata":"download")+" body");
                    Check(Reject(()=>pending.GetAwaiter().GetResult()),"Stalled body cancellation does not report success");
                }
            }
            Console.WriteLine("Update tests: "+checks+" passed");return 0;
        }catch(Exception ex){Console.WriteLine("FAIL "+ex.Message);return 1;}
        finally{
            string resolved=Path.GetFullPath(directory),temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!resolved.StartsWith(temp,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(resolved).StartsWith("DeskPill-UpdateTests-"))throw new Exception("Unexpected cleanup path");
            if(Directory.Exists(resolved))Directory.Delete(resolved,true);
        }
    }
}
