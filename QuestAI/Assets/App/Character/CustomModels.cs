using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UniGLTF;
using UniVRM10;
using UnityEngine;
using UnityEngine.Networking;

namespace QuestAI
{
    // Characters the user adds as VRM files (VRM 0.x or 1.0). Put "name.vrm" in the Models folder, optionally with "name.txt"
    // (first line: display name, the rest: personality). They appear in the character list and are loaded when a session
    // starts, then get the same animation library, rig binding, IK and body system as the built-in mascots; the face uses
    // the model's own VRM expressions, eye bones and spring bones.
    public static class CustomModels
    {
        public const string Prefix = "custom:";
        public static string Folder => Path.Combine(Application.persistentDataPath, "Models");
        public static CharacterProfile[] Scan()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                return Directory.GetFiles(Folder, "*.vrm").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Select(Profile).ToArray();
            }
            catch (Exception e) { Debug.LogWarning("Custom models unavailable: " + e.GetType().Name); return Array.Empty<CharacterProfile>(); }
        }
        public static CharacterProfile Profile(string path)
        {
            var profile = ScriptableObject.CreateInstance<CharacterProfile>(); string file = Path.GetFileNameWithoutExtension(path);
            profile.id = profile.name = Prefix + file; profile.modelPath = path; profile.voice = "marin"; profile.personalSpace = .8f;
            string title = file, personality = "あなたが登録したキャラクターの「" + file + "」。明るく親しみやすく、相手のペースに合わせて短く自然な言葉で話す。";
            string notes = Path.ChangeExtension(path, ".txt");
            if (File.Exists(notes))
            {
                var lines = File.ReadAllLines(notes).Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
                if (lines.Length > 0) title = lines[0];
                if (lines.Length > 1) personality = string.Join("\n", lines.Skip(1));
            }
            profile.displayName = title + " · マイモデル"; profile.personality = personality;
            return profile;
        }

        const int MaxBytes = 300 * 1024 * 1024;
        // Downloads a .vrm from a direct link into the Models folder and returns its path. A file that is not a binary glTF
        // (e.g. a share page) is rejected before it is saved.
        public static async Task<string> Download(string url, CancellationToken ct = default)
        {
            if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                throw new InvalidOperationException("https:// で始まるURLを入力してください。");
            using var request = UnityWebRequest.Get(uri); request.timeout = 180;
            var operation = request.SendWebRequest();
            while (!operation.isDone) { if (ct.IsCancellationRequested) { request.Abort(); ct.ThrowIfCancellationRequested(); } await Task.Yield(); }
            if (request.result != UnityWebRequest.Result.Success) throw new InvalidOperationException("ダウンロードできませんでした（" + (request.responseCode > 0 ? request.responseCode.ToString() : request.error) + "）。URLを確認してください。");
            var data = request.downloadHandler.data;
            if (data == null || data.Length < 20 || data[0] != 'g' || data[1] != 'l' || data[2] != 'T' || data[3] != 'F')
                throw new InvalidOperationException("VRMファイルではありませんでした。共有ページではなく .vrm ファイルへの直接リンクを指定してください。");
            if (data.Length > MaxBytes) throw new InvalidOperationException("ファイルが大きすぎます（300MBまで）。");
            string name = new string(Path.GetFileNameWithoutExtension(Uri.UnescapeDataString(uri.AbsolutePath)).Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray()).Trim();
            if (name.Length == 0) name = "model";
            Directory.CreateDirectory(Folder); string path = Path.Combine(Folder, name + ".vrm");
            for (int i = 2; File.Exists(path); i++) path = Path.Combine(Folder, name + "_" + i + ".vrm");
            File.WriteAllBytes(path, data); return path;
        }

        // Loads the VRM and turns it into a companion like the built-in prefabs. The result is inactive so the caller can
        // place it first; activating it starts the clips, face and body.
        public static async Task<GameObject> Load(CharacterProfile profile, ClipAnimator library, CancellationToken ct = default)
        {
            if (!File.Exists(profile.modelPath)) throw new InvalidOperationException("モデルファイルが見つかりません: " + Path.GetFileName(profile.modelPath));
            Vrm10Instance vrm;
            try
            {
                vrm = await Vrm10.LoadPathAsync(profile.modelPath, canLoadVrm0X: true, controlRigGenerationOption: ControlRigGenerationOption.None, showMeshes: false,
                    materialGenerator: new UrpVrm10MaterialDescriptorGenerator(), ct: ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { Debug.LogWarning("VRM load failed: " + e); throw new InvalidOperationException("モデルを読み込めませんでした。VRM（0.x / 1.0）の人型モデルか確認してください。"); }
            if (!vrm) throw new InvalidOperationException("モデルを読み込めませんでした。VRM（0.x / 1.0）の人型モデルか確認してください。");
            try { return Build(profile, vrm, library); }
            catch { UnityEngine.Object.Destroy(vrm.gameObject); throw; }
        }

        public static GameObject Build(CharacterProfile profile, Vrm10Instance vrm, ClipAnimator library)
        {
            var body = vrm.gameObject; var animator = body.GetComponent<Animator>();
            if (!animator || !animator.avatar || !animator.avatar.isHuman) throw new InvalidOperationException("このモデルには人型（Humanoid）の骨格がありません。");
            Transform Bone(HumanBodyBones b) => animator.GetBoneTransform(b);
            var root = new GameObject(profile.displayName);
            body.name = "Model"; body.transform.SetParent(root.transform, false);
            // The rooms spawn the character facing its root's +Z: its left side must be on -X.
            if (Bone(HumanBodyBones.LeftUpperArm).position.x > Bone(HumanBodyBones.RightUpperArm).position.x) body.transform.localRotation = Quaternion.Euler(0, 180, 0);
            var renderers = body.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers) { r.enabled = true; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; if (r is SkinnedMeshRenderer s) s.updateWhenOffscreen = true; }
            // Keep the model's own size unless it is far from a companion's; then bring it into 0.6–1.9 m.
            var bounds = BoundsOf(renderers); float height = Mathf.Max(.01f, bounds.size.y);
            float scale = height > 1.9f ? 1.9f / height : height < .6f ? .6f / height : 1;
            body.transform.localScale *= scale; bounds = BoundsOf(renderers);
            var hips = Bone(HumanBodyBones.Hips).position;
            body.transform.position -= new Vector3(hips.x, bounds.min.y, hips.z);
            // Sample while the model is still active, then hold everything until the caller places it.
            float yaw = library ? AnimationYaw(body, library.idle) : 0;
            root.SetActive(false);

            animator.runtimeAnimatorController = null; animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var player = root.AddComponent<ClipAnimator>(); player.animator = animator;
            if (library) { player.idle = library.idle; player.walk = library.walk; player.run = library.run; player.carry = library.carry; player.gestures = library.gestures.ToArray(); }
            var rig = root.AddComponent<CharacterRigBinding>(); rig.animator = animator;
            rig.head = Bone(HumanBodyBones.Head); rig.chest = Bone(HumanBodyBones.UpperChest) ?? Bone(HumanBodyBones.Chest) ?? Bone(HumanBodyBones.Spine); rig.hips = Bone(HumanBodyBones.Hips);
            rig.leftArm = Bone(HumanBodyBones.LeftUpperArm); rig.rightArm = Bone(HumanBodyBones.RightUpperArm);
            rig.leftForearm = Bone(HumanBodyBones.LeftLowerArm); rig.rightForearm = Bone(HumanBodyBones.RightLowerArm);
            rig.leftLeg = Bone(HumanBodyBones.LeftUpperLeg); rig.rightLeg = Bone(HumanBodyBones.RightUpperLeg);
            rig.leftKnee = Bone(HumanBodyBones.LeftLowerLeg); rig.rightKnee = Bone(HumanBodyBones.RightLowerLeg);
            rig.faceMeshes = Array.Empty<SkinnedMeshRenderer>(); rig.blinkShape = rig.mouthShape = rig.lookXShape = rig.lookYShape = "";
            rig.animationYawOffset = yaw;
            vrm.LookAtTargetType = VRM10ObjectLookAt.LookAtTargetTypes.YawPitchValue; vrm.DrawLookAtGizmo = false;
            root.AddComponent<VrmFaceDriver>().vrm = vrm;
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 8;
            root.AddComponent<BodySystem>().rig = rig;
            return root;
        }
        // Plays the idle clip once and checks which way the retargeted body faces, then restores the pose.
        static float AnimationYaw(GameObject body, AnimationClip idle)
        {
            if (!idle) return 0;
            var bones = body.GetComponentsInChildren<Transform>(true); var saved = bones.Select(b => (b.localPosition, b.localRotation)).ToArray();
            try
            {
                idle.SampleAnimation(body, .5f); var animator = body.GetComponent<Animator>();
                return animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).position.x > animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position.x ? 180 : 0;
            }
            finally { for (int i = 0; i < bones.Length; i++) { bones[i].localPosition = saved[i].localPosition; bones[i].localRotation = saved[i].localRotation; } }
        }
        static Bounds BoundsOf(Renderer[] renderers)
        {
            if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.one);
            var bounds = renderers[0].bounds; foreach (var r in renderers) bounds.Encapsulate(r.bounds); return bounds;
        }
    }
}
