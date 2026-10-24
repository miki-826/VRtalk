using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace QuestAI
{
    // Local-only diagnostic: recording stays in memory and is never sent to a provider.
    public sealed class MicrophoneCheck : MonoBehaviour
    {
        AudioCaptureService capture;
        AudioSource output;
        AudioClip replay;
        Coroutine routine;
        public void Begin(float volume,Action<string> status)
        {
            Stop();
            if(!capture)capture=gameObject.AddComponent<AudioCaptureService>();
            if(!output){output=gameObject.AddComponent<AudioSource>();output.playOnAwake=false;output.spatialBlend=0;}
            output.volume=volume;
            routine=StartCoroutine(Check(status));
        }
        IEnumerator Check(Action<string> status)
        {
            if(!capture.Begin(out string error)){status(error);yield break;}
            var samples=new List<float>(96000);float end=Time.realtimeSinceStartup+4,peak=0;
            while(Time.realtimeSinceStartup<end)
            {
                byte[] pcm=capture.DrainPcm();float level=0;
                for(int i=0;i+1<pcm.Length;i+=2){float s=(short)(pcm[i]|pcm[i+1]<<8)/32768f;samples.Add(s);level=Mathf.Max(level,Mathf.Abs(s));}
                peak=Mathf.Max(peak,level);
                status("マイク確認 · あと"+Mathf.CeilToInt(end-Time.realtimeSinceStartup)+"秒\n声を出してください  "+new string('■',Mathf.Clamp(Mathf.CeilToInt(level*30),0,10)));
                yield return new WaitForSecondsRealtime(.1f);
            }
            capture.Stop();
            if(samples.Count<2400||peak<.002f){status("声を検出できませんでした。マイクの許可・本体のミュートを確認してください。");yield break;}
            replay=AudioClip.Create("Local microphone check",samples.Count,1,24000,false);replay.SetData(samples.ToArray(),0);output.clip=replay;output.Play();
            status("録音した声を再生中です。聞こえない場合は本体とアプリの音量を確認してください。");
            yield return new WaitForSecondsRealtime(replay.length+.2f);
            status("マイク入力を確認しました。声が聞こえたら、部屋へ戻って会話に接続してください。");
            output.clip=null;Destroy(replay);replay=null;routine=null;
        }
        public void Stop(){if(routine!=null)StopCoroutine(routine);routine=null;if(capture)capture.Stop();if(output){output.Stop();output.clip=null;}if(replay)Destroy(replay);replay=null;}
        void OnApplicationPause(bool paused){if(paused)Stop();}
        void OnApplicationFocus(bool focused){if(!focused)Stop();}
        void OnDestroy()=>Stop();
    }
}
