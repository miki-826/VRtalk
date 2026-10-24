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
    // Outdoor life: wind-swayed tree impostors, falling snow (or autumn leaves at Botanica), birds and ambient sound.
    public static partial class RealisticUpgrade
    {
        const string Trees="Assets/ThirdParty/PolyHaven/Trees";
        class Impostor { public Material material; public float frame, baseV, treeHeight; }
        // Kenney rooms look west through x=-4; Botanica through the x=-5 glazing and glass roof.
        static readonly Vector3[] SnowTrees={new Vector3(-7.5f,5.5f,-4.5f),new Vector3(-9,6.5f,1),new Vector3(-8,5,5.5f),new Vector3(-12,7.5f,-1.5f),new Vector3(-13.5f,8,4),new Vector3(-11,7,-7.5f),new Vector3(-15.5f,8.5f,9),new Vector3(-16,9,-4)};
        static readonly Vector3[] ParkTrees={new Vector3(-9,7.5f,-4),new Vector3(-11,9,2.5f),new Vector3(-14,10,-1),new Vector3(-10,8,7.5f),new Vector3(-16,11,6),new Vector3(-13,9.5f,-8.5f)};

        [MenuItem("Quest AI/Add ambient life (snow, wind trees, birds, sounds) and render")]
        public static void AmbientAndRender(){AddAmbientLife();ContentValidation.RenderAll();}

        public static void AddAmbientLife()
        {
            Directory.CreateDirectory(Output+"/Textures");Directory.CreateDirectory(Output+"/Materials");
            var fir=CaptureImpostor("fir_sapling",new Color(.86f,.92f,.88f));var park=CaptureImpostor("tree_small_02",new Color(1.05f,.95f,.78f));
            var snowyFir=Mat(Output+"/Materials/fir_sapling impostor snowy.mat");
            if(!snowyFir){snowyFir=new Material(fir.material);AssetDatabase.CreateAsset(snowyFir,Output+"/Materials/fir_sapling impostor snowy.mat");}
            snowyFir.CopyPropertiesFromMaterial(fir.material);snowyFir.SetFloat("_Snow",.6f);EditorUtility.SetDirty(snowyFir);
            var cards=CrossedCards();
            var snow=WeatherMaterial("Snowflake",false);var leaves=WeatherMaterial("Falling leaf",true);
            var birdMat=Mat(Output+"/Materials/Bird silhouette.mat");var unlit=Shader.Find("Universal Render Pipeline/Unlit");
            if(!birdMat){birdMat=new Material(unlit);AssetDatabase.CreateAsset(birdMat,Output+"/Materials/Bird silhouette.mat");}
            birdMat.shader=unlit;birdMat.SetColor("_BaseColor",new Color(.07f,.065f,.06f));birdMat.enableInstancing=true;EditorUtility.SetDirty(birdMat);
            foreach(string room in Rooms)
            {
                var scene=EditorSceneManager.OpenScene("Assets/Scenes/"+room+".unity");
                var old=GameObject.Find("Ambient life");if(old)Object.DestroyImmediate(old);
                var root=new GameObject("Ambient life");bool cafe=room=="BotanicaCafe";
                var life=root.AddComponent<AmbientLife>();
                var spots=new List<Transform>();int seed=0;
                foreach(var p in cafe?ParkTrees:SnowTrees)spots.Add(PlaceImpostor(cards,cafe?park:fir,cafe?park.material:snowyFir,root.transform,new Vector3(p.x,-.15f,p.z),p.y,seed++*73+11));
                life.chirpSpots=spots.ToArray();
                life.windowCenter=cafe?new Vector3(-5,1.8f,0):new Vector3(-4.1f,1.9f,0);
                life.flightCentre=cafe?new Vector3(-17,7,0):new Vector3(-18,6,0);life.flightDirection=Vector3.forward;life.flightLength=55;life.flightHeight=3;
                life.birdMaterial=birdMat;
                var flame=Object.FindObjectsByType<MeshRenderer>().FirstOrDefault(r=>r.name=="Warm flame");
                if(flame){life.hasHearth=true;life.hearth=new Vector3(.5f,.45f,3.05f);}
                life.weather=Weather(root.transform,cafe,cafe?leaves:snow);life.weatherDrift=cafe?.6f:.45f;
                EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();Debug.Log("AMBIENT_LIFE_APPLIED");
        }

        // ---------- Impostor capture ----------
        static Impostor CaptureImpostor(string asset,Color tint)
        {
            string folder=Trees+"/"+asset,fbx=folder+"/"+asset+".fbx";
            var importer=(ModelImporter)AssetImporter.GetAtPath(fbx);
            if(importer.generateSecondaryUV||importer.importAnimation){importer.generateSecondaryUV=false;importer.importAnimation=false;importer.importCameras=false;importer.importLights=false;importer.SaveAndReimport();}
            // The capture scene must exist first: opening a scene unloads unreferenced in-memory materials.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            // Plain URP Lit materials are enough for the one-off capture.
            var lit=Shader.Find("Universal Render Pipeline/Lit");var sets=new Dictionary<string,Material>();
            foreach(string diffuse in Directory.GetFiles(folder+"/textures","*_diff.*").Select(f=>f.Replace('\\','/')).Where(f=>!f.EndsWith(".meta")))
            {
                string set=Path.GetFileNameWithoutExtension(diffuse);set=set.Substring(0,set.Length-5);string tex=folder+"/textures/"+set;
                string alpha=File.Exists(tex+"_alpha.png")?tex+"_alpha.png":null;
                var m=new Material(lit){name=set};m.SetTexture("_BaseMap",alpha!=null?PackAlbedoAlpha(diffuse,alpha,set,1024):Import(diffuse,true,false,1024));m.SetFloat("_Smoothness",.15f);
                if(File.Exists(tex+"_nor_gl.jpg")){m.SetTexture("_BumpMap",Import(tex+"_nor_gl.jpg",false,true,1024));m.EnableKeyword("_NORMALMAP");}
                if(alpha!=null){m.SetFloat("_AlphaClip",1);m.EnableKeyword("_ALPHATEST_ON");m.SetFloat("_Cutoff",.4f);m.SetFloat("_Cull",0);m.renderQueue=(int)RenderQueue.AlphaTest;}
                sets[set.Length>asset.Length?set.Substring(asset.Length+1).ToLowerInvariant():""]=m;
            }
            var tree=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
            foreach(var r in tree.GetComponentsInChildren<Renderer>())r.sharedMaterials=r.sharedMaterials.Select(m=>{string n=m?m.name.ToLowerInvariant():"";
                var key=sets.Keys.Where(k=>k!=""&&n.Contains(k)).OrderByDescending(k=>k.Length).FirstOrDefault()??(sets.ContainsKey("")?"":sets.Keys.First());return sets[key];}).ToArray();
            // Some sources hold several specimens side by side; keep only the tallest.
            var specimens=tree.transform.Cast<Transform>().Where(t=>t.GetComponentInChildren<Renderer>()).ToList();
            if(specimens.Count>1){var tallest=specimens.OrderByDescending(t=>BoundsOf(t.gameObject).size.y).First();foreach(var t in specimens)if(t!=tallest)t.gameObject.SetActive(false);}
            var bounds=BoundsOf(tree);
            // Light from above-front so upward-facing branches are brightest (the shader turns those into snow).
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.42f,.45f,.48f);
            var sun=new GameObject("Capture sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.6f;sun.color=new Color(1,.97f,.92f);sun.transform.rotation=Quaternion.Euler(65,160,0);
            float size=Mathf.Max(bounds.size.y,bounds.size.x)*.52f;
            var camera=new GameObject("Capture camera").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=size;camera.aspect=1;camera.allowHDR=false;camera.allowMSAA=true;
            camera.transform.position=bounds.center-Vector3.forward*(bounds.extents.z+20);camera.transform.rotation=Quaternion.identity;camera.nearClipPlane=.1f;camera.farClipPlane=bounds.size.z+40;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            // Render on black and on white: alpha = 1 - (white - black), colour = black / alpha. Robust to how the pipeline treats alpha.
            const int res=1024;Color[] Shot(Color bg)
            {
                camera.backgroundColor=bg;var rt=new RenderTexture(res,res,24,RenderTextureFormat.ARGB32){antiAliasing=4};camera.targetTexture=rt;camera.Render();
                var old=RenderTexture.active;RenderTexture.active=rt;var img=new Texture2D(res,res,TextureFormat.RGBA32,false);img.ReadPixels(new Rect(0,0,res,res),0,0);RenderTexture.active=old;
                camera.targetTexture=null;rt.Release();var px=img.GetPixels();Object.DestroyImmediate(img);return px;
            }
            var black=Shot(new Color(0,0,0,0));var white=Shot(new Color(1,1,1,0));var outPx=new Color[res*res];
            for(int i=0;i<outPx.Length;i++)
            {
                float a=Mathf.Clamp01(1-((white[i].r-black[i].r)+(white[i].g-black[i].g)+(white[i].b-black[i].b))/3);
                outPx[i]=a>.01f?new Color(black[i].r/a,black[i].g/a,black[i].b/a,a):new Color(0,0,0,0);
            }
            var output=new Texture2D(res,res,TextureFormat.RGBA32,false);output.SetPixels(outPx);string path=Output+"/Textures/"+asset+"_impostor.png";File.WriteAllBytes(path,output.EncodeToPNG());Object.DestroyImmediate(output);
            AssetDatabase.ImportAsset(path);var ti=(TextureImporter)AssetImporter.GetAtPath(path);ti.alphaIsTransparency=true;ti.mipmapEnabled=true;ti.wrapMode=TextureWrapMode.Clamp;ti.maxTextureSize=1024;ti.SaveAndReimport();
            var result=new Impostor{frame=size*2,baseV=(bounds.min.y-(bounds.center.y-size))/(size*2),treeHeight=bounds.size.y};
            var shader=Shader.Find("QuestAI/Wind Impostor");string matPath=Output+"/Materials/"+asset+" impostor.mat";var mat=Mat(matPath);
            if(!mat){mat=new Material(shader);AssetDatabase.CreateAsset(mat,matPath);}
            mat.shader=shader;mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(path));mat.SetColor("_Tint",tint);mat.SetFloat("_Base",result.baseV);mat.SetFloat("_Sway",.09f);mat.SetFloat("_Snow",0);mat.enableInstancing=true;EditorUtility.SetDirty(mat);
            result.material=mat;Debug.Log("IMPOSTOR "+asset+" tree "+bounds.size.ToString("F2")+" base "+result.baseV.ToString("F3"));
            return result;
        }
        static Mesh CrossedCards()
        {
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            for(int k=0;k<2;k++)
            {
                var r=Quaternion.Euler(0,k*90,0);int s=vertices.Count;
                foreach(var p in new[]{new Vector3(-.5f,0,0),new Vector3(.5f,0,0),new Vector3(-.5f,1,0),new Vector3(.5f,1,0)})vertices.Add(r*p);
                uv.AddRange(new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one});triangles.AddRange(new[]{s,s+2,s+1,s+1,s+2,s+3});
            }
            var mesh=new Mesh{name="Impostor cards"};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            var b=mesh.bounds;b.Expand(.3f);mesh.bounds=b;
            string path=Output+"/Impostor cards.asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(existing){EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);return existing;}
            AssetDatabase.CreateAsset(mesh,path);return mesh;
        }
        static Transform PlaceImpostor(Mesh cards,Impostor impostor,Material material,Transform parent,Vector3 ground,float height,int seed)
        {
            var go=new GameObject("Wind tree",typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(parent,false);
            float scale=height*impostor.frame/impostor.treeHeight;
            go.transform.position=ground-Vector3.up*impostor.baseV*scale;go.transform.rotation=Quaternion.Euler(0,(seed*47)%360,0);go.transform.localScale=Vector3.one*scale;
            go.GetComponent<MeshFilter>().sharedMesh=cards;var r=go.GetComponent<MeshRenderer>();r.sharedMaterial=material;
            r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;r.lightProbeUsage=LightProbeUsage.Off;r.reflectionProbeUsage=ReflectionProbeUsage.Off;
            return go.transform;
        }

        // ---------- Snow / leaves ----------
        static Material WeatherMaterial(string name,bool leaf)
        {
            string texPath=Output+"/Textures/"+name+".png";const int size=64;var tex=new Texture2D(size,size,TextureFormat.RGBA32,false);var px=new Color[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float u=(x+.5f)/size*2-1,v=(y+.5f)/size*2-1;float a;
                // Leaf: pointed ellipse with a darker midrib; snow: soft round flake.
                if(leaf){float w=.55f*(1-v*v);a=Mathf.Clamp01((w-Mathf.Abs(u))*12);float rib=Mathf.Abs(u)<.05f?.75f:1;px[y*size+x]=new Color(rib,rib,rib,a);}
                else{float r=Mathf.Sqrt(u*u+v*v);a=Mathf.Pow(Mathf.Clamp01(1-r),1.6f);px[y*size+x]=new Color(1,1,1,a);}
            }
            tex.SetPixels(px);File.WriteAllBytes(texPath,tex.EncodeToPNG());Object.DestroyImmediate(tex);AssetDatabase.ImportAsset(texPath);
            var ti=(TextureImporter)AssetImporter.GetAtPath(texPath);ti.alphaIsTransparency=true;ti.wrapMode=TextureWrapMode.Clamp;ti.SaveAndReimport();
            string path=Output+"/Materials/"+name+".mat";var shader=Shader.Find("Universal Render Pipeline/Particles/Unlit");var m=Mat(path);if(!m){m=new Material(shader);AssetDatabase.CreateAsset(m,path);}
            m.shader=shader;m.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));m.SetColor("_BaseColor",Color.white);
            m.SetFloat("_Surface",1);m.SetFloat("_Blend",0);m.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);m.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);m.SetFloat("_ZWrite",0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");m.SetOverrideTag("RenderType","Transparent");m.renderQueue=(int)RenderQueue.Transparent;EditorUtility.SetDirty(m);return m;
        }
        static ParticleSystem Weather(Transform parent,bool leaves,Material material)
        {
            var go=new GameObject(leaves?"Falling leaves":"Falling snow");go.transform.SetParent(parent,false);
            // Spawn band above the outdoor area; everything stays on the far side of the glass.
            go.transform.position=leaves?new Vector3(-10,8,0):new Vector3(-9,6.5f,0);
            var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;main.loop=true;main.playOnAwake=true;main.prewarm=true;main.simulationSpace=ParticleSystemSimulationSpace.World;
            main.startSpeed=0;main.startLifetime=leaves?11:10;main.maxParticles=leaves?120:3200;
            main.startSize=leaves?new ParticleSystem.MinMaxCurve(.05f,.09f):new ParticleSystem.MinMaxCurve(.02f,.045f);
            main.startRotation=new ParticleSystem.MinMaxCurve(0,Mathf.PI*2);
            main.startColor=leaves?new ParticleSystem.MinMaxGradient(new Color(.85f,.38f,.1f),new Color(.9f,.72f,.25f)):new ParticleSystem.MinMaxGradient(Color.white);
            var emission=ps.emission;emission.rateOverTime=leaves?9:300;
            var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=leaves?new Vector3(8,1,18):new Vector3(8.2f,1,16);
            var velocity=ps.velocityOverLifetime;velocity.enabled=true;velocity.space=ParticleSystemSimulationSpace.World;
            velocity.x=new ParticleSystem.MinMaxCurve(-.2f);velocity.y=new ParticleSystem.MinMaxCurve(leaves?-.8f:-.65f);velocity.z=new ParticleSystem.MinMaxCurve(leaves?.15f:.05f);
            var noise=ps.noise;noise.enabled=true;noise.strength=leaves?.9f:.28f;noise.frequency=leaves?.8f:.6f;noise.scrollSpeed=.4f;
            if(leaves){var spin=ps.rotationOverLifetime;spin.enabled=true;spin.z=new ParticleSystem.MinMaxCurve(-3,3);}
            var fade=ps.colorOverLifetime;fade.enabled=true;var g=new Gradient();g.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.08f),new GradientAlphaKey(1,.9f),new GradientAlphaKey(0,1)});fade.color=g;
            var r=go.GetComponent<ParticleSystemRenderer>();r.sharedMaterial=material;r.renderMode=ParticleSystemRenderMode.Billboard;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;r.sortMode=ParticleSystemSortMode.None;
            return ps;
        }
    }
}
