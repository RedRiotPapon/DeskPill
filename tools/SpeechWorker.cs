// DeskPill's isolated speech worker. Vosk is loaded only in this short-lived process.
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
using System.Web.Script.Serialization;

class SpeechWorker
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll")]
    static extern bool SetDefaultDllDirectories(uint flags);
    [DllImport("libvosk.dll", CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr vosk_model_new(byte[] path);
    [DllImport("libvosk.dll", CallingConvention = CallingConvention.Cdecl)]
    static extern void vosk_model_free(IntPtr model);
    [DllImport("libvosk.dll", CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr vosk_recognizer_new(IntPtr model, float rate);
    [DllImport("libvosk.dll", CallingConvention = CallingConvention.Cdecl)]
    static extern void vosk_recognizer_free(IntPtr recognizer);
    [DllImport("libvosk.dll", CallingConvention = CallingConvention.Cdecl)]
    static extern int vosk_recognizer_accept_waveform(IntPtr recognizer, byte[] data, int size);
    [DllImport("libvosk.dll", CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr vosk_recognizer_result(IntPtr recognizer);
    [DllImport("libvosk.dll", CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr vosk_recognizer_final_result(IntPtr recognizer);
    [DllImport("libvosk.dll", CallingConvention = CallingConvention.Cdecl)]
    static extern void vosk_set_log_level(int level);
    static string Text(IntPtr json)
    {
        var parsed = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(Marshal.PtrToStringAnsi(json));
        object value;
        return parsed.TryGetValue("text", out value) ? Convert.ToString(value) : "";
    }

    static int Main(string[] args)
    {
        if((args.Length==3||args.Length==4)&&args[0]=="--parakeet")return ParakeetWorker.Run(args[1],args[2],args.Length==4?args[3]:"CPU");
        IntPtr model = IntPtr.Zero, recognizer = IntPtr.Zero;
        try
        {
            if (args.Length != 2 || !SetDefaultDllDirectories(0x1000) || LoadLibraryEx(Path.Combine(Path.GetFullPath(args[0]), "libvosk.dll"), IntPtr.Zero, 0x1100) == IntPtr.Zero)
                return 2;
            byte[] wav;
            using (var memory = new MemoryStream())
            using (var input = Console.OpenStandardInput())
            {
                var buffer = new byte[8192];
                int count;
                while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (memory.Length + count > 2880044)
                        return 2;
                    memory.Write(buffer, 0, count);
                }

                wav = memory.ToArray();
            }

            if (wav.Length < 44 || Encoding.ASCII.GetString(wav, 0, 4) != "RIFF" || BitConverter.ToInt32(wav, 24) != 16000 || BitConverter.ToInt16(wav, 22) != 1 || BitConverter.ToInt16(wav, 34) != 16)
                return 2;
            vosk_set_log_level(-1);
            model = vosk_model_new(Encoding.UTF8.GetBytes(Path.GetFullPath(args[1]) + "\0"));
            if (model == IntPtr.Zero)
                return 3;
            recognizer = vosk_recognizer_new(model, 16000);
            if (recognizer == IntPtr.Zero)
                return 3;
            var result = new List<string>();
            for (int offset = 44; offset < wav.Length;)
            {
                int count = Math.Min(8000, wav.Length - offset);
                var chunk = new byte[count];
                Buffer.BlockCopy(wav, offset, chunk, 0, count);
                offset += count;
                if (vosk_recognizer_accept_waveform(recognizer, chunk, count) != 0)
                    result.Add(Text(vosk_recognizer_result(recognizer)));
            }

            result.Add(Text(vosk_recognizer_final_result(recognizer)));
            Console.OutputEncoding = new UTF8Encoding(false);
            Console.Write(string.Join(" ", result).Trim());
            return 0;
        }
        catch
        {
            Console.Error.Write("Speech recognition failed.");
            return 1;
        }
        finally
        {
            if (recognizer != IntPtr.Zero)
                vosk_recognizer_free(recognizer);
            if (model != IntPtr.Zero)
                vosk_model_free(model);
        }
    }
}
