using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UniGLTF;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UniVRM10;
using VRM10.MToon10;

namespace QuestAI.Editor
{
    // Rebuilds しろ・こはく・もも・むぎ from CC0 VRoid sample avatars (Assets/ThirdParty/VRoidSamples/LICENSE.md).
    // The VRM 1.0 importer migrates them and renders with the URP MToon10 toon shader; the textures are extracted so Android
    // can compress them. Each character keeps WhiteChibi's CC0 animation library, the shared rig binding and IK, and takes
    // its face (expressions, eye bones, visemes) and hair/skirt spring bones from the VRM itself.
    // Batch: -executeMethod QuestAI.Editor.VrmCharacterBuilder.BuildAndReview
    public static class VrmCharacterBuilder
    {
        const string Source = "Assets/ThirdParty/VRoidSamples/", Output = "Assets/Generated/VrmCharacters", Chibi = "Assets/Generated/WhiteChibi.prefab";
        // eyes: wanted eye height in metres. The samples range from 1.4 to 1.8 m; companions read friendlier a little smaller.
        sealed class Spec { public string id, file, name; public bool bunny, cat; public float eyes; }
        static readonly Spec[] Specs = {
            new Spec { id = "calm", file = "AvatarSample_F", name = "Shiro", eyes = 1.36f },
            new Spec { id = "bright", file = "AvatarSample_E", name = "Kohaku", eyes = 1.3f },
            new Spec { id = "bunny", file = "AvatarSample_G", name = "MomoBunny", bunny = true, eyes = 1.36f },
            new Spec { id = "cat", file = "SakuradaFumiriya", name = "MugiCat", cat = true, eyes = 1.42f } };

