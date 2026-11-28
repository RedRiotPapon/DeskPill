using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

// Original DeskPill adapter for transcribe.cpp 0.5.1. CPU/Vulkan inference lives only in this worker.
internal static class ParakeetWorker
{
    const CallingConvention C=CallingConvention.Cdecl;
    [StructLayout(LayoutKind.Sequential)]struct BackendParams{public ulong Size;public IntPtr Directory;public uint Mask;}
    [StructLayout(LayoutKind.Sequential)]struct ModelParams{public ulong Size;public int Backend;public IntPtr Device;[MarshalAs(UnmanagedType.I1)]public bool NoMap;}
    [StructLayout(LayoutKind.Sequential)]struct SessionParams{public ulong Size;public int Threads,Kv,Context;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr LoadLibraryEx(string file,IntPtr unused,uint flags);
    [DllImport("kernel32.dll")]static extern bool SetDefaultDllDirectories(uint flags);
    [DllImport("transcribe.dll",CallingConvention=C)]static extern IntPtr transcribe_version();
    [DllImport("transcribe.dll",CallingConvention=C)]static extern void transcribe_backend_init_params_init(ref BackendParams p);
    [DllImport("transcribe.dll",CallingConvention=C)]static extern int transcribe_init_backends_ex(ref BackendParams p);
    [DllImport("transcribe.dll",CallingConvention=C)]static extern void transcribe_model_load_params_init(ref ModelParams p);
    [DllImport("transcribe.dll",CallingConvention=C)]static extern void transcribe_session_params_init(ref SessionParams p);
    [DllImport("transcribe.dll",CallingConvention=C)]static extern int transcribe_model_load_file(byte[] path,ref ModelParams p,out IntPtr model);
    [DllImport("transcribe.dll",CallingConvention=C)]static extern int transcribe_session_init(IntPtr model,ref SessionParams p,out IntPtr session);
    [DllImport("transcribe.dll",CallingConvention=C)]static extern int transcribe_run(IntPtr session,float[] pcm,int count,IntPtr p);
    [DllImport("transcribe.dll",CallingConvention=C)]static extern IntPtr transcribe_full_text(IntPtr session);
    [DllImport("transcribe.dll",CallingConvention=C)]static extern void transcribe_session_free(IntPtr session);
    [DllImport("transcribe.dll",CallingConvention=C)]static extern void transcribe_model_free(IntPtr model);
    static void Check(int code,string stage){if(code!=0)throw new InvalidOperationException(stage+" failed (native status "+code+").");}
    static string Utf8(IntPtr text){if(text==IntPtr.Zero)return "";int count=0;while(count<65536&&Marshal.ReadByte(text,count)!=0)count++;if(count==65536)throw new InvalidDataException("Speech result exceeds limit.");byte[] data=new byte[count];Marshal.Copy(text,data,0,count);return Encoding.UTF8.GetString(data);}
    [DllImport("transcribe.dll",CallingConvention=C)][return:MarshalAs(UnmanagedType.I1)]static extern bool transcribe_backend_available(int kind);
    [DllImport("transcribe.dll",CallingConvention=C)]static extern IntPtr transcribe_model_backend(IntPtr model);
    public static int Run(string runtime,string modelPath,string backendChoice)
    {
        IntPtr model=IntPtr.Zero,session=IntPtr.Zero,dir=IntPtr.Zero;
        try{
            byte[] wav;using(var input=Console.OpenStandardInput())using(var data=new MemoryStream()){
                byte[] buffer=new byte[8192];int n;while((n=input.Read(buffer,0,buffer.Length))>0){if(data.Length+n>2880044)throw new InvalidDataException("Recording exceeds 90 seconds.");data.Write(buffer,0,n);}wav=data.ToArray();
            }
            if(wav.Length<44||(wav.Length-44)%2!=0||Encoding.ASCII.GetString(wav,0,4)!="RIFF"||Encoding.ASCII.GetString(wav,8,4)!="WAVE"||BitConverter.ToInt16(wav,20)!=1||BitConverter.ToInt16(wav,22)!=1||BitConverter.ToInt32(wav,24)!=16000||BitConverter.ToInt16(wav,34)!=16||Encoding.ASCII.GetString(wav,36,4)!="data"||BitConverter.ToInt32(wav,40)!=wav.Length-44)throw new InvalidDataException("Expected mono 16 kHz PCM WAV.");
            if(!SetDefaultDllDirectories(0x1000)||LoadLibraryEx(Path.Combine(Path.GetFullPath(runtime),"transcribe.dll"),IntPtr.Zero,0x1100)==IntPtr.Zero)throw new InvalidOperationException("Parakeet runtime could not load. Install the Microsoft Visual C++ 2015–2022 x64 runtime, or reinstall this speech pack.");
            if(Utf8(transcribe_version())!="0.5.1")throw new InvalidOperationException("Parakeet runtime version mismatch.");
            var backend=new BackendParams();transcribe_backend_init_params_init(ref backend);dir=Utf8Path(runtime);
            backend.Directory=dir;backend.Mask=backendChoice=="CPU"?1u:5u;Check(transcribe_init_backends_ex(ref backend),"Device initialization");
            bool gpu=backendChoice!="CPU"&&transcribe_backend_available(3);
            if(backendChoice=="GPU"&&!gpu)throw new InvalidOperationException("No compatible Vulkan GPU is available. Update your graphics driver or choose Automatic/CPU in Dictation settings.");
            var parameters=new ModelParams();transcribe_model_load_params_init(ref parameters);parameters.Backend=gpu?3:1;
            Check(transcribe_model_load_file(Encoding.UTF8.GetBytes(Path.GetFullPath(modelPath)+"\0"),ref parameters,out model),"Model load");
            Console.Error.WriteLine("DeskPill speech device: "+Utf8(transcribe_model_backend(model)));
            var settings=new SessionParams();transcribe_session_params_init(ref settings);settings.Threads=Math.Max(1,Math.Min(4,Environment.ProcessorCount-1));
            Check(transcribe_session_init(model,ref settings,out session),"Speech session");
            var samples=new float[(wav.Length-44)/2];for(int i=0;i<samples.Length;i++)samples[i]=BitConverter.ToInt16(wav,44+i*2)/32768f;
            Check(transcribe_run(session,samples,samples.Length,IntPtr.Zero),"Transcription");
            Console.OutputEncoding=new UTF8Encoding(false);Console.Write(Utf8(transcribe_full_text(session)).Trim());return 0;
        }catch(Exception e){Console.Error.Write(e.Message);return 1;}
        finally{if(session!=IntPtr.Zero)transcribe_session_free(session);if(model!=IntPtr.Zero)transcribe_model_free(model);if(dir!=IntPtr.Zero)Marshal.FreeCoTaskMem(dir);}
    }
    static IntPtr Utf8Path(string path){byte[] text=Encoding.UTF8.GetBytes(Path.GetFullPath(path)+"\0");var memory=Marshal.AllocCoTaskMem(text.Length);Marshal.Copy(text,0,memory,text.Length);return memory;}
}
