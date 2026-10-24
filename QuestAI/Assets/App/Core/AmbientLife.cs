using System.Collections.Generic;
using UnityEngine;

namespace QuestAI
{
    // Life outside (and around) the room: wind gusts that sway trees and drift snow/leaves, wind and fire
    // sounds, songbird chirps from the trees, and small flocks that occasionally fly past the windows.
    // Placed in each room scene by RealisticUpgrade; everything else is built at runtime.
    public class AmbientLife : MonoBehaviour
    {
        public Vector3 windowCenter;
        public bool hasHearth;public Vector3 hearth;
        public Vector3 flightCentre,flightDirection=Vector3.forward;public float flightLength=40,flightHeight=3;
        public Transform[] chirpSpots;
        public ParticleSystem weather;public float weatherDrift=.5f;
        public Material birdMaterial;
        public static float Wind{get;private set;}

        EnvironmentLighting lighting;Material weatherMaterial;float gust,gustTarget,nextGust,nextChirp,nextFlock,tintCheck;
        AudioSource windSource;readonly List<Bird> birds=new List<Bird>();Mesh bodyMesh,wingMesh;
        class Bird{public Transform root,left,right;public Vector3 start,velocity;public float age,life,phase;}

        static AudioSource Source(Transform parent,Vector3 position,AudioClip clip,float volume,bool loop,float lowPass)
        {
            var go=new GameObject(clip?clip.name+" audio":"Ambient audio");go.transform.SetParent(parent,false);go.transform.position=position;
            var s=go.AddComponent<AudioSource>();s.clip=clip;s.volume=volume;s.loop=loop;s.spatialBlend=1;s.spatialize=true;s.minDistance=1.5f;s.maxDistance=40;s.rolloffMode=AudioRolloffMode.Logarithmic;s.playOnAwake=false;s.dopplerLevel=0;
            // Outdoor sounds reach the listener through glass.
            if(lowPass>0)go.AddComponent<AudioLowPassFilter>().cutoffFrequency=lowPass;
            if(loop&&clip){s.time=Random.value*clip.length;s.Play();}
            return s;
        }
        void Start()
        {
            lighting=FindAnyObjectByType<EnvironmentLighting>();
            windSource=Source(transform,windowCenter,ProceduralAudio.Wind(),.25f,true,1800);windSource.minDistance=4;
            if(hasHearth){var fire=Source(transform,hearth,ProceduralAudio.Crackle(),.5f,true,0);fire.minDistance=.8f;fire.maxDistance=12;}
            if(weather){var r=weather.GetComponent<ParticleSystemRenderer>();weatherMaterial=r.material;}
            BuildBirdMeshes();
            nextGust=Time.time+Random.Range(4f,10f);nextChirp=Time.time+Random.Range(2f,6f);nextFlock=Time.time+Random.Range(6f,15f);
        }
        int Phase=>lighting?lighting.Phase:1;
        void Update()
        {
            float t=Time.time;
            // Gusts: occasional swells lasting a few seconds on top of a light breeze.
            if(t>nextGust){gustTarget=Random.Range(.5f,1f);nextGust=t+Random.Range(8f,20f);}
            if(gustTarget>0&&gust>=gustTarget-.02f)gustTarget=0;
            gust=Mathf.MoveTowards(gust,gustTarget,Time.deltaTime*(gustTarget>gust?.35f:.15f));
            Wind=.18f+.08f*Mathf.PerlinNoise(t*.3f,0)+gust*.8f;
            Shader.SetGlobalFloat("_QuestAIWind",Wind);
            if(windSource)windSource.volume=.12f+.35f*Wind;
            if(weather){var v=weather.velocityOverLifetime;v.xMultiplier=-weatherDrift*(.3f+Wind*1.6f);}
            if(weatherMaterial&&t>tintCheck){tintCheck=t+1;var tint=Shader.GetGlobalColor("_QuestAIOutdoorTint");weatherMaterial.SetColor("_BaseColor",new Color(tint.r,tint.g,tint.b,1)*1.1f);}
            int phase=Phase;
            // Birds sing in the morning and by day, less in the evening, not at night.
            if(t>nextChirp)
            {
                nextChirp=t+(phase==0?Random.Range(2f,6f):phase==1?Random.Range(4f,10f):Random.Range(10f,25f));
                if(phase!=3&&chirpSpots!=null&&chirpSpots.Length>0)
                {
                    var spot=chirpSpots[Random.Range(0,chirpSpots.Length)];var s=Source(transform,spot.position+Vector3.up*Random.Range(1f,3f),ProceduralAudio.Chirp(Random.Range(0,6)),Random.Range(.25f,.45f),false,5000);
                    s.pitch=Random.Range(.9f,1.15f);s.Play();Destroy(s.gameObject,s.clip.length/s.pitch+.1f);
                }
            }
            if(t>nextFlock){nextFlock=t+Random.Range(15f,40f);if(phase!=3)SpawnFlock();}
            UpdateBirds();
        }

