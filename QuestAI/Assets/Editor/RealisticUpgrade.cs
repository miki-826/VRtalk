using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace QuestAI.Editor
{
    // Console-style realism pass: photoscanned CC0 furniture (Poly Haven) in place of the low-poly Kenney set,
    // QuestAI/Realistic Surface on every lit material, photographic window views, contact shadow and filmic post-processing.
    // Fetch the models first with tools/download_polyhaven_models.py.
    public static partial class RealisticUpgrade
    {
        const string Models="Assets/ThirdParty/PolyHaven/Models";
        const string Output="Assets/Generated/Realistic";
        const string Panorama="Assets/ThirdParty/PolyHaven/snowy_forest_path_01_panorama.jpg";
        const string GradePath="Assets/Resources/Graphics/CinematicGrade.asset";
        static readonly string[] KenneyRooms={"Bedroom","LivingRoom","Cafe","MainMenu"};
        static readonly string[] Rooms={"Bedroom","LivingRoom","Cafe","MainMenu","BotanicaCafe"};
        enum Model { None, Fabric, Skin, Foliage }
        // Kenney object name -> Poly Haven asset. Poly Haven models are real-world scale, so they keep their size.
        static readonly Dictionary<string,string> Replacements=new Dictionary<string,string>{
            {"loungeSofa","sofa_03"},{"loungeChairRelax","mid_century_lounge_chair"},{"tableCoffee","modern_coffee_table_01"},
            {"bookcaseOpen","wooden_display_shelves_01"},{"bookcaseOpenLow","modern_wooden_cabinet"},{"pottedPlant","potted_plant_02"},
            {"plantSmall1","potted_plant_04"},{"plantSmall2","potted_plant_04"},{"bedDouble","GothicBed_01"},
            {"cabinetBedDrawer","ClassicNightstand_01"},{"lampRoundTable","vintage_oil_lamp"},{"chairCushion","GreenChair_01"},
            {"tableRound","round_wooden_table_01"},{"books","book_encyclopedia_set_01"}};
        // Extra yaw so each model's front matches the Kenney piece it replaces.
        static readonly Dictionary<string,float> YawOffset=new Dictionary<string,float>();
        static readonly HashSet<string> Varnished=new HashSet<string>{"modern_coffee_table_01","ClassicNightstand_01","modern_wooden_cabinet","wooden_display_shelves_01","round_wooden_table_01","mid_century_lounge_chair","round_wooden_table_02","dining_chair_02"};
        static readonly HashSet<string> Cloth=new HashSet<string>{"sofa_03","GreenChair_01","throw_pillows_01","modern_arm_chair_01"};

        static Shader Realistic=>Shader.Find("QuestAI/Realistic Surface");

        [MenuItem("Quest AI/Realistic upgrade (models, shaders, post) and bake")]
        public static void ApplyBakeAndRender()
        {
            Apply();ContentValidation.BakeRooms(Rooms,false);GraphicsUpgrade.FixSurfaceTangents();ContentValidation.RenderAll();
        }
        // Quick look without the long lightmap bake (new furniture is unlit by lightmaps until baked).
        public static void ApplyAndPreview(){Apply();ContentValidation.RenderAll();}
        // Material/character tweaks only; lightmaps stay valid.
        public static void RefreshMaterialsAndPreview(){UpgradeMaterials();AddContactShadow();AssetDatabase.SaveAssets();ContentValidation.RenderAll();}
        public static void Apply()
        {
            Directory.CreateDirectory(Output+"/Materials");Directory.CreateDirectory(Output+"/Textures");Directory.CreateDirectory("Assets/Resources/Graphics");
            AssetDatabase.Refresh();
            ConfigurePipeline();
            var models=ImportModels();
            foreach(string room in KenneyRooms)ReplaceFurniture(room,models);
            ReplaceBotanica(models);
            // Rebuild the procedural plants/glass layer now that the Kenney plants are gone, then restyle it.
            NaturalSceneUpgrade.Apply();
            UpgradeHearths();
            UpgradeMaterials();
            foreach(string room in Rooms)FinishRoom(room);
            AddContactShadow();
            AddAmbientLife();
            AssetDatabase.SaveAssets();Debug.Log("REALISTIC_UPGRADE_APPLIED");
        }

        // ---------- Pipeline and post-processing ----------
        static void ConfigurePipeline()
        {
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Generated/QuestURP.asset");
            pipeline.supportsHDR=true;pipeline.hdrColorBufferPrecision=HDRColorBufferPrecision._32Bits;
            var serialized=new SerializedObject(pipeline);serialized.FindProperty("m_ReflectionProbeBoxProjection").boolValue=true;serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);
            var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Generated/Renderer.asset");
            if(!renderer.postProcessData){renderer.postProcessData=AssetDatabase.LoadAssetAtPath<PostProcessData>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");EditorUtility.SetDirty(renderer);}
            if(AssetDatabase.LoadAssetAtPath<VolumeProfile>(GradePath))AssetDatabase.DeleteAsset(GradePath);
            var profile=ScriptableObject.CreateInstance<VolumeProfile>();
            var tone=profile.Add<Tonemapping>(true);tone.mode.Override(TonemappingMode.ACES);
            var bloom=profile.Add<Bloom>(true);bloom.threshold.Override(1.1f);bloom.intensity.Override(.3f);bloom.scatter.Override(.6f);bloom.highQualityFiltering.Override(false);bloom.downscale.Override(BloomDownscaleMode.Quarter);bloom.maxIterations.Override(4);
            var grade=profile.Add<ColorAdjustments>(true);grade.postExposure.Override(.45f);grade.contrast.Override(8);grade.saturation.Override(4);
            var balance=profile.Add<WhiteBalance>(true);balance.temperature.Override(3);
            AssetDatabase.CreateAsset(profile,GradePath);foreach(var c in profile.components)AssetDatabase.AddObjectToAsset(c,profile);
            AssetDatabase.SaveAssets();
        }

        // ---------- Models, textures and materials ----------
        class TextureSet { public string name,part; public Texture2D albedo,normal,mask; public bool alpha; }
        static Texture2D Import(string path,bool srgb,bool normal,int max)
        {
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;importer.sRGBTexture=srgb&&!normal;importer.mipmapEnabled=true;importer.anisoLevel=4;importer.maxTextureSize=max;
            importer.alphaIsTransparency=false;importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        static Texture2D Load(string path){var t=new Texture2D(2,2,TextureFormat.RGBA32,false,true);t.LoadImage(File.ReadAllBytes(path));return t;}
        static float Sample(Texture2D t,float u,float v)=>t.GetPixelBilinear(u,v).r;
        // R metallic, G occlusion, A smoothness — the URP Lit mask layout read by the Realistic Surface shader.
        static Texture2D PackMask(string folder,string set,int max)
        {
            string tex=folder+"/textures/"+set;string dest=Output+"/Textures/"+set+"_mask.png";
            Texture2D rough=File.Exists(tex+"_rough.jpg")?Load(tex+"_rough.jpg"):null,metal=File.Exists(tex+"_metal.jpg")?Load(tex+"_metal.jpg"):null,ao=File.Exists(tex+"_ao.jpg")?Load(tex+"_ao.jpg"):null;
            int size=rough?rough.width:ao?ao.width:256;var result=new Texture2D(size,size,TextureFormat.RGBA32,false,true);var pixels=new Color[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float u=(x+.5f)/size,v=(y+.5f)/size;
                pixels[y*size+x]=new Color(metal?Sample(metal,u,v):0,ao?Sample(ao,u,v):1,0,rough?1-Sample(rough,u,v):.35f);
            }
            result.SetPixels(pixels);File.WriteAllBytes(dest,result.EncodeToPNG());
            foreach(var t in new[]{rough,metal,ao,result})if(t)Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(dest);return Import(dest,false,false,max);
        }
        static Texture2D PackAlbedoAlpha(string diffuse,string alpha,string set,int max)
        {
            string dest=Output+"/Textures/"+set+"_albedo.png";var color=Load(diffuse);var mask=Load(alpha);var pixels=color.GetPixels();
            for(int y=0;y<color.height;y++)for(int x=0;x<color.width;x++)pixels[y*color.width+x].a=Sample(mask,(x+.5f)/color.width,(y+.5f)/color.height);
            color.SetPixels(pixels);File.WriteAllBytes(dest,color.EncodeToPNG());Object.DestroyImmediate(color);Object.DestroyImmediate(mask);
            AssetDatabase.ImportAsset(dest);return Import(dest,true,false,max);
        }
        static void SetModel(Material m,Model model)
        {
            m.SetFloat("_QuestAI",(float)model);
            foreach(var k in new[]{"_QUESTAI_NONE","_QUESTAI_FABRIC","_QUESTAI_SKIN","_QUESTAI_FOLIAGE"})m.DisableKeyword(k);
            m.EnableKeyword("_QUESTAI_"+model.ToString().ToUpperInvariant());
        }
        static void Varnish(Material m,float amount,float gloss)
        {
            m.SetFloat("_ClearCoatOn",amount>0?1:0);m.SetFloat("_ClearCoatMask",amount);m.SetFloat("_ClearCoatSmoothness",gloss);
            if(amount>0)m.EnableKeyword("_CLEARCOAT");else m.DisableKeyword("_CLEARCOAT");
        }
        static Material BuildMaterial(string asset,TextureSet set)
        {
            string path=Output+"/Materials/"+set.name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!m){m=new Material(Realistic);AssetDatabase.CreateAsset(m,path);}
            m.shader=Realistic;m.shaderKeywords=new string[0];m.enableInstancing=true;
            m.SetTexture("_BaseMap",set.albedo);m.SetColor("_BaseColor",Color.white);
            if(set.normal){m.SetTexture("_BumpMap",set.normal);m.SetFloat("_BumpScale",1);m.EnableKeyword("_NORMALMAP");}
            m.SetTexture("_MetallicGlossMap",set.mask);m.SetFloat("_Smoothness",1);m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetTexture("_OcclusionMap",set.mask);m.SetFloat("_OcclusionStrength",1);m.EnableKeyword("_OCCLUSIONMAP");
            var model=set.part=="leaves"?Model.Foliage:Cloth.Contains(asset)&&set.part!="legs"?Model.Fabric:Model.None;SetModel(m,model);
            Varnish(m,Varnished.Contains(asset)?.45f:0,.88f);
            m.SetFloat("_Cull",set.alpha?0:2);m.SetFloat("_AlphaClip",set.alpha?1:0);m.SetFloat("_AlphaToMask",set.alpha?1:0);m.SetFloat("_Cutoff",.45f);
            if(set.alpha){m.EnableKeyword("_ALPHATEST_ON");m.renderQueue=(int)RenderQueue.AlphaTest;m.SetOverrideTag("RenderType","TransparentCutout");}else{m.renderQueue=-1;m.SetOverrideTag("RenderType","");}
            if(set.part=="flame"){m.EnableKeyword("_EMISSION");m.SetTexture("_EmissionMap",set.albedo);m.SetColor("_EmissionColor",new Color(1,.62f,.3f)*3);m.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;}
            EditorUtility.SetDirty(m);return m;
        }
        static Dictionary<string,GameObject> ImportModels()
        {
            var result=new Dictionary<string,GameObject>();
            foreach(string folder in Directory.GetDirectories(Models).Select(d=>d.Replace('\\','/')))
            {
                string asset=Path.GetFileName(folder);string fbx=folder+"/"+asset+".fbx";if(!File.Exists(fbx))continue;
                var importer=(ModelImporter)AssetImporter.GetAtPath(fbx);
                importer.generateSecondaryUV=true;importer.secondaryUVPackMargin=4;importer.importAnimation=false;importer.importCameras=false;importer.importLights=false;importer.isReadable=false;importer.meshCompression=ModelImporterMeshCompression.Off;
                importer.materialImportMode=ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                // Automatic Mesh LODs keep the dense photoscans (plants, books) affordable at a distance on Quest.
                var lods=new SerializedObject(importer).FindProperty("m_GenerateMeshLods");if(lods!=null){lods.boolValue=true;lods.serializedObject.ApplyModifiedPropertiesWithoutUndo();}
                importer.SaveAndReimport();
                int max=asset=="sofa_03"||asset=="GothicBed_01"?2048:1024;
                var sets=new List<TextureSet>();
                foreach(string diffuse in Directory.GetFiles(folder+"/textures","*_diff.jpg").Select(f=>f.Replace('\\','/')))
                {
                    string set=Path.GetFileNameWithoutExtension(diffuse);set=set.Substring(0,set.Length-5);
                    string tex=folder+"/textures/"+set;string alpha=File.Exists(tex+"_alpha.png")?tex+"_alpha.png":File.Exists(tex+"_opacity.png")?tex+"_opacity.png":null;
                    var s=new TextureSet{name=set,part=set.Length>asset.Length?set.Substring(asset.Length+1).ToLowerInvariant():"",alpha=alpha!=null};
                    s.albedo=alpha!=null?PackAlbedoAlpha(diffuse,alpha,set,max):Import(diffuse,true,false,max);
                    s.normal=File.Exists(tex+"_nor_gl.jpg")?Import(tex+"_nor_gl.jpg",false,true,max):null;
                    s.mask=PackMask(folder,set,max);sets.Add(s);
                }
                // The sofa's opacity map only trims a fringe; an opaque sofa is cheaper and shades better.
                foreach(var s in sets)if(asset=="sofa_03"&&s.alpha){s.alpha=false;s.albedo=Import(folder+"/textures/"+s.name+"_diff.jpg",true,false,max);}
                var materials=sets.ToDictionary(s=>s,s=>BuildMaterial(asset,s));
                var glass=AssetDatabase.LoadAssetAtPath<Material>("Assets/Generated/Natural/Window glass.mat");
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
                var remap=new Dictionary<string,Material>();
                foreach(var r in prefab.GetComponentsInChildren<Renderer>(true))foreach(var source in r.sharedMaterials)
                {
                    if(!source||remap.ContainsKey(source.name))continue;string name=source.name.ToLowerInvariant();
                    var match=sets.Where(s=>s.part!=""&&name.Contains(s.part)).OrderByDescending(s=>s.part.Length).FirstOrDefault()??sets.FirstOrDefault(s=>s.part=="")??sets.FirstOrDefault();
                    remap[source.name]=match!=null&&match.part=="glass"&&glass?glass:match!=null?materials[match]:null;
                }
                foreach(var pair in remap)if(pair.Value)importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),pair.Key),pair.Value);
                importer.SaveAndReimport();
                result[asset]=AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
                var b=BoundsOf(result[asset]);Debug.Log("REALISTIC_MODEL "+asset+" size "+b.size.ToString("F2")+" materials "+string.Join(",",remap.Select(p=>p.Key+"->"+(p.Value?p.Value.name:"none"))));
            }
            return result;
        }

        // ---------- Scene replacement ----------
        static Bounds BoundsOf(GameObject root)
        {
            var renderers=root.GetComponentsInChildren<Renderer>();var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);return bounds;
        }
        static Bounds LocalBounds(GameObject prefab,Quaternion rotation)
        {
            var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab);go.transform.SetPositionAndRotation(Vector3.zero,rotation);var b=BoundsOf(go);Object.DestroyImmediate(go);return b;
        }
        static void ReplaceFurniture(string room,Dictionary<string,GameObject> models)
        {
            var scene=EditorSceneManager.OpenScene("Assets/Scenes/"+room+".unity");
            // Kenney originals are kept inactive (not rendered or baked) so the pass can be re-run with new placements.
            var all=Object.FindObjectsByType<Transform>(FindObjectsInactive.Include);
            var originals=all.FirstOrDefault(t=>t.name=="Kenney originals"&&t.parent==null);if(!originals)originals=new GameObject("Kenney originals").transform;
            foreach(Transform child in originals.Cast<Transform>().ToList()){child.SetParent(null,true);child.gameObject.SetActive(true);}
            originals.gameObject.SetActive(false);
            var old=all.FirstOrDefault(t=>t&&t.name=="Realistic furniture"&&t.parent==null);if(old)Object.DestroyImmediate(old.gameObject);
            var parent=new GameObject("Realistic furniture").transform;
            var targets=Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).Where(t=>t&&t.parent==null&&Replacements.ContainsKey(t.name)).OrderBy(t=>BoundsOf(t.gameObject).min.y).ToList();
            var placed=new List<GameObject>();int shelfBooks=0;
            foreach(var target in targets)
            {
                string asset=Replacements[target.name];var bounds=BoundsOf(target.gameObject);float yaw=target.eulerAngles.y;
                // Kenney shelves held three rows of books; keep one encyclopedia set and swap one row for a vase.
                if(target.name=="books"&&bounds.min.y<1.3f&&bounds.min.y>.3f){shelfBooks++;if(shelfBooks==2)asset="ceramic_vase_01";else if(shelfBooks==3){target.SetParent(originals,true);continue;}}
                if(!models.TryGetValue(asset,out var prefab)){Debug.LogWarning("REALISTIC_MISSING "+asset);continue;}
                string name=target.name;target.SetParent(originals,true);
                var rotation=Quaternion.Euler(0,yaw+(YawOffset.TryGetValue(asset,out float o)?o:0),0)*prefab.transform.localRotation;var local=LocalBounds(prefab,rotation);
                var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);go.name="Realistic "+name;go.transform.SetParent(parent,true);
                // Real-world size; only shrink when a piece would badly overflow the space the layout left for it.
                float fit=Mathf.Min(1,Mathf.Max(bounds.size.x,bounds.size.z)*1.6f/Mathf.Max(local.size.x,local.size.z),bounds.size.y*2.2f/local.size.y);
                if(name=="books"||name=="lampRoundTable"||name.StartsWith("plantSmall"))fit=Mathf.Min(1,Mathf.Max(bounds.size.x,bounds.size.z)*1.4f/Mathf.Max(local.size.x,local.size.z));
                go.transform.rotation=rotation;go.transform.localScale=prefab.transform.localScale*fit;
                float floor=bounds.min.y<.05f?0:SupportHeight(new Vector3(bounds.center.x,bounds.min.y+.25f,bounds.center.z),placed,bounds.min.y);
                var now=BoundsOf(go);go.transform.position+=new Vector3(bounds.center.x,floor,bounds.center.z)-new Vector3(now.center.x,now.min.y,now.center.z);
                foreach(var r in go.GetComponentsInChildren<Renderer>())
                {
                    GameObjectUtility.SetStaticEditorFlags(r.gameObject,StaticEditorFlags.ContributeGI|StaticEditorFlags.ReflectionProbeStatic|StaticEditorFlags.OccludeeStatic);
                    var so=new SerializedObject(r);so.FindProperty("m_ScaleInLightmap").floatValue=name=="books"?1:2;so.ApplyModifiedPropertiesWithoutUndo();
                }
                placed.Add(go);Debug.Log("REALISTIC_PLACE "+room+" "+name+" -> "+asset+" scale "+fit.ToString("F2")+" at "+go.transform.position.ToString("F2"));
            }
            EditorSceneManager.SaveScene(scene);
        }
        // Finds the top of whatever now sits under a small item (new shelf, nightstand, mantel, window sill).
        static float SupportHeight(Vector3 from,List<GameObject> placed,float fallback)
        {
            var temp=new List<Collider>();
            foreach(var go in placed)foreach(var f in go.GetComponentsInChildren<MeshFilter>()){var c=f.gameObject.AddComponent<MeshCollider>();c.sharedMesh=f.sharedMesh;temp.Add(c);}
            Physics.SyncTransforms();float height=fallback;
            if(Physics.Raycast(from,Vector3.down,out var hit,1.2f))height=hit.point.y;
            foreach(var c in temp)Object.DestroyImmediate(c);return height;
        }

        // ---------- Materials across the project ----------
        static Material Mat(string path)=>AssetDatabase.LoadAssetAtPath<Material>(path);
        static void Upgrade(Material m,Model model=Model.None,float varnish=0,float gloss=.85f)
        {
            if(!m)return;var lit=Shader.Find("Universal Render Pipeline/Lit");
            if(m.shader!=lit&&m.shader!=Realistic)return;
            if(m.HasProperty("_Surface")&&m.GetFloat("_Surface")>0)return;
            m.shader=Realistic;SetModel(m,model);Varnish(m,varnish,gloss);m.enableInstancing=true;EditorUtility.SetDirty(m);
        }
        static void UpgradeMaterials()
        {
            const string g="Assets/Generated/Materials/";
            Upgrade(Mat(g+"Warm oak.mat"),Model.None,.5f,.88f);Upgrade(Mat(g+"Warm plaster.mat"));Upgrade(Mat(g+"Hearth stone.mat"));Upgrade(Mat(g+"Antique brass.mat"),Model.None,0);
            // Woven wool rug (photoscanned herringbone) instead of the flat-coloured linen box.
            var rug=Mat(g+"Terracotta wool.mat");const string wool="Assets/ThirdParty/PolyHaven/Textures/poly_wool_herringbone";
            if(rug&&File.Exists(wool+"_diff.jpg"))
            {
                rug.SetTexture("_BaseMap",Import(wool+"_diff.jpg",true,false,1024));rug.SetColor("_BaseColor",new Color(.95f,.7f,.56f));rug.mainTextureScale=Vector2.one*.4f;
                rug.SetTexture("_BumpMap",Import(wool+"_nor_gl.jpg",false,true,1024));rug.SetFloat("_BumpScale",1);rug.EnableKeyword("_NORMALMAP");
                var mask=PackMask("Assets/ThirdParty/PolyHaven","poly_wool_herringbone",1024);rug.SetTexture("_MetallicGlossMap",mask);rug.SetTexture("_OcclusionMap",mask);rug.SetFloat("_Smoothness",1);
                rug.EnableKeyword("_METALLICSPECGLOSSMAP");rug.EnableKeyword("_OCCLUSIONMAP");
            }
            Upgrade(rug,Model.Fabric);Upgrade(Mat(g+"Forest textile.mat"),Model.Fabric);
            foreach(string guid in AssetDatabase.FindAssets("Kenney_ t:Material",new[]{"Assets/Generated/Materials"}))
            {var m=Mat(AssetDatabase.GUIDToAssetPath(guid));Upgrade(m,m.name.Contains("carpet")?Model.Fabric:Model.None,m.name.Contains("wood")?.4f:0);}
            Upgrade(Mat("Assets/Generated/Natural/Charred logs.mat"));
            // WhiteChibi: soft scattering on the body/head, wet clear coat on the eyes.
            foreach(var name in new[]{"Chibi_Chibi_White","Chibi_Chibi_Head"}){var m=Mat(g+name+".mat");Upgrade(m,Model.Skin);if(m)m.SetFloat("_Smoothness",.42f);}
            Upgrade(Mat(g+"Chibi_Eyes • cocoa.mat"),Model.None,1,.96f);
            Upgrade(Mat(g+"Chibi_Smile • pink.mat"),Model.Skin);
            const string b="Assets/Generated/Botanica/Materials/";
            Upgrade(Mat(b+"MAT_SageUpholstery.mat"),Model.Fabric);Upgrade(Mat(b+"MAT_Wood_Warm.mat"),Model.None,.4f);Upgrade(Mat(b+"MAT_Wood_VerticalFlutes.mat"),Model.None,.4f);
            Upgrade(Mat(b+"MAT_Concrete.mat"),Model.None,.2f,.7f);Upgrade(Mat(b+"MAT_Ceramic.mat"),Model.None,.9f,.95f);
            foreach(var n in new[]{"MAT_Wall","MAT_Terracotta","MAT_Brass","MAT_BrushedSteel","MAT_Blackboard","MAT_PastryGolden","MAT_Coffee"})Upgrade(Mat(b+n+".mat"));
            AssetDatabase.SaveAssets();
        }

        // ---------- Per-room finishing ----------
        static void FinishRoom(string room)
        {
            var scene=EditorSceneManager.OpenScene("Assets/Scenes/"+room+".unity");
            var lighting=Object.FindAnyObjectByType<EnvironmentLighting>();var probe=lighting.reflectionProbe?lighting.reflectionProbe:Object.FindAnyObjectByType<ReflectionProbe>();
            if(probe){probe.boxProjection=true;probe.resolution=256;EditorUtility.SetDirty(probe);}
            if(room!="BotanicaCafe")
            {
                // A photographic snowy forest through the windows replaces the procedural snow plane and needle cards.
                foreach(var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
                    if(t.name=="Snow beyond window"||t.name=="Pine trunk"||t.name=="Distant pine"||t.name==room+"-Pine"||t.name==room+"-Grass")t.gameObject.SetActive(false);
                var sky=lighting.sky;var photo=AssetDatabase.LoadAssetAtPath<Texture2D>(Panorama);
                var importer=(TextureImporter)AssetImporter.GetAtPath(Panorama);if(importer.maxTextureSize!=4096){importer.maxTextureSize=4096;importer.SaveAndReimport();}
                sky.shader=Shader.Find("Skybox/Panoramic");sky.SetTexture("_MainTex",photo);sky.SetFloat("_Mapping",1);sky.SetFloat("_ImageType",0);sky.SetFloat("_Rotation",90);sky.SetColor("_Tint",new Color(.5f,.5f,.5f));EditorUtility.SetDirty(sky);
            }
            lighting.Apply(TimeMode.Evening,true);EditorUtility.SetDirty(lighting);EditorSceneManager.SaveScene(scene);
        }

        // ---------- Character ----------
        static void AddContactShadow()
        {
            // Radial falloff baked into a small texture, drawn with stock URP Unlit alpha blending.
            string texPath=Output+"/Textures/ContactShadow.png";const int size=128;var tex=new Texture2D(size,size,TextureFormat.RGBA32,false,true);var pixels=new Color[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++){float r=Mathf.Clamp01(new Vector2(x+.5f-size/2f,y+.5f-size/2f).magnitude/(size/2f));pixels[y*size+x]=new Color(0,0,0,Mathf.Pow(1-Mathf.SmoothStep(0,1,r),1.3f)*.62f);}
            tex.SetPixels(pixels);File.WriteAllBytes(texPath,tex.EncodeToPNG());Object.DestroyImmediate(tex);AssetDatabase.ImportAsset(texPath);
            var ti=(TextureImporter)AssetImporter.GetAtPath(texPath);ti.sRGBTexture=false;ti.alphaIsTransparency=true;ti.wrapMode=TextureWrapMode.Clamp;ti.mipmapEnabled=true;ti.SaveAndReimport();
            string matPath=Output+"/Materials/Contact shadow.mat";var mat=Mat(matPath);var unlit=Shader.Find("Universal Render Pipeline/Unlit");
            if(!mat){mat=new Material(unlit);AssetDatabase.CreateAsset(mat,matPath);}mat.shader=unlit;mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));mat.SetColor("_BaseColor",Color.white);
            mat.SetFloat("_Surface",1);mat.SetFloat("_Blend",0);mat.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);mat.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);mat.SetFloat("_SrcBlendAlpha",(float)BlendMode.One);mat.SetFloat("_DstBlendAlpha",(float)BlendMode.OneMinusSrcAlpha);mat.SetFloat("_ZWrite",0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");mat.SetOverrideTag("RenderType","Transparent");mat.renderQueue=(int)RenderQueue.Transparent-10;mat.enableInstancing=true;EditorUtility.SetDirty(mat);
            var quad=Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            foreach(string path in new[]{"calm","bright"}.Select(id=>AssetDatabase.LoadAssetAtPath<CharacterProfile>("Assets/Resources/Profiles/"+id+".asset")).Where(p=>p&&p.prefab).Select(p=>AssetDatabase.GetAssetPath(p.prefab)).Distinct())
            {
                var root=PrefabUtility.LoadPrefabContents(path);
                var existing=root.transform.Find("Contact shadow");if(existing)Object.DestroyImmediate(existing.gameObject);
                var go=new GameObject("Contact shadow",typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(root.transform,false);
                // Sized in world units (the rig root may carry an import scale); sits just above the 2.5 cm rug.
                float unit=root.transform.lossyScale.x;go.transform.localPosition=new Vector3(0,.045f/unit,0);go.transform.localRotation=Quaternion.Euler(90,0,0);go.transform.localScale=new Vector3(1.2f,1.2f,1)/unit;
                go.GetComponent<MeshFilter>().sharedMesh=quad;var r=go.GetComponent<MeshRenderer>();r.sharedMaterial=mat;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;r.lightProbeUsage=LightProbeUsage.Off;r.reflectionProbeUsage=ReflectionProbeUsage.Off;
                PrefabUtility.SaveAsPrefabAsset(root,path);PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
