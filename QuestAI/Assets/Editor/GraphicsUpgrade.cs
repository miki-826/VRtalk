using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace QuestAI.Editor
{
    public static class GraphicsUpgrade
    {
        const string Maps="Assets/ThirdParty/PolyHaven/Botanica/";
        const string Output="Assets/Generated/Graphics";
        static readonly string[] Rooms={"Bedroom","LivingRoom","Cafe","MainMenu","BotanicaCafe"};
        static Material Material(string name)=>AssetDatabase.LoadAssetAtPath<Material>("Assets/Generated/Materials/"+name+".mat");
        static Texture2D Map(string set,string suffix,bool normal=false)
        {
            string path=Maps+set+"_"+suffix+(suffix=="mask"?".png":".jpg");
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
            importer.sRGBTexture=suffix=="albedo";importer.mipmapEnabled=true;importer.anisoLevel=4;importer.maxTextureSize=suffix=="albedo"?2048:1024;importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        static void Surface(Material material,string set,Color tint,float bump,bool albedo)
        {
            if(albedo)material.SetTexture("_BaseMap",Map(set,"albedo"));
            material.SetColor("_BaseColor",tint);material.SetTexture("_BumpMap",Map(set,"normal",true));material.SetFloat("_BumpScale",bump);material.EnableKeyword("_NORMALMAP");
            var mask=Map(set,"mask");material.SetTexture("_MetallicGlossMap",mask);material.SetFloat("_Smoothness",1);material.EnableKeyword("_METALLICSPECGLOSSMAP");material.SetTexture("_OcclusionMap",mask);material.SetFloat("_OcclusionStrength",.6f);material.EnableKeyword("_OCCLUSIONMAP");material.mainTextureScale=Vector2.one;EditorUtility.SetDirty(material);
        }
        [MenuItem("Quest AI/Upgrade room graphics and bake")]
        public static void UpgradeAndBake()
        {
            Apply();ContentValidation.BakeAll();
        }
        // Batch entry: apply, bake every room, then refresh tangents and render review images.
        public static void UpgradeBakeAndRender()
        {
            UpgradeAndBake();RefreshSurfaceTangents();
        }
        // Kenney FBX files ship without lightmap UVs, so baking onto overlapping UV0 produced blotches on furniture.
        static void FurnitureLightmapUVs()
        {
            foreach(string guid in AssetDatabase.FindAssets("t:Model",new[]{"Assets/ThirdParty/Kenney/Models"}))
            {
                var importer=AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) as ModelImporter;
                if(!importer||importer.generateSecondaryUV)continue;
                importer.generateSecondaryUV=true;importer.secondaryUVPackMargin=8;importer.SaveAndReimport();
            }
        }
        static void Finish(string name,float smoothness,float metallic=0)
        {
            var material=Material(name);if(!material)return;
            material.SetFloat("_Smoothness",smoothness);material.SetFloat("_Metallic",metallic);EditorUtility.SetDirty(material);
        }
        public static void Apply()
        {
            Directory.CreateDirectory(Output);
            var wood=Material("Warm oak");var plaster=Material("Warm plaster");var rug=Material("Terracotta wool");var green=Material("Forest textile");var stone=Material("Hearth stone");
            Surface(wood,"oak_veneer_01",new Color(.88f,.82f,.74f),.4f,true);
            Surface(plaster,"painted_plaster_wall",new Color(.91f,.88f,.81f),.35f,true);
            Surface(rug,"rough_linen",new Color(.38f,.23f,.16f),.7f,false);
            Surface(green,"rough_linen",new Color(.18f,.25f,.21f),.55f,false);
            stone.mainTextureScale=Vector2.one;EditorUtility.SetDirty(stone);
            FurnitureLightmapUVs();
            Finish("Kenney_wood",.32f);Finish("Kenney_woodDark",.36f);Finish("Kenney_carpet",.04f);Finish("Kenney_carpetDarker",.04f);Finish("Kenney_carpetWhite",.06f);
            Finish("Kenney_metal",.55f,.7f);Finish("Kenney_metalMedium",.45f,.6f);Finish("Kenney_lamp",.2f);Finish("Kenney_plant",.18f);Finish("Kenney__defaultMat",.2f);
            foreach(string room in Rooms)
            {
                var scene=EditorSceneManager.OpenScene("Assets/Scenes/"+room+".unity");
                if(room!="BotanicaCafe")
                {
                    int index=0;
                    foreach(var filter in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
                    {
                        var renderer=filter.GetComponent<MeshRenderer>();if(!renderer)continue;
                        string name=filter.name;
                        if(name=="Back wall"||name=="Behind user"||name=="Ceiling")renderer.sharedMaterial=plaster;
                        var material=renderer.sharedMaterial;
                        if(material!=wood&&material!=plaster&&material!=rug&&material!=green&&material!=stone)continue;
                        if(filter.sharedMesh.vertexCount!=24)continue;
                        float tile=material==rug||material==green?.3f:material==wood?1.2f:2;
                        var mesh=Object.Instantiate(filter.sharedMesh);mesh.name="Room surface UV";
                        var vertices=mesh.vertices;var normals=mesh.normals;var uv=new Vector2[vertices.Length];
                        for(int i=0;i<vertices.Length;i++)
                        {
                            var p=filter.transform.TransformPoint(vertices[i]);var n=filter.transform.TransformDirection(normals[i]);
                            n=new Vector3(Mathf.Abs(n.x),Mathf.Abs(n.y),Mathf.Abs(n.z));
                            uv[i]=(n.y>=n.x&&n.y>=n.z?new Vector2(p.x,p.z):n.x>=n.z?new Vector2(p.z,p.y):new Vector2(p.x,p.y))/tile;
                        }
                        mesh.uv=uv;mesh.RecalculateTangents();string path=Output+"/"+room+"-"+(index++)+".asset";
                        var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(existing){EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);mesh=existing;}else AssetDatabase.CreateAsset(mesh,path);
                        filter.sharedMesh=mesh;
                    }
                }
                // Small furniture gets denser lightmap texels so contact shadows stay crisp.
                foreach(var renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                {
                    if(!renderer.sharedMaterials.Any(m=>m&&m.name.StartsWith("Kenney_")))continue;
                    var so=new SerializedObject(renderer);so.FindProperty("m_ScaleInLightmap").floatValue=2;so.ApplyModifiedPropertiesWithoutUndo();
                }
                var lighting=Object.FindAnyObjectByType<EnvironmentLighting>();
                lighting.ambient=new[]{new Color(.67f,.63f,.56f),new Color(.73f,.78f,.86f),new Color(.57f,.51f,.43f),new Color(.14f,.18f,.27f)};
                lighting.reflectionProbe.resolution=256;
                var settings=Lightmapping.lightingSettings;
                settings.lightmapResolution=20;settings.lightmapMaxSize=1024;settings.directSampleCount=64;settings.indirectSampleCount=128;settings.environmentSampleCount=64;
                var serialized=new SerializedObject(settings);serialized.FindProperty("m_AO").boolValue=true;serialized.FindProperty("m_AOMaxDistance").floatValue=.3f;serialized.FindProperty("m_CompAOExponent").floatValue=.65f;serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(settings);EditorUtility.SetDirty(lighting);EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();Debug.Log("GRAPHICS_UPGRADE_APPLIED");
        }
        public static void RefreshSurfaceTangents()
        {
            FixSurfaceTangents();ContentValidation.RenderAll();
        }
        public static void FixSurfaceTangents()
        {
            foreach(string guid in AssetDatabase.FindAssets("t:Mesh",new[]{Output}))
            {
                var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(AssetDatabase.GUIDToAssetPath(guid));mesh.RecalculateTangents();EditorUtility.SetDirty(mesh);
            }
            AssetDatabase.SaveAssets();
        }
    }
}
