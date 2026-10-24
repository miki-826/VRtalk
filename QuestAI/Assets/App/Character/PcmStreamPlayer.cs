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
            if(audible)Interlocked.Exchange(ref lastAudible,DateTime.UtcNow.Ticks);
        }
        public void Clear(){lock(sync){read=write=count=0;}Interlocked.Exchange(ref lastAudible,0);}
        void OnDestroy(){Clear();if(source){source.Stop();if(source.clip==clip)source.clip=null;source.loop=false;}if(clip)Destroy(clip);}
    }
}
