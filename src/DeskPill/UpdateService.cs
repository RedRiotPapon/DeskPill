using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace DeskPill
{
    public sealed class UpdateRelease
    {
        public string Tag,Name,Url,Hash;
        public long Bytes;
        public Version Version;
    }
    public sealed class UpdateService : IDisposable
    {
        const string Repository="https://github.com/RedRiotPapon/DeskPill";
        readonly HttpClient client;
        readonly bool ownsClient;
        public UpdateService(HttpClient client=null)
        {
            ownsClient=client==null;this.client=client??new HttpClient {Timeout=TimeSpan.FromMinutes(5)};
        }
        static string Value(Dictionary<string,object> data,string key){object value;return data.TryGetValue(key,out value)?Convert.ToString(value):"";}
        static Version Normal(Version value){return new Version(value.Major,value.Minor,Math.Max(0,value.Build),Math.Max(0,value.Revision));}
        public static UpdateRelease Parse(string json,Version installed)
        {
            if(json==null||json.Length>262144)throw new InvalidDataException("The release response is too large.");
            var data=new JavaScriptSerializer {MaxJsonLength=262144}.DeserializeObject(json) as Dictionary<string,object>;
            if(data==null)throw new InvalidDataException("GitHub returned an unreadable release.");
            if(Value(data,"draft")=="True"||Value(data,"prerelease")=="True")throw new InvalidDataException("This is not a stable release.");
            string tag=Value(data,"tag_name");Version version;
            if(!System.Text.RegularExpressions.Regex.IsMatch(tag,@"^v?\d{1,6}\.\d{1,6}\.\d{1,6}$")||!Version.TryParse(tag.TrimStart('v'),out version))throw new InvalidDataException("The release version is invalid.");
            if(Normal(version)<=Normal(installed))return null;
            string expected="DeskPill-"+version.ToString(3)+"-portable.zip";
            object raw;var assets=data.TryGetValue("assets",out raw)?raw as object[]:null;
            var asset=assets==null?null:assets.OfType<Dictionary<string,object>>().SingleOrDefault(a=>Value(a,"name")==expected);
            if(asset==null)throw new InvalidDataException("The Windows portable download is not ready. Try again later.");
            long bytes;if(!long.TryParse(Value(asset,"size"),out bytes))throw new InvalidDataException("The download size is missing.");
            var release=new UpdateRelease {Tag=tag,Version=version,Name=expected,Url=Value(asset,"browser_download_url"),Hash=Value(asset,"digest"),Bytes=bytes};
            Validate(release);return release;
        }
        static void Validate(UpdateRelease release)
        {
            if(release==null||release.Version==null||release.Name!="DeskPill-"+release.Version.ToString(3)+"-portable.zip"||
                release.Url!=Repository+"/releases/download/"+release.Tag+"/"+release.Name||
                release.Bytes<10000||release.Bytes>50*1024*1024||
                !System.Text.RegularExpressions.Regex.IsMatch(release.Hash??"",@"^sha256:[a-fA-F0-9]{64}$"))
                throw new InvalidDataException("This release cannot be verified. Open the releases page or try again later.");
        }
        public async Task<UpdateRelease> CheckAsync(Version installed,CancellationToken token)
        {
            using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(token)){deadline.CancelAfter(TimeSpan.FromSeconds(25));return await CheckCoreAsync(installed,deadline.Token);}
        }
        async Task<UpdateRelease> CheckCoreAsync(Version installed,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            try{
            using(var request=new HttpRequestMessage(HttpMethod.Get,"https://api.github.com/repos/RedRiotPapon/DeskPill/releases/latest")){
                request.Headers.UserAgent.ParseAdd("DeskPill/"+installed.ToString());request.Headers.Accept.ParseAdd("application/vnd.github+json");
                using(var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token)){
                    using(var abort=token.Register(()=>response.Dispose())){
                    response.EnsureSuccessStatusCode();
                    using(var stream=await response.Content.ReadAsStreamAsync())using(var memory=new MemoryStream()){
                        var buffer=new byte[8192];int count;
                        while((count=await stream.ReadAsync(buffer,0,buffer.Length,token))>0){if(memory.Length+count>262144)throw new InvalidDataException("The release response is too large.");memory.Write(buffer,0,count);}
                        return Parse(Encoding.UTF8.GetString(memory.ToArray()),installed);
                    }
                    }
                }
            }
            }catch{token.ThrowIfCancellationRequested();throw;}
        }
        static bool Matches(string path,UpdateRelease release)
        {
            if(!File.Exists(path)||new FileInfo(path).Length!=release.Bytes)return false;
            using(var stream=File.OpenRead(path))using(var sha=SHA256.Create())return "sha256:"+BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant()==release.Hash.ToLowerInvariant();
        }
        public async Task<string> DownloadAsync(UpdateRelease release,string directory,Action<int> progress,CancellationToken token)
        {
            using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(token)){deadline.CancelAfter(TimeSpan.FromMinutes(5));return await DownloadCoreAsync(release,directory,progress,deadline.Token);}
        }
        async Task<string> DownloadCoreAsync(UpdateRelease release,string directory,Action<int> progress,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();Validate(release);
            string target=Path.Combine(Path.GetFullPath(directory),release.Name);
            if(await Task.Run(()=>Matches(target,release),token)){token.ThrowIfCancellationRequested();progress(100);return target;}
            Directory.CreateDirectory(directory);
            string stage=Path.Combine(Path.GetFullPath(directory),"download-"+Guid.NewGuid().ToString("N")+".part");
            try{
                using(var response=await client.GetAsync(release.Url,HttpCompletionOption.ResponseHeadersRead,token))using(var abort=token.Register(()=>response.Dispose())){
                    response.EnsureSuccessStatusCode();
                    if(response.Content.Headers.ContentLength.HasValue&&response.Content.Headers.ContentLength.Value!=release.Bytes)throw new InvalidDataException("The download size changed. Please check again.");
                    using(var input=await response.Content.ReadAsStreamAsync())using(var output=new FileStream(stage,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true)){
                        byte[] buffer=new byte[81920];long total=0;int count;
                        while((count=await input.ReadAsync(buffer,0,buffer.Length,token))>0){total+=count;if(total>release.Bytes)throw new InvalidDataException("The download exceeded its expected size.");await output.WriteAsync(buffer,0,count,token);progress((int)(total*95/release.Bytes));}
                        if(total!=release.Bytes)throw new InvalidDataException("The download was incomplete. Please try again.");
                    }
                }
                if(!await Task.Run(()=>Matches(stage,release),token))throw new InvalidDataException("The download didn't pass verification. Please check again.");
                token.ThrowIfCancellationRequested();
                if(File.Exists(target))File.Replace(stage,target,null);else File.Move(stage,target);
                progress(100);return target;
            }catch{token.ThrowIfCancellationRequested();throw;}
            finally{if(File.Exists(stage))File.Delete(stage);}
        }
        public void Dispose(){if(ownsClient)client.Dispose();}
    }
}