        // ---------- Birds ----------
        void BuildBirdMeshes()
        {
            bodyMesh=new Mesh{name="Bird body"};
            bodyMesh.vertices=new[]{new Vector3(0,0,.09f),new Vector3(-.018f,0,0),new Vector3(.018f,0,0),new Vector3(0,.016f,0),new Vector3(0,-.012f,0),new Vector3(0,0,-.08f)};
            bodyMesh.triangles=new[]{0,3,1,0,2,3,0,1,4,0,4,2,5,1,3,5,3,2,5,4,1,5,2,4};bodyMesh.RecalculateNormals();
            wingMesh=new Mesh{name="Bird wing"};
            wingMesh.vertices=new[]{new Vector3(0,0,.03f),new Vector3(0,0,-.03f),new Vector3(.14f,0,-.02f),new Vector3(.2f,0,-.05f)};
            wingMesh.triangles=new[]{0,2,1,1,2,3,0,1,2,1,3,2};wingMesh.RecalculateNormals();
        }
        Transform Part(string name,Mesh mesh,Transform parent)
        {
            var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(parent,false);go.GetComponent<MeshFilter>().sharedMesh=mesh;
            var r=go.GetComponent<MeshRenderer>();r.sharedMaterial=birdMaterial;r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;r.receiveShadows=false;r.lightProbeUsage=UnityEngine.Rendering.LightProbeUsage.Off;r.reflectionProbeUsage=UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return go.transform;
        }
        void SpawnFlock()
        {
            if(!birdMaterial)return;
            int count=Random.Range(1,7);float side=Random.value<.5f?1:-1;var dir=flightDirection.normalized*side;
            var lead=flightCentre-dir*flightLength*.5f+new Vector3(0,Random.Range(0,flightHeight),0);float speed=Random.Range(6f,9f);
            var chirp=Random.value<.6f;
            for(int i=0;i<count;i++)
            {
                var root=new GameObject("Bird").transform;root.SetParent(transform,false);
                Part("Body",bodyMesh,root);var left=Part("Left wing",wingMesh,root);var right=Part("Right wing",wingMesh,root);right.localScale=new Vector3(-1,1,1);
                root.localScale=Vector3.one*Random.Range(.9f,1.3f);
                var offset=new Vector3(Random.Range(-1.5f,1.5f),Random.Range(-.6f,.6f),0)+(-dir)*i*Random.Range(.8f,1.6f);
                var b=new Bird{root=root,left=left,right=right,start=lead+offset,velocity=dir*speed*Random.Range(.92f,1.08f),life=flightLength/speed+2,phase=Random.value*10};
                root.position=b.start;root.rotation=Quaternion.LookRotation(dir);birds.Add(b);
                if(i==0&&chirp){var s=root.gameObject.AddComponent<AudioSource>();s.clip=ProceduralAudio.Chirp(Random.Range(0,6));s.spatialBlend=1;s.spatialize=true;s.volume=.3f;s.minDistance=2;s.maxDistance=40;s.dopplerLevel=0;s.Play();}
            }
        }
        void UpdateBirds()
        {
            for(int i=birds.Count-1;i>=0;i--)
            {
                var b=birds[i];b.age+=Time.deltaTime;
                if(b.age>b.life||!b.root){if(b.root)Destroy(b.root.gameObject);birds.RemoveAt(i);continue;}
                // Bursts of flapping between short glides, with a gentle rise and fall.
                float cycle=Mathf.Repeat(b.age+b.phase,1.6f);bool flapping=cycle<1.1f;
                float wing=flapping?Mathf.Sin((b.age+b.phase)*28)*55:8;
                b.left.localRotation=Quaternion.Euler(0,0,wing);b.right.localRotation=Quaternion.Euler(0,0,-wing);
                b.root.position=b.start+b.velocity*b.age+Vector3.up*Mathf.Sin(b.age*1.3f+b.phase)*.4f;
            }
        }
    }
}
