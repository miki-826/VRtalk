using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestAI.Editor
{
    // Authored mesh accessories on the existing expressive Humanoid: all shared clips, facial channels and IK remain usable.
    public static class CuteCharacterBuilder
    {
        const string Output = "Assets/Generated/Companions";
        [MenuItem("Quest AI/Add bunny and cat companions")]
        public static void Build()
        {
            Directory.CreateDirectory(Output + "/Materials"); Directory.CreateDirectory(Output + "/Meshes"); AssetDatabase.Refresh();
            AlignHumanoidFacing();
            Make(true); Make(false); AssetDatabase.SaveAssets(); Debug.Log("CUTE_COMPANIONS_READY bunny,cat");
        }
        static void AlignHumanoidFacing()
        {
            const string path = "Assets/Generated/WhiteChibi.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var rig = root.GetComponent<CharacterRigBinding>();
                rig.hips = rig.animator.GetBoneTransform(HumanBodyBones.Hips); rig.animationYawOffset = 180;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        static Material Mat(string name, Color color)
        {
            string path = Output + "/Materials/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
            mat.color = color; mat.SetFloat("_Smoothness", .28f); mat.SetFloat("_Metallic", 0); EditorUtility.SetDirty(mat); return mat;
        }
        static Transform Pivot(string name, Transform parent, Vector3 position)
        {
            var t = new GameObject(name).transform; t.SetParent(parent, false); t.position = position; return t;
        }
        static GameObject Ellipsoid(string name, Transform parent, Vector3 position, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere); go.name = name; UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false); go.transform.position = position; go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.On; return go;
        }
        static void Make(bool bunny)
        {
            string id = bunny ? "bunny" : "cat";
            var root = PrefabUtility.LoadPrefabContents("Assets/Generated/WhiteChibi.prefab");
            try
            {
                root.name = bunny ? "MomoBunny" : "MugiCat";
                var rig = root.GetComponent<CharacterRigBinding>(); var animator = rig.animator;
                rig.hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                rig.leftLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg); rig.rightLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                rig.leftKnee = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg); rig.rightKnee = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
                var coat = Mat(id + "_Coat", bunny ? new Color(1, .91f, .90f) : new Color(.94f, .70f, .43f));
                var cream = Mat(id + "_Cream", new Color(1, .96f, .87f));
                var pink = Mat(id + "_Rose", new Color(1, .48f, .60f));
                var accent = Mat(id + "_Accent", bunny ? new Color(.56f, .32f, .53f) : new Color(.24f, .48f, .42f));
                foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if (renderer.name.StartsWith("Ear.")) renderer.gameObject.SetActive(false);
                    if (renderer.name.StartsWith("Body_") || renderer.name.StartsWith("Head_")) renderer.sharedMaterials = renderer.sharedMaterials.Select(_ => coat).ToArray();
                }
                var head = root.GetComponentsInChildren<SkinnedMeshRenderer>().First(r => r.name.StartsWith("Head_"));
                var bounds = head.bounds; Vector3 center = bounds.center; float w = bounds.extents.x;
                Debug.Log("COMPANION_HEAD " + id + " " + bounds + " scale=" + root.transform.lossyScale);
                var secondary = root.AddComponent<CompanionSecondaryMotion>(); secondary.rabbit = bunny;
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 basePoint = new Vector3(center.x + side * w * .53f, bounds.max.y - w * .12f, center.z);
                    var ear = Pivot(side < 0 ? "Left ear spring" : "Right ear spring", rig.head, basePoint);
                    if (bunny)
                    {
                        Ellipsoid("Velvet ear", ear, basePoint + Vector3.up * w * .70f, new Vector3(w * .52f, w * 1.85f, w * .38f), coat);
                        Ellipsoid("Rose inner ear", ear, basePoint + new Vector3(0, w * .72f, w * .145f), new Vector3(w * .28f, w * 1.36f, w * .14f), pink);
                    }
                    else
                    {
                        CatEar(ear, basePoint, w, coat, false); CatEar(ear, basePoint + Vector3.forward * w * .13f, w * .62f, pink, true);
                    }
                    ear.localRotation *= Quaternion.Euler(bunny ? -4 : 0, 0, -side * (bunny ? 10 : 18));
                    if (side < 0) secondary.leftEar = ear; else secondary.rightEar = ear;
                    // Small rosy cheeks sit just on the face surface and travel with the head.
                    Ellipsoid("Rosy cheek", rig.head, new Vector3(center.x + side * w * .58f, center.y - w * .24f, bounds.max.z - w * .13f), new Vector3(w * .31f, w * .16f, w * .09f), pink);
                }
                float neck = bounds.min.y + w * .02f;
                Vector3 bow = new Vector3(center.x, neck, center.z + w * .65f);
                Ellipsoid("Bow left", rig.chest, bow + Vector3.left * w * .17f, new Vector3(w * .39f, w * .25f, w * .19f), accent);
                Ellipsoid("Bow right", rig.chest, bow + Vector3.right * w * .17f, new Vector3(w * .39f, w * .25f, w * .19f), accent);
                Ellipsoid("Bow knot", rig.chest, bow + Vector3.forward * .01f, Vector3.one * w * .19f, cream);
                var body = root.GetComponentsInChildren<SkinnedMeshRenderer>().First(r => r.name.StartsWith("Body_"));
                var b = body.bounds;
                Ellipsoid("Cream tummy", rig.chest, new Vector3(b.center.x, b.center.y + .025f, b.max.z - .015f), new Vector3(b.size.x * .54f, b.size.y * .56f, .05f), cream);
                Vector3 tailPoint = new Vector3(center.x, rig.hips.position.y + .03f, b.min.z + .015f);
                secondary.tail = Pivot("Tail spring", rig.hips, tailPoint);
                if (bunny) Ellipsoid("Cotton tail", secondary.tail, tailPoint + Vector3.back * .06f, Vector3.one * w * .54f, cream);
                else
                {
                    // A curved plush tail, modeled along a cubic curve with a softly tapering round section.
                    Tail(secondary.tail, w, coat);
                    var nose = Ellipsoid("Little nose", rig.head, new Vector3(center.x, center.y - w * .12f, bounds.max.z - w * .01f), new Vector3(w * .14f, w * .10f, w * .12f), pink);
                }
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 8;
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, Output + "/" + id + ".prefab");
                string path = "Assets/Resources/Profiles/" + id + ".asset";
                var profile = AssetDatabase.LoadAssetAtPath<CharacterProfile>(path);
                if (!profile) { profile = ScriptableObject.CreateInstance<CharacterProfile>(); AssetDatabase.CreateAsset(profile, path); }
                profile.id = id; profile.prefab = prefab; profile.displayName = bunny ? "もも · うさぎ" : "むぎ · ねこ";
                profile.voice = bunny ? "marin" : "gleam"; profile.defaultEmotion = bunny ? Emotion.happy : Emotion.curious;
                profile.personalSpace = bunny ? .75f : .85f; profile.initiative = bunny ? .4f : .35f;
                profile.personality = bunny ? "桃色の長い耳とリボンをつけた小さなうさぎのもも。やさしく人なつっこく、一緒に遊ぶと耳まで嬉しそう。相手の気持ちを大切にして短く話す。" : "はちみつ色の耳とふわふわのしっぽを持つ小さな猫のむぎ。好奇心旺盛で少しマイペース。気に入ったものを見つけるのが上手で、親しく自然に話す。";
                profile.speechStyle = bunny ? "柔らかく弾む明るい日本語。嬉しいと少し声が弾む。語尾を無理に動物の鳴き声にせず、親しい相手へ1〜3文で話す。" : "くつろいだ温かな日本語。少しのんびりしたテンポで、発見したときは楽しげに。猫の鳴き声は連発せず、1〜3文で話す。";
                EditorUtility.SetDirty(profile);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        static void CatEar(Transform parent, Vector3 point, float w, Material mat, bool inner)
        {
            var mesh = new Mesh { name = inner ? "Cat inner ear" : "Cat outer ear" };
            mesh.vertices = new[] { new Vector3(-.37f, 0, -.12f) * w, new Vector3(.37f, 0, -.12f) * w, new Vector3(0, .92f, 0) * w, new Vector3(-.37f, 0, .12f) * w, new Vector3(.37f, 0, .12f) * w };
            mesh.triangles = new[] { 0, 2, 1, 3, 4, 2, 0, 3, 2, 1, 2, 4, 0, 1, 4, 0, 4, 3 }; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            mesh = SaveMesh(mesh, inner ? "CatInnerEar" : "CatEar");
            var go = new GameObject(mesh.name, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(parent, false); go.transform.position = point;
            go.GetComponent<MeshFilter>().sharedMesh = mesh; go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }
        static Mesh SaveMesh(Mesh mesh, string name)
        {
            string path = Output + "/Meshes/" + name + ".asset"; var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing) { EditorUtility.CopySerialized(mesh, existing); UnityEngine.Object.DestroyImmediate(mesh); }
            else AssetDatabase.CreateAsset(mesh, path);
            return existing ? existing : mesh;
        }
        static void Tail(Transform parent, float w, Material mat)
        {
            const int rings = 17, sides = 10; var vertices = new Vector3[rings * sides]; var triangles = new int[(rings - 1) * sides * 6];
            for (int i = 0; i < rings; i++)
            {
                float t = (float)i / (rings - 1), radius = w * .18f * Mathf.Sin(Mathf.Lerp(.65f, Mathf.PI, t));
                Vector3 center = new Vector3(Mathf.Sin(t * Mathf.PI * .7f) * w * .85f, t * w * .9f, -w * (.08f + Mathf.Sin(t * Mathf.PI * .8f)));
                for (int j = 0; j < sides; j++)
                {
                    float a = j * Mathf.PI * 2 / sides; vertices[i * sides + j] = center + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * radius;
                    if (i == rings - 1) continue;
                    int k = (i * sides + j) * 6, n = i * sides + (j + 1) % sides, v = i * sides + j;
                    triangles[k] = v; triangles[k + 1] = n + sides; triangles[k + 2] = n; triangles[k + 3] = v; triangles[k + 4] = v + sides; triangles[k + 5] = n + sides;
                }
            }
            var mesh = new Mesh { name = "Curled cat tail", vertices = vertices, triangles = triangles }; mesh.RecalculateNormals(); mesh.RecalculateBounds(); SaveMesh(mesh, "CatTail");
            var go = new GameObject("Curled plush tail", typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(Output + "/Meshes/CatTail.asset"); go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }
        static Vector3 BakedCenter(SkinnedMeshRenderer renderer)
        {
            var mesh = new Mesh(); renderer.BakeMesh(mesh); var center = renderer.transform.TransformPoint(mesh.bounds.center); UnityEngine.Object.DestroyImmediate(mesh); return center;
        }
        public static void BuildAndReview()
        {
            Build(); EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.65f, .68f, .73f);
            var light = new GameObject("Soft key").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2.2f; light.transform.rotation = Quaternion.Euler(35, -30, 0);
            var fill = new GameObject("Fill").AddComponent<Light>(); fill.type = LightType.Directional; fill.intensity = .7f; fill.transform.rotation = Quaternion.Euler(20, 140, 0);
            var camera = new GameObject("Review camera").AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.13f, .18f, .20f); camera.nearClipPlane = .03f; camera.fieldOfView = 43;
            Directory.CreateDirectory("../artifacts/previews");
            foreach (string id in new[] { "bunny", "cat" })
            {
                var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Output + "/" + id + ".prefab"));
                var rig = go.GetComponent<CharacterRigBinding>(); var clips = go.GetComponent<ClipAnimator>();
                clips.idle.SampleAnimation(clips.animator.gameObject, .5f);
                rig.hips.rotation = Quaternion.AngleAxis(rig.animationYawOffset, go.transform.up) * rig.hips.rotation;
                Debug.Log("COMPANION_SAMPLE " + id + " root=" + go.transform.eulerAngles + " hips=" + rig.hips.eulerAngles);
                var eyes = go.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r => r.name == "Eye.L" || r.name == "Eye.R").ToArray();
                var head = go.GetComponentsInChildren<SkinnedMeshRenderer>().First(r => r.name.StartsWith("Head_"));
                Vector3 front = Vector3.ProjectOnPlane(eyes.Select(BakedCenter).Aggregate(Vector3.zero, (a, b) => a + b) / eyes.Length - BakedCenter(head), Vector3.up).normalized;
                light.transform.rotation = Quaternion.LookRotation(-front + Vector3.down * .65f);
                fill.transform.rotation = Quaternion.LookRotation(front + Vector3.down * .4f);
                camera.transform.position = front * 2.8f + Vector3.Cross(Vector3.up, front) * .85f + Vector3.up * 1.1f; camera.transform.LookAt(new Vector3(0, .80f, 0));
                ContentValidation.Render(camera, "../artifacts/previews/Companion-" + id + ".png");
                camera.transform.position = -front * 2.8f + Vector3.Cross(Vector3.up, front) * .85f + Vector3.up * 1.1f; camera.transform.LookAt(new Vector3(0, .80f, 0));
                ContentValidation.Render(camera, "../artifacts/previews/Companion-" + id + "-back.png");
                UnityEngine.Object.DestroyImmediate(go);
            }
            var lineup = new GameObject("Companion lineup");
            int index = 0;
            foreach (string id in new[] { "bunny", "cat" })
            {
                var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Output + "/" + id + ".prefab"), lineup.transform);
                var clips = go.GetComponent<ClipAnimator>(); var rig = go.GetComponent<CharacterRigBinding>();
                clips.idle.SampleAnimation(clips.animator.gameObject, .5f);
                rig.hips.rotation = Quaternion.AngleAxis(rig.animationYawOffset, go.transform.up) * rig.hips.rotation;
                go.transform.position = new Vector3(index++ == 0 ? -.55f : .55f, 0, 0);
            }
            camera.orthographic = true; camera.orthographicSize = .94f;
            camera.transform.position = new Vector3(0, 1.05f, 3.5f); camera.transform.LookAt(new Vector3(0, .8f, 0));
            light.transform.rotation = Quaternion.Euler(35, 150, 0); fill.transform.rotation = Quaternion.Euler(20, -30, 0);
            ContentValidation.Render(camera, "../artifacts/previews/Companions-lineup.png");
            Debug.Log("CUTE_COMPANIONS_REVIEW_DONE");
        }
    }
}
