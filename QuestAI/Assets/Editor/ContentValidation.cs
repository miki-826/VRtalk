using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace QuestAI.Editor
{
    public static class ContentValidation
    {
        static readonly string[] Rooms={"Bedroom","LivingRoom","Cafe","MainMenu","BotanicaCafe"};
        static Bounds BoundsOf(GameObject root)
        {
            var renderers=root.GetComponentsInChildren<Renderer>();var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);return bounds;
        }
        static void PlaceFurniture(string name,Vector3 floorCenter,float maxWidth,float maxHeight)
        {
            var root=GameObject.Find(name);if(!root)throw new Exception("Missing furniture: "+name);
            var bounds=BoundsOf(root);Debug.Log("ROOM_LAYOUT "+name+" previous "+bounds);
            root.transform.localScale*=Mathf.Min(maxWidth/bounds.size.x,maxHeight/bounds.size.y);
            bounds=BoundsOf(root);root.transform.position+=floorCenter-new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
            Debug.Log("ROOM_LAYOUT "+name+" corrected "+BoundsOf(root));
        }
        public static void FixBedroomAndBuild()
        {
            var scene=EditorSceneManager.OpenScene("Assets/Scenes/Bedroom.unity");
            PlaceFurniture("bedDouble",new Vector3(2.35f,0,1.5f),1.7f,.8f);
            PlaceFurniture("bookcaseOpenLow",new Vector3(-1.8f,0,3.1f),2,1.2f);
            PlaceFurniture("cabinetBedDrawer",new Vector3(3.3f,0,2.65f),.55f,.8f);
            float cabinetTop=BoundsOf(GameObject.Find("cabinetBedDrawer")).max.y;
            PlaceFurniture("lampRoundTable",new Vector3(3.3f,cabinetTop,2.65f),.3f,.5f);
            EditorSceneManager.SaveScene(scene);BakeRooms(new[]{"Bedroom"});ProjectBuilder.BuildApk();
        }
        public static void PrepareRuntimeLighting()
        {
            var avatar=PrefabUtility.LoadPrefabContents("Assets/Generated/WhiteChibi.prefab");
            foreach(var child in avatar.GetComponentsInChildren<Transform>(true))child.gameObject.layer=8;
            PrefabUtility.SaveAsPrefabAsset(avatar,"Assets/Generated/WhiteChibi.prefab");PrefabUtility.UnloadPrefabContents(avatar);
            foreach(var room in Rooms)
            {
                var scene=EditorSceneManager.OpenScene("Assets/Scenes/"+room+".unity");
                foreach(var renderer in UnityEngine.Object.FindObjectsByType<Renderer>())GameObjectUtility.SetStaticEditorFlags(renderer.gameObject,GameObjectUtility.GetStaticEditorFlags(renderer.gameObject)&~StaticEditorFlags.BatchingStatic);
                if(!GameObject.Find("Character soft fill")){var light=new GameObject("Character soft fill").AddComponent<Light>();light.type=LightType.Directional;light.color=new Color(1,.95f,.88f);light.intensity=.65f;light.cullingMask=1<<8;light.shadows=LightShadows.None;light.lightmapBakeType=LightmapBakeType.Realtime;light.transform.rotation=Quaternion.Euler(40,0,0);}
                EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();RenderAll();
        }
        public static void RebuildAndBake(){ProjectBuilder.Generate();BakeAll();}
        [MenuItem("Quest AI/Bake all time variants")]
        public static void BakeAll()=>BakeRooms(Rooms);
        public static void BakeMainMenu()=>BakeRooms(new[]{"MainMenu"});
        public static void BakeRooms(string[] rooms,bool renderReviews=true)
        {
            foreach(var room in rooms)
            {
                var scene=EditorSceneManager.OpenScene("Assets/Scenes/"+room+".unity");
                var lighting=UnityEngine.Object.FindAnyObjectByType<EnvironmentLighting>();
                Lightmapping.lightingSettings.directionalityMode=LightmapsMode.NonDirectional;
                EditorUtility.SetDirty(Lightmapping.lightingSettings);
                lighting.bakedRenderers=UnityEngine.Object.FindObjectsByType<Renderer>().Where(r=>GameObjectUtility.AreStaticEditorFlagsSet(r.gameObject,StaticEditorFlags.ContributeGI)).ToArray();
                lighting.reflectionProbe=UnityEngine.Object.FindAnyObjectByType<ReflectionProbe>();
                lighting.bakes=new EnvironmentLighting.Bake[4];
                for(int phase=0;phase<4;phase++)
                {
                    string dir="Assets/Generated/Lighting/"+room+"/"+(TimeMode)(phase+1);Directory.CreateDirectory(dir);AssetDatabase.Refresh();
                    lighting.Apply((TimeMode)(phase+1),true);
                    lighting.reflectionProbe.mode=ReflectionProbeMode.Baked;
                    EditorSceneManager.SaveScene(scene);
                    Debug.Log("QUESTAI_BAKE_START "+room+" "+phase);
                    if(!Lightmapping.Bake())throw new Exception("Lighting bake failed: "+room);
                    var source=LightmapSettings.lightmaps;
                    if(source.Length==0)throw new Exception("Bake produced no lightmaps: "+room);
                    var result=new EnvironmentLighting.Bake{maps=new Texture2D[source.Length],indices=lighting.bakedRenderers.Select(r=>r.lightmapIndex).ToArray(),offsets=lighting.bakedRenderers.Select(r=>r.lightmapScaleOffset).ToArray()};
                    for(int i=0;i<source.Length;i++)
                    {
                        var path=AssetDatabase.GetAssetPath(source[i].lightmapColor);string dest=dir+"/Lightmap"+i+Path.GetExtension(path);
                        if(File.Exists(dest))File.Copy(path,dest,true);else if(!AssetDatabase.CopyAsset(path,dest))throw new Exception("Cannot preserve baked lightmap");
                        AssetDatabase.ImportAsset(dest,ImportAssetOptions.ForceUpdate);result.maps[i]=AssetDatabase.LoadAssetAtPath<Texture2D>(dest);
                    }
                    var sh=LightmapSettings.lightProbes.bakedProbes;result.probes=new float[sh.Length*27];for(int i=0;i<sh.Length;i++)for(int c=0;c<3;c++)for(int k=0;k<9;k++)result.probes[i*27+c*9+k]=sh[i][c,k];
                    string reflection=dir+"/Reflection.exr";
                    if(!Lightmapping.BakeReflectionProbe(lighting.reflectionProbe,reflection))throw new Exception("Reflection bake failed");
                    AssetDatabase.ImportAsset(reflection);result.reflection=AssetDatabase.LoadAssetAtPath<Cubemap>(reflection);
                    lighting.bakes[phase]=result;EditorUtility.SetDirty(lighting);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
                    Debug.Log("QUESTAI_BAKE_DONE "+room+" "+phase);
                }
                lighting.Apply(TimeMode.Evening,true);EditorSceneManager.SaveScene(scene);
            }
            if(renderReviews)RenderAll();
        }
        [MenuItem("Quest AI/Render review images")]
        public static void RenderAll()
        {
            Directory.CreateDirectory("../artifacts/previews");
            foreach(var room in Rooms)
            {
                EditorSceneManager.OpenScene("Assets/Scenes/"+room+".unity");
                var lighting=UnityEngine.Object.FindAnyObjectByType<EnvironmentLighting>();
                var prefab=AssetDatabase.LoadAssetAtPath<CharacterProfile>("Assets/Resources/Profiles/calm.asset").prefab;
                var character=UnityEngine.Object.Instantiate(prefab,new Vector3(0,0,.6f),Quaternion.Euler(0,180,0));
                var rig=character.GetComponent<CharacterRigBinding>();
                // Match BodySystem's initial lowered-arm pose for still review images.
                rig.leftArm.localRotation*=Quaternion.Euler(0,0,-65);rig.rightArm.localRotation*=Quaternion.Euler(0,0,65);
                var camera=new GameObject("Review camera").AddComponent<Camera>();camera.transform.position=new Vector3(1.8f,1.6f,-2.9f);camera.transform.LookAt(new Vector3(-.7f,1.35f,1.4f));camera.fieldOfView=73;camera.nearClipPlane=.05f;camera.farClipPlane=100;
                // Same filmic grade as the headset (AppRoot adds it at runtime).
                var grade=AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Resources/Graphics/CinematicGrade.asset");if(grade){camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;var volume=new GameObject("Review grade").AddComponent<Volume>();volume.isGlobal=true;volume.sharedProfile=grade;}
                // Particles (snow, leaves, sparks) only simulate in play mode; advance them so stills show them.
                foreach(var ps in UnityEngine.Object.FindObjectsByType<ParticleSystem>())ps.Simulate(8,true,true);
                foreach(TimeMode time in new[]{TimeMode.Morning,TimeMode.Day,TimeMode.Evening,TimeMode.Night})
                {
                    lighting.Apply(time,true);Render(camera,"../artifacts/previews/"+room+"-"+time+".png");
                }
            }
            Debug.Log("QUESTAI_PREVIEWS_DONE");
        }
        public static void Render(Camera camera,string path)
        {
            var target=new RenderTexture(1600,1000,24,RenderTextureFormat.ARGB32);target.Create();camera.targetTexture=target;camera.Render();
            var old=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());RenderTexture.active=old;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(image);target.Release();UnityEngine.Object.DestroyImmediate(target);
        }
    }
}

