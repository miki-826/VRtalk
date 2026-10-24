using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace QuestAI
{
    public class AudioCaptureService : MonoBehaviour, IAudioCapture
    {
        AudioClip clip; string device; float started; int readPosition;
        public bool Recording => clip != null;
        public bool LimitReached => Recording && Time.realtimeSinceStartup - started >= 60;
        public bool Begin(out string error)
        {
            error = "";
            if (Recording) return false;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
                error = "マイクを許可した後、もう一度PTTを押してください。"; return false;
            }
#endif
            if (Microphone.devices.Length == 0) { error = "マイクが見つかりません。"; return false; }
            device = Microphone.devices[0]; started = Time.realtimeSinceStartup; readPosition=0;
            clip = Microphone.Start(device, false, 60, 24000);
            if (!clip) { error = "録音を開始できませんでした。"; return false; }
            return true;
        }
        public byte[] Finish()
        {
            if (!clip) return null;
            int count = LimitReached ? clip.samples : Microphone.GetPosition(device);
            Microphone.End(device);
            var source = clip; clip = null;
            try
            {
                if (count < source.frequency * .15f) throw new InvalidOperationException("もう少し長く話してください。");
                float[] raw = new float[count * source.channels]; source.GetData(raw, 0);
                float[] mono = new float[count]; double energy = 0;
                for(int i=0;i<count;i++) { for(int c=0;c<source.channels;c++) mono[i] += raw[i*source.channels+c]/source.channels; energy += mono[i]*mono[i]; }
                if (Math.Sqrt(energy / count) < .001) throw new InvalidOperationException("音声が小さいか無音です。もう一度話してください。");
                return EncodeWav(mono, source.frequency);
            }
            finally { Destroy(source); }
        }
        public static byte[] EncodeWav(float[] data,int rate)
        {
            using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36+data.Length*2);w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));w.Write(16);w.Write((short)1);w.Write((short)1);w.Write(rate);w.Write(rate*2);w.Write((short)2);w.Write((short)16);w.Write(Encoding.ASCII.GetBytes("data"));w.Write(data.Length*2);
            foreach(float sample in data) w.Write((short)(Mathf.Clamp(sample,-1,1)*32767));return stream.ToArray();
        }
        public byte[] DrainPcm()
        {
            if(!clip)return Array.Empty<byte>();
            int position=LimitReached?clip.samples:Microphone.GetPosition(device);
            int count=position-readPosition;if(count<=0)return Array.Empty<byte>();
            float[] samples=new float[count*clip.channels];clip.GetData(samples,readPosition);readPosition=position;
            int outputCount=(int)((long)count*24000/clip.frequency);var bytes=new byte[outputCount*2];
            for(int i=0;i<outputCount;i++){float index=i*clip.frequency/24000f;int a=Math.Min(count-1,(int)index),b=Math.Min(count-1,a+1);float sample=0;for(int c=0;c<clip.channels;c++)sample+=Mathf.Lerp(samples[a*clip.channels+c],samples[b*clip.channels+c],index-a)/clip.channels;short pcm=(short)(Mathf.Clamp(sample,-1,1)*32767);bytes[2*i]=(byte)pcm;bytes[2*i+1]=(byte)(pcm>>8);}
            return bytes;
        }
        public void Stop() { if (!clip) return; Microphone.End(device); Destroy(clip); clip=null; }
        void OnDestroy() => Stop();
    }
}
