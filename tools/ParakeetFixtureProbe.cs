using System;
using System.Diagnostics;
using System.IO;
using System.Speech.Synthesis;
using System.Speech.AudioFormat;
using System.Threading.Tasks;
using DeskPill;
class ParakeetFixtureProbe
{
 [STAThread]static int Main(string[] args){try{
  string runtime=args.Length>0?args[0]:ParakeetAssets.Runtime,model=args.Length>1?args[1]:ParakeetAssets.Model;
  if(!SpeechAssets.HashMatches(model,ParakeetAssets.ModelHash))throw new Exception("Model checksum mismatch");
  byte[] pcm=null;using(var wave=new MemoryStream()){
   using(var synth=new SpeechSynthesizer()){synth.SelectVoiceByHints(VoiceGender.NotSet,VoiceAge.NotSet,0,new System.Globalization.CultureInfo("en-US"));synth.SetOutputToAudioStream(wave,new SpeechAudioFormatInfo(16000,AudioBitsPerSample.Sixteen,AudioChannel.Mono));synth.Speak("Hello, this is a test of offline English speech recognition. Please remember to drink water today.");synth.SetOutputToNull();}
   pcm=wave.ToArray();
  }
  if(pcm==null)throw new Exception("No generated PCM");var watch=Stopwatch.StartNew();
  using(var child=new Process{StartInfo=new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"DeskPill.Speech.exe"),"--parakeet \""+runtime+"\" \""+model+"\" "+(args.Length>2?args[2]:"CPU")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=System.Text.Encoding.UTF8,StandardErrorEncoding=System.Text.Encoding.UTF8}}){
   if(args.Length>3&&args[3]=="cpu-only")child.StartInfo.EnvironmentVariables["TRANSCRIBE_BACKENDS"]="cpu";
   child.Start();var output=child.StandardOutput.ReadToEndAsync();var error=child.StandardError.ReadToEndAsync();byte[] audio=DictationAudio.Wave(pcm);child.StandardInput.BaseStream.Write(audio,0,audio.Length);child.StandardInput.Close();long peak=0;
   while(!child.WaitForExit(100)){child.Refresh();peak=Math.Max(peak,child.PeakWorkingSet64);if(watch.Elapsed.TotalSeconds>180){child.Kill();throw new Exception("Fixture timeout");}}
   string text=output.GetAwaiter().GetResult(),diagnostic=error.GetAwaiter().GetResult();if(child.ExitCode!=0){if(args.Length>3&&args[3]=="cpu-only"&&args[2]=="GPU"&&diagnostic.Contains("No compatible Vulkan GPU")){Console.WriteLine("PASS Forced GPU reports missing Vulkan support without silently using CPU.");return 0;}throw new Exception("Worker failed: "+diagnostic);}
   Console.WriteLine("Transcript: "+text);foreach(string line in diagnostic.Split('\n'))if(line.StartsWith("DeskPill speech device:"))Console.WriteLine(line.Trim());if(text.IndexOf("hello",StringComparison.OrdinalIgnoreCase)<0||text.IndexOf("water",StringComparison.OrdinalIgnoreCase)<0)throw new Exception("Generated fixture words missing");
   Console.WriteLine("PASS Parakeet worker: "+watch.Elapsed.TotalSeconds.ToString("0.00")+" seconds; peak sampled working set "+(peak/1024/1024)+" MiB; audio "+(pcm.Length/32000.0).ToString("0.00")+" seconds. No microphone, clipboard or settings changes.");
  }return 0;
 }catch(Exception e){Console.WriteLine("FAIL "+e.Message);return 1;}}
}
