using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DeskPill
{
    public static class SpeechModels
    {
        public static string Normalize(string model){return model=="Parakeet"?"Parakeet":"Vosk";}
        public static string NormalizeBackend(string backend){return backend=="CPU"||backend=="GPU"?backend:"Auto";}
        public static string Name(string model){return Normalize(model)=="Parakeet"?"Parakeet Unified English":"Vosk Small English";}
        public static bool IsReady(string model){return Normalize(model)=="Parakeet"?ParakeetAssets.Ready:SpeechAssets.Ready;}
        public static Task InstallAsync(string model,Action<string> progress,CancellationToken token){return Normalize(model)=="Parakeet"?ParakeetAssets.InstallAsync(progress,token):SpeechAssets.InstallAsync(progress,token,true);}
    }
    public static class ParakeetAssets
    {
        public const string Version="0.5.1";
        public const string RuntimeUrl="https://github.com/handy-computer/transcribe.cpp/releases/download/v0.5.1/transcribe-native-0.5.1-windows-x86_64-cpu-vulkan.tar.gz";
        public const string RuntimeHash="166c38a7f65bed1ee2d6e7b688e7e99da3092469e185ecb1edd618231164bf86";
        public const string ModelUrl="https://huggingface.co/handy-computer/parakeet-unified-en-0.6b-gguf/resolve/7e948f21b7bdbac698d3318db9d350f1096f3b6c/parakeet-unified-en-0.6b-Q8_0.gguf";
        public const string ModelHash="4b50b6dd862bf6e346929aaf4f5eaacec003bfa3f56462d6c874b41ef2f38795";
        public const long ModelBytes=731357568;
        public static string Root {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DeskPill","speech","parakeet-unified-1");}}
        public static string Runtime {get{return Path.Combine(Root,"runtime","transcribe-native-windows-x86_64-cpu-vulkan");}}
        public static string Model {get{return Path.Combine(Root,"model.gguf");}}
        public static bool Ready {get{try{return File.Exists(Path.Combine(Runtime,"transcribe.dll"))&&File.Exists(Path.Combine(Runtime,"ggml-base.dll"))&&File.Exists(Model)&&new FileInfo(Model).Length==ModelBytes&&File.ReadAllText(Path.Combine(Root,"verified.txt"))==RuntimeHash+"\n"+ModelHash;}catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}}}
        static readonly SemaphoreSlim installLock=new SemaphoreSlim(1,1);
        public static async Task InstallAsync(Action<string> progress,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if(!Environment.Is64BitOperatingSystem||Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE")=="ARM64"||Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432")=="ARM64")throw new InvalidOperationException("This Parakeet pack requires Windows x64 (Intel or AMD). Use Vosk on supported x64 emulation instead.");
            if(!await installLock.WaitAsync(0))throw new InvalidOperationException("Parakeet installation is already running.");
            string parent=Path.GetDirectoryName(Root),stage=Path.Combine(parent,"install-parakeet-"+Guid.NewGuid().ToString("N")),backup=Root+".old-"+Guid.NewGuid().ToString("N");
            try{
                Directory.CreateDirectory(stage);
                string archive=Path.Combine(stage,"runtime.tar.gz"),model=Path.Combine(stage,"model.gguf");
                await Download(RuntimeUrl,archive,17824610,"runtime (18 MB)",progress,token);
                await Verify(archive,RuntimeHash,token);progress("Unpacking Parakeet runtime…");
                await Task.Run(delegate{ExtractRuntime(archive,Path.Combine(stage,"runtime"),token);},token);File.Delete(archive);
                await Download(ModelUrl,model,ModelBytes,"Parakeet model (731 MB)",progress,token);
                progress("Verifying Parakeet model…");await Verify(model,ModelHash,token);
                File.WriteAllText(Path.Combine(stage,"MODEL-NOTICE.txt"),"Parakeet Unified English 0.6B by NVIDIA. GGUF Q8_0 conversion by handy-computer.\nSource: "+ModelUrl+"\nCC BY 4.0: https://creativecommons.org/licenses/by/4.0/\nConversion repackages and quantizes the upstream weights; DeskPill has not retrained them.\nNative runtime: transcribe.cpp 0.5.1 (MIT); dependency licenses are under runtime/licenses.\n");
                File.WriteAllText(Path.Combine(stage,"verified.txt"),RuntimeHash+"\n"+ModelHash);token.ThrowIfCancellationRequested();
                bool replaced=false;
                try{if(Directory.Exists(Root))Directory.Move(Root,backup);Directory.Move(stage,Root);replaced=true;}
                finally{if(!replaced&&Directory.Exists(backup)&&!Directory.Exists(Root))Directory.Move(backup,Root);}
                if(Directory.Exists(backup))DeleteOwned(backup,parent,"parakeet-unified-1.old-");
                progress("Parakeet installed. Select it and Save to use it.");
            }finally{try{if(Directory.Exists(stage))DeleteOwned(stage,parent,"install-parakeet-");}finally{installLock.Release();}}
        }
        static void DeleteOwned(string directory,string parent,string prefix)
        {
            string path=Path.GetFullPath(directory);if(Path.GetDirectoryName(path)!=Path.GetFullPath(parent)||!Path.GetFileName(path).StartsWith(prefix,StringComparison.Ordinal))throw new IOException("Unexpected speech cleanup path.");Directory.Delete(path,true);
        }
        static Task Verify(string path,string hash,CancellationToken token){return Task.Run(delegate{token.ThrowIfCancellationRequested();if(!SpeechAssets.HashMatches(path,hash))throw new InvalidDataException("Speech download checksum did not match. Retry the download.");token.ThrowIfCancellationRequested();},token);}
        static async Task Download(string url,string path,long expected,string label,Action<string> progress,CancellationToken token)
        {
            using(var client=new System.Net.Http.HttpClient()){
                client.Timeout=TimeSpan.FromMinutes(30);
                using(var response=await client.GetAsync(url,System.Net.Http.HttpCompletionOption.ResponseHeadersRead,token)){
                    response.EnsureSuccessStatusCode();if(response.Content.Headers.ContentLength.HasValue&&response.Content.Headers.ContentLength.Value!=expected)throw new InvalidDataException("Unexpected speech download size.");
                    using(var input=await response.Content.ReadAsStreamAsync())using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true)){
                        var buffer=new byte[81920];long total=0;int count,last=-1;
                        while((count=await input.ReadAsync(buffer,0,buffer.Length,token))>0){total+=count;if(total>expected)throw new InvalidDataException("Speech download exceeds expected size.");await output.WriteAsync(buffer,0,count,token);int percent=(int)(total*100/expected);if(percent!=last){last=percent;progress("Downloading "+label+" · "+percent+"%");}}
                        if(total!=expected)throw new InvalidDataException("Speech download was incomplete.");
                    }
                }
            }
        }
        // Minimal regular-file tar reader: pinned archives, bounded expansion, no links or path escapes.
        public static void ExtractRuntime(string archive,string directory,CancellationToken token)
        {
            string root=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;long expanded=0;int entries=0;
            using(var file=File.OpenRead(archive))using(var zip=new GZipStream(file,CompressionMode.Decompress)){
                var header=new byte[512];
                while(ReadBlock(zip,header,512)){
                    token.ThrowIfCancellationRequested();if(header[0]==0)break;if(++entries>256)throw new InvalidDataException("Too many runtime entries.");
                    string name=Field(header,0,100),prefix=Field(header,345,155);if(prefix.Length>0)name=prefix+"/"+name;
                    long size;try{size=Convert.ToInt64(Field(header,124,12).Trim(),8);}catch(Exception){throw new InvalidDataException("Invalid runtime entry size.");}
                    if(size<0||size>80000000||(expanded+=size)>160000000)throw new InvalidDataException("Runtime archive exceeds size limit.");
                    string path=Path.GetFullPath(Path.Combine(root,name.Replace('/',Path.DirectorySeparatorChar)));
                    if(name.Contains("\\")||Path.IsPathRooted(name)||!path.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Runtime archive contains an unsafe path.");
                    byte kind=header[156];if(kind!=0&&kind!=(byte)'0'&&kind!=(byte)'5')throw new InvalidDataException("Runtime archive links and special files are unsupported.");
                    if(kind==(byte)'5'){Directory.CreateDirectory(path);if(size!=0)throw new InvalidDataException("Invalid directory entry.");continue;}
                    Directory.CreateDirectory(Path.GetDirectoryName(path));using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)){CopyExactly(zip,output,size,token);}
                    CopyExactly(zip,Stream.Null,(512-size%512)%512,token);
                }
            }
        }
        static string Field(byte[] data,int offset,int count){return Encoding.ASCII.GetString(data,offset,count).TrimEnd('\0',' ');}
        static bool ReadBlock(Stream stream,byte[] buffer,int count){int done=0,n;while(done<count&&(n=stream.Read(buffer,done,count-done))>0)done+=n;if(done==0)return false;if(done!=count)throw new InvalidDataException("Truncated runtime archive.");return true;}
        static void CopyExactly(Stream input,Stream output,long size,CancellationToken token){var buffer=new byte[81920];while(size>0){token.ThrowIfCancellationRequested();int n=input.Read(buffer,0,(int)Math.Min(size,buffer.Length));if(n==0)throw new InvalidDataException("Truncated runtime entry.");output.Write(buffer,0,n);size-=n;}}
    }
}
