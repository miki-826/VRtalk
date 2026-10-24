using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace QuestAI.Editor
{
    // Imports the CC0 Quaternius Universal Animation Library (Standard 1 + 2) as Humanoid clips, gives WhiteChibi a ClipAnimator
    // with them (real walk/run/carry cycles and ~85 actions), widens the robot's own clip set, and builds the prop catalog
    // from CC0 Kenney furniture models. Batch: -executeMethod QuestAI.Editor.AnimationLibraryBuilder.Build
    public static class AnimationLibraryBuilder
    {
        static readonly string[] Sources = { "Assets/ThirdParty/Quaternius/UAL1_Standard.fbx", "Assets/ThirdParty/Quaternius/UAL2_Standard.fbx" };
        const string Chibi = "Assets/Generated/WhiteChibi.prefab", Robot = "Assets/Generated/Robot/RobotExpressive.prefab", RobotModel = "Assets/ThirdParty/RobotExpressive/RobotExpressive.fbx";
        const string Catalog = "Assets/Resources/Motion/Props.asset", Kenney = "Assets/ThirdParty/Kenney/Models/FBX format/";
        // Explicit Humanoid mapping, used only if Unity's automatic mapping does not produce a valid avatar.
        static readonly Dictionary<string, string>[] Maps = {
            new Dictionary<string, string> { ["Hips"]="DEF-hips", ["Spine"]="DEF-spine.001", ["Chest"]="DEF-spine.002", ["UpperChest"]="DEF-spine.003", ["Neck"]="DEF-neck", ["Head"]="DEF-head",
                ["LeftShoulder"]="DEF-shoulder.L", ["LeftUpperArm"]="DEF-upper_arm.L", ["LeftLowerArm"]="DEF-forearm.L", ["LeftHand"]="DEF-hand.L", ["RightShoulder"]="DEF-shoulder.R", ["RightUpperArm"]="DEF-upper_arm.R", ["RightLowerArm"]="DEF-forearm.R", ["RightHand"]="DEF-hand.R",
                ["LeftUpperLeg"]="DEF-thigh.L", ["LeftLowerLeg"]="DEF-shin.L", ["LeftFoot"]="DEF-foot.L", ["LeftToes"]="DEF-toe.L", ["RightUpperLeg"]="DEF-thigh.R", ["RightLowerLeg"]="DEF-shin.R", ["RightFoot"]="DEF-foot.R", ["RightToes"]="DEF-toe.R" },
            new Dictionary<string, string> { ["Hips"]="pelvis", ["Spine"]="spine_01", ["Chest"]="spine_02", ["UpperChest"]="spine_03", ["Neck"]="neck_01", ["Head"]="Head",
                ["LeftShoulder"]="clavicle_l", ["LeftUpperArm"]="upperarm_l", ["LeftLowerArm"]="lowerarm_l", ["LeftHand"]="hand_l", ["RightShoulder"]="clavicle_r", ["RightUpperArm"]="upperarm_r", ["RightLowerArm"]="lowerarm_r", ["RightHand"]="hand_r",
                ["LeftUpperLeg"]="thigh_l", ["LeftLowerLeg"]="calf_l", ["LeftFoot"]="foot_l", ["LeftToes"]="ball_l", ["RightUpperLeg"]="thigh_r", ["RightLowerLeg"]="calf_r", ["RightFoot"]="foot_r", ["RightToes"]="ball_r" } };

        [MenuItem("Quest AI/Import CC0 animation library")]
        public static void Build()
        {
            var clips = new Dictionary<string, AnimationClip>();
            for (int i = 0; i < Sources.Length; i++) foreach (var clip in Import(Sources[i], Maps[i])) clips[clip.name] = clip;
            if (!clips.ContainsKey("Idle_Loop") || !clips.ContainsKey("Walk_Loop")) throw new Exception("Animation library is missing Idle_Loop/Walk_Loop: " + string.Join(",", clips.Keys));
            AttachToChibi(clips); WidenRobot(); BuildCatalog();
            AssetDatabase.SaveAssets();
            Debug.Log("ANIMATION_LIBRARY_READY " + clips.Count + " clips: " + string.Join(",", clips.Keys.OrderBy(k => k)));
        }
        static string Short(string take) { int bar = take.LastIndexOf('|'); return bar >= 0 ? take.Substring(bar + 1) : take; }
        static IEnumerable<AnimationClip> Import(string path, Dictionary<string, string> map)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path) ?? throw new Exception("Missing " + path);
            importer.animationType = ModelImporterAnimationType.Human; importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true; importer.materialImportMode = ModelImporterMaterialImportMode.None; importer.importBlendShapes = false;
            importer.resampleCurves = true; importer.animationCompression = ModelImporterAnimationCompression.Optimal;
            Configure(importer); importer.SaveAndReimport();
            var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            if (!avatar || !avatar.isHuman)
            {
                // Map the rig by hand, with the model's own bind pose as the skeleton.
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var names = new HashSet<string>(model.GetComponentsInChildren<Transform>(true).Select(t => t.name));
                var description = importer.humanDescription;
                description.human = map.Where(p => names.Contains(p.Value)).Select(p => new HumanBone { humanName = p.Key, boneName = p.Value, limit = new HumanLimit { useDefaultValues = true } }).ToArray();
                description.skeleton = model.GetComponentsInChildren<Transform>(true).Select(t => new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray();
                importer.humanDescription = description; importer.SaveAndReimport();
                avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
                Debug.Log("ANIMATION_LIBRARY_MANUAL_MAP " + path + " human=" + (avatar && avatar.isHuman));
            }
            if (!avatar || !avatar.isHuman) throw new Exception("No humanoid avatar for " + path);
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToArray();
        }
        static void Configure(ModelImporter importer)
        {
            var clips = importer.defaultClipAnimations.Where(c => !Short(c.takeName).EndsWith("_RM") && Short(c.takeName) != "A_TPose").ToArray();
            foreach (var clip in clips)
            {
                clip.name = Short(clip.takeName);
                clip.loopTime = clip.name.EndsWith("_Loop") || clip.name == "Sword_Idle";
                // In place: the character's own locomotion moves it, the clip only animates the body.
                clip.lockRootRotation = true; clip.lockRootHeightY = true; clip.lockRootPositionXZ = true;
                clip.keepOriginalOrientation = true; clip.keepOriginalPositionY = true; clip.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
        }
        static void AttachToChibi(Dictionary<string, AnimationClip> clips)
        {
            var root = PrefabUtility.LoadPrefabContents(Chibi);
            try
            {
                var rig = root.GetComponent<CharacterRigBinding>(); var animator = rig ? rig.animator : root.GetComponentInChildren<Animator>();
                if (!animator || !animator.avatar || !animator.avatar.isHuman) throw new Exception("WhiteChibi has no humanoid Animator");
                animator.runtimeAnimatorController = null; animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var player = root.GetComponent<ClipAnimator>(); if (!player) player = root.AddComponent<ClipAnimator>();
                player.animator = animator; player.idle = clips["Idle_Loop"]; player.walk = clips["Walk_Loop"];
                player.run = clips.TryGetValue("Jog_Fwd_Loop", out var run) ? run : null; player.carry = clips.TryGetValue("Walk_Carry_Loop", out var carry) ? carry : null;
                player.gestures = clips.OrderBy(p => p.Key).Select(p => new NamedClip { name = p.Key, clip = p.Value }).ToArray();
                PrefabUtility.SaveAsPrefabAsset(root, Chibi);
                Debug.Log("ANIMATION_LIBRARY_CHIBI clips=" + player.gestures.Length + " humanScale=" + animator.humanScale.ToString("F3"));
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        static void WidenRobot()
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(RobotModel).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToDictionary(c => c.name);
            if (clips.Count == 0) return;
            var root = PrefabUtility.LoadPrefabContents(Robot);
            try
            {
                var player = root.GetComponent<ClipAnimator>(); if (!player) return;
                player.run = clips.TryGetValue("Running", out var run) ? run : null;
                player.gestures = clips.Where(p => p.Key != "Idle" && p.Key != "Walking").OrderBy(p => p.Key).Select(p => new NamedClip { name = p.Key, clip = p.Value }).ToArray();
                PrefabUtility.SaveAsPrefabAsset(root, Robot);
                Debug.Log("ANIMATION_LIBRARY_ROBOT clips=" + string.Join(",", player.gestures.Select(g => g.name)));
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        static void BuildCatalog()
        {
            System.IO.Directory.CreateDirectory("Assets/Resources/Motion");
            var catalog = AssetDatabase.LoadAssetAtPath<PropCatalog>(Catalog);
            if (!catalog) { catalog = ScriptableObject.CreateInstance<PropCatalog>(); AssetDatabase.CreateAsset(catalog, Catalog); }
            PropCatalog.Entry Entry(string label, string model, float size, float mass) => new PropCatalog.Entry { label = label, model = AssetDatabase.LoadAssetAtPath<GameObject>(Kenney + model + ".fbx"), size = size, mass = mass };
            catalog.entries = new[] { Entry("クッション", "pillow", .45f, .4f), Entry("本", "books", .28f, .6f), Entry("箱", "cardboardBoxClosed", .4f, .5f), Entry("ぬいぐるみ", "bear", .38f, .3f) };
            foreach (var e in catalog.entries) if (!e.model) throw new Exception("Missing Kenney model for " + e.label);
            EditorUtility.SetDirty(catalog);
            Debug.Log("ANIMATION_LIBRARY_PROPS " + string.Join(",", catalog.entries.Select(e => e.label + ":" + e.model.name)));
        }
    }
}
