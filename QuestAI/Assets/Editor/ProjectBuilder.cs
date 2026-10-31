using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;

namespace QuestAI.Editor
{
    public static class ProjectBuilder
    {
        const string Generated="Assets/Generated";
        static Material wood,stone,cream,brass,fabric,green,glass,fire;
        static readonly string[] Rooms={"Bedroom","LivingRoom","Cafe"};
        [MenuItem("Quest AI/Generate content and configure")]
        public static void Generate()
        {
            Directory.CreateDirectory(Generated);Directory.CreateDirectory("Assets/Scenes");Directory.CreateDirectory("Assets/Resources/Profiles");Directory.CreateDirectory("Assets/Generated/Materials");Directory.CreateDirectory("Assets/Generated/Meshes");
            Configure();CreateMaterials();var character=BuildCharacter();CreateProfiles(character);
            foreach(var room in Rooms)BuildRoom(room);
            BuildRoom("MainMenu");
            BotanicaCafeInstaller.BuildScene();
            var bootstrap=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var root=new GameObject("Application").AddComponent<AppRoot>();
            root.xrRigPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.meta.xr.sdk.interaction.ovr/Runtime/Prefabs/OVRComprehensiveInteractionRig.prefab");
            EditorSceneManager.SaveScene(bootstrap,"Assets/Scenes/Bootstrap.unity");
            EditorBuildSettings.scenes=new[]{"Bootstrap","MainMenu","Bedroom","LivingRoom","Cafe","BotanicaCafe"}.Select(x=>new EditorBuildSettingsScene("Assets/Scenes/"+x+".unity",true)).ToArray();
            GraphicsUpgrade.Apply();
            NaturalSceneUpgrade.Apply();
            RealisticUpgrade.Apply();
            AssetDatabase.SaveAssets();File.WriteAllText("../artifacts/content-generated.txt",DateTime.UtcNow.ToString("O")+"\nScenes, profiles, blendshapes and XR settings generated.\n");Debug.Log("QUESTAI_GENERATED");
        }
        static T Asset<T>(T value,string path) where T:UnityEngine.Object
        {
            var old=AssetDatabase.LoadAssetAtPath<T>(path);if(old){EditorUtility.CopySerialized(value,old);UnityEngine.Object.DestroyImmediate(value);return old;}AssetDatabase.CreateAsset(value,path);return value;
        }
        static void Configure()
        {
            PlayerSettings.companyName="VibeCodingClub";PlayerSettings.productName="White Room";PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,"com.vibecodingclub.questai");PlayerSettings.bundleVersion="0.1.0";
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android,ScriptingImplementation.IL2CPP);PlayerSettings.Android.targetArchitectures=AndroidArchitecture.ARM64;PlayerSettings.Android.minSdkVersion=AndroidSdkVersions.AndroidApiLevel32;PlayerSettings.Android.targetSdkVersion=(AndroidSdkVersions)36;PlayerSettings.Android.forceInternetPermission=true;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android,false);PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,new[]{GraphicsDeviceType.Vulkan});PlayerSettings.colorSpace=ColorSpace.Linear;PlayerSettings.runInBackground=false;
            var ps=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);var input=ps.FindProperty("activeInputHandler");if(input!=null)input.intValue=2;ps.ApplyModifiedPropertiesWithoutUndo();
            var renderer=Asset(ScriptableObject.CreateInstance<UniversalRendererData>(),Generated+"/Renderer.asset");
            var pipeline=ScriptableObject.CreateInstance<UniversalRenderPipelineAsset>();var serialized=new SerializedObject(pipeline);serialized.FindProperty("m_RendererDataList").GetArrayElementAtIndex(0).objectReferenceValue=renderer;serialized.ApplyModifiedPropertiesWithoutUndo();
            pipeline.msaaSampleCount=4;pipeline.renderScale=1;pipeline.supportsHDR=false;pipeline.shadowDistance=12;pipeline.supportsCameraDepthTexture=false;pipeline.supportsCameraOpaqueTexture=false;
            pipeline=Asset(pipeline,Generated+"/QuestURP.asset");GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;QualitySettings.vSyncCount=0;
            var audio=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset")[0]);audio.FindProperty("m_SpatializerPlugin").stringValue="Meta XR Audio";audio.ApplyModifiedPropertiesWithoutUndo();
            ConfigureXR();
        }
        static void ConfigureXR()
        {
            var general=XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
            if(!general)
            {
                var perTarget=ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();general=ScriptableObject.CreateInstance<XRGeneralSettings>();general.Manager=ScriptableObject.CreateInstance<XRManagerSettings>();
                AssetDatabase.CreateAsset(perTarget,Generated+"/XRSettings.asset");AssetDatabase.AddObjectToAsset(general,perTarget);AssetDatabase.AddObjectToAsset(general.Manager,perTarget);perTarget.SetSettingsForBuildTarget(BuildTargetGroup.Android,general);EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey,perTarget,true);
            }
            if(!general.Manager){general.Manager=ScriptableObject.CreateInstance<XRManagerSettings>();AssetDatabase.AddObjectToAsset(general.Manager,general);}
            general.InitManagerOnStart=true;XRPackageMetadataStore.AssignLoader(general.Manager,"UnityEngine.XR.OpenXR.OpenXRLoader",BuildTargetGroup.Android);
            var xr=OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);if(xr){xr.renderMode=OpenXRSettings.RenderMode.SinglePassInstanced;
                foreach(var feature in xr.GetFeatures<UnityEngine.XR.OpenXR.Features.OpenXRFeature>())
                {
                    string name=feature.GetType().Name;
                    if(name=="MetaXRFeature"||name=="OculusTouchControllerProfile"||name=="MetaQuestTouchPlusControllerProfile"||name=="HandTracking"||name=="HandInteractionProfile")feature.enabled=true;
                    if(name=="OculusQuestFeature")feature.enabled=false;
                }EditorUtility.SetDirty(xr);
            }
            var config=OVRProjectConfig.CachedProjectConfig;config.targetDeviceTypes=new List<OVRProjectConfig.DeviceType>{OVRProjectConfig.DeviceType.Quest3S,OVRProjectConfig.DeviceType.Quest3};config.handTrackingSupport=OVRProjectConfig.HandTrackingSupport.ControllersAndHands;config.requiresSystemKeyboard=true;EditorUtility.SetDirty(config);EditorUtility.SetDirty(general);EditorUtility.SetDirty(general.Manager);
        }
        static Material Mat(string name,Color color,string texture=null,bool emission=false)
        {
            var m=new Material(Shader.Find("Universal Render Pipeline/Lit"));m.name=name;m.color=color;m.SetFloat("_Smoothness",.18f);
            if(texture!=null){m.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(texture);m.mainTextureScale=new Vector2(2,2);}
            if(emission){m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",color*2);m.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;}
            return Asset(m,Generated+"/Materials/"+name+".mat");
        }
        static void CreateMaterials()
        {
            wood=Mat("Warm oak",new Color(.7f,.72f,.7f),"Assets/ThirdParty/PolyHaven/wood_floor_deck.jpg");stone=Mat("Hearth stone",new Color(.8f,.78f,.75f),"Assets/ThirdParty/PolyHaven/rock_boulder_dry.jpg");cream=Mat("Warm plaster",new Color(.76f,.68f,.53f));brass=Mat("Antique brass",new Color(.53f,.34f,.12f));fabric=Mat("Terracotta wool",new Color(.48f,.28f,.18f));green=Mat("Forest textile",new Color(.16f,.24f,.17f));glass=Mat("Night window",new Color(.14f,.23f,.31f));fire=Mat("Lantern amber",new Color(1,.48f,.11f),null,true);
            foreach(var guid in AssetDatabase.FindAssets("t:Texture2D",new[]{"Assets/ThirdParty"}))
            {var importer=AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) as TextureImporter;if(importer){importer.maxTextureSize=1024;importer.mipmapEnabled=true;importer.SaveAndReimport();}}
        }
        static GameObject BuildCharacter()
        {
            var original=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/WhiteChibi/WhiteChibi.prefab");if(!original)throw new Exception("Missing WhiteChibi prefab");
            var avatar=UnityEngine.Object.Instantiate(original);avatar.name="WhiteChibi";
            var animator=avatar.GetComponent<Animator>();if(!animator)animator=avatar.AddComponent<Animator>();
            var rig=avatar.AddComponent<CharacterRigBinding>();rig.animator=animator;rig.head=animator.GetBoneTransform(HumanBodyBones.Head);rig.chest=animator.GetBoneTransform(HumanBodyBones.Chest);rig.leftArm=animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);rig.rightArm=animator.GetBoneTransform(HumanBodyBones.RightUpperArm);rig.leftForearm=animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);rig.rightForearm=animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            var faces=new List<SkinnedMeshRenderer>();
            foreach(var renderer in avatar.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>{string name="Chibi_"+m.name.Replace("/","_");var existing=AssetDatabase.LoadAssetAtPath<Material>(Generated+"/Materials/"+name+".mat");if(existing)return existing;var copy=new Material(Shader.Find("Universal Render Pipeline/Lit"));copy.name=name;copy.color=m.color;copy.mainTexture=m.mainTexture;copy.SetFloat("_Smoothness",.2f);AssetDatabase.CreateAsset(copy,Generated+"/Materials/"+name+".mat");return copy;}).ToArray();
                string lower=renderer.name.ToLowerInvariant();if(!(lower.Contains("eye")||lower.Contains("brow")||lower.Contains("smile")))continue;
                var mesh=UnityEngine.Object.Instantiate(renderer.sharedMesh);mesh.name=renderer.name+"_Expressions";
                var vertices=mesh.vertices;var rootPoints=vertices.Select(v=>avatar.transform.InverseTransformPoint(renderer.transform.TransformPoint(v))).ToArray();Vector3 center=rootPoints.Aggregate(Vector3.zero,(a,b)=>a+b)/rootPoints.Length;
                foreach(string shape in new[]{"Blink","MouthOpen","LookX","LookY","happy","sad","concerned","excited","embarrassed","curious"})
                {
                    var delta=new Vector3[vertices.Length];
                    for(int i=0;i<delta.Length;i++)
                    {
                        Vector3 p=rootPoints[i],d=Vector3.zero;bool eye=lower.Contains("eye"),brow=lower.Contains("brow"),mouth=lower.Contains("smile");
                        if(shape=="Blink"&&eye)d.y=-(p.y-center.y)*.97f;
                        if(shape=="MouthOpen"&&mouth)d.y=(p.y-center.y)*1.15f-.008f;
                        if(shape=="LookX"&&eye)d.x=.015f;if(shape=="LookY"&&eye)d.y=.009f;
                        if(shape=="happy"){if(eye)d.y=-(p.y-center.y)*.3f;if(mouth)d.y=Mathf.Abs(p.x-center.x)*.18f;}
                        if(shape=="sad"||shape=="concerned"){if(brow)d.y=(p.x-center.x)*Mathf.Sign(center.x)*.4f;if(mouth)d.y=-Mathf.Abs(p.x-center.x)*.3f;}
                        if(shape=="excited"){if(eye)d.y=(p.y-center.y)*.12f;if(mouth)d.y=(p.y-center.y)*.5f;}
                        if(shape=="embarrassed"){if(eye)d.y=-(p.y-center.y)*.4f;if(brow)d.y=.005f;}
                        if(shape=="curious"&&brow)d.y=center.x>0?.012f:-.003f;
                        delta[i]=renderer.transform.InverseTransformVector(avatar.transform.TransformVector(d));
                    }
                    mesh.AddBlendShapeFrame(shape,100,delta,null,null);
                }
                renderer.sharedMesh=Asset(mesh,Generated+"/Meshes/"+mesh.name+".asset");faces.Add(renderer);
            }
            rig.faceMeshes=faces.ToArray();var controllerPath=Generated+"/Character.controller";var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if(!controller){controller=AnimatorController.CreateAnimatorControllerAtPath(controllerPath);controller.AddParameter("Gesture",AnimatorControllerParameterType.Int);var idle=new AnimationClip{name="Idle"};AssetDatabase.CreateAsset(idle,Generated+"/Idle.anim");controller.layers[0].stateMachine.AddState("Idle").motion=idle;}
            animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;
            foreach(var child in avatar.GetComponentsInChildren<Transform>(true))child.gameObject.layer=8;
            var body=avatar.AddComponent<BodySystem>();body.rig=rig;
            var result=PrefabUtility.SaveAsPrefabAsset(avatar,Generated+"/WhiteChibi.prefab");UnityEngine.Object.DestroyImmediate(avatar);return result;
        }
        static void CreateProfiles(GameObject prefab)
        {
            string[] ids={"calm","bright"},names={"しろ · 穏やか","こはく · 元気"},voices={"marin","gleam"},personalities={"落ち着いて優しく話し、相手のペースを大事にする。短く自然な言葉で寄り添う。","明るく好奇心旺盛。身近な小さな楽しみを見つけ、一緒に喜ぶ。押しつけず相手を尊重する。"};
            for(int i=0;i<2;i++){var p=ScriptableObject.CreateInstance<CharacterProfile>();p.id=ids[i];p.displayName=names[i];p.voice=voices[i];p.personality=personalities[i];p.prefab=prefab;Asset(p,"Assets/Resources/Profiles/"+ids[i]+".asset");}
            string[] labels={"ベッドルーム","暖炉のリビング","森のカフェ"};for(int i=0;i<3;i++){var p=ScriptableObject.CreateInstance<EnvironmentProfile>();p.id=p.sceneName=Rooms[i];p.displayName=labels[i];p.characterPosition=new Vector3(0,0,.6f);p.semanticObjects=new[]{"窓","椅子","テーブル","植物","暖炉"};Asset(p,"Assets/Resources/Profiles/"+Rooms[i]+".asset");}
        }
        static GameObject Box(string name,Vector3 pos,Vector3 scale,Material mat,bool collider=true)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.position=pos;go.transform.localScale=scale;go.GetComponent<Renderer>().sharedMaterial=mat;
            if(!collider)UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.ContributeGI|StaticEditorFlags.ReflectionProbeStatic|StaticEditorFlags.OccludeeStatic);return go;
        }
        static GameObject Furniture(string name,Vector3 position,float width,float yaw=0,float maxHeight=float.PositiveInfinity)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ThirdParty/Kenney/Models/FBX format/"+name+".fbx");if(!prefab)throw new Exception("Missing furniture "+name);
            var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab);go.name=name;var renderers=go.GetComponentsInChildren<Renderer>();Bounds bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
            float factor=Mathf.Min(width/Mathf.Max(.001f,bounds.size.x),maxHeight/Mathf.Max(.001f,bounds.size.y));go.transform.localScale*=factor;go.transform.position=Vector3.zero;go.transform.rotation=Quaternion.Euler(0,yaw,0);
            bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);go.transform.position=position-new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
            foreach(var r in renderers){r.sharedMaterials=r.sharedMaterials.Select(m=>{string key="Kenney_"+m.name;var found=AssetDatabase.LoadAssetAtPath<Material>(Generated+"/Materials/"+key+".mat");if(found)return found;var copy=new Material(Shader.Find("Universal Render Pipeline/Lit"));copy.color=m.HasProperty("_Color")?m.color:Color.white;copy.mainTexture=m.mainTexture;copy.SetFloat("_Smoothness",.15f);AssetDatabase.CreateAsset(copy,Generated+"/Materials/"+key+".mat");return copy;}).ToArray();GameObjectUtility.SetStaticEditorFlags(r.gameObject,StaticEditorFlags.ContributeGI|StaticEditorFlags.ReflectionProbeStatic);}
            return go;
        }
        static void BuildRoom(string name)
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            Box("Oak floor",new Vector3(0,-.12f,0),new Vector3(8,.24f,7),wood);
            Box("Back wall",new Vector3(0,1.7f,3.5f),new Vector3(8,3.4f,.2f),wood);Box("Right wall",new Vector3(4,1.7f,0),new Vector3(.2f,3.4f,7),cream);Box("Behind user",new Vector3(0,1.7f,-3.5f),new Vector3(8,3.4f,.2f),wood);
            Box("Ceiling",new Vector3(0,3.5f,0),new Vector3(8,.15f,7),wood);
            for(int i=0;i<4;i++)Box("Ceiling beam",new Vector3(-3+i*2,3.3f,0),new Vector3(.22f,.3f,7),wood);
            // Open west wall with actual scenery, divided by thick timber window frames.
            Box("Window lower wall",new Vector3(-4,.4f,0),new Vector3(.2f,.8f,7),wood);Box("Window header",new Vector3(-4,3.15f,0),new Vector3(.2f,.5f,7),wood);
            for(int i=0;i<5;i++)Box("Window mullion",new Vector3(-4,1.9f,-3.5f+i*1.75f),new Vector3(.2f,2.5f,.12f),wood);
            Box("Window sill",new Vector3(-3.85f,.85f,0),new Vector3(.5f,.12f,7),wood);
            Box("Snow beyond window",new Vector3(-12,-.25f,0),new Vector3(17,.15f,40),cream);
            for(int i=0;i<15;i++){float x=-6-(i%3)*3,z=-13+i*1.8f;Box("Pine trunk",new Vector3(x,1,z),new Vector3(.15f,2,.15f),wood,false);for(int j=0;j<3;j++){var tree=GameObject.CreatePrimitive(PrimitiveType.Capsule);tree.name="Distant pine";tree.transform.position=new Vector3(x,1.8f+j*.8f,z);tree.transform.localScale=new Vector3(1.5f-j*.35f,1.3f,1.5f-j*.35f);tree.GetComponent<Renderer>().sharedMaterial=green;UnityEngine.Object.DestroyImmediate(tree.GetComponent<Collider>());}}
            for(int i=0;i<4;i++)Box("Curtain panel",new Vector3(-3.8f,1.93f,-3.3f+i*2.1f),new Vector3(.1f,2.1f,.35f),green,false);
            Box("Woven rug",new Vector3(0,.015f,.2f),new Vector3(3.3f,.025f,3.8f),fabric,false);
            if(name=="LivingRoom"||name=="MainMenu")
            {
                Hearth();Furniture("loungeSofa",new Vector3(2.5f,0,.5f),2.4f,-90);Furniture("loungeChairRelax",new Vector3(-2.6f,0,1.8f),.9f,35);Furniture("tableCoffee",new Vector3(1.7f,0,.25f),1.1f);Furniture("bookcaseOpen",new Vector3(-2.4f,0,3.25f),.9f,180);Furniture("pottedPlant",new Vector3(3.2f,0,2.7f),.75f);
            }
            else if(name=="Bedroom")
            {
                Furniture("bedDouble",new Vector3(2.35f,0,1.5f),1.7f,180,.8f);
                var cabinet=Furniture("cabinetBedDrawer",new Vector3(3.3f,0,2.65f),.55f,0,.8f);
                float cabinetTop=cabinet.GetComponentsInChildren<Renderer>().Max(r=>r.bounds.max.y);
                Furniture("lampRoundTable",new Vector3(3.3f,cabinetTop,2.65f),.3f,0,.5f);
                Furniture("bookcaseOpenLow",new Vector3(-1.8f,0,3.1f),2,180,1.2f);Furniture("chairCushion",new Vector3(-2.7f,0,1.7f),.5f,35);Furniture("pottedPlant",new Vector3(-3.4f,0,2.7f),.7f);
            }
            else
            {
                Box("Cafe counter",new Vector3(2.5f,.5f,2.5f),new Vector3(2.6f,1,1),wood);Furniture("kitchenCoffeeMachine",new Vector3(2.7f,1,2.5f),.5f);Furniture("tableRound",new Vector3(-2.4f,0,1.6f),1.25f);Furniture("chairCushion",new Vector3(-3.3f,0,1.6f),.6f,90);Furniture("chairCushion",new Vector3(-1.6f,0,2.3f),.6f,210);Furniture("bookcaseOpen",new Vector3(-2.5f,0,3.2f),.9f,180);Furniture("pottedPlant",new Vector3(3.4f,0,0),.8f);
            }
            for(int i=0;i<3;i++)Furniture("books",new Vector3(-2.35f,.42f+i*.43f,3.1f),.4f);
            Furniture("lampRoundFloor",new Vector3(3.4f,0,-2.2f),.5f);Furniture("plantSmall1",new Vector3(-3.65f,.94f,1),.3f);
            var sun=new GameObject("Window sunlight").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=.6f;sun.shadows=LightShadows.Soft;sun.lightmapBakeType=LightmapBakeType.Baked;sun.transform.rotation=Quaternion.Euler(45,110,0);
            foreach(var pos in new[]{new Vector3(2.7f,2.5f,1.5f),new Vector3(-2.4f,2.5f,-1)}){var lamp=new GameObject("Warm ceiling light").AddComponent<Light>();lamp.type=LightType.Point;lamp.transform.position=pos;lamp.color=new Color(1,.87f,.71f);lamp.intensity=3;lamp.range=6;lamp.lightmapBakeType=LightmapBakeType.Baked;lamp.shadows=LightShadows.Soft;}
            var fill=new GameObject("Character soft fill").AddComponent<Light>();fill.type=LightType.Directional;fill.color=new Color(1,.95f,.88f);fill.intensity=.65f;fill.cullingMask=1<<8;fill.shadows=LightShadows.None;fill.lightmapBakeType=LightmapBakeType.Realtime;fill.transform.rotation=Quaternion.Euler(40,0,0);
            var probes=new GameObject("Character light probes").AddComponent<LightProbeGroup>();var points=new List<Vector3>();for(int x=-2;x<=2;x+=2)for(int z=-2;z<=2;z+=2)for(int y=0;y<3;y++)points.Add(new Vector3(x,.3f+y,z));probes.probePositions=points.ToArray();
            var reflection=new GameObject("Room reflection").AddComponent<ReflectionProbe>();reflection.transform.position=new Vector3(0,1.5f,0);reflection.size=new Vector3(8,3.4f,7);reflection.mode=ReflectionProbeMode.Baked;reflection.resolution=128;
            var lighting=new GameObject("Time of day").AddComponent<EnvironmentLighting>();lighting.sun=sun;lighting.reflectionProbe=reflection;var sky=new Material(Shader.Find("Skybox/Procedural"));lighting.sky=Asset(sky,Generated+"/Materials/"+name+"Sky.mat");lighting.Apply(TimeMode.Evening);
            var settings=new LightingSettings{name=name+" Lighting"};settings.bakedGI=true;settings.realtimeGI=false;settings.lightmapper=LightingSettings.Lightmapper.ProgressiveCPU;settings.lightmapResolution=12;settings.lightmapMaxSize=1024;settings.lightmapPadding=4;settings.directSampleCount=16;settings.indirectSampleCount=32;settings.environmentSampleCount=16;settings.directionalityMode=LightmapsMode.NonDirectional;
            Lightmapping.lightingSettings=Asset(settings,Generated+"/"+name+"Lighting.asset");EditorSceneManager.SaveScene(scene,"Assets/Scenes/"+name+".unity");
        }
        static void Hearth()
        {
            Box("Stone chimney",new Vector3(.5f,2.55f,3.17f),new Vector3(2.6f,1.7f,.6f),stone);
            Box("Hearth left",new Vector3(-.5f,.65f,3.07f),new Vector3(.55f,1.3f,.8f),stone);Box("Hearth right",new Vector3(1.5f,.65f,3.07f),new Vector3(.55f,1.3f,.8f),stone);Box("Mantel",new Vector3(.5f,1.45f,2.93f),new Vector3(3,.23f,1),wood);Box("Hearth",new Vector3(.5f,.08f,2.8f),new Vector3(2.9f,.16f,1.3f),stone);
            Box("Fire bed",new Vector3(.5f,.23f,3.1f),new Vector3(1.3f,.14f,.4f),brass);
            for(int i=0;i<7;i++){var flame=new GameObject("Warm flame",typeof(MeshFilter),typeof(MeshRenderer));flame.GetComponent<MeshFilter>().sharedMesh=FlameMesh();flame.transform.position=new Vector3(.08f+i*.14f,.43f+(i%2)*.09f,3.1f);flame.transform.localScale=new Vector3(.09f,.2f+(i%3)*.035f,.1f);flame.GetComponent<Renderer>().sharedMaterial=fire;}
            var light=new GameObject("Baked hearth glow").AddComponent<Light>();light.type=LightType.Point;light.transform.position=new Vector3(.5f,.7f,2.5f);light.color=new Color(1,.65f,.3f);light.intensity=1.8f;light.range=5;light.lightmapBakeType=LightmapBakeType.Baked;
            Furniture("plantSmall2",new Vector3(1.3f,1.6f,2.9f),.4f);Furniture("books",new Vector3(-.35f,1.6f,2.9f),.5f);
        }
        static Mesh FlameMesh()
        {
            string path=Generated+"/Meshes/Flame.asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(existing)return existing;
            var mesh=new Mesh{name="Flame"};var vertices=new Vector3[10];for(int i=0;i<8;i++)vertices[i]=new Vector3(Mathf.Cos(i*Mathf.PI/4),0,Mathf.Sin(i*Mathf.PI/4));vertices[8]=new Vector3(.3f,2,0);vertices[9]=new Vector3(0,-.3f,0);var triangles=new int[48];for(int i=0;i<8;i++){int j=i*6;triangles[j]=i;triangles[j+1]=8;triangles[j+2]=(i+1)%8;triangles[j+3]=(i+1)%8;triangles[j+4]=9;triangles[j+5]=i;}mesh.vertices=vertices;mesh.triangles=triangles;mesh.RecalculateNormals();AssetDatabase.CreateAsset(mesh,path);return mesh;
        }
        [MenuItem("Quest AI/Build APK")]
        public static void BuildApk()
        {
            PlayerSettings.Android.applicationEntry=AndroidApplicationEntry.Activity;
            Directory.CreateDirectory("Builds");EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android,BuildTarget.Android);
            var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=EditorBuildSettings.scenes.Where(x=>x.enabled).Select(x=>x.path).ToArray(),locationPathName="Builds/WhiteRoom-Quest3S.apk",target=BuildTarget.Android,options=BuildOptions.None});
            File.WriteAllText("../artifacts/build-report.txt",result.summary.result+"\n"+result.summary.totalSize+" bytes\n"+result.summary.totalTime);
            if(result.summary.result!=BuildResult.Succeeded)throw new Exception("APK build failed");
        }
    }
}

