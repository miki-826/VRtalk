using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
public static class BuildChibi {
 public static void Run() {
  const string path="Assets/WhiteChibi/WhiteChibi.fbx";
  AssetDatabase.Refresh();
  var importer=(ModelImporter)AssetImporter.GetAtPath(path);
  var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);
  var names=new HashSet<string>(model.GetComponentsInChildren<Transform>(true).Select(t=>t.name));
  var desc=importer.humanDescription;
  desc.human=HumanTrait.BoneName.Where(n=>names.Contains(n)).Select(n=>new HumanBone{humanName=n,boneName=n,limit=new HumanLimit{useDefaultValues=true}}).ToArray();
  desc.skeleton=model.GetComponentsInChildren<Transform>(true).Select(t=>new SkeletonBone{name=t.name,position=t.localPosition,rotation=t.localRotation,scale=t.localScale}).ToArray();
  desc.upperArmTwist=.5f;desc.lowerArmTwist=.5f;desc.upperLegTwist=.5f;desc.lowerLegTwist=.5f;desc.armStretch=.05f;desc.legStretch=.05f;desc.feetSpacing=0;
  importer.animationType=ModelImporterAnimationType.Human;importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;importer.humanDescription=desc;importer.importAnimation=false;importer.isReadable=true;importer.SaveAndReimport();
  model=AssetDatabase.LoadAssetAtPath<GameObject>(path);
  var avatar=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
  if(avatar==null || !avatar.isValid || !avatar.isHuman)throw new Exception("Humanoid avatar invalid");
  Directory.CreateDirectory("Assets/WhiteChibi/Materials");
  var texture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/WhiteChibi/Chibi_Head_BaseColor.png");
  foreach(var source in model.GetComponentsInChildren<SkinnedMeshRenderer>().SelectMany(r=>r.sharedMaterials).Distinct()) {
   var m=new Material(Shader.Find("Standard"));m.name=source.name;m.color=source.HasProperty("_Color")?source.color:Color.white;m.SetFloat("_Glossiness",.3f);
   if(source.name.Contains("Head")){m.mainTexture=texture;m.color=Color.white;}
   var mp="Assets/WhiteChibi/Materials/"+source.name+".mat";var existing=AssetDatabase.LoadAssetAtPath<Material>(mp);if(existing){EditorUtility.CopySerialized(m,existing);UnityEngine.Object.DestroyImmediate(m);m=existing;}else AssetDatabase.CreateAsset(m,mp);
   importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),source.name),m);
  }
  importer.SaveAndReimport();model=AssetDatabase.LoadAssetAtPath<GameObject>(path);
  var go=(GameObject)PrefabUtility.InstantiatePrefab(model);go.name="WhiteChibi";
  var animator=go.GetComponent<Animator>();if(!animator)animator=go.AddComponent<Animator>();animator.avatar=avatar;
  PrefabUtility.SaveAsPrefabAsset(go,"Assets/WhiteChibi/WhiteChibi.prefab");
  var renderers=go.GetComponentsInChildren<SkinnedMeshRenderer>();
  int vertices=renderers.Sum(r=>r.sharedMesh.vertexCount);
  foreach(var r in renderers){if(r.bones.Length==0)throw new Exception("Missing skin bones "+r.name);}
  var left=animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);var rest=left.localRotation;left.localRotation*=Quaternion.Euler(0,0,45);
  foreach(var r in renderers){var mesh=new Mesh();r.BakeMesh(mesh);if(mesh.vertices.Any(v=>float.IsNaN(v.x)||float.IsInfinity(v.x)))throw new Exception("Invalid deformation");UnityEngine.Object.DestroyImmediate(mesh);}
  left.localRotation=rest;
  string report="Unity "+Application.unityVersion+"\nHumanoid avatar: valid\nSkinned meshes: "+renderers.Length+"\nVertices: "+vertices+"\nPose bake: finite\nMapped bones: "+desc.human.Length;
  File.WriteAllText("../unity_validation.txt",report);
  UnityEngine.Object.DestroyImmediate(go);AssetDatabase.SaveAssets();
  AssetDatabase.ExportPackage("Assets/WhiteChibi","../WhiteChibi_Unity.unitypackage",ExportPackageOptions.Recurse);
  Debug.Log("CHIBI_UNITY_SUCCESS\n"+report);
 }
}

