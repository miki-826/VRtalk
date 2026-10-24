using System;
using UnityEngine;
using UnityEngine.Rendering;
namespace QuestAI
{
    public class EnvironmentLighting : MonoBehaviour
    {
        public Light sun;
        public Material sky;
        public Color[] ambient = {new Color(.65f,.55f,.44f),new Color(.72f,.77f,.85f),new Color(.55f,.36f,.28f),new Color(.12f,.17f,.28f)};
        [Serializable] public class Bake
        {
            public Texture2D[] maps;
            public int[] indices;
            public Vector4[] offsets;
            public float[] probes;
            public Cubemap reflection;
        }
        public Bake[] bakes = new Bake[4];
        public Renderer[] bakedRenderers;
        public ReflectionProbe reflectionProbe;
        TimeMode mode;int applied=-1;float checkTime;
        // 0 morning, 1 day, 2 evening, 3 night (-1 before the first Apply).
        public int Phase=>applied;
        public static int Resolve(TimeMode time,int hour) => time!=TimeMode.Auto?(int)time-1:hour>=5&&hour<10?0:hour>=10&&hour<16?1:hour>=16&&hour<20?2:3;
        public void Apply(TimeMode value,bool force=false){mode=value;int phase=Resolve(mode,DateTime.Now.Hour);if(applied==phase&&!force)return;applied=phase;
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=ambient[phase];RenderSettings.ambientIntensity=1;
            if(sun){sun.color=ambient[phase];sun.intensity=phase==3?.08f:phase==1?.8f:.45f;sun.transform.rotation=Quaternion.Euler(phase==1?55:12,phase==2?240:120,0);}
            // Key light for QuestAI/Realistic Surface highlights on lightmapped surfaces: window sun by day, warm ceiling lamps at night.
            Vector3 keyDir=phase==3||!sun?new Vector3(.15f,1,.1f).normalized:-sun.transform.forward;
            Color keyColor=phase==3||!sun?new Color(1,.78f,.55f)*.7f:sun.color*(sun.intensity*1.8f);
            Shader.SetGlobalVector("_QuestAIKeyLightDir",keyDir);Shader.SetGlobalColor("_QuestAIKeyLightColor",keyColor);
            // Brightness of the outdoor photo for 3D things placed outside (trees, snow, birds) so they match the panorama.
            Shader.SetGlobalColor("_QuestAIOutdoorTint",Color.Lerp(Color.white,ambient[phase],.45f)*(phase==3?.2f:phase==2?.8f:1));
            if(sky){RenderSettings.skybox=sky;sky.SetColor("_SkyTint",ambient[phase]);sky.SetFloat("_Exposure",phase==3?.3f:1);
                // Photographic panorama skies (Skybox/Panoramic): 0.5 grey is neutral, so only lean the photo towards the time-of-day colour.
                if(sky.HasProperty("_Tint")){sky.SetColor("_Tint",Color.Lerp(Color.white,ambient[phase],.45f)*.5f);sky.SetFloat("_Exposure",phase==3?.18f:phase==2?.8f:1);}}
            var bake=bakes!=null&&phase<bakes.Length?bakes[phase]:null;
            if(bake?.maps!=null&&bake.maps.Length>0)
            {
                var data=new LightmapData[bake.maps.Length];for(int i=0;i<data.Length;i++)data[i]=new LightmapData{lightmapColor=bake.maps[i]};LightmapSettings.lightmapsMode=LightmapsMode.NonDirectional;LightmapSettings.lightmaps=data;
                for(int i=0;i<bakedRenderers.Length;i++)if(bakedRenderers[i]){bakedRenderers[i].lightmapIndex=bake.indices[i];bakedRenderers[i].lightmapScaleOffset=bake.offsets[i];}
                if(LightmapSettings.lightProbes!=null&&bake.probes!=null&&bake.probes.Length==LightmapSettings.lightProbes.count*27)
                {var sh=new SphericalHarmonicsL2[bake.probes.Length/27];for(int i=0;i<sh.Length;i++)for(int c=0;c<3;c++)for(int k=0;k<9;k++)sh[i][c,k]=bake.probes[i*27+c*9+k];LightmapSettings.lightProbes.bakedProbes=sh;}
                if(reflectionProbe&&bake.reflection){reflectionProbe.mode=ReflectionProbeMode.Custom;reflectionProbe.customBakedTexture=bake.reflection;}
            }
        }
        void Update(){if(mode==TimeMode.Auto&&Time.time>checkTime){checkTime=Time.time+60;Apply(mode);}}
    }
}
