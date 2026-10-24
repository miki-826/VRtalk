using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace QuestAI.Editor
{
    // Adds the CC0 RobotExpressive model (Tomás Laulhé / Quaternius, three.js examples) as a third, animated character.
    // The FBX comes from tools/convert-robot.py; WhiteChibi stays the model of the existing two profiles.
    public static class RobotCharacterBuilder
    {
        const string Source = "Assets/ThirdParty/RobotExpressive/RobotExpressive.fbx";
        const string Output = "Assets/Generated/Robot";
        const float Height = 1.15f;
        static readonly string[] Looping = { "Idle", "Walking", "Running", "Dance" };
        static readonly string[] Gestures = { "Wave", "Yes", "No", "Jump", "ThumbsUp", "Dance" };

        [MenuItem("Quest AI/Add robot character (free CC0 model)")]
        public static void Build()
        {
            Directory.CreateDirectory(Output + "/Materials");
            ConfigureImport();
            var prefab = BuildPrefab();
            var profile = AssetDatabase.LoadAssetAtPath<CharacterProfile>("Assets/Resources/Profiles/robot.asset");
            if (!profile) { profile = ScriptableObject.CreateInstance<CharacterProfile>(); AssetDatabase.CreateAsset(profile, "Assets/Resources/Profiles/robot.asset"); }
            profile.id = "robot"; profile.displayName = "ロボ · 好奇心"; profile.voice = "marin"; profile.defaultEmotion = Emotion.curious; profile.personalSpace = .8f; profile.initiative = .5f;
            profile.personality = "好奇心いっぱいの小さなロボット。部屋の物に興味を持ち、歩いて見に行っては感想を話すのが好き。丁寧で親しみやすく、短く話す。";
            profile.prefab = prefab; EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets(); Debug.Log("ROBOT_CHARACTER_READY " + AssetDatabase.GetAssetPath(prefab));
        }
        static void ConfigureImport()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(Source);
            importer.animationType = ModelImporterAnimationType.Generic; importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true; importer.importBlendShapes = true; importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.resampleCurves = true; importer.animationCompression = ModelImporterAnimationCompression.Optimal;
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                clip.name = Short(clip.takeName);
                clip.loopTime = Looping.Contains(clip.name); clip.lockRootPositionXZ = true; clip.keepOriginalPositionXZ = true; clip.keepOriginalPositionY = true; clip.keepOriginalOrientation = true; clip.lockRootRotation = true;
            }
            importer.clipAnimations = clips; importer.SaveAndReimport();
            Debug.Log("ROBOT_CLIPS " + string.Join(",", clips.Select(c => c.name)));
        }
        static string Short(string take) { int bar = take.LastIndexOf('|'); return bar >= 0 ? take.Substring(bar + 1) : take; }
        static GameObject BuildPrefab()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Source);
            var clips = AssetDatabase.LoadAllAssetsAtPath(Source).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToDictionary(c => c.name);
            var root = new GameObject("RobotExpressive");
            var body = (GameObject)PrefabUtility.InstantiatePrefab(model); body.name = "Model"; body.transform.SetParent(root.transform, false);
            // Normalize to a small companion height and stand the feet on the root.
            var bounds = BoundsOf(body); float scale = Height / Mathf.Max(.01f, bounds.size.y);
            body.transform.localScale *= scale; bounds = BoundsOf(body);
            body.transform.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            var animator = body.GetComponent<Animator>(); if (!animator) animator = body.AddComponent<Animator>();
            animator.runtimeAnimatorController = null; animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var player = root.AddComponent<ClipAnimator>(); player.animator = animator; player.idle = clips["Idle"]; player.walk = clips["Walking"];
            player.gestures = Gestures.Where(clips.ContainsKey).Select(g => new NamedClip { name = g, clip = clips[g] }).ToArray();
            var skinned = body.GetComponentsInChildren<SkinnedMeshRenderer>();
            Transform Bone(string name) => skinned.SelectMany(s => s.bones).FirstOrDefault(b => b && b.name == name) ?? body.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == name && !t.GetComponent<Renderer>());
            var rig = root.AddComponent<CharacterRigBinding>(); rig.animator = animator;
            rig.head = Bone("Head"); rig.chest = Bone("Torso") ?? Bone("Body"); rig.leftArm = Bone("UpperArm.L"); rig.rightArm = Bone("UpperArm.R");
            rig.leftForearm = Bone("LowerArm.L"); rig.rightForearm = Bone("LowerArm.R"); rig.hips = Bone("Hips");
            rig.leftLeg = Bone("UpperLeg.L"); rig.rightLeg = Bone("UpperLeg.R"); rig.leftKnee = Bone("LowerLeg.L"); rig.rightKnee = Bone("LowerLeg.R");
            rig.faceMeshes = skinned.Where(s => s.sharedMesh && s.sharedMesh.GetBlendShapeIndex("Sad") >= 0).ToArray();
            rig.blinkShape = rig.mouthShape = rig.lookXShape = rig.lookYShape = "";
            rig.emotionShapes = new[] {
                new EmotionShape { emotion = Emotion.excited, shape = "Surprised", weight = 85 }, new EmotionShape { emotion = Emotion.curious, shape = "Surprised", weight = 40 },
                new EmotionShape { emotion = Emotion.sad, shape = "Sad", weight = 90 }, new EmotionShape { emotion = Emotion.concerned, shape = "Sad", weight = 50 },
                new EmotionShape { emotion = Emotion.embarrassed, shape = "Sad", weight = 30 }, new EmotionShape { emotion = Emotion.happy, shape = "Surprised", weight = 15 } };
            Debug.Log("ROBOT_RIG head=" + rig.head + " chest=" + rig.chest + " hips=" + rig.hips + " legs=" + rig.leftLeg + "/" + rig.rightKnee + " forearm=" + rig.rightForearm + " faces=" + rig.faceMeshes.Length + " scale=" + scale.ToString("F3") + " clips=" + string.Join(",", clips.Keys));
            foreach (var renderer in body.GetComponentsInChildren<Renderer>())
                renderer.sharedMaterials = renderer.sharedMaterials.Select(Convert).ToArray();
            // Reuse WhiteChibi's soft contact shadow so the robot reads as standing on the floor.
            var shadowMaterial = AssetDatabase.FindAssets("\"Contact shadow\" t:Material").Select(g => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault();
            if (shadowMaterial)
            {
                var shadow = new GameObject("Contact shadow", typeof(MeshFilter), typeof(MeshRenderer)); shadow.transform.SetParent(root.transform, false);
                shadow.transform.localPosition = new Vector3(0, .045f, 0); shadow.transform.localRotation = Quaternion.Euler(90, 0, 0); shadow.transform.localScale = new Vector3(1.1f, 1.1f, 1);
                shadow.GetComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
                var r = shadow.GetComponent<MeshRenderer>(); r.sharedMaterial = shadowMaterial; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 8;
            var system = root.AddComponent<BodySystem>(); system.rig = rig;
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, Output + "/RobotExpressive.prefab"); UnityEngine.Object.DestroyImmediate(root); return prefab;
        }
        static Material Convert(Material source)
        {
            string path = Output + "/Materials/Robot_" + source.name.Replace("/", "_") + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
            material.color = source.HasProperty("_Color") ? source.color : Color.white; material.mainTexture = source.mainTexture;
            material.SetFloat("_Metallic", 0); material.SetFloat("_Smoothness", .45f); EditorUtility.SetDirty(material);
            return material;
        }
        static Bounds BoundsOf(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(); var bounds = renderers[0].bounds; foreach (var r in renderers) bounds.Encapsulate(r.bounds); return bounds;
        }

        // Batch entry: build, then render review stills and log what the character perceives in every room.
        public static void BuildAndReview()
        {
            Build();
            Directory.CreateDirectory("../artifacts/previews");
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/LivingRoom.unity");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Output + "/RobotExpressive.prefab");
            var camera = new GameObject("Review camera").AddComponent<Camera>(); camera.transform.position = new Vector3(.9f, 1.1f, -1.1f); camera.transform.LookAt(new Vector3(0, .55f, .6f)); camera.fieldOfView = 50; camera.nearClipPlane = .05f;
            var grade = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Resources/Graphics/CinematicGrade.asset"); if (grade) { camera.GetUniversalAdditionalCameraData().renderPostProcessing = true; var volume = new GameObject("Review grade").AddComponent<Volume>(); volume.isGlobal = true; volume.sharedProfile = grade; }
            UnityEngine.Object.FindAnyObjectByType<EnvironmentLighting>()?.Apply(TimeMode.Day, true);
            var robot = UnityEngine.Object.Instantiate(prefab, new Vector3(0, 0, .6f), Quaternion.Euler(0, 180, 0));
            var player = robot.GetComponent<ClipAnimator>(); var rig = robot.GetComponent<CharacterRigBinding>();
            void Pose(AnimationClip clip, float time, string shape, float weight, string file)
            {
                clip.SampleAnimation(player.animator.gameObject, time);
                foreach (var face in rig.faceMeshes) for (int i = 0; i < face.sharedMesh.blendShapeCount; i++) face.SetBlendShapeWeight(i, face.sharedMesh.GetBlendShapeName(i) == shape ? weight : 0);
                ContentValidation.Render(camera, "../artifacts/previews/" + file + ".png");
            }
            var wave = player.gestures.First(g => g.name == "Wave").clip; var arm = rig.rightArm; var before = arm.localRotation;
            wave.SampleAnimation(player.animator.gameObject, .8f);
            Debug.Log("ROBOT_SAMPLE bindings=" + AnimationUtility.GetCurveBindings(wave).Length + " paths=" + string.Join(",", AnimationUtility.GetCurveBindings(wave).Select(b => b.path).Distinct().Take(4)) + " armMoved=" + Quaternion.Angle(before, arm.localRotation).ToString("F1") + " armPath=" + AnimationUtility.CalculateTransformPath(arm, player.animator.transform));
            Pose(player.idle, .5f, null, 0, "Robot-idle");
            Pose(player.walk, .3f, null, 0, "Robot-walk");
            Pose(player.gestures.First(g => g.name == "Wave").clip, .8f, "Surprised", 85, "Robot-wave-excited");
            Pose(player.idle, .5f, "Sad", 90, "Robot-sad");
            UnityEngine.Object.DestroyImmediate(robot);
            foreach (var room in new[] { "Bedroom", "LivingRoom", "Cafe", "BotanicaCafe" })
            {
                var opened = EditorSceneManager.OpenScene("Assets/Scenes/" + room + ".unity");
                var world = SceneAwareness.Scan(opened, new Vector3(0, 0, .6f));
                Debug.Log("ROOM_AWARENESS " + room + " free=" + world.grid.FreeCells().Count() + " objects=" + string.Join(" | ", world.objects.Select(o => o.id + (o.obstacle ? "*" : "") + " " + o.bounds.center.ToString("F1"))));
                Debug.Log("ROOM_DESCRIBE " + world.Describe(room, new Vector3(0, 0, .6f), Vector3.back, new Vector3(0, 1.55f, -1.8f), Vector3.forward));
            }
            Debug.Log("ROBOT_REVIEW_DONE");
        }
    }
}
