using System;
using System.IO;
using System.Speech.Synthesis;
using System.Speech.AudioFormat;
using System.Threading;
using DeskPill;
class SpeechFixtureProbe {
 static int Main(string[] args){try{
 System.Net.ServicePointManager.SecurityProtocol=System.Net.SecurityProtocolType.Tls12;
 if(!SpeechAssets.Ready){if(Array.IndexOf(args,"--install")<0)throw new Exception("Install English dictation in DeskPill Settings first, or run this probe with --install.");SpeechAssets.InstallAsync(Console.WriteLine,CancellationToken.None).GetAwaiter().GetResult();}
 var file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"synthetic-english.wav");
 using(var synth=new SpeechSynthesizer()){synth.SelectVoiceByHints(VoiceGender.NotSet,VoiceAge.NotSet,0,new System.Globalization.CultureInfo("en-US"));synth.SetOutputToWaveFile(file,new SpeechAudioFormatInfo(16000,AudioBitsPerSample.Sixteen,AudioChannel.Mono));synth.Speak("Hello, this is a test of offline English speech recognition. Please remember to drink water today.");}
 byte[] source=File.ReadAllBytes(file);byte[] pcm=null;using(var reader=new BinaryReader(new MemoryStream(source))){reader.BaseStream.Position=12;while(reader.BaseStream.Position+8<source.Length){string id=System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4));int size=reader.ReadInt32();if(id=="data"){pcm=reader.ReadBytes(size);break;}reader.BaseStream.Position+=size+(size%2);}}
 if(pcm==null)throw new Exception("No fixture PCM");
 string text=SpeechTranscriber.TranscribeAsync(DictationAudio.Wave(pcm),CancellationToken.None).GetAwaiter().GetResult();
 Console.WriteLine("Synthetic fixture result: "+text);
 if(!text.Contains("hello")||!text.Contains("water"))throw new Exception("Fixture words missing");
 Console.WriteLine("PASS Independent speech worker transcribed synthetic English; no microphone or clipboard access.");File.Delete(file);return 0;
 }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}}
}
