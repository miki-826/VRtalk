using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestAI.Editor
{
    // Botanica cafe furniture/plants and the fireplace effects for the realism pass.
    public static partial class RealisticUpgrade
    {
        const string BotanicaRoot="Botanica Cafe | Blender source";
        // Merged Blender meshes replaced by individual Poly Haven models (kept inactive so the pass can re-run).
        static readonly string[] BotanicaReplaced={"FURNITURE_MAT_Wood_Warm","FURNITURE_MAT_WindowFrame","FURNITURE_MAT_SageUpholstery","PLANTS_MAT_Terracotta","PLANTS_MAT_Soil","PLANTS_MAT_Leaf"};
        // Pot centres measured from PLANTS_MAT_Terracotta: floor planters, then the wall shelf and counter pots.
        static readonly Vector3[] FloorPlants={new Vector3(-4.35f,0,3),new Vector3(-4.35f,0,-3),new Vector3(.2f,0,3),new Vector3(4.1f,0,3)};
        static readonly Vector3[] SmallPlants={new Vector3(.65f,0,-2.8f),new Vector3(4.76f,2.23f,-.65f),new Vector3(4.76f,2.23f,.75f),new Vector3(4.76f,2.23f,1.9f),new Vector3(2.58f,1.05f,2.6f)};

        [MenuItem("Quest AI/Realistic Botanica and fire (bake Botanica, LivingRoom, MainMenu)")]
        public static void ApplyCafeAndFireBakeAndRender()
        {
            Apply();ContentValidation.BakeRooms(new[]{"BotanicaCafe","LivingRoom","MainMenu"},false);GraphicsUpgrade.FixSurfaceTangents();ContentValidation.RenderAll();
        }

        // Resume after an interrupted ApplyBakeAndRender: Apply and the Bedroom bake already finished.
        public static void BakeRemainingAndRender()
        {
            ContentValidation.BakeRooms(new[]{"LivingRoom","Cafe","MainMenu","BotanicaCafe"},false);GraphicsUpgrade.FixSurfaceTangents();ContentValidation.RenderAll();
        }

        public static void BakeBedroomAndRender(){ContentValidation.BakeRooms(new[]{"Bedroom"},false);GraphicsUpgrade.FixSurfaceTangents();ContentValidation.RenderAll();}

        static GameObject Place(GameObject prefab,Transform parent,string name,Vector3 floor,float yaw,float scale,float lightmapScale=2)
        {
            var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent.gameObject.scene);go.name=name;go.transform.SetParent(parent,true);
            go.transform.rotation=Quaternion.Euler(0,yaw,0)*prefab.transform.localRotation;go.transform.localScale=prefab.transform.localScale*scale;
            var b=BoundsOf(go);go.transform.position+=floor-new Vector3(b.center.x,b.min.y,b.center.z);
            foreach(var r in go.GetComponentsInChildren<Renderer>())
            {
                GameObjectUtility.SetStaticEditorFlags(r.gameObject,StaticEditorFlags.ContributeGI|StaticEditorFlags.ReflectionProbeStatic|StaticEditorFlags.OccludeeStatic);
                var so=new SerializedObject(r);so.FindProperty("m_ScaleInLightmap").floatValue=lightmapScale;so.ApplyModifiedPropertiesWithoutUndo();
            }
            return go;
        }
        static float Height(GameObject prefab)=>LocalBounds(prefab,prefab.transform.localRotation).size.y;

        static void ReplaceBotanica(Dictionary<string,GameObject> models)
        {
            var scene=EditorSceneManager.OpenScene("Assets/Scenes/BotanicaCafe.unity");
            var root=GameObject.Find(BotanicaRoot).transform;
            foreach(string name in BotanicaReplaced){var t=root.Find(name);if(t)t.gameObject.SetActive(false);}
            var old=root.Find("Realistic furniture");if(old)Object.DestroyImmediate(old.gameObject);
            var parent=new GameObject("Realistic furniture").transform;parent.SetParent(root,false);
            var colliders=root.Find("Simple collisions").Cast<Transform>().ToDictionary(t=>t.name,t=>t.GetComponent<BoxCollider>());
            Vector3 Centre(BoxCollider c)=>c.transform.TransformPoint(c.center);
            float Top(BoxCollider c)=>Centre(c).y+c.size.y*.5f;
            // Tables: match the Blender table-top height so cups and vases keep sitting on them.
            var table=models["round_wooden_table_02"];
            foreach(var pair in colliders.Where(p=>p.Key.StartsWith("Table_Top")))
            {
                var c=Centre(pair.Value);Place(table,parent,"Realistic table",new Vector3(c.x,0,c.z),0,Mathf.Clamp(Top(pair.Value)/Height(table),.85f,1.2f));
            }
            // Chairs and lounge chairs face from their back rest towards the seat.
            void Seats(string seatName,string backName,string asset,string label)
            {
                foreach(var pair in colliders.Where(p=>p.Key.StartsWith(seatName)))
                {
                    string suffix=pair.Key.Substring(seatName.Length);if(!colliders.TryGetValue(backName+suffix,out var back))continue;
                    Vector3 seat=Centre(pair.Value),rest=Centre(back);var facing=seat-rest;facing.y=0;
                    float yaw=Mathf.Atan2(facing.x,facing.z)*Mathf.Rad2Deg+(YawOffset.TryGetValue(asset,out float o)?o:0);
                    var at=Vector3.Lerp(seat,rest,.25f);Place(models[asset],parent,label,new Vector3(at.x,0,at.z),yaw,1);
                }
            }
            Seats("Chair_Seat","Chair_Back","dining_chair_02","Realistic chair");
            Seats("Lounge_Seat","Lounge_Back","modern_arm_chair_01","Realistic lounge chair");
            for(int i=0;i<FloorPlants.Length;i++)Place(models[i%2==0?"potted_plant_01":"potted_plant_02"],parent,"Realistic floor plant",FloorPlants[i],i*67,1.35f);
            for(int i=0;i<SmallPlants.Length;i++)Place(models["potted_plant_04"],parent,"Realistic small plant",SmallPlants[i],i*83,SmallPlants[i].y<.01f?1.8f:.9f,1);
            EditorSceneManager.SaveScene(scene);
        }

        // ---------- Fireplace ----------
        static Mesh FlamePlanes()
        {
            // Three ribbons at 60 degrees: the fire reads as a volume from any angle in VR.
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            for(int k=0;k<3;k++)
            {
                var rotation=Quaternion.Euler(0,k*60,0);int start=vertices.Count;
                foreach(var p in new[]{new Vector3(-.5f,0,0),new Vector3(.5f,0,0),new Vector3(-.5f,1,0),new Vector3(.5f,1,0)})vertices.Add(rotation*p);
                uv.AddRange(new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one});triangles.AddRange(new[]{start,start+2,start+1,start+1,start+2,start+3});
            }
            var mesh=new Mesh{name="Flame tri-planes"};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            string path=Output+"/Flame tri-planes.asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(existing){EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);return existing;}
            AssetDatabase.CreateAsset(mesh,path);return mesh;
        }
        static Material EmberMaterial()
        {
            const string bark="Assets/ThirdParty/PolyHaven/Textures/bark_brown_02";string path=Output+"/Materials/Hearth embers.mat";
            var shader=Shader.Find("QuestAI/Hearth Embers");var m=Mat(path);if(!m){m=new Material(shader);AssetDatabase.CreateAsset(m,path);}
            m.shader=shader;m.shaderKeywords=new string[0];m.enableInstancing=true;
            m.SetTexture("_BaseMap",Import(bark+"_diff.jpg",true,false,1024));m.SetColor("_BaseColor",new Color(.75f,.68f,.62f));m.mainTextureScale=new Vector2(1,2.5f);
            m.SetTexture("_BumpMap",Import(bark+"_nor_gl.jpg",false,true,1024));m.SetFloat("_BumpScale",1.2f);m.EnableKeyword("_NORMALMAP");
            var mask=PackMask("Assets/ThirdParty/PolyHaven","bark_brown_02",1024);m.SetTexture("_MetallicGlossMap",mask);m.SetTexture("_OcclusionMap",mask);m.SetFloat("_Smoothness",1);
            m.EnableKeyword("_METALLICSPECGLOSSMAP");m.EnableKeyword("_OCCLUSIONMAP");m.SetColor("_EmissionColor",new Color(4.5f,1.2f,.25f));
            EditorUtility.SetDirty(m);return m;
        }
        static Material SparkMaterial()
        {
            string texPath=Output+"/Textures/Spark.png";const int size=32;var tex=new Texture2D(size,size,TextureFormat.RGBA32,false,true);var pixels=new Color[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++){float r=Mathf.Clamp01(new Vector2(x+.5f-size/2f,y+.5f-size/2f).magnitude/(size/2f));float a=Mathf.Pow(1-r,2.5f);pixels[y*size+x]=new Color(a,a,a,a);}
            tex.SetPixels(pixels);File.WriteAllBytes(texPath,tex.EncodeToPNG());Object.DestroyImmediate(tex);AssetDatabase.ImportAsset(texPath);
            string path=Output+"/Materials/Hearth sparks.mat";var shader=Shader.Find("Universal Render Pipeline/Particles/Unlit");var m=Mat(path);if(!m){m=new Material(shader);AssetDatabase.CreateAsset(m,path);}
            m.shader=shader;m.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));m.SetColor("_BaseColor",new Color(3.5f,1.4f,.45f,1));
            m.SetFloat("_Surface",1);m.SetFloat("_Blend",2);m.SetFloat("_SrcBlend",(float)BlendMode.One);m.SetFloat("_DstBlend",(float)BlendMode.One);m.SetFloat("_ZWrite",0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");m.SetOverrideTag("RenderType","Transparent");m.renderQueue=(int)RenderQueue.Transparent;EditorUtility.SetDirty(m);return m;
        }
        static void UpgradeHearths()
        {
            var flameMesh=FlamePlanes();var embers=EmberMaterial();var sparks=SparkMaterial();
            var fire=Mat("Assets/Generated/Natural/Hearth fire.mat");
            if(fire){fire.SetFloat("_Intensity",2.6f);fire.SetFloat("_Turbulence",.65f);fire.SetFloat("_CoreHeat",1.5f);fire.SetFloat("_Distortion",.15f);fire.SetFloat("_TipFade",.8f);EditorUtility.SetDirty(fire);}
            float[] heights={.5f,.68f,.6f,.78f,.64f,.7f,.48f};
            foreach(string room in KenneyRooms)
            {
                var scene=EditorSceneManager.OpenScene("Assets/Scenes/"+room+".unity");
                var flames=Object.FindObjectsByType<MeshRenderer>().Where(r=>r.name=="Warm flame").OrderBy(r=>r.transform.position.x).ToList();
                if(flames.Count==0)continue;
                var oldFx=GameObject.Find("Hearth fire FX");if(oldFx)Object.DestroyImmediate(oldFx);
                var fx=new GameObject("Hearth fire FX").transform;
                for(int i=0;i<flames.Count;i++)
                {
                    var f=flames[i];f.GetComponent<MeshFilter>().sharedMesh=flameMesh;
                    // Fuller in the middle of the grate, lower and narrower at the edges.
                    float centre=1-Mathf.Abs(i-(flames.Count-1)*.5f)/(flames.Count*.5f);
                    f.transform.position=new Vector3(.14f+i*.12f,.29f,3.08f+(i%2)*.05f);f.transform.rotation=Quaternion.Euler(0,i*23,0);
                    f.transform.localScale=new Vector3(.26f+.1f*centre,heights[i%heights.Length]*(.75f+.35f*centre),.26f+.1f*centre);
                }
                // Logs: the procedural charred cylinders become glowing bark, plus two crossed logs on top.
                foreach(var r in Object.FindObjectsByType<MeshRenderer>().Where(r=>r.name=="Charred hearth log"))r.sharedMaterial=embers;
                for(int k=0;k<2;k++)
                {
                    var log=GameObject.CreatePrimitive(PrimitiveType.Cylinder);log.name="Crossed hearth log";Object.DestroyImmediate(log.GetComponent<Collider>());log.transform.SetParent(fx,false);
                    log.transform.position=new Vector3(.32f+k*.36f,.42f,3.04f);log.transform.rotation=Quaternion.Euler(18,k==0?35:-35,90);log.transform.localScale=new Vector3(.1f,.3f,.1f);
                    log.GetComponent<Renderer>().sharedMaterial=embers;
                }
                var bed=Object.FindObjectsByType<MeshRenderer>().FirstOrDefault(r=>r.name=="Fire bed");if(bed)bed.sharedMaterial=embers;
                // Sparks drifting up the chimney.
                var spark=new GameObject("Hearth sparks");spark.transform.SetParent(fx,false);spark.transform.position=new Vector3(.5f,.36f,3.08f);
                var ps=spark.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                var main=ps.main;main.loop=true;main.playOnAwake=true;main.startLifetime=new ParticleSystem.MinMaxCurve(.8f,2.2f);main.startSpeed=new ParticleSystem.MinMaxCurve(.25f,.75f);
                main.startSize=new ParticleSystem.MinMaxCurve(.006f,.016f);main.gravityModifier=-.06f;main.maxParticles=60;main.simulationSpace=ParticleSystemSimulationSpace.World;
                main.startColor=new ParticleSystem.MinMaxGradient(new Color(1,.55f,.18f),new Color(1,.85f,.45f));
                var emission=ps.emission;emission.rateOverTime=14;
                var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(.75f,.22f,.04f);shape.rotation=new Vector3(-90,0,0);
                var noise=ps.noise;noise.enabled=true;noise.strength=.35f;noise.frequency=1.4f;noise.scrollSpeed=.7f;
                var colour=ps.colorOverLifetime;colour.enabled=true;var gradient=new Gradient();
                gradient.SetKeys(new[]{new GradientColorKey(new Color(1,.9f,.6f),0),new GradientColorKey(new Color(1,.35f,.08f),.6f),new GradientColorKey(new Color(.6f,.08f,.02f),1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(.9f,.5f),new GradientAlphaKey(0,1)});colour.color=gradient;
                var psr=spark.GetComponent<ParticleSystemRenderer>();psr.sharedMaterial=sparks;psr.renderMode=ParticleSystemRenderMode.Stretch;psr.velocityScale=.04f;psr.lengthScale=1.5f;psr.shadowCastingMode=ShadowCastingMode.Off;psr.receiveShadows=false;
                // Realtime flicker on top of the baked hearth glow.
                var lightGo=new GameObject("Hearth flicker light");lightGo.transform.SetParent(fx,false);lightGo.transform.position=new Vector3(.5f,.55f,2.6f);
                var light=lightGo.AddComponent<Light>();light.type=LightType.Point;light.color=new Color(1,.55f,.22f);light.intensity=1.1f;light.range=3.6f;light.shadows=LightShadows.None;light.lightmapBakeType=LightmapBakeType.Realtime;
                lightGo.AddComponent<FireFlicker>();
                EditorSceneManager.SaveScene(scene);
            }
        }
    }
}
