using System;
using System.Threading;
using UnityEngine;
namespace QuestAI
{
    public class PcmStreamPlayer : MonoBehaviour
    {
        const int Capacity=24000*8;
        readonly float[] ring=new float[Capacity];readonly object sync=new object();
        int read,write,count;long lastAudible;AudioSource source;AudioClip clip;
        // Set from the main thread; read on the audio thread.
        public volatile bool Robot;
        readonly RobotVoice robot=new RobotVoice(24000);
        public bool Speaking => (DateTime.UtcNow.Ticks-Interlocked.Read(ref lastAudible))/(double)TimeSpan.TicksPerSecond<.7;
        public void Initialize(AudioSource output)
        {
            source=output;clip=AudioClip.Create("GPT-Live stream",24000,1,24000,true,Read);source.clip=clip;source.loop=true;source.Play();
        }
        public void Append(byte[] pcm)
        {
            if((pcm.Length&1)!=0)throw new InvalidOperationException("GPT-Live音声の形式が不正です。");
            lock(sync){if(count+pcm.Length/2>Capacity)throw new InvalidOperationException("音声の受信が再生に追いつきません。再接続してください。");for(int i=0;i<pcm.Length;i+=2){ring[write]=(short)(pcm[i]|pcm[i+1]<<8)/32768f;write=(write+1)%Capacity;count++;}}
        }
        void Read(float[] output)
        {
            bool audible=false;
            lock(sync){for(int i=0;i<output.Length;i++){float value=0;if(count>0){value=ring[read];read=(read+1)%Capacity;count--;}output[i]=value;if(Math.Abs(value)>.006f)audible=true;}}
            if(Robot)robot.Process(output);else robot.Reset();
            if(audible)Interlocked.Exchange(ref lastAudible,DateTime.UtcNow.Ticks);
        }
        public void Clear(){lock(sync){read=write=count=0;}Interlocked.Exchange(ref lastAudible,0);}
        void OnDestroy(){Clear();if(source){source.Stop();if(source.clip==clip)source.clip=null;source.loop=false;}if(clip)Destroy(clip);}
    }
    // Robotic timbre: ring modulation for the metallic buzz, a short resonant echo for the "tin can" body and light quantisation.
    public sealed class RobotVoice
    {
        readonly float[] delay;readonly float step;int index;double phase;
        public float carrier=58,mix=.65f,feedback=.55f;public int levels=48;
        public RobotVoice(int sampleRate){delay=new float[Math.Max(1,sampleRate*6/1000)];step=1f/sampleRate;}
        public void Reset(){Array.Clear(delay,0,delay.Length);index=0;phase=0;}
        public void Process(float[] samples)
        {
            for(int i=0;i<samples.Length;i++)
            {
                float dry=samples[i];
                phase+=2*Math.PI*carrier*step;if(phase>2*Math.PI)phase-=2*Math.PI;
                float ringed=dry*(1-mix)+dry*(float)Math.Sin(phase)*mix*1.6f;
                float echoed=ringed+delay[index]*feedback;delay[index]=echoed;index=(index+1)%delay.Length;
                float quantised=(float)Math.Round(echoed*levels)/levels;
                samples[i]=(float)Math.Tanh(quantised*1.2f)*.9f;
            }
        }
    }
}
