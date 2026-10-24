using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestAI.Editor
{
    public static class NaturalSceneUpgrade
    {
        const string Folder="Assets/Generated/Natural";
        static readonly string[] Rooms={"Bedroom","LivingRoom","Cafe","MainMenu","BotanicaCafe"};
        static Material Mat(string name,string shader,Color color)
        {
            string path=Folder+"/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!m){m=new Material(Shader.Find(shader));AssetDatabase.CreateAsset(m,path);}m.shader=Shader.Find(shader);if(m.HasProperty("_BaseColor"))m.SetColor("_BaseColor",color);m.enableInstancing=true;EditorUtility.SetDirty(m);return m;
        }
        class Leaves
        {
            readonly List<Vector3> points=new List<Vector3>(),normals=new List<Vector3>();readonly List<Vector2> uv=new List<Vector2>();readonly List<Color> colors=new List<Color>();readonly List<int> triangles=new List<int>();
            public void Add(Vector3 origin,Quaternion rotation,float width,float length,int seed)
            {
                int start=points.Count;float tone=.78f+.25f*Mathf.Repeat(seed*.618034f,1);
                for(int row=0;row<=4;row++)for(int side=0;side<2;side++)
                {
                    float y=row/4f,x=side-.5f;
                    var p=new Vector3(x*width,y*length,Mathf.Sin(y*Mathf.PI)*length*.12f);
                    points.Add(origin+rotation*p);normals.Add(rotation*Vector3.back);uv.Add(new Vector2(side,y));colors.Add(new Color(tone,tone, .86f*tone,1));
                }
                for(int row=0;row<4;row++){int a=start+row*2;triangles.AddRange(new[]{a,a+2,a+1,a+1,a+2,a+3});}
            }
            public void Save(string name,Material material,Transform parent)
            {
                var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};mesh.SetVertices(points);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.SetColors(colors);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();var bounds=mesh.bounds;bounds.Expand(.15f);mesh.bounds=bounds;
                string path=Folder+"/"+name+".asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(existing){EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);mesh=existing;}else AssetDatabase.CreateAsset(mesh,path);
                var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(parent,false);go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterial=material;
            }
        }
        static void PreservePot(Transform plant,float foliageStart,string prefix)
        {
            int index=0;
            foreach(var filter in plant.GetComponentsInChildren<MeshFilter>())
            {
                var original=PrefabUtility.GetCorrespondingObjectFromOriginalSource(filter);
                var source=original?original.sharedMesh:filter.sharedMesh;var mesh=Object.Instantiate(source);var vertices=mesh.vertices;
                for(int sub=0;sub<mesh.subMeshCount;sub++)
                {
                    var sourceIndices=source.GetTriangles(sub);var kept=new List<int>();
                    for(int i=0;i<sourceIndices.Length;i+=3)
                    {
                        var center=(vertices[sourceIndices[i]]+vertices[sourceIndices[i+1]]+vertices[sourceIndices[i+2]])/3;
                        if(filter.transform.TransformPoint(center).y<=foliageStart)kept.AddRange(new[]{sourceIndices[i],sourceIndices[i+1],sourceIndices[i+2]});
                    }
                    mesh.SetTriangles(kept,sub,false);
                }
                string path=Folder+"/"+prefix+"-Pot"+(index++)+".asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(existing){EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);mesh=existing;}else AssetDatabase.CreateAsset(mesh,path);
                filter.sharedMesh=mesh;
            }
        }
        [MenuItem("Quest AI/Upgrade fire glass and plants")]
        public static void ApplyAndRender(){Apply();GraphicsUpgrade.FixSurfaceTangents();RefreshReflections();ContentValidation.RenderAll();}
        static void RefreshReflections()
        {
            foreach(string room in Rooms)
            {
                var scene=EditorSceneManager.OpenScene("Assets/Scenes/"+room+".unity");var lighting=Object.FindAnyObjectByType<EnvironmentLighting>();
                for(int phase=0;phase<4;phase++)
                {
                    var time=(TimeMode)(phase+1);lighting.Apply(time,true);lighting.reflectionProbe.mode=ReflectionProbeMode.Baked;
                    string path="Assets/Generated/Lighting/"+room+"/"+time+"/Reflection.exr";
                    if(!Lightmapping.BakeReflectionProbe(lighting.reflectionProbe,path))throw new System.InvalidOperationException("Reflection bake failed: "+room);
                    AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);lighting.bakes[phase].reflection=AssetDatabase.LoadAssetAtPath<Cubemap>(path);
                }
                lighting.Apply(TimeMode.Evening,true);EditorUtility.SetDirty(lighting);EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();
        }
        public static void Apply()
        {
            Directory.CreateDirectory(Folder);
            var fire=Mat("Hearth fire","QuestAI/Hearth Fire",Color.white);
            var charcoal=Mat("Charred logs","Universal Render Pipeline/Lit",new Color(.07f,.045f,.03f));charcoal.SetFloat("_Smoothness",.1f);
            var glass=Mat("Window glass","QuestAI/Architectural Glass",Color.white);glass.SetColor("_Tint",new Color(.78f,.9f,.86f,.025f));glass.SetFloat("_Reflection",.5f);
            var pine=Mat("Pine needles","QuestAI/Natural Leaves",new Color(.11f,.25f,.12f));pine.SetFloat("_Needles",1);pine.SetFloat("_Wind",.045f);
            var grass=Mat("Meadow grass","QuestAI/Natural Leaves",new Color(.23f,.35f,.1f));grass.SetFloat("_Wind",.035f);
            var leaves=Mat("Houseplant leaves","QuestAI/Natural Leaves",new Color(.15f,.32f,.1f));leaves.SetFloat("_Wind",.008f);
            var flameMesh=new Mesh{name="Crossed flame ribbons"};
            flameMesh.vertices=new[]{new Vector3(-.5f,0,0),new Vector3(.5f,0,0),new Vector3(-.5f,1,0),new Vector3(.5f,1,0),new Vector3(0,0,-.5f),new Vector3(0,0,.5f),new Vector3(0,1,-.5f),new Vector3(0,1,.5f)};
            flameMesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one,Vector2.zero,Vector2.right,Vector2.up,Vector2.one};flameMesh.triangles=new[]{0,2,1,1,2,3,4,6,5,5,6,7};flameMesh.RecalculateNormals();
            string flamePath=Folder+"/Flame.asset";var oldMesh=AssetDatabase.LoadAssetAtPath<Mesh>(flamePath);if(oldMesh){EditorUtility.CopySerialized(flameMesh,oldMesh);Object.DestroyImmediate(flameMesh);flameMesh=oldMesh;}else AssetDatabase.CreateAsset(flameMesh,flamePath);
            var botanicaGlass=AssetDatabase.LoadAssetAtPath<Material>("Assets/Generated/Botanica/Materials/MAT_Glass.mat");if(botanicaGlass){botanicaGlass.shader=glass.shader;botanicaGlass.SetColor("_Tint",new Color(.78f,.9f,.86f,.025f));botanicaGlass.SetFloat("_Reflection",.5f);EditorUtility.SetDirty(botanicaGlass);}
            foreach(string room in Rooms)
            {
                var scene=EditorSceneManager.OpenScene("Assets/Scenes/"+room+".unity");
                var oldRoot=GameObject.Find("Natural details");if(oldRoot)Object.DestroyImmediate(oldRoot);
                var root=new GameObject("Natural details");int f=0;
                foreach(var renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))if(renderer.name=="Warm flame")
                {
                    renderer.sharedMaterial=fire;renderer.GetComponent<MeshFilter>().sharedMesh=flameMesh;renderer.transform.localScale=new Vector3(.25f,.48f+(f%3)*.09f,.25f);renderer.transform.position=new Vector3(.08f+f*.14f,.29f,3.1f);renderer.shadowCastingMode=ShadowCastingMode.Off;f++;
                }
                if(f>0)for(int log=0;log<3;log++)
                {
                    var go=GameObject.CreatePrimitive(PrimitiveType.Cylinder);go.name="Charred hearth log";Object.DestroyImmediate(go.GetComponent<Collider>());go.transform.SetParent(root.transform,false);go.transform.position=new Vector3(.5f,.34f+log*.025f,2.96f+log*.12f);go.transform.rotation=Quaternion.Euler(0,log*15,90);go.transform.localScale=new Vector3(.12f,.49f,.12f);go.GetComponent<Renderer>().sharedMaterial=charcoal;
                }
                if(room!="BotanicaCafe")
                {
                    foreach(var renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))if(renderer.name=="Distant pine")renderer.enabled=false;
                    var forest=new Leaves();var meadow=new Leaves();var indoor=new Leaves();
                    for(int tree=0;tree<15;tree++)
                    {
                        var trunk=new Vector3(-6-(tree%3)*3,0,-13+tree*1.8f);
                        for(int tier=0;tier<7;tier++)for(int branch=0;branch<7;branch++)
                        {
                            float yaw=branch*360f/7+tree*29+tier*21;
                            var rotation=Quaternion.Euler(62,yaw,0);float length=1.2f-tier*.115f;
                            forest.Add(trunk+Vector3.up*(.8f+tier*.38f),rotation,length*.75f,length,tree*49+tier*7+branch);
                        }
                    }
                    for(int i=0;i<150;i++)
                    {
                        var p=new Vector3(-4.6f-Mathf.Repeat(i*.618f,1)*6,-.16f,-6+Mathf.Repeat(i*.4142f,1)*12);
                        for(int j=0;j<3;j++)meadow.Add(p,Quaternion.Euler(12+j*9,i*137+j*120,0),.045f,.2f+Mathf.Repeat(i*.31f,1)*.25f,i+j);
                    }
                    int plantIndex=0;
                    foreach(var tr in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                    {
                        if(tr.name!="pottedPlant"&&tr.name!="plantSmall1"&&tr.name!="plantSmall2")continue;
                        var renderers=tr.GetComponentsInChildren<Renderer>();if(renderers.Length==0)continue;var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
                        var anchor=bounds.center;anchor.y=bounds.min.y+bounds.size.y*.38f;
                        PreservePot(tr,anchor.y,room+"-Plant"+(plantIndex++));
                        for(int leaf=0;leaf<18;leaf++)indoor.Add(anchor,Quaternion.Euler(20+leaf%4*12,leaf*137.5f,0),bounds.size.y*.2f,bounds.size.y*(.4f+leaf%3*.1f),leaf);
                    }
                    forest.Save(room+"-Pine",pine,root.transform);meadow.Save(room+"-Grass",grass,root.transform);indoor.Save(room+"-Leaves",leaves,root.transform);
                    for(int pane=0;pane<4;pane++)
                    {
                        var go=GameObject.CreatePrimitive(PrimitiveType.Quad);go.name="Reflective window pane";Object.DestroyImmediate(go.GetComponent<Collider>());go.transform.SetParent(root.transform,false);go.transform.position=new Vector3(-3.98f,1.95f,-2.625f+pane*1.75f);go.transform.rotation=Quaternion.Euler(0,90,0);go.transform.localScale=new Vector3(1.61f,2.08f,1);var r=go.GetComponent<MeshRenderer>();r.sharedMaterial=glass;r.shadowCastingMode=ShadowCastingMode.Off;
                    }
                }
                EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();Debug.Log("NATURAL_DETAILS_APPLIED");
        }
    }
}