        [MenuItem("Quest AI/Build VRM characters (CC0 VRoid)")]
        public static void Build()
        {
            Directory.CreateDirectory(Output + "/Materials"); Directory.CreateDirectory(Output + "/Meshes"); AssetDatabase.Refresh();
            foreach (var spec in Specs)
            {
                string path = Source + spec.file + ".vrm";
                Import(path);
                var prefab = BuildPrefab(spec, path);
                UpdateProfile(spec, prefab);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("VRM_CHARACTERS_READY " + string.Join(",", Specs.Select(s => s.id)));
        }

        // Migrate to VRM 1.0 with URP materials, then move textures and materials out of the importer so they get normal
        // import settings (ASTC on Quest, 1024 px) and can be adjusted.
        static void Import(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as VrmScriptedImporter ?? throw new Exception("Not imported by the VRM 1.0 importer: " + path);
            importer.MigrateToVrm1 = true; importer.RenderPipeline = ImporterRenderPipelineTypes.UniversalRenderPipeline;
            EditorUtility.SetDirty(importer); importer.SaveAndReimport();
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(path)) throw new Exception("VRM import failed: " + path);
            var map = importer.GetExternalObjectMap();
            if (!map.Values.Any(v => v is Texture))
            {
                using (var data = new GlbFileParser(path).Parse())
                using (var migrated = Vrm10Data.Migrate(data, out var vrm1, out var migration))
                {
                    if (vrm1 == null) throw new Exception("VRM migration failed: " + path + " " + migration?.Message);
                    var subAssets = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Texture>().ToDictionary(t => new SubAssetKey(t), t => t);
                    var folder = UnityPath.FromUnityPath(Path.ChangeExtension(path, null) + ".Textures");
                    var extractor = new TextureExtractor(vrm1.Data, folder, subAssets);
                    foreach (var texture in new Vrm10TextureDescriptorGenerator(vrm1.Data).Get().GetEnumerable()) extractor.Extract(texture.SubAssetKey, texture);
                    AssetDatabase.Refresh();
                    foreach (var pair in extractor.Textures)
                        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(pair.Key.Type, pair.Key.Name), AssetDatabase.LoadAssetAtPath<Texture2D>(pair.Value.Value));
                }
                importer.SaveAndReimport();
                RemapEditorMaterial.ExtractMaterials(importer, p => Path.GetFileNameWithoutExtension(p) + ".Materials");
            }
            foreach (var texture in AssetDatabase.FindAssets("t:Texture2D", new[] { Path.ChangeExtension(path, null) + ".Textures" }).Select(AssetDatabase.GUIDToAssetPath))
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(texture);
                var android = ti.GetPlatformTextureSettings("Android");
                if (android.overridden && android.maxTextureSize == 1024) continue;
                android.overridden = true; android.maxTextureSize = 1024; android.format = TextureImporterFormat.ASTC_6x6; android.compressionQuality = 100;
                ti.SetPlatformTextureSettings(android); ti.mipmapEnabled = true; ti.SaveAndReimport();
            }
        }

        static GameObject BuildPrefab(Spec spec, string path)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var root = new GameObject(spec.name);
            try
            {
                var body = (GameObject)PrefabUtility.InstantiatePrefab(model); body.name = "Model"; body.transform.SetParent(root.transform, false);
                var animator = body.GetComponent<Animator>();
                if (!animator || !animator.avatar || !animator.avatar.isHuman) throw new Exception(spec.file + " has no humanoid avatar");
                Transform Bone(HumanBodyBones b) => animator.GetBoneTransform(b);
                // The rooms spawn the character facing its root's +Z: its left side must be on -X.
                if (Bone(HumanBodyBones.LeftUpperArm).position.x > Bone(HumanBodyBones.RightUpperArm).position.x) body.transform.localRotation = Quaternion.Euler(0, 180, 0);
                var bounds = BoundsOf(body);
                var eye = Bone(HumanBodyBones.LeftEye) ?? Bone(HumanBodyBones.Head);
                float scale = Mathf.Min(1, spec.eyes / Mathf.Max(.1f, eye.position.y - bounds.min.y));
                body.transform.localScale = Vector3.one * scale; bounds = BoundsOf(body);
                var hips = Bone(HumanBodyBones.Hips).position;
                body.transform.position -= new Vector3(hips.x, bounds.min.y, hips.z);
                bounds = BoundsOf(body);

                animator.runtimeAnimatorController = null; animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var library = AssetDatabase.LoadAssetAtPath<GameObject>(Chibi).GetComponent<ClipAnimator>();
                var player = root.AddComponent<ClipAnimator>(); player.animator = animator;
                player.idle = library.idle; player.walk = library.walk; player.run = library.run; player.carry = library.carry; player.gestures = library.gestures.ToArray();

                var rig = root.AddComponent<CharacterRigBinding>(); rig.animator = animator;
                rig.head = Bone(HumanBodyBones.Head); rig.chest = Bone(HumanBodyBones.UpperChest) ?? Bone(HumanBodyBones.Chest); rig.hips = Bone(HumanBodyBones.Hips);
                rig.leftArm = Bone(HumanBodyBones.LeftUpperArm); rig.rightArm = Bone(HumanBodyBones.RightUpperArm);
                rig.leftForearm = Bone(HumanBodyBones.LeftLowerArm); rig.rightForearm = Bone(HumanBodyBones.RightLowerArm);
                rig.leftLeg = Bone(HumanBodyBones.LeftUpperLeg); rig.rightLeg = Bone(HumanBodyBones.RightUpperLeg);
                rig.leftKnee = Bone(HumanBodyBones.LeftLowerLeg); rig.rightKnee = Bone(HumanBodyBones.RightLowerLeg);
                rig.faceMeshes = Array.Empty<SkinnedMeshRenderer>(); rig.blinkShape = rig.mouthShape = rig.lookXShape = rig.lookYShape = "";
                rig.animationYawOffset = AnimationYaw(root, player, animator);

                var vrm = body.GetComponent<Vrm10Instance>() ?? throw new Exception(spec.file + " has no Vrm10Instance");
                vrm.LookAtTargetType = VRM10ObjectLookAt.LookAtTargetTypes.YawPitchValue; vrm.DrawLookAtGizmo = false;
                root.AddComponent<VrmFaceDriver>().vrm = vrm;
                foreach (var renderer in body.GetComponentsInChildren<SkinnedMeshRenderer>()) { renderer.updateWhenOffscreen = true; renderer.shadowCastingMode = ShadowCastingMode.On; }

                if (spec.bunny || spec.cat) Accessories(spec, root, rig, body);
                ContactShadow(root);
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 8;
                var system = root.AddComponent<BodySystem>(); system.rig = rig;
                Debug.Log("VRM_CHARACTER " + spec.id + " height=" + bounds.size.y.ToString("F2") + " scale=" + scale.ToString("F3") + " yawOffset=" + rig.animationYawOffset + " clips=" + player.gestures.Length + " tris=" + body.GetComponentsInChildren<SkinnedMeshRenderer>().Sum(r => r.sharedMesh.triangles.Length / 3) + " materials=" + body.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials).Distinct().Count());
                return PrefabUtility.SaveAsPrefabAsset(root, Output + "/" + spec.id + ".prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        // Plays the idle clip on a copy and checks which way the retargeted body faces; the shared library faced backwards on WhiteChibi.
        static float AnimationYaw(GameObject root, ClipAnimator player, Animator animator)
        {
            var copy = UnityEngine.Object.Instantiate(root);
            try
            {
                var a = copy.GetComponentInChildren<Animator>(); player.idle.SampleAnimation(a.gameObject, .5f);
                var left = a.GetBoneTransform(HumanBodyBones.LeftUpperArm).position; var right = a.GetBoneTransform(HumanBodyBones.RightUpperArm).position;
                return left.x > right.x ? 180 : 0;
            }
            finally { UnityEngine.Object.DestroyImmediate(copy); }
        }

        static void UpdateProfile(Spec spec, GameObject prefab)
        {
            string path = "Assets/Resources/Profiles/" + spec.id + ".asset";
            var profile = AssetDatabase.LoadAssetAtPath<CharacterProfile>(path) ?? throw new Exception("Missing profile " + path);
            profile.prefab = prefab; profile.personalSpace = Mathf.Max(profile.personalSpace, .85f);
            switch (spec.id)
            {
                case "calm":
                    profile.personality = "白い髪と色違いの瞳を持つ、落ち着いた少女のしろ。優しく穏やかに話し、相手のペースを大事にする。短く自然な言葉で寄り添う。";
                    profile.speechStyle = "穏やかで柔らかく、少し大人びた落ち着きのある日本語。ゆっくりめのテンポで、親しい相手へ一度に1〜3文話す。ささやきすぎない。"; break;
                case "bright":
                    profile.personality = "琥珀色の髪をお団子にまとめた、明るく好奇心旺盛な少女のこはく。身近な小さな楽しみを見つけ、一緒に喜ぶ。押しつけず相手を尊重する。";
                    profile.speechStyle = "明るく弾む元気な日本語。やや高めで楽しそうな抑揚をつけるが、早口や叫び声は避け、一度に1〜3文話す。"; break;
                case "bunny":
                    profile.personality = "桃色に色づく長い髪と、ふわふわのうさ耳を持つ女の子のもも。やさしく人なつっこく、一緒に遊ぶと耳まで嬉しそうに揺れる。相手の気持ちを大切にして短く話す。"; break;
                case "cat":
                    profile.personality = "はちみつ色の髪と猫耳、しなやかなしっぽを持つむぎ。好奇心旺盛で少しマイペース。気に入ったものを見つけるのが上手で、親しく自然に話す。"; break;
            }
            EditorUtility.SetDirty(profile);
        }

        static Bounds BoundsOf(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(); var bounds = renderers[0].bounds; foreach (var r in renderers) bounds.Encapsulate(r.bounds); return bounds;
        }
        static List<Vector3> BakedVertices(GameObject body)
        {
            var points = new List<Vector3>(); var mesh = new Mesh();
            foreach (var renderer in body.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                renderer.BakeMesh(mesh, true); var m = renderer.transform.localToWorldMatrix;
                foreach (var v in mesh.vertices) points.Add(m.MultiplyPoint3x4(v));
            }
            UnityEngine.Object.DestroyImmediate(mesh); return points;
        }
        static float Percentile(IEnumerable<float> values, float p) { var list = values.OrderBy(v => v).ToList(); return list.Count == 0 ? 0 : list[Mathf.Clamp((int)(p * (list.Count - 1)), 0, list.Count - 1)]; }

        // Ears and tail are toon-shaded like the model, coloured from its own hair, and spring with CompanionSecondaryMotion.
        static void Accessories(Spec spec, GameObject root, CharacterRigBinding rig, GameObject body)
        {
            var points = BakedVertices(body); var head = rig.head.position; var hips = rig.hips.position;
            var crown = points.Where(p => p.y > head.y && new Vector2(p.x - head.x, p.z - head.z).magnitude < .12f).Select(p => p.y);
            float top = Percentile(crown, .97f);
            float half = Percentile(points.Where(p => p.y > head.y + .04f && p.y < head.y + .11f && Mathf.Abs(p.z - head.z) < .1f).Select(p => Mathf.Abs(p.x - head.x)), .95f);
            float back = Percentile(points.Where(p => Mathf.Abs(p.y - hips.y) < .1f && Mathf.Abs(p.x - hips.x) < .12f).Select(p => p.z), .02f);
            // Fluffy white for the bunny, honey for the cat (むぎ's hair fades from peach to violet, so its average reads purple).
            Color hair = HairColor(body);
            Color coat = spec.bunny ? new Color(1f, .96f, .94f) : new Color(.96f, .72f, .47f), inner = new Color(1f, .64f, .72f);
            var outline = body.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials).FirstOrDefault(m => m && m.name.IndexOf("hair", StringComparison.OrdinalIgnoreCase) >= 0);
            var coatMat = Toon(spec.id + "_Coat", coat, outline); var innerMat = Toon(spec.id + "_Inner", inner, outline);
            Debug.Log("VRM_ACCESSORY " + spec.id + " top=" + (top - head.y).ToString("F3") + " half=" + half.ToString("F3") + " back=" + (back - hips.z).ToString("F3") + " hair=" + hair);
            var secondary = root.AddComponent<CompanionSecondaryMotion>(); secondary.rabbit = spec.bunny;
            for (int side = -1; side <= 1; side += 2)
            {
                // Bunny ears rise from the crown; cat ears sit further out on the sides of the head.
                var basePoint = new Vector3(head.x + side * half * (spec.bunny ? .42f : .6f), top - half * (spec.bunny ? .22f : .18f), head.z - half * .08f);
                var pivot = new GameObject(side < 0 ? "Left ear spring" : "Right ear spring").transform; pivot.SetParent(rig.head, false);
                pivot.position = basePoint; pivot.rotation = Quaternion.AngleAxis(-side * (spec.bunny ? 9 : 20), Vector3.forward) * Quaternion.AngleAxis(spec.bunny ? -10 : -4, Vector3.right);
                if (spec.bunny)
                {
                    Part("Velvet ear", pivot, Vector3.zero, BunnyEar("BunnyEar", .2f, .052f, .03f, 0), coatMat);
                    Part("Rose inner ear", pivot, new Vector3(0, .012f, .011f), BunnyEar("BunnyInnerEar", .165f, .03f, .012f, 0), innerMat);
                }
                else
                {
                    Part("Cat ear", pivot, Vector3.zero, CatEar("CatEar", .095f, .05f, .032f), coatMat);
                    Part("Pink inner ear", pivot, new Vector3(0, .008f, .013f), CatEar("CatInnerEar", .07f, .032f, .01f), innerMat);
                }
                if (side < 0) secondary.leftEar = pivot; else secondary.rightEar = pivot;
            }
            var tail = new GameObject("Tail spring").transform; tail.SetParent(rig.hips, false);
            tail.position = new Vector3(hips.x, hips.y - .07f, back - .01f); tail.rotation = Quaternion.identity; secondary.tail = tail;
            if (spec.bunny) Part("Cotton tail", tail, new Vector3(0, 0, -.035f), Pompom(), coatMat);
            else Part("Plush tail", tail, Vector3.zero, CatTail(), coatMat);
        }
        static Color HairColor(GameObject body)
        {
            var mat = body.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials).FirstOrDefault(m => m && m.name.IndexOf("hair", StringComparison.OrdinalIgnoreCase) >= 0 && m.mainTexture);
            if (!mat) return new Color(.95f, .75f, .55f);
            string file = AssetDatabase.GetAssetPath(mat.mainTexture);
            var tex = new Texture2D(2, 2); if (!File.Exists(file) || !tex.LoadImage(File.ReadAllBytes(file))) return mat.color;
            var pixels = tex.GetPixels32(); double r = 0, g = 0, b = 0; int n = 0;
            for (int i = 0; i < pixels.Length; i += 7) { var p = pixels[i]; if (p.a < 200) continue; r += p.r; g += p.g; b += p.b; n++; }
            UnityEngine.Object.DestroyImmediate(tex);
            return n == 0 ? mat.color : new Color((float)(r / n / 255), (float)(g / n / 255), (float)(b / n / 255)) * mat.color;
        }
        static Material Toon(string name, Color color, Material outlineFrom)
        {
            string path = Output + "/Materials/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat) { mat = new Material(Shader.Find("VRM10/Universal Render Pipeline/MToon10")); AssetDatabase.CreateAsset(mat, path); }
            var c = new MToon10Context(mat) { BaseColorFactorSrgb = color, ShadeColorFactorSrgb = Color.Lerp(color * .78f, new Color(.9f, .55f, .62f), .18f), ShadingToonyFactor = .9f, ShadingShiftFactor = -.1f, GiEqualizationFactor = .9f, ParametricRimColorFactorSrgb = new Color(.18f, .16f, .16f), ParametricRimFresnelPowerFactor = 4, RimLightingMixFactor = 1 };
            if (outlineFrom)
            {
                var source = new MToon10Context(outlineFrom);
                c.OutlineWidthMode = source.OutlineWidthMode; c.OutlineWidthFactor = source.OutlineWidthFactor; c.OutlineColorFactorSrgb = source.OutlineColorFactorSrgb; c.OutlineLightingMixFactor = source.OutlineLightingMixFactor;
            }
            c.Validate(); EditorUtility.SetDirty(mat); return mat;
        }
        static void Part(string name, Transform parent, Vector3 local, Mesh mesh, Material material)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(parent, false); go.transform.localPosition = local;
            go.GetComponent<MeshFilter>().sharedMesh = mesh; var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.On;
        }

        // A rounded lop-free bunny ear: an elliptic section that swells then narrows to a soft tip, bending slightly back.
        static Mesh BunnyEar(string name, float length, float width, float depth, float cup)
        {
            return Sweep(name, 24, 16, t =>
            {
                float profile = Mathf.Pow(Mathf.Sin(Mathf.Lerp(.35f, Mathf.PI, t)), .55f);
                var center = new Vector3(0, t * length, -Mathf.Pow(t, 2) * length * .12f);
                return (center, width * .5f * Mathf.Max(.05f, profile * (1 - .25f * t)), depth * .5f * Mathf.Max(.15f, profile));
            });
        }
        // A cat ear: a cone with an elliptic base that is flatter front to back, rounded at the tip.
        static Mesh CatEar(string name, float height, float width, float depth)
        {
            return Sweep(name, 14, 16, t =>
            {
                float profile = Mathf.Cos(t * Mathf.PI * .5f); profile = Mathf.Pow(Mathf.Max(0, profile), .85f);
                return (new Vector3(0, t * height, -t * t * height * .1f), width * Mathf.Max(.04f, profile), depth * .5f * Mathf.Max(.12f, profile));
            });
        }
        // A plush tail seen from the side as a soft "?": out of the lower back, sagging down and back, then curling up.
        static readonly Vector3[] TailPath = { new Vector3(0, 0, 0), new Vector3(0, -.05f, -.1f), new Vector3(.02f, -.1f, -.22f), new Vector3(.04f, -.04f, -.34f), new Vector3(.05f, .09f, -.38f), new Vector3(.04f, .2f, -.34f) };
        static Vector3 TailPoint(float t)
        {
            // Catmull-Rom through TailPath.
            float f = Mathf.Clamp01(t) * (TailPath.Length - 1); int i = Mathf.Min((int)f, TailPath.Length - 2); float u = f - i;
            Vector3 p0 = TailPath[Mathf.Max(0, i - 1)], p1 = TailPath[i], p2 = TailPath[i + 1], p3 = TailPath[Mathf.Min(TailPath.Length - 1, i + 2)];
            return .5f * (2 * p1 + (p2 - p0) * u + (2 * p0 - 5 * p1 + 4 * p2 - p3) * u * u + (3 * p1 - p0 - 3 * p2 + p3) * u * u * u);
        }
        static Mesh CatTail()
        {
            return Sweep("CatTail", 36, 14, t =>
            {
                float r = Mathf.Lerp(.03f, .022f, t) * Mathf.Min(1, Mathf.Sin(Mathf.Lerp(.25f, Mathf.PI, t)) * 3.5f);
                return (TailPoint(t), Mathf.Max(.004f, r), Mathf.Max(.004f, r));
            });
        }
        static Mesh Pompom()
        {
            return Sweep("BunnyTail", 12, 16, t =>
            {
                float a = t * Mathf.PI; return (new Vector3(0, -Mathf.Cos(a) * .045f, 0), Mathf.Max(.002f, Mathf.Sin(a) * .05f), Mathf.Max(.002f, Mathf.Sin(a) * .045f));
            });
        }
        // Lofts elliptic rings along a path (rings x sides) with closed ends; ring frames follow the path tangent.
        static Mesh Sweep(string name, int rings, int sides, Func<float, (Vector3 center, float rx, float rz)> ring)
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            for (int i = 0; i < rings; i++)
            {
                float t = (float)i / (rings - 1); var (c, rx, rz) = ring(t);
                var ahead = ring(Mathf.Min(1, t + .01f)).center - ring(Mathf.Max(0, t - .01f)).center; if (ahead.sqrMagnitude < 1e-10f) ahead = Vector3.up;
                var frame = Quaternion.FromToRotation(Vector3.up, ahead.normalized);
                for (int j = 0; j < sides; j++) { float a = j * Mathf.PI * 2 / sides; vertices.Add(c + frame * new Vector3(Mathf.Cos(a) * rx, 0, Mathf.Sin(a) * rz)); }
            }
            for (int i = 0; i < rings - 1; i++) for (int j = 0; j < sides; j++)
                {
                    int v = i * sides + j, n = i * sides + (j + 1) % sides;
                    triangles.AddRange(new[] { v, n + sides, n, v, v + sides, n + sides });
                }
            int bottom = vertices.Count; vertices.Add(ring(0).center); int tip = vertices.Count; vertices.Add(ring(1).center);
            for (int j = 0; j < sides; j++)
            {
                int n = (j + 1) % sides; triangles.AddRange(new[] { bottom, n, j });
                int last = (rings - 1) * sides; triangles.AddRange(new[] { tip, last + j, last + n });
            }
            var mesh = new Mesh { name = name }; mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            string path = Output + "/Meshes/" + name + ".asset"; var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing) { EditorUtility.CopySerialized(mesh, existing); UnityEngine.Object.DestroyImmediate(mesh); return existing; }
            AssetDatabase.CreateAsset(mesh, path); return mesh;
        }
        static void ContactShadow(GameObject root)
        {
            var material = AssetDatabase.FindAssets("\"Contact shadow\" t:Material").Select(g => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault();
            if (!material) return;
            var shadow = new GameObject("Contact shadow", typeof(MeshFilter), typeof(MeshRenderer)); shadow.transform.SetParent(root.transform, false);
            shadow.transform.localPosition = new Vector3(0, .012f, 0); shadow.transform.localRotation = Quaternion.Euler(90, 0, 0); shadow.transform.localScale = new Vector3(.75f, .75f, 1);
            shadow.GetComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var r = shadow.GetComponent<MeshRenderer>(); r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        // Batch: build, then render each character in the living room (full body front/back, head with ears, expressions) for review.
        public static void BuildAndReview()
        {
            Build();
            Directory.CreateDirectory("../artifacts/previews");
            // Asynchronous compilation would capture the cyan placeholder shader on the first frames.
            bool async = EditorSettings.asyncShaderCompilation; EditorSettings.asyncShaderCompilation = false;
            try
            {
                EditorSceneManager.OpenScene("Assets/Scenes/LivingRoom.unity");
                UnityEngine.Object.FindAnyObjectByType<EnvironmentLighting>()?.Apply(TimeMode.Day, true);
                LightProbes.Tetrahedralize();
                var camera = new GameObject("Review camera").AddComponent<Camera>(); camera.nearClipPlane = .03f;
                var grade = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Resources/Graphics/CinematicGrade.asset");
                if (grade) { camera.GetUniversalAdditionalCameraData().renderPostProcessing = true; var volume = new GameObject("Review grade").AddComponent<Volume>(); volume.isGlobal = true; volume.sharedProfile = grade; }
                var lineup = new List<GameObject>();
                for (int i = 0; i < Specs.Length; i++)
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Output + "/" + Specs[i].id + ".prefab"));
                    go.transform.SetPositionAndRotation(new Vector3((i - 1.5f) * .55f, 0, .3f), Quaternion.Euler(0, 180, 0)); Pose(go); lineup.Add(go);
                }
                camera.fieldOfView = 42; camera.transform.position = new Vector3(0, 1.0f, -2.6f); camera.transform.LookAt(new Vector3(0, .8f, .3f));
                // New renderers have no probe lighting on their first frame; render once to settle it.
                ContentValidation.Render(camera, "../artifacts/previews/Vrm-lineup.png");
                ContentValidation.Render(camera, "../artifacts/previews/Vrm-lineup.png");
                foreach (var go in lineup) go.SetActive(false);
                foreach (var (spec, go) in Specs.Zip(lineup, (s, g) => (s, g)))
                {
                    go.SetActive(true); go.transform.position = new Vector3(0, 0, .3f);
                    var head = go.GetComponent<CharacterRigBinding>().head.position;
                    camera.fieldOfView = 36; camera.transform.position = new Vector3(.45f, head.y * .62f, -2.1f); camera.transform.LookAt(new Vector3(0, head.y * .52f, .3f));
                    ContentValidation.Render(camera, "../artifacts/previews/Vrm-" + spec.id + ".png");
                    go.transform.rotation = Quaternion.Euler(0, 20, 0); ContentValidation.Render(camera, "../artifacts/previews/Vrm-" + spec.id + "-back.png"); go.transform.rotation = Quaternion.Euler(0, 180, 0);
                    camera.fieldOfView = 26; camera.transform.position = head + new Vector3(.45f, .12f, -.8f); camera.transform.LookAt(head + Vector3.up * .08f);
                    ContentValidation.Render(camera, "../artifacts/previews/Vrm-" + spec.id + "-head.png");
                    camera.fieldOfView = 20; camera.transform.position = head + new Vector3(.1f, .04f, -.75f); camera.transform.LookAt(head + Vector3.up * .05f);
                    foreach (var emotion in new[] { Emotion.neutral, Emotion.happy, Emotion.excited, Emotion.sad, Emotion.concerned, Emotion.embarrassed, Emotion.curious })
                    {
                        Express(go, emotion); ContentValidation.Render(camera, "../artifacts/previews/Vrm-" + spec.id + "-" + emotion + ".png");
                    }
                    Express(go, Emotion.neutral);
                    foreach (var x in go.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Face preview").ToArray()) UnityEngine.Object.DestroyImmediate(x.gameObject);
                    foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) r.enabled = true;
                    go.SetActive(false);
                }
            }
            finally { EditorSettings.asyncShaderCompilation = async; }
            Debug.Log("VRM_CHARACTERS_REVIEW_DONE");
        }
        static void Pose(GameObject go)
        {
            var player = go.GetComponent<ClipAnimator>(); var rig = go.GetComponent<CharacterRigBinding>();
            player.idle.SampleAnimation(player.animator.gameObject, .5f);
            if (rig.animationYawOffset != 0) rig.hips.rotation = Quaternion.AngleAxis(rig.animationYawOffset, go.transform.up) * rig.hips.rotation;
        }
        // Edit-mode preview of the runtime face: the same weights VrmFaceDriver gives each expression, applied through the
        // VRM's own morph-target bindings (the VRM runtime only runs in Play mode).
        static void Express(GameObject go, Emotion emotion)
        {
            var vrm = go.GetComponent<VrmFaceDriver>().vrm; var expressions = vrm.Vrm.Expression;
            var weights = new Dictionary<(SkinnedMeshRenderer, int), float>();
            foreach (var (preset, clip) in expressions.Clips)
            {
                if (!clip) continue; float w = VrmFaceDriver.Moods.Contains(preset) ? VrmFaceDriver.MoodWeight(emotion, preset) : 0;
                foreach (var bind in clip.MorphTargetBindings)
                {
                    var target = vrm.transform.Find(bind.RelativePath); var renderer = target ? target.GetComponent<SkinnedMeshRenderer>() : null; if (!renderer) continue;
                    weights.TryGetValue((renderer, bind.Index), out float sum); weights[(renderer, bind.Index)] = Mathf.Min(100, sum + bind.Weight * w * MorphTargetBinding.VRM_TO_UNITY);
                }
            }
            foreach (var pair in weights) pair.Key.Item1.SetBlendShapeWeight(pair.Key.Item2, pair.Value);
            // Batch edit-mode renders do not re-deform skinned meshes after a weight change; show a baked copy instead.
            foreach (var old in go.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Face preview").ToArray()) UnityEngine.Object.DestroyImmediate(old.gameObject);
            foreach (var renderer in weights.Keys.Select(k => k.Item1).Distinct())
            {
                var baked = new Mesh(); renderer.BakeMesh(baked, true);
                var preview = new GameObject("Face preview", typeof(MeshFilter), typeof(MeshRenderer)); preview.transform.SetParent(renderer.transform.parent, false);
                preview.transform.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation);
                preview.GetComponent<MeshFilter>().sharedMesh = baked; var r = preview.GetComponent<MeshRenderer>(); r.sharedMaterials = renderer.sharedMaterials; r.probeAnchor = renderer.probeAnchor;
                renderer.enabled = false;
            }
        }
    }
}
