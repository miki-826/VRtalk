using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace QuestAI.Editor
{
    // Source geometry is exported from the user's Blender scene, in Unity metres/Y-up.
    [InitializeOnLoad]
    public static class BotanicaCafeInstaller
    {
        const string Source="Assets/Art/Botanica/BotanicaGeometry.json";
        const string Generated="Assets/Generated/Botanica";
        const string ScenePath="Assets/Scenes/BotanicaCafe.unity";
        const string Request="../artifacts/botanica-import.request";
        const string Textures="Assets/ThirdParty/PolyHaven/Botanica";
        // Poly Haven CC0 surfaces per Blender material: texture set, metres per tile, normal strength, and whether the photo albedo is used.
        // Albedos are pre-graded to the Blender palette, so their base colour stays white; normal-only sets keep the Blender colour.
        class Surface { public string set; public float tile,bump; public bool albedo; }
        static readonly Dictionary<string,Surface> Surfaces=new Dictionary<string,Surface>{
            {"MAT_Concrete",new Surface{set="concrete_floor_01",tile=1.2f,bump=.45f,albedo=true}},
            {"MAT_Wall",new Surface{set="painted_plaster_wall",tile=2f,bump=.5f,albedo=true}},
            {"MAT_Wood_Warm",new Surface{set="oak_veneer_01",tile=1.2f,bump=.5f,albedo=true}},
            {"MAT_Wood_VerticalFlutes",new Surface{set="oak_veneer_01",tile=1.2f,bump=.5f,albedo=true}},
            {"MAT_SageUpholstery",new Surface{set="rough_linen",tile=.45f,bump=1f}},
            {"MAT_Terracotta",new Surface{set="clay_plaster",tile=.5f,bump=.7f}}};
        [Serializable] class MaterialData { public string name; public Color color; public float metallic,roughness; public bool glass,emission; }
        [Serializable] class MeshData { public string name,material; public Vector3[] vertices,normals; public Vector2[] uv; public int[] triangles; }
        [Serializable] class ColliderData { public string name; public Vector3 center,size; }
        [Serializable] class Geometry { public MaterialData[] materials; public MeshData[] meshes; public ColliderData[] colliders; public int triangles; }
        static BotanicaCafeInstaller() { EditorApplication.delayCall+=ProcessRequest; }
        static void ProcessRequest()
        {
            if(!File.Exists(Request))return;
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode){EditorApplication.delayCall+=ProcessRequest;return;}
            for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty){Debug.LogWarning("BOTANICA_WAIT: Save the open scene, then use Quest AI/Import Botanica cafe.");return;}
            File.Copy(Request,Request+".running",true);File.Delete(Request);
            var setup=EditorSceneManager.GetSceneManagerSetup();
            try
            {
                BuildScene();
                ContentValidation.BakeRooms(new[]{"BotanicaCafe"},false);
                ValidateAndRender();
                File.WriteAllText("../artifacts/botanica-import-result.txt","SUCCESS\nBotanicaCafe added to environment profiles and build scenes. Four lightmap variants baked.\n"+DateTime.UtcNow.ToString("O"));
                Debug.Log("BOTANICA_IMPORT_SUCCESS");
            }
            catch(Exception e){File.WriteAllText("../artifacts/botanica-import-result.txt",e.ToString());Debug.LogException(e);}
            finally { File.Delete(Request+".running");EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }
        [MenuItem("Quest AI/Import Botanica cafe")]
        public static void ImportFromMenu(){Directory.CreateDirectory("../artifacts");File.WriteAllText(Request,"import");ProcessRequest();}
        [MenuItem("Quest AI/Open Botanica cafe")]
        public static void OpenCafe()
        {
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            EditorSceneManager.OpenScene(ScenePath);
            if(SceneView.lastActiveSceneView)SceneView.lastActiveSceneView.LookAt(new Vector3(0,1.3f,0),Quaternion.Euler(10,0,0),7);
        }
        static T Save<T>(T asset,string path) where T:UnityEngine.Object
        {
            var old=AssetDatabase.LoadAssetAtPath<T>(path);
            if(old){EditorUtility.CopySerialized(asset,old);UnityEngine.Object.DestroyImmediate(asset);return old;}
            AssetDatabase.CreateAsset(asset,path);return asset;
        }
        public static void BuildScene()
        {
            if(!File.Exists(Source))throw new FileNotFoundException("Export BotanicaGeometry.json from Blender first.");
            var data=JsonUtility.FromJson<Geometry>(File.ReadAllText(Source));
            Directory.CreateDirectory(Generated+"/Meshes");Directory.CreateDirectory(Generated+"/Materials");AssetDatabase.Refresh();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root=new GameObject("Botanica Cafe | Blender source");
            var materials=new Dictionary<string,Material>();
            foreach(var d in data.materials)
            {
                var m=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=d.name};
                // Blender's exported base colour is linear; Unity's serialized material colour is sRGB.
                m.SetColor("_BaseColor",d.color.gamma);m.SetFloat("_Metallic",d.metallic);m.SetFloat("_Smoothness",1-d.roughness);m.enableInstancing=true;
                if(d.glass)
                {
                    var c=d.color.gamma;c.a=.12f;m.SetColor("_BaseColor",c);m.SetFloat("_Surface",1);m.SetFloat("_Blend",0);m.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);m.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);m.SetFloat("_ZWrite",0);m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");m.SetOverrideTag("RenderType","Transparent");m.renderQueue=(int)RenderQueue.Transparent;
                }
                if(d.emission){m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",new Color(1,.55f,.2f)*2);m.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;}
                if(Surfaces.TryGetValue(d.name,out var surface))ApplySurface(m,surface);
                if(d.name=="MAT_Leaf"){m.shader=Shader.Find("QuestAI/Botanica Foliage");m.SetColor("_BaseColor",d.color.gamma);m.SetFloat("_Smoothness",.42f);}
                if(d.glass)m.SetFloat("_Smoothness",.96f);
                materials[d.name]=Save(m,Generated+"/Materials/"+d.name+".mat");
            }
            foreach(var d in data.meshes)
            {
                var mesh=new Mesh{name=d.name,indexFormat=IndexFormat.UInt32};mesh.vertices=d.vertices;mesh.normals=d.normals;mesh.uv=Surfaces.TryGetValue(d.material,out var surface)?BoxUV(d,surface.tile):d.uv;mesh.triangles=d.triangles;mesh.RecalculateBounds();
                if(surface!=null)mesh.RecalculateTangents();
                UnwrapParam.SetDefaults(out var unwrap);unwrap.packMargin=.008f;Unwrapping.GenerateSecondaryUVSet(mesh,unwrap);
                mesh=Save(mesh,Generated+"/Meshes/"+d.name+".asset");
                var go=new GameObject(d.name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(root.transform,false);go.GetComponent<MeshFilter>().sharedMesh=mesh;
                var r=go.GetComponent<MeshRenderer>();r.sharedMaterial=materials[d.material];r.receiveGI=ReceiveGI.Lightmaps;
                if(d.material=="MAT_Glass"){r.shadowCastingMode=ShadowCastingMode.Off;r.receiveGI=ReceiveGI.LightProbes;}
                else GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.ContributeGI|StaticEditorFlags.ReflectionProbeStatic|StaticEditorFlags.OccludeeStatic);
            }
            var collisions=new GameObject("Simple collisions");collisions.transform.SetParent(root.transform,false);
            foreach(var d in data.colliders){var go=new GameObject(d.name);go.transform.SetParent(collisions.transform,false);go.transform.position=d.center;go.AddComponent<BoxCollider>().size=d.size;}
            var sun=Light("Window sunlight",LightType.Directional,new Vector3(0,5,0),Color.white,.6f);sun.transform.rotation=Quaternion.Euler(40,120,0);sun.shadows=LightShadows.Soft;
            foreach(float z in new[]{-2.2f,-.75f,.75f,2.2f}){var l=Light("Pendant baked",LightType.Point,new Vector3(2.58f,2.24f,z),new Color(1,.77f,.49f),1.6f);l.range=3.5f;}
            var area=Light("Window daylight",LightType.Rectangle,new Vector3(-4.8f,1.8f,0),new Color(1,.92f,.79f),2);area.areaSize=new Vector2(6,2.7f);area.transform.rotation=Quaternion.Euler(0,90,0);
            var fill=Light("Character soft fill",LightType.Directional,new Vector3(0,3,0),new Color(1,.95f,.88f),.65f);fill.cullingMask=1<<8;fill.lightmapBakeType=LightmapBakeType.Realtime;fill.shadows=LightShadows.None;fill.transform.rotation=Quaternion.Euler(40,0,0);
            var probes=new GameObject("Character light probes").AddComponent<LightProbeGroup>();var points=new List<Vector3>();
            foreach(float x in new[]{-3f,0,1.5f,4f})foreach(float z in new[]{-2.8f,0,2.8f})foreach(float y in new[]{.3f,1.3f,2.4f})points.Add(new Vector3(x,y,z));probes.probePositions=points.ToArray();
            var reflection=new GameObject("Room reflection").AddComponent<ReflectionProbe>();reflection.transform.position=new Vector3(0,1.5f,0);reflection.size=new Vector3(10,3.4f,7);reflection.mode=ReflectionProbeMode.Baked;reflection.resolution=128;
            var lighting=new GameObject("Time of day").AddComponent<EnvironmentLighting>();lighting.sun=sun;lighting.reflectionProbe=reflection;lighting.sky=Save(PanoramaSky(),Generated+"/Materials/BotanicaSky.mat");lighting.Apply(TimeMode.Evening);
            var settings=new LightingSettings{name="Botanica Lighting",bakedGI=true,realtimeGI=false,lightmapper=LightingSettings.Lightmapper.ProgressiveCPU,lightmapResolution=8,lightmapMaxSize=1024,lightmapPadding=4,directSampleCount=16,indirectSampleCount=32,environmentSampleCount=16,directionalityMode=LightmapsMode.NonDirectional};Lightmapping.lightingSettings=Save(settings,Generated+"/Lighting.asset");
            EditorSceneManager.SaveScene(scene,ScenePath);
            PrefabUtility.SaveAsPrefabAsset(root,Generated+"/BotanicaCafe.prefab");
            var profile=ScriptableObject.CreateInstance<EnvironmentProfile>();profile.id=profile.sceneName="BotanicaCafe";profile.displayName="ボタニカ・カフェ";profile.characterPosition=new Vector3(.55f,0,.6f);profile.semanticObjects=new[]{"大きな窓","木のカウンター","コーヒーマシン","焼き菓子","観葉植物","ラウンジチェア"};Save(profile,"Assets/Resources/Profiles/BotanicaCafe.asset");
            if(!EditorBuildSettings.scenes.Any(s=>s.path==ScenePath))EditorBuildSettings.scenes=EditorBuildSettings.scenes.Concat(new[]{new EditorBuildSettingsScene(ScenePath,true)}).ToArray();
            AssetDatabase.SaveAssets();File.WriteAllText("../artifacts/botanica-geometry-report.txt",data.triangles+" triangles including menu text\n"+data.meshes.Length+" mesh renderers\n"+data.colliders.Length+" box colliders\n");
        }
        static Texture2D Tex(string set,string map,bool linear,bool normal,int size)
        {
            string path=Textures+"/"+set+"_"+map+(map=="mask"?".png":".jpg");
            var importer=AssetImporter.GetAtPath(path) as TextureImporter;if(!importer)throw new FileNotFoundException("Missing Poly Haven texture "+path);
            var type=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
            if(importer.textureType!=type||importer.sRGBTexture==linear||importer.maxTextureSize!=size||!importer.mipmapEnabled)
            {importer.textureType=type;importer.sRGBTexture=!linear;importer.maxTextureSize=size;importer.mipmapEnabled=true;importer.anisoLevel=4;importer.SaveAndReimport();}
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        static void ApplySurface(Material m,Surface s)
        {
            // Large surfaces get 2K maps; small upholstery/pots stay at 1K for Quest memory.
            int size=s.albedo?2048:1024;
            if(s.albedo){m.SetTexture("_BaseMap",Tex(s.set,"albedo",false,false,size));m.SetColor("_BaseColor",Color.white);}
            m.SetTexture("_BumpMap",Tex(s.set,"normal",true,true,size));m.SetFloat("_BumpScale",s.bump);m.EnableKeyword("_NORMALMAP");
            var mask=Tex(s.set,"mask",true,false,1024);
            m.SetTexture("_MetallicGlossMap",mask);m.SetFloat("_Smoothness",1);m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetTexture("_OcclusionMap",mask);m.SetFloat("_OcclusionStrength",.8f);m.EnableKeyword("_OCCLUSIONMAP");
        }
        // Exported triangles never share vertices, so each triangle can take its own world-space box projection.
        // Wood grain runs along the texture's V axis; it is turned to follow the longer side of the face.
        static Vector2[] BoxUV(MeshData d,float tile)
        {
            var uv=new Vector2[d.vertices.Length];
            for(int t=0;t<d.triangles.Length;t+=3)
            {
                int a=d.triangles[t],b=d.triangles[t+1],c=d.triangles[t+2];
                var n=Vector3.Cross(d.vertices[b]-d.vertices[a],d.vertices[c]-d.vertices[a]);n=new Vector3(Mathf.Abs(n.x),Mathf.Abs(n.y),Mathf.Abs(n.z));
                int axis=n.x>=n.y&&n.x>=n.z?0:n.y>=n.z?1:2;
                Func<Vector3,Vector2> project=p=>axis==0?new Vector2(p.z,p.y):axis==1?new Vector2(p.x,p.z):new Vector2(p.x,p.y);
                Vector2 pa=project(d.vertices[a]),pb=project(d.vertices[b]),pc=project(d.vertices[c]);
                var size=Vector2.Max(Vector2.Max(pa,pb),pc)-Vector2.Min(Vector2.Min(pa,pb),pc);bool swap=size.x>size.y;
                foreach(int i in new[]{a,b,c}){var p=project(d.vertices[i])/tile;uv[i]=swap?new Vector2(p.y,p.x):p;}
            }
            return uv;
        }
        static Material PanoramaSky()
        {
            // Poly Haven "Eilenriede Park" (CC0) tonemapped panorama: open meadow and trees seen through the glazing and roof beams.
            const string path=Textures+"/eilenriede_park_panorama.jpg";
            var importer=AssetImporter.GetAtPath(path) as TextureImporter;if(!importer)throw new FileNotFoundException("Missing panorama "+path);
            if(importer.maxTextureSize!=4096||importer.mipmapEnabled||importer.wrapModeV!=TextureWrapMode.Clamp){importer.textureType=TextureImporterType.Default;importer.maxTextureSize=4096;importer.mipmapEnabled=false;importer.wrapModeU=TextureWrapMode.Repeat;importer.wrapModeV=TextureWrapMode.Clamp;importer.SaveAndReimport();}
            var sky=new Material(Shader.Find("Skybox/Panoramic"));sky.SetTexture("_MainTex",AssetDatabase.LoadAssetAtPath<Texture2D>(path));sky.SetFloat("_Mapping",1);sky.SetFloat("_ImageType",0);sky.SetFloat("_Rotation",0);sky.SetColor("_Tint",new Color(.5f,.5f,.5f));
            return sky;
        }
        static Light Light(string name,LightType type,Vector3 pos,Color color,float energy){var l=new GameObject(name).AddComponent<Light>();l.type=type;l.transform.position=pos;l.color=color;l.intensity=energy;l.lightmapBakeType=LightmapBakeType.Baked;return l;}
        public static void ValidateAndRender()
        {
            EditorSceneManager.OpenScene(ScenePath);
            var lighting=UnityEngine.Object.FindAnyObjectByType<EnvironmentLighting>();
            if(lighting.bakes.Length!=4||lighting.bakes.Any(b=>b==null||b.maps==null||b.maps.Length==0))throw new Exception("Missing time of day bake");
            var profile=AssetDatabase.LoadAssetAtPath<EnvironmentProfile>("Assets/Resources/Profiles/BotanicaCafe.asset");
            Physics.SyncTransforms();
            if(Physics.CheckCapsule(profile.characterPosition+Vector3.up*.2f,profile.characterPosition+Vector3.up*1.4f,.18f))throw new Exception("Character position intersects furniture");
            if(!Physics.Raycast(new Vector3(0,1,-1.8f),Vector3.down,2))throw new Exception("Missing floor collision");
            var camera=new GameObject("Review camera").AddComponent<Camera>();camera.transform.position=new Vector3(-.5f,2.1f,-6.4f);camera.transform.LookAt(new Vector3(0,1.4f,.8f));camera.fieldOfView=74;camera.nearClipPlane=.05f;
            Directory.CreateDirectory("../artifacts/previews");
            foreach(var time in new[]{TimeMode.Morning,TimeMode.Day,TimeMode.Evening,TimeMode.Night}){lighting.Apply(time,true);ContentValidation.Render(camera,"../artifacts/previews/BotanicaCafe-"+time+".png");}
            UnityEngine.Object.DestroyImmediate(camera.gameObject);lighting.Apply(TimeMode.Evening,true);EditorSceneManager.SaveScene(SceneManager.GetActiveScene());AssetDatabase.SaveAssets();
        }
    }
}
