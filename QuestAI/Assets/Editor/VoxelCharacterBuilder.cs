using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace QuestAI.Editor
{
    // Surface voxels retain the original skin weights and facial deformation channels.
    public static class VoxelCharacterBuilder
    {
        const string Folder="Assets/Generated/VoxelCharacter";
        const float Cell=.022f;
        struct Sample { public int vertex; public float distance; }
        static readonly Vector3Int[] Directions={Vector3Int.right,Vector3Int.left,Vector3Int.up,Vector3Int.down,new Vector3Int(0,0,1),new Vector3Int(0,0,-1)};
        static readonly Vector3[][] Faces={
            new[]{new Vector3(1,-1,-1),new Vector3(1,1,-1),new Vector3(1,1,1),new Vector3(1,-1,1)},
            new[]{new Vector3(-1,-1,1),new Vector3(-1,1,1),new Vector3(-1,1,-1),new Vector3(-1,-1,-1)},
            new[]{new Vector3(-1,1,-1),new Vector3(-1,1,1),new Vector3(1,1,1),new Vector3(1,1,-1)},
            new[]{new Vector3(-1,-1,1),new Vector3(-1,-1,-1),new Vector3(1,-1,-1),new Vector3(1,-1,1)},
            new[]{new Vector3(1,-1,1),new Vector3(1,1,1),new Vector3(-1,1,1),new Vector3(-1,-1,1)},
            new[]{new Vector3(-1,-1,-1),new Vector3(-1,1,-1),new Vector3(1,1,-1),new Vector3(1,-1,-1)}};

        [MenuItem("Quest AI/Use pixel character")]
        public static void Build()
        {
            Directory.CreateDirectory(Folder);
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Generated/WhiteChibi.prefab");
            var avatar=PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(source));
            try
            {
                avatar.name="PixelChibi";
                int total=0,index=0;
                foreach(var renderer in avatar.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    // Smaller face pixels preserve the expression silhouette and eye highlights.
                    string lower=renderer.name.ToLowerInvariant();
                    float size=lower.Contains("eye")||lower.Contains("brow")||lower.Contains("smile")?Cell*.45f:Cell;
                    var mesh=ConvertMesh(renderer,avatar.transform,size);
                    string path=Folder+"/Part"+(index++)+".asset";
                    var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if(existing){EditorUtility.CopySerialized(mesh,existing);UnityEngine.Object.DestroyImmediate(mesh);mesh=existing;}else AssetDatabase.CreateAsset(mesh,path);
                    renderer.sharedMesh=mesh;total+=mesh.triangles.Length/3;
                    renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>PixelMaterial(m)).ToArray();
                }
                AddContactShadow(avatar);
                var prefab=PrefabUtility.SaveAsPrefabAsset(avatar,Folder+"/PixelChibi.prefab");
                foreach(string id in new[]{"calm","bright"}){var profile=AssetDatabase.LoadAssetAtPath<CharacterProfile>("Assets/Resources/Profiles/"+id+".asset");profile.prefab=prefab;EditorUtility.SetDirty(profile);}
                AssetDatabase.SaveAssets();Debug.Log("PIXEL_CHARACTER triangles="+total);
            }
            finally{PrefabUtility.UnloadPrefabContents(avatar);}
        }
        [MenuItem("Quest AI/Restore original character")]
        public static void RestoreOriginal()
        {
            var original=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Generated/WhiteChibi.prefab");
            if(!original)throw new InvalidOperationException("Original character prefab is missing");
            foreach(string id in new[]{"calm","bright"})
            {
                var profile=AssetDatabase.LoadAssetAtPath<CharacterProfile>("Assets/Resources/Profiles/"+id+".asset");
                profile.prefab=original;EditorUtility.SetDirty(profile);
            }
            AssetDatabase.SaveAssets();Debug.Log("ORIGINAL_CHARACTER_RESTORED");
        }
        static Material PixelMaterial(Material original)
        {
            string path=Folder+"/"+original.name.Replace("/","_")+".mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!mat){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,path);}
            mat.color=original.color;mat.SetFloat("_Smoothness",.06f);mat.SetFloat("_Metallic",0);EditorUtility.SetDirty(mat);return mat;
        }
        static void AddContactShadow(GameObject avatar)
        {
            string texturePath=Folder+"/ContactShadow.asset";
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if(!texture){texture=new Texture2D(32,32,TextureFormat.RGBA32,false){name="ContactShadow",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};AssetDatabase.CreateAsset(texture,texturePath);}
            var pixels=new Color[32*32];
            for(int y=0;y<32;y++)for(int x=0;x<32;x++)
            {
                float radius=new Vector2((x+.5f)/16-1,(y+.5f)/16-1).sqrMagnitude;
                float alpha=Mathf.Floor(Mathf.Pow(Mathf.Clamp01(1-radius),2)*6)/6*.3f;
                pixels[y*32+x]=new Color(.035f,.025f,.02f,alpha);
            }
            texture.SetPixels(pixels);texture.Apply();EditorUtility.SetDirty(texture);
            string materialPath=Folder+"/ContactShadow.mat";var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if(!material){material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));AssetDatabase.CreateAsset(material,materialPath);}
            material.SetTexture("_BaseMap",texture);material.SetColor("_BaseColor",Color.white);material.SetFloat("_Surface",1);material.SetFloat("_Blend",0);
            material.SetFloat("_SrcBlend",(float)UnityEngine.Rendering.BlendMode.SrcAlpha);material.SetFloat("_DstBlend",(float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite",0);material.SetFloat("_Cull",0);material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");material.SetOverrideTag("RenderType","Transparent");material.renderQueue=3000;EditorUtility.SetDirty(material);
            var shadow=GameObject.CreatePrimitive(PrimitiveType.Quad);shadow.name="Pixel contact shadow";UnityEngine.Object.DestroyImmediate(shadow.GetComponent<Collider>());
            shadow.transform.SetParent(avatar.transform,false);shadow.transform.localPosition=new Vector3(0,.012f,0);shadow.transform.localRotation=Quaternion.Euler(90,0,0);shadow.transform.localScale=new Vector3(.65f,.45f,1);
            var renderer=shadow.GetComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
        }
        static Mesh ConvertMesh(SkinnedMeshRenderer renderer,Transform root,float size)
        {
            var source=renderer.sharedMesh;var vertices=source.vertices;var weights=source.boneWeights;
            var points=vertices.Select(v=>root.InverseTransformPoint(renderer.transform.TransformPoint(v))).ToArray();
            var cells=new Dictionary<Vector3Int,Sample>();
            var triangles=source.triangles;
            for(int t=0;t<triangles.Length;t+=3)
            {
                int a=triangles[t],b=triangles[t+1],c=triangles[t+2];
                int steps=Mathf.Max(1,Mathf.CeilToInt(Mathf.Max((points[a]-points[b]).magnitude,(points[a]-points[c]).magnitude,(points[b]-points[c]).magnitude)/(size*.45f)));
                for(int u=0;u<=steps;u++)for(int v=0;v<=steps-u;v++)
                {
                    float bu=u/(float)steps,cv=v/(float)steps;var p=points[a]*(1-bu-cv)+points[b]*bu+points[c]*cv;
                    var key=Vector3Int.FloorToInt(p/size);var center=((Vector3)key+Vector3.one*.5f)*size;
                    float distance=(p-center).sqrMagnitude;
                    if(cells.TryGetValue(key,out var prior)&&prior.distance<=distance)continue;
                    int nearest=(p-points[a]).sqrMagnitude<(p-points[b]).sqrMagnitude?a:b;if((p-points[c]).sqrMagnitude<(p-points[nearest]).sqrMagnitude)nearest=c;
                    cells[key]=new Sample{vertex=nearest,distance=distance};
                }
            }
            var output=new List<Vector3>();var normals=new List<Vector3>();var skin=new List<BoneWeight>();var ids=new List<int>();var indices=new List<int>();
            foreach(var cell in cells)for(int face=0;face<6;face++)
            {
                if(cells.ContainsKey(cell.Key+Directions[face]))continue;
                int start=output.Count;var center=((Vector3)cell.Key+Vector3.one*.5f)*size;
                foreach(var corner in Faces[face])
                {
                    output.Add(renderer.transform.InverseTransformPoint(root.TransformPoint(center+corner*(size*.502f))));
                    normals.Add(renderer.transform.InverseTransformDirection(root.TransformDirection(Directions[face])).normalized);
                    // A rigid cell must not shear apart under blended head/neck weights.
                    var weight=weights[cell.Value.vertex];int bone=weight.boneIndex0;float largest=weight.weight0;
                    if(weight.weight1>largest){bone=weight.boneIndex1;largest=weight.weight1;}
                    if(weight.weight2>largest){bone=weight.boneIndex2;largest=weight.weight2;}
                    if(weight.weight3>largest)bone=weight.boneIndex3;
                    skin.Add(new BoneWeight{boneIndex0=bone,weight0=1});ids.Add(cell.Value.vertex);
                }
                indices.AddRange(new[]{start,start+1,start+2,start,start+2,start+3});
            }
            var mesh=new Mesh{name=renderer.name+"_Pixel",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.SetVertices(output);mesh.SetNormals(normals);mesh.SetTriangles(indices,0);mesh.boneWeights=skin.ToArray();mesh.bindposes=source.bindposes;
            for(int shape=0;shape<source.blendShapeCount;shape++)for(int frame=0;frame<source.GetBlendShapeFrameCount(shape);frame++)
            {
                var delta=new Vector3[vertices.Length];source.GetBlendShapeFrameVertices(shape,frame,delta,null,null);
                // Concern has raised, gentler brows; sadness retains the original downward mouth.
                if(source.GetBlendShapeName(shape)=="concerned")for(int i=0;i<delta.Length;i++)
                {
                    delta[i]*=.55f;
                    if(renderer.name.ToLowerInvariant().Contains("brow"))delta[i]+=renderer.transform.InverseTransformVector(root.TransformVector(Vector3.up*.006f));
                }
                mesh.AddBlendShapeFrame(source.GetBlendShapeName(shape),source.GetBlendShapeFrameWeight(shape,frame),ids.Select(i=>delta[i]).ToArray(),null,null);
            }
            mesh.RecalculateBounds();return mesh;
        }
    }
}
