using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VRM10.MToon10;

namespace QuestAI.Editor
{
    // しろ（いぬ）・こはく（ロボ）・もも（うさぎ）・むぎ（ねこ）: chibi mascots made from scratch by this script, about two and a half
    // heads tall. It builds the skeleton and humanoid avatar, a skinned round body with stubby limbs and paws, a big head
    // (a rounded box for the robot) with a muzzle, a face of blend-shape decals projected onto the head (eyes with
    // catchlights on eye bones, mouth, nose, blush), ears and tails that spring with CompanionSecondaryMotion, a scarf,
    // collar, bow or chest panel, and toon materials (the MToon10 shader from UniVRM). The shared CC0 animation library
    // on WhiteChibi retargets onto the avatar. Measurements are metres for a 0.95 m figure facing +Z.
    // Batch: -executeMethod QuestAI.Editor.MascotCharacterBuilder.BuildAndReview
    public static class MascotCharacterBuilder
    {
        const string Output = "Assets/Generated/Mascots", Chibi = "Assets/Generated/WhiteChibi.prefab";
        enum Kind { Dog, Robot, Bunny, Cat }
        sealed class Spec
        {
            public string id, name, displayName, personality; public Kind kind; public float scale = 1;
            public Color fur, belly, ear, paw, accent, eye = new Color(.16f, .1f, .08f);
        }
        static readonly Spec[] Specs = {
            new Spec { id = "calm", name = "ShiroDog", kind = Kind.Dog, scale = 1.04f, displayName = "しろ · いぬ", fur = new Color(.98f, .96f, .92f), belly = new Color(1f, .99f, .97f), ear = new Color(.86f, .66f, .46f), paw = new Color(.95f, .9f, .84f), accent = new Color(.36f, .58f, .9f),
                personality = "白いふわふわの毛と垂れ耳の子犬のしろ。おっとり穏やかで、相手のペースを大事にする。短く自然な言葉で寄り添う。" },
            new Spec { id = "bright", name = "KohakuRobot", kind = Kind.Robot, displayName = "こはく · ロボ", fur = new Color(.99f, .66f, .25f), belly = new Color(.95f, .95f, .93f), ear = new Color(.62f, .64f, .68f), paw = new Color(.9f, .9f, .9f), accent = new Color(.2f, .92f, 1f), eye = new Color(.25f, .95f, 1f),
                personality = "オレンジ色の小さなロボットのこはく。明るく好奇心旺盛で、身近な小さな楽しみを見つけて一緒に喜ぶ。押しつけず相手を尊重する。" },
            new Spec { id = "bunny", name = "MomoBunny", kind = Kind.Bunny, displayName = "もも · うさぎ", fur = new Color(1f, .9f, .93f), belly = new Color(1f, .98f, .98f), ear = new Color(1f, .66f, .76f), paw = new Color(1f, .96f, .97f), accent = new Color(.96f, .44f, .62f),
                personality = "桃色のふわふわの毛と長い耳の小さなうさぎのもも。やさしく人なつっこく、一緒に遊ぶと耳まで嬉しそうに揺れる。相手の気持ちを大切にして短く話す。" },
            new Spec { id = "cat", name = "MugiCat", kind = Kind.Cat, scale = 1.02f, displayName = "むぎ · ねこ", fur = new Color(.99f, .77f, .46f), belly = new Color(1f, .95f, .86f), ear = new Color(1f, .68f, .74f), paw = new Color(1f, .95f, .86f), accent = new Color(.9f, .25f, .28f),
                personality = "はちみつ色の毛並みに鈴の首輪をつけた子猫のむぎ。好奇心旺盛で少しマイペース。気に入ったものを見つけるのが上手で、親しく自然に話す。" } };

        // Skeleton in the bind (T) pose: name, parent, position. Right-side bones mirror the left ones.
        static readonly (string name, string parent, Vector3 at)[] Bones = {
            ("Hips", null, new Vector3(0, .3f, 0)), ("Spine", "Hips", new Vector3(0, .35f, 0)), ("Chest", "Spine", new Vector3(0, .41f, 0)), ("UpperChest", "Chest", new Vector3(0, .47f, 0)),
            ("Neck", "UpperChest", new Vector3(0, .53f, -.005f)), ("Head", "Neck", new Vector3(0, .56f, 0)), ("LeftEye", "Head", new Vector3(-.075f, .735f, .08f)),
            ("LeftShoulder", "UpperChest", new Vector3(-.035f, .5f, 0)), ("LeftUpperArm", "LeftShoulder", new Vector3(-.1f, .49f, 0)),
            ("LeftLowerArm", "LeftUpperArm", new Vector3(-.19f, .49f, 0)), ("LeftHand", "LeftLowerArm", new Vector3(-.27f, .49f, 0)),
            ("LeftUpperLeg", "Hips", new Vector3(-.065f, .27f, 0)), ("LeftLowerLeg", "LeftUpperLeg", new Vector3(-.065f, .16f, .005f)),
            ("LeftFoot", "LeftLowerLeg", new Vector3(-.065f, .06f, 0)), ("LeftToes", "LeftFoot", new Vector3(-.065f, .03f, .07f)) };
        static readonly Vector3 HeadCenter = new Vector3(0, .75f, 0);
        const float EyeY = .735f, EyeX = .075f, EyeHalf = .026f;
        static Spec current;

        [MenuItem("Quest AI/Build mascot characters")]
        public static void Build()
        {
            foreach (var folder in new[] { "", "/Materials", "/Meshes" }) Directory.CreateDirectory(Output + folder);
            AssetDatabase.Refresh();
            foreach (var spec in Specs) UpdateProfile(spec, BuildPrefab(spec));
            AssetDatabase.SaveAssets();
            Debug.Log("MASCOTS_READY " + string.Join(",", Specs.Select(s => s.id)));
        }

        // ---------- mesh assembly ----------
        sealed class M
        {
            public readonly List<Vector3> v = new List<Vector3>(); public readonly List<Vector2> uv = new List<Vector2>(); public readonly List<BoneWeight> w = new List<BoneWeight>();
            public readonly List<Material> mats = new List<Material>(); readonly List<List<int>> subs = new List<List<int>>();
            public readonly Dictionary<string, Dictionary<int, Vector3>> shapes = new Dictionary<string, Dictionary<int, Vector3>>();
            public int Sub(Material m) { int i = mats.IndexOf(m); if (i >= 0) return i; mats.Add(m); subs.Add(new List<int>()); return mats.Count - 1; }
            public int Add(Vector3 p, Vector2 t, BoneWeight b) { v.Add(p); uv.Add(t); w.Add(b); return v.Count - 1; }
            // Unity's front faces wind clockwise, so their cross product points at the viewer: flip to face `outward`.
            public void Tri(int s, int a, int b, int c, Vector3 outward) { if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), outward) < 0) (b, c) = (c, b); subs[s].Add(a); subs[s].Add(b); subs[s].Add(c); }
            public void Shape(string name, int i, Vector3 delta) { if (!shapes.TryGetValue(name, out var d)) shapes[name] = d = new Dictionary<int, Vector3>(); d[i] = delta; }
            public Mesh Build(string name, Matrix4x4[] bindposes, string[] shapeNames)
            {
                var mesh = new Mesh { name = name, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.subMeshCount = subs.Count;
                for (int i = 0; i < subs.Count; i++) mesh.SetTriangles(subs[i], i);
                mesh.boneWeights = w.ToArray(); mesh.bindposes = bindposes; mesh.RecalculateNormals(); mesh.RecalculateBounds();
                foreach (var shape in shapeNames)
                {
                    var deltas = new Vector3[v.Count];
                    if (shapes.TryGetValue(shape, out var d)) foreach (var pair in d) deltas[pair.Key] = pair.Value;
                    mesh.AddBlendShapeFrame(shape, 100, deltas, null, null);
                }
                return mesh;
            }
        }
        sealed class Rig
        {
            public readonly Dictionary<string, Transform> bone = new Dictionary<string, Transform>(); public readonly List<Transform> list = new List<Transform>();
            public Transform this[string name] => bone[name];
            public Transform Add(string name, Transform parent, Vector3 at)
            {
                var t = new GameObject(name).transform; t.SetParent(parent, false); t.position = at; bone[name] = t; list.Add(t); return t;
            }
            public BoneWeight W(string a) => new BoneWeight { boneIndex0 = list.IndexOf(bone[a]), weight0 = 1 };
            // Blends a into b by t; the heavier bone comes first.
            public BoneWeight W(string a, string b, float t)
            {
                t = Mathf.Clamp01(t); if (t <= .001f) return W(a); if (t >= .999f) return W(b);
                int ia = list.IndexOf(bone[a]), ib = list.IndexOf(bone[b]);
                return t < .5f ? new BoneWeight { boneIndex0 = ia, weight0 = 1 - t, boneIndex1 = ib, weight1 = t } : new BoneWeight { boneIndex0 = ib, weight0 = t, boneIndex1 = ia, weight1 = 1 - t };
            }
            // Smooth blend along a chain of (bone, station) pairs, e.g. the spine by height.
            public BoneWeight Along(float y, params (string bone, float at)[] chain)
            {
                if (y <= chain[0].at) return W(chain[0].bone);
                for (int i = 0; i < chain.Length - 1; i++) if (y < chain[i + 1].at) return W(chain[i].bone, chain[i + 1].bone, Step(chain[i].at, chain[i + 1].at, y));
                return W(chain[chain.Length - 1].bone);
            }
        }
        // 0 at a, 1 at b, eased (a may be above b). Mathf.SmoothStep interpolates values; it is not GLSL's smoothstep.
        static float Step(float a, float b, float x) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(a, b, x));
        struct Ring { public Vector3 c, a, b; public BoneWeight w; public Ring(Vector3 c, Vector3 a, Vector3 b, BoneWeight w) { this.c = c; this.a = a; this.b = b; this.w = w; } }
        // Rings of `sides` points c + a·cos θ + b·sin θ (scaled by an optional radial factor) joined by quads, with optional fan caps.
        static void Loft(M m, int s, IList<Ring> rings, int sides, bool capStart, bool capEnd, Func<int, float, float> radius = null)
        {
            int start = m.v.Count;
            for (int i = 0; i < rings.Count; i++)
                for (int j = 0; j < sides; j++)
                {
                    float th = j * Mathf.PI * 2 / sides, k = radius?.Invoke(i, th) ?? 1; var r = rings[i];
                    m.Add(r.c + (r.a * Mathf.Cos(th) + r.b * Mathf.Sin(th)) * k, new Vector2((float)j / sides, (float)i / rings.Count), r.w);
                }
            for (int i = 0; i < rings.Count - 1; i++)
                for (int j = 0; j < sides; j++)
                {
                    int a = start + i * sides + j, b = start + i * sides + (j + 1) % sides, c = a + sides, d = b + sides;
                    var outward = (m.v[a] + m.v[d]) * .5f - (rings[i].c + rings[i + 1].c) * .5f;
                    m.Tri(s, a, b, d, outward); m.Tri(s, a, d, c, outward);
                }
            void Cap(int ring, Vector3 outward)
            {
                int center = m.Add(rings[ring].c, new Vector2(.5f, .5f), rings[ring].w);
                for (int j = 0; j < sides; j++) m.Tri(s, center, start + ring * sides + j, start + ring * sides + (j + 1) % sides, outward);
            }
            if (capStart) Cap(0, rings[0].c - rings[1].c);
            if (capEnd) Cap(rings.Count - 1, rings[rings.Count - 1].c - rings[rings.Count - 2].c);
        }
        // A lat-long sphere through point(direction). Returns its triangle corners (for projecting the face).
        static List<Vector3> Ball(M m, int s, int lat, int lon, Func<Vector3, Vector3> point, Func<Vector3, BoneWeight> weight, Vector3 center)
        {
            var index = new int[lat + 1, lon]; var corners = new List<Vector3>();
            for (int i = 0; i <= lat; i++)
                for (int j = 0; j < lon; j++)
                {
                    float th = Mathf.PI * i / lat, ph = Mathf.PI * 2 * j / lon; var d = new Vector3(Mathf.Sin(th) * Mathf.Sin(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Cos(ph));
                    if ((i == 0 || i == lat) && j > 0) { index[i, j] = index[i, 0]; continue; }
                    index[i, j] = m.Add(point(d), new Vector2((float)j / lon, 1 - (float)i / lat), weight(d));
                }
            void Triangle(int a, int b, int c)
            {
                if (a == b || b == c || a == c) return;
                m.Tri(s, a, b, c, (m.v[a] + m.v[b] + m.v[c]) / 3 - center); corners.Add(m.v[a]); corners.Add(m.v[b]); corners.Add(m.v[c]);
            }
            for (int i = 0; i < lat; i++)
                for (int j = 0; j < lon; j++) { int n = (j + 1) % lon; Triangle(index[i, j], index[i, n], index[i + 1, n]); Triangle(index[i, j], index[i + 1, n], index[i + 1, j]); }
            return corners;
        }
        static List<Vector3> Ellipsoid(M m, int s, Vector3 c, Vector3 r, BoneWeight w, int lat = 10, int lon = 16) => Ball(m, s, lat, lon, d => c + Vector3.Scale(d, r), d => w, c);

        // ---------- head and face ----------
        // A big round head with full cheeks; the robot's is a rounded box.
        static Vector3 HeadPoint(Vector3 d, float grow = 0)
        {
            if (current.kind == Kind.Robot)
            {
                Vector3 q = new Vector3(Mathf.Sign(d.x) * Mathf.Pow(Mathf.Abs(d.x), .45f), Mathf.Sign(d.y) * Mathf.Pow(Mathf.Abs(d.y), .45f), Mathf.Sign(d.z) * Mathf.Pow(Mathf.Abs(d.z), .45f));
                q /= Mathf.Max(1, q.magnitude / 1.18f);
                return HeadCenter + Vector3.Scale(q, new Vector3(.19f + grow, .16f + grow, .16f + grow));
            }
            float cheeks = 1 + .07f * Mathf.Clamp01(-d.y * 1.5f) * Mathf.Clamp01(d.z + .5f);
            return HeadCenter + new Vector3(d.x * (.2f * cheeks + grow), d.y * ((d.y > 0 ? .18f : .165f) + grow), d.z * (.18f + grow));
        }
        static List<Vector3> headSurface;
        // Front of the head at (x, y): a ray along -Z against the head and muzzle triangles.
        static float SurfaceZ(float x, float y)
        {
            var o = new Vector3(x, y, 1); var dir = Vector3.back; float best = float.NegativeInfinity;
            for (int i = 0; i < headSurface.Count; i += 3)
            {
                Vector3 a = headSurface[i], e1 = headSurface[i + 1] - a, e2 = headSurface[i + 2] - a, p = Vector3.Cross(dir, e2); float det = Vector3.Dot(e1, p);
                if (Mathf.Abs(det) < 1e-9f) continue;
                float inv = 1 / det; var tv = o - a; float u = Vector3.Dot(tv, p) * inv; if (u < 0 || u > 1) continue;
                var q = Vector3.Cross(tv, e1); float w = Vector3.Dot(dir, q) * inv; if (w < 0 || u + w > 1) continue;
                float t = Vector3.Dot(e2, q) * inv; if (t > 0) best = Mathf.Max(best, 1 - t);
            }
            return float.IsNegativeInfinity(best) ? HeadCenter.z : best;
        }
        // A grid decal laid on the face, lifted off it, with blend shapes that move its points to other face positions.
        static void Decal(M m, int s, int cols, int rows, float lift, BoneWeight w, Func<float, float, Vector2> at, params (string name, Func<float, float, Vector2> at)[] shapes)
        {
            Vector3 On(Vector2 p) => new Vector3(p.x, p.y, SurfaceZ(p.x, p.y) + lift);
            int start = m.v.Count;
            for (int r = 0; r <= rows; r++)
                for (int c = 0; c <= cols; c++)
                {
                    float u = (float)c / cols, v = (float)r / rows; var p = On(at(u, v)); int i = m.Add(p, new Vector2(u, v), w);
                    foreach (var shape in shapes) { var q = On(shape.at(u, v)); if ((q - p).sqrMagnitude > 1e-12f) m.Shape(shape.name, i, q - p); }
                }
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    int a = start + r * (cols + 1) + c, b = a + 1, d = a + cols + 1, e = d + 1;
                    m.Tri(s, a, b, e, Vector3.forward); m.Tri(s, a, e, d, Vector3.forward);
                }
        }
        // Eye outline in eye units: h runs from the inner (-1) to the outer (+1) side. A tall, rounded bead.
        static float Top(float h) { h = Mathf.Clamp(h, -1, 1); return .033f * Mathf.Pow(Mathf.Max(0, 1 - Mathf.Pow(Mathf.Abs(h), 2.2f)), 1 / 2.2f); }
        static float Bottom(float h) { h = Mathf.Clamp(h, -1, 1); return -.031f * Mathf.Pow(Mathf.Max(0, 1 - Mathf.Pow(Mathf.Abs(h), 2.2f)), 1 / 2.2f); }
        // Closed eye: a soft downward curve; happy eye: a "^".
        static float Closed(float h) => -.004f - .006f * (1 - h * h);
        static float Arch(float h) => .012f * (1 - Mathf.Abs(Mathf.Clamp(h, -1, 1))) - .003f;
        static float Sad(float h) => -.009f * (1 + h) * .5f;

        static void Face(M m, Rig rig, Spec spec, Dictionary<string, Material> mat)
        {
            var head = rig.W("Head"); float lift = spec.kind == Kind.Robot ? .006f : 0;
            if (spec.kind == Kind.Robot)
            {
                // Dark visor screen with rounded corners; the face glows on it.
                Decal(m, m.Sub(mat["Visor"]), 40, 24, .004f, head, (u, v) =>
                {
                    float x = u * 2 - 1, y = v * 2 - 1, k = Mathf.Pow(Mathf.Pow(Mathf.Abs(x), 4) + Mathf.Pow(Mathf.Abs(y), 4), .25f), scale = k > 1e-4f ? Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) / k : 1;
                    return new Vector2(x * scale * .145f, .715f + y * scale * .085f);
                });
            }
            for (int side = -1; side <= 1; side += 2)
            {
                float ex = side * EyeX; var eyeBone = rig.W(side < 0 ? "LeftEye" : "RightEye");
                // side -1 is the character's left eye (on -X); h = +1 is always the outer side.
                Vector2 P(float h, float y) => new Vector2(ex + side * h * EyeHalf, EyeY + y);
                const float closedThick = .0055f;
                Decal(m, m.Sub(mat["Eye"]), 14, 8, .0015f + lift, eyeBone, (u, v) => { float h = u * 2 - 1; return P(h, Mathf.Lerp(Bottom(h), Top(h), v)); },
                    ("Blink", (u, v) => { float h = u * 2 - 1; return P(h * .95f, Closed(h) + (v - .5f) * closedThick); }),
                    ("EyeHappy", (u, v) => { float h = u * 2 - 1; return P(h * .95f, Arch(h) + (v - .5f) * closedThick); }),
                    ("EyeWide", (u, v) => { float h = u * 2 - 1; return P(h * 1.08f, Mathf.Lerp(Bottom(h), Top(h), v) * 1.14f); }),
                    ("EyeSad", (u, v) => { float h = u * 2 - 1; return P(h, Mathf.Lerp(Bottom(h), Top(h) * .8f, v) + Sad(h)); }));
                if (spec.kind != Kind.Robot)
                {
                    // Two catchlights that shrink away when the eye closes.
                    foreach (var (cx, cy, r) in new[] { (-.35f, .45f, .4f), (.35f, -.4f, .18f) })
                    {
                        Vector2 Shine(float u, float v, float k, float dy) { float a = u * Mathf.PI * 2; return P(cx + Mathf.Cos(a) * v * r * k, cy * .03f + Mathf.Sin(a) * v * r * k * EyeHalf + dy); }
                        Decal(m, m.Sub(mat["Shine"]), 12, 1, .0025f + lift, eyeBone, (u, v) => Shine(u, v, 1, 0),
                            ("Blink", (u, v) => Shine(u, v, 0, Closed(cx) - cy * .03f)), ("EyeHappy", (u, v) => Shine(u, v, 0, Arch(cx) - cy * .03f)),
                            ("EyeWide", (u, v) => Shine(u, v, 1.1f, 0)), ("EyeSad", (u, v) => Shine(u, v, .9f, Sad(cx))));
                    }
                }
                // Blush: soft ovals under the eyes that grow with the Blush shape.
                Vector2 Blush(float u, float v, float k) { float a = u * Mathf.PI * 2; return new Vector2(side * .125f + Mathf.Cos(a) * v * .03f * k, EyeY - .05f + Mathf.Sin(a) * v * .015f * k); }
                Decal(m, m.Sub(mat["Blush"]), 16, 2, .0012f + lift, head, (u, v) => Blush(u, v, .7f), ("Blush", (u, v) => Blush(u, v, 1.15f)));
            }
            // Nose: a pink tick for the cat and the bunny (the dog's is a real button nose on its muzzle).
            if (spec.kind == Kind.Cat || spec.kind == Kind.Bunny)
                Decal(m, m.Sub(mat["Nose"]), 4, 2, .001f, head, (u, v) => new Vector2((u - .5f) * .022f * (1 - v * .85f), .697f - v * .012f));
            // Mouth: a line that opens into vowel shapes; the cat's is an "ω".
            float mouthY = spec.kind == Kind.Dog ? .652f : spec.kind == Kind.Robot ? .668f : .675f;
            Vector2 Mouth(float u, float v, float width, float smile, float open, float top, float down)
            {
                float h = u * 2 - 1, round = Mathf.Sqrt(Mathf.Max(0, 1 - h * h));
                float c = spec.kind == Kind.Cat ? -.006f * Mathf.Abs(Mathf.Sin(Mathf.PI * h)) + .002f : .003f * h * h;
                c += smile * (.006f * h * h - .001f) - down * .006f * h * h;
                float upper = c + .0015f + top * round, lower = c - .0015f - open * Mathf.Pow(round, .7f);
                return new Vector2(h * .018f * width, mouthY + Mathf.Lerp(upper, lower, v));
            }
            Decal(m, m.Sub(mat["Mouth"]), 16, 4, .0012f + lift, head, (u, v) => Mouth(u, v, 1, 0, 0, 0, 0),
                ("Smile", (u, v) => Mouth(u, v, 1.1f, 1, 0, 0, 0)), ("Frown", (u, v) => Mouth(u, v, .9f, 0, 0, 0, 1)),
                ("MouthOpen", (u, v) => Mouth(u, v, .95f, 0, .022f, .002f, 0)), ("MouthEe", (u, v) => Mouth(u, v, 1.2f, 0, .008f, .001f, 0)),
                ("MouthOh", (u, v) => Mouth(u, v, .55f, 0, .018f, .004f, 0)));
        }

        // ---------- body ----------
        static readonly (string bone, float at)[] Spine = { ("Hips", .3f), ("Spine", .35f), ("Chest", .41f), ("UpperChest", .47f), ("Neck", .53f), ("Head", .59f) };
        static void Body(M m, Rig rig, Spec spec, Dictionary<string, Material> mat)
        {
            bool robot = spec.kind == Kind.Robot;
            int fur = m.Sub(mat["Fur"]), belly = m.Sub(mat["Belly"]), paw = m.Sub(mat["Paw"]), accent = m.Sub(mat["Accent"]), joint = robot ? m.Sub(mat["Ear"]) : paw;
            var headWeight = rig.W("Head");
            headSurface = Ball(m, fur, 26, 36, d => HeadPoint(d), d => headWeight, HeadCenter);
            // Muzzle: a soft bump the mouth and nose sit on.
            if (spec.kind == Kind.Dog) headSurface.AddRange(Ellipsoid(m, belly, new Vector3(0, .675f, .135f), new Vector3(.085f, .062f, .07f), headWeight, 14, 20));
            if (spec.kind == Kind.Cat || spec.kind == Kind.Bunny) headSurface.AddRange(Ellipsoid(m, belly, new Vector3(0, .68f, .14f), new Vector3(.07f, .042f, .042f), headWeight, 12, 18));
            if (spec.kind == Kind.Dog) Ellipsoid(m, m.Sub(mat["Nose"]), new Vector3(0, .703f, .2f), new Vector3(.026f, .019f, .017f), headWeight);
            // Bean-shaped body; the robot's is boxier.
            var torso = new List<Ring>();
            foreach (var (y, rx, rz) in new[] { (.2f, .06f, .055f), (.24f, .11f, .1f), (.29f, .135f, .12f), (.35f, .143f, .127f), (.41f, .137f, .12f), (.47f, .12f, .106f), (.52f, .1f, .09f), (.57f, .075f, .07f), (.61f, .05f, .05f) })
                torso.Add(new Ring(new Vector3(0, y, 0), Vector3.right * rx, Vector3.forward * rz, rig.Along(y, Spine)));
            Func<int, float, float> boxy = robot ? (i, th) => 1 / Mathf.Pow(Mathf.Pow(Mathf.Abs(Mathf.Cos(th)), 4) + Mathf.Pow(Mathf.Abs(Mathf.Sin(th)), 4), .25f) * .92f : (Func<int, float, float>)null;
            Loft(m, fur, torso, 32, true, true, boxy);
            var chest = rig.W("Chest");
            if (robot)
            {
                // Chest panel with three buttons.
                Ellipsoid(m, belly, new Vector3(0, .4f, .118f), new Vector3(.075f, .065f, .014f), chest);
                for (int i = -1; i <= 1; i++) Ellipsoid(m, m.Sub(mat[i == -1 ? "Accent" : i == 0 ? "Nose" : "Blush"]), new Vector3(i * .032f, .39f, .13f), new Vector3(.012f, .012f, .007f), chest, 6, 10);
            }
            else Ellipsoid(m, belly, new Vector3(0, .36f, .085f), new Vector3(.095f, .105f, .055f), rig.W("Spine"));
            var neck = rig.W("UpperChest");
            if (spec.kind == Kind.Dog)
            {
                // Bandana: a band round the neck and a triangle in front.
                Loft(m, accent, new[] { new Ring(new Vector3(0, .6f, 0), Vector3.right * .088f, Vector3.forward * .085f, neck), new Ring(new Vector3(0, .565f, 0), Vector3.right * .108f, Vector3.forward * .1f, neck) }, 28, false, false);
                Loft(m, accent, new[] { new Ring(new Vector3(0, .575f, .09f), Vector3.right * .055f, Vector3.forward * .012f, neck), new Ring(new Vector3(0, .51f, .11f), Vector3.right * .006f, Vector3.forward * .004f, neck) }, 10, true, true);
            }
            if (spec.kind == Kind.Cat)
            {
                // Collar and a gold bell.
                Loft(m, accent, new[] { new Ring(new Vector3(0, .6f, 0), Vector3.right * .083f, Vector3.forward * .08f, neck), new Ring(new Vector3(0, .582f, 0), Vector3.right * .09f, Vector3.forward * .086f, neck) }, 28, false, false);
                Ellipsoid(m, m.Sub(mat["Bell"]), new Vector3(0, .565f, .095f), new Vector3(.022f, .022f, .022f), neck);
            }
            for (int side = -1; side <= 1; side += 2)
            {
                string S(string bone) => (side < 0 ? "Left" : "Right") + bone;
                // Stubby arm along ±X ending in a round paw.
                var arm = new List<Ring>();
                foreach (var (x, r) in new[] { (.1f, .042f), (.13f, .043f), (.19f, .039f), (.25f, .036f) })
                    arm.Add(new Ring(new Vector3(side * x, .49f, 0), Vector3.up * r, Vector3.forward * r, rig.Along(x, (S("UpperArm"), .16f), (S("LowerArm"), .22f), (S("Hand"), .27f))));
                Loft(m, robot ? joint : fur, arm, 16, false, false);
                // A round shoulder centred on the arm's pivot, so the arm root never pokes out when it swings.
                Ellipsoid(m, robot ? joint : fur, new Vector3(side * .1f, .49f, 0), new Vector3(.046f, .046f, .046f), rig.W(S("UpperArm")), 10, 16);
                Ellipsoid(m, robot ? fur : paw, new Vector3(side * .283f, .49f, 0), new Vector3(.05f, .046f, .05f), rig.W(S("Hand")), 10, 16);
                // Short leg and an oval foot.
                var leg = new List<Ring>();
                foreach (var (y, r) in new[] { (.29f, .058f), (.21f, .055f), (.13f, .05f), (.07f, .047f) })
                    leg.Add(new Ring(new Vector3(side * .065f, y, 0), Vector3.right * r, Vector3.forward * r, rig.Along(-y, (S("UpperLeg"), -.2f), (S("LowerLeg"), -.13f), (S("Foot"), -.07f))));
                Loft(m, robot ? joint : fur, leg, 16, false, false);
                Ball(m, robot ? fur : paw, 10, 18, d => new Vector3(side * .065f, .042f, .025f) + Vector3.Scale(d, new Vector3(.058f, .043f, .08f)), d => rig.W(S("Foot"), S("Toes"), Mathf.InverseLerp(.02f, .08f, .025f + d.z * .08f)), new Vector3(side * .065f, .042f, .025f));
            }
        }

        // ---------- ears, tails and the antenna ----------
        static void Accessories(Spec spec, GameObject root, Rig rig, Dictionary<string, Material> mat)
        {
            var secondary = root.AddComponent<CompanionSecondaryMotion>(); secondary.rabbit = spec.kind == Kind.Bunny;
            Transform Pivot(string name, Transform parent, Vector3 at, Quaternion rotation) { var t = new GameObject(name).transform; t.SetParent(parent, false); t.position = at; t.rotation = rotation; return t; }
            if (spec.kind == Kind.Robot)
            {
                // A springy antenna with a glowing tip, and round bolts for ears.
                var antenna = Pivot("Antenna spring", rig["Head"], HeadPoint(Vector3.up) - Vector3.up * .01f, Quaternion.AngleAxis(-8, Vector3.forward)); secondary.leftEar = antenna;
                Part("Antenna", antenna, Vector3.zero, Sweep("Antenna", 8, 10, t => (new Vector3(0, t * .11f, 0), .007f, .007f)), mat["Ear"]);
                Part("Antenna tip", antenna, new Vector3(0, .11f, 0), Sweep("AntennaTip", 10, 14, t => { float a = t * Mathf.PI; return (new Vector3(0, -Mathf.Cos(a) * .022f, 0), Mathf.Max(.001f, Mathf.Sin(a) * .022f), Mathf.Max(.001f, Mathf.Sin(a) * .022f)); }), mat["Accent"]);
                var bolt = Sweep("Bolt", 6, 18, t => (new Vector3(0, t * .03f, 0), .042f * (1 - .2f * t), .042f * (1 - .2f * t)));
                for (int side = -1; side <= 1; side += 2) Part("Ear bolt", rig["Head"], HeadPoint(new Vector3(side, 0, 0)) - new Vector3(side * .012f, 0, 0), bolt, mat["Ear"], Quaternion.AngleAxis(-side * 90, Vector3.forward));
                return;
            }
            Mesh ear, inner = null;
            if (spec.kind == Kind.Bunny) { ear = BunnyEar("BunnyEar", .27f, .075f, .04f); inner = BunnyEar("BunnyInnerEar", .22f, .045f, .015f); }
            else if (spec.kind == Kind.Cat) { ear = CatEar("CatEar", .11f, .075f, .045f); inner = CatEar("CatInnerEar", .08f, .046f, .014f); }
            else ear = DogEar();
            for (int side = -1; side <= 1; side += 2)
            {
                Transform pivot;
                if (spec.kind == Kind.Dog)
                    // Floppy ears hang from the sides of the head, tipped a little outward.
                    pivot = Pivot(side < 0 ? "Left ear spring" : "Right ear spring", rig["Head"], HeadPoint(new Vector3(side * .8f, .55f, .05f).normalized, .008f), Quaternion.AngleAxis(-side * 30, Vector3.up) * Quaternion.AngleAxis(side * 18, Vector3.forward));
                else
                    pivot = Pivot(side < 0 ? "Left ear spring" : "Right ear spring", rig["Head"], HeadPoint(spec.kind == Kind.Bunny ? new Vector3(side * .32f, .95f, -.05f).normalized : new Vector3(side * .58f, .8f, .05f).normalized, -.012f),
                        Quaternion.AngleAxis(-side * (spec.kind == Kind.Bunny ? 10 : 22), Vector3.forward) * Quaternion.AngleAxis(-6, Vector3.right));
                Part(spec.kind == Kind.Dog ? "Floppy ear" : "Ear", pivot, Vector3.zero, ear, mat["Ear"]);
                if (inner) Part("Inner ear", pivot, new Vector3(0, spec.kind == Kind.Bunny ? .015f : .01f, spec.kind == Kind.Bunny ? .017f : .017f), inner, mat["Inner"]);
                if (side < 0) secondary.leftEar = pivot; else secondary.rightEar = pivot;
            }
            if (spec.kind == Kind.Bunny)
            {
                // A bow by the right ear.
                var bow = HeadPoint(new Vector3(.5f, .72f, .35f).normalized, .01f); var w = rig.W("Head");
                var bowParent = Pivot("Bow", rig["Head"], bow, Quaternion.AngleAxis(-25, Vector3.forward));
                foreach (int s in new[] { -1, 1 }) Part("Bow loop", bowParent, new Vector3(s * .028f, 0, 0), Sweep("BowLoop", 10, 14, t => { float a = t * Mathf.PI; return (new Vector3(-Mathf.Cos(a) * .026f, 0, 0), Mathf.Max(.001f, Mathf.Sin(a) * .02f), Mathf.Max(.001f, Mathf.Sin(a) * .01f)); }), mat["Accent"]);
                Part("Bow knot", bowParent, Vector3.zero, Sweep("BowKnot", 8, 12, t => { float a = t * Mathf.PI; return (new Vector3(0, -Mathf.Cos(a) * .011f, 0), Mathf.Max(.001f, Mathf.Sin(a) * .011f), Mathf.Max(.001f, Mathf.Sin(a) * .011f)); }), mat["Accent"]);
            }
            var tail = Pivot("Tail spring", rig["Hips"], new Vector3(0, .27f, -.115f), Quaternion.identity); secondary.tail = tail;
            if (spec.kind == Kind.Bunny) Part("Cotton tail", tail, new Vector3(0, 0, -.03f), Pompom(), mat["Belly"]);
            else if (spec.kind == Kind.Cat) Part("Tail", tail, Vector3.zero, CatTail(), mat["Fur"]);
            else Part("Curly tail", tail, Vector3.zero, DogTail(), mat["Fur"]);
        }
        static void Part(string name, Transform parent, Vector3 at, Mesh mesh, Material material, Quaternion? rotation = null)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(parent, false);
            if (rotation.HasValue) { go.transform.position = at; go.transform.rotation = rotation.Value; } else go.transform.localPosition = at;
            go.GetComponent<MeshFilter>().sharedMesh = mesh; var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.On;
        }
        // A long bunny ear: an elliptic section that swells then narrows to a soft tip, bending slightly back.
        static Mesh BunnyEar(string name, float length, float width, float depth) => Sweep(name, 24, 16, t =>
        {
            float profile = Mathf.Pow(Mathf.Sin(Mathf.Lerp(.35f, Mathf.PI, t)), .55f);
            return (new Vector3(0, t * length, -t * t * length * .12f), width * .5f * Mathf.Max(.05f, profile * (1 - .25f * t)), depth * .5f * Mathf.Max(.15f, profile));
        });
        // A cat ear: a cone with an elliptic base that is flatter front to back, rounded at the tip.
        static Mesh CatEar(string name, float height, float width, float depth) => Sweep(name, 14, 16, t =>
        {
            float profile = Mathf.Pow(Mathf.Max(0, Mathf.Cos(t * Mathf.PI * .5f)), .85f);
            return (new Vector3(0, t * height, -t * t * height * .1f), width * Mathf.Max(.04f, profile), depth * .5f * Mathf.Max(.12f, profile));
        });
        // A floppy dog ear: a flat rounded flap hanging down, its broad side facing out from the head.
        static Mesh DogEar() => Sweep("DogEar", 18, 16, t =>
        {
            float profile = Mathf.Pow(Mathf.Sin(Mathf.Lerp(.25f, Mathf.PI, t)), .6f);
            return (new Vector3(t * t * .025f, -t * .17f, 0), .018f * Mathf.Max(.3f, profile), .07f * Mathf.Max(.08f, profile));
        });
        static Vector3 Spline(Vector3[] path, float t)
        {
            // Catmull-Rom through the path.
            float f = Mathf.Clamp01(t) * (path.Length - 1); int i = Mathf.Min((int)f, path.Length - 2); float u = f - i;
            Vector3 p0 = path[Mathf.Max(0, i - 1)], p1 = path[i], p2 = path[i + 1], p3 = path[Mathf.Min(path.Length - 1, i + 2)];
            return .5f * (2 * p1 + (p2 - p0) * u + (2 * p0 - 5 * p1 + 4 * p2 - p3) * u * u + (3 * p1 - p0 - 3 * p2 + p3) * u * u * u);
        }
        // A cat tail seen from the side as a soft "?".
        static readonly Vector3[] CatTailPath = { new Vector3(0, 0, 0), new Vector3(0, -.03f, -.08f), new Vector3(.015f, -.02f, -.17f), new Vector3(.03f, .06f, -.23f), new Vector3(.03f, .15f, -.22f), new Vector3(.02f, .21f, -.17f) };
        static Mesh CatTail() => Sweep("CatTail", 32, 14, t => { float r = Mathf.Max(.004f, Mathf.Lerp(.028f, .022f, t) * Mathf.Min(1, Mathf.Sin(Mathf.Lerp(.25f, Mathf.PI, t)) * 3.5f)); return (Spline(CatTailPath, t), r, r); });
        // A short tail curling up over the back.
        static readonly Vector3[] DogTailPath = { new Vector3(0, 0, 0), new Vector3(0, .03f, -.05f), new Vector3(0, .09f, -.07f), new Vector3(0, .13f, -.04f), new Vector3(0, .12f, -.01f) };
        static Mesh DogTail() => Sweep("DogTail", 20, 14, t => { float r = Mathf.Max(.004f, .03f * Mathf.Min(1, Mathf.Sin(Mathf.Lerp(.3f, Mathf.PI, t)) * 2.5f)); return (Spline(DogTailPath, t), r, r); });
        static Mesh Pompom() => Sweep("BunnyTail", 12, 16, t => { float a = t * Mathf.PI; return (new Vector3(0, -Mathf.Cos(a) * .045f, 0), Mathf.Max(.002f, Mathf.Sin(a) * .05f), Mathf.Max(.002f, Mathf.Sin(a) * .045f)); });
        static readonly Dictionary<string, Mesh> swept = new Dictionary<string, Mesh>();
        // Lofts elliptic rings along a path (rings x sides) with closed ends; ring frames follow the path tangent. Each mesh
        // is saved once per build and shared.
        static Mesh Sweep(string name, int rings, int sides, Func<float, (Vector3 center, float rx, float rz)> ring)
        {
            if (swept.TryGetValue(name, out var cached) && cached) return cached;
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
            return swept[name] = Save(mesh, Output + "/Meshes/" + name + ".asset");
        }

        // ---------- materials ----------
        static Material Toon(string name, Color color, Color shade, float outline = .0022f, MToon10AlphaMode alpha = MToon10AlphaMode.Opaque, float shift = 0, float alphaValue = 1, Color? glow = null)
        {
            string path = Output + "/Materials/" + name + ".mat"; var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat) { mat = new Material(Shader.Find("VRM10/Universal Render Pipeline/MToon10")); AssetDatabase.CreateAsset(mat, path); }
            color.a = alphaValue;
            var c = new MToon10Context(mat)
            {
                AlphaMode = alpha, DoubleSidedMode = MToon10DoubleSidedMode.Off, BaseColorFactorSrgb = color, ShadeColorFactorSrgb = shade, EmissiveFactorLinear = glow ?? Color.black,
                ShadingToonyFactor = .9f, ShadingShiftFactor = shift, GiEqualizationFactor = .9f, ParametricRimColorFactorSrgb = new Color(.18f, .16f, .16f), ParametricRimFresnelPowerFactor = 4, RimLightingMixFactor = 1,
                OutlineWidthMode = outline > 0 ? MToon10OutlineMode.World : MToon10OutlineMode.None, OutlineWidthFactor = outline, OutlineColorFactorSrgb = Color.Lerp(shade * .4f, new Color(.25f, .15f, .15f), .4f), OutlineLightingMixFactor = 1
            };
            c.Validate(); EditorUtility.SetDirty(mat); return mat;
        }
        static Color Shade(Color c, float k = .8f) => Color.Lerp(c * k, new Color(.8f, .58f, .68f), .15f);
        static Dictionary<string, Material> Materials(Spec spec)
        {
            string p = spec.id + "_"; bool robot = spec.kind == Kind.Robot; var m = new Dictionary<string, Material>();
            m["Fur"] = Toon(p + "Fur", spec.fur, Shade(spec.fur), shift: .05f);
            m["Belly"] = Toon(p + "Belly", spec.belly, Shade(spec.belly, .86f), shift: .1f);
            m["Paw"] = Toon(p + "Paw", spec.paw, Shade(spec.paw, .84f));
            m["Ear"] = Toon(p + "Ear", spec.ear, Shade(spec.ear));
            m["Inner"] = Toon(p + "Inner", new Color(1f, .7f, .76f), Shade(new Color(1f, .7f, .76f), .88f), .0012f);
            m["Accent"] = Toon(p + "Accent", spec.accent, Shade(spec.accent), glow: robot ? (Color?)(spec.accent.linear * .8f) : null);
            m["Eye"] = Toon(p + "Eye", spec.eye, spec.eye, 0, shift: .7f, glow: robot ? (Color?)(spec.eye.linear * 1.2f) : null);
            m["Shine"] = Toon(p + "Shine", Color.white, Color.white, 0, shift: .7f);
            m["Mouth"] = Toon(p + "Mouth", robot ? spec.eye : new Color(.35f, .14f, .14f), robot ? spec.eye : new Color(.35f, .14f, .14f), 0, shift: .7f, glow: robot ? (Color?)(spec.eye.linear) : null);
            var nose = spec.kind == Kind.Dog ? new Color(.16f, .12f, .12f) : robot ? new Color(1f, .82f, .2f) : new Color(1f, .55f, .62f);
            m["Nose"] = Toon(p + "Nose", nose, Shade(nose, .7f), spec.kind == Kind.Dog ? .0015f : 0, shift: .5f, glow: robot ? (Color?)(nose.linear * .6f) : null);
            m["Blush"] = Toon(p + "Blush", new Color(1f, .45f, .55f), new Color(1f, .45f, .55f), 0, MToon10AlphaMode.Transparent, .7f, .5f, robot ? (Color?)new Color(.6f, .15f, .2f) : null);
            m["Bell"] = Toon(p + "Bell", new Color(1f, .82f, .3f), new Color(.85f, .55f, .2f), .0015f);
            m["Visor"] = Toon(p + "Visor", new Color(.07f, .09f, .14f), new Color(.05f, .06f, .1f), 0, shift: .3f);
            return m;
        }

        // ---------- prefab ----------
        static GameObject BuildPrefab(Spec spec)
        {
            current = spec; swept.Clear();
            var root = new GameObject(spec.name);
            try
            {
                var model = new GameObject("Model"); model.transform.SetParent(root.transform, false);
                var rig = new Rig(); var armature = new GameObject("Armature").transform; armature.SetParent(model.transform, false);
                foreach (var (name, parent, at) in Bones)
                {
                    rig.Add(name, parent == null ? armature : rig[parent], at);
                    if (name.StartsWith("Left")) rig.Add("Right" + name.Substring(4), parent.StartsWith("Left") ? rig["Right" + parent.Substring(4)] : rig[parent], new Vector3(-at.x, at.y, at.z));
                }
                var mat = Materials(spec);
                var body = new M(); Body(body, rig, spec, mat);
                var face = new M(); Face(face, rig, spec, mat);
                var bones = rig.list.ToArray();
                // Bind poses relative to the model, which stays at the origin and unscaled until the avatar is built.
                var bind = bones.Select(b => b.worldToLocalMatrix * model.transform.localToWorldMatrix).ToArray();
                SkinnedMeshRenderer Skinned(string name, M data, string[] shapes)
                {
                    var go = new GameObject(name); go.transform.SetParent(model.transform, false);
                    var mesh = Save(data.Build(spec.id + "_" + name, bind, shapes), Output + "/Meshes/" + spec.id + "_" + name + ".asset");
                    var r = go.AddComponent<SkinnedMeshRenderer>(); r.sharedMesh = mesh; r.bones = bones; r.rootBone = rig["Hips"]; r.sharedMaterials = data.mats.ToArray();
                    r.updateWhenOffscreen = true; r.shadowCastingMode = name == "Face" ? ShadowCastingMode.Off : ShadowCastingMode.On; return r;
                }
                Skinned("Body", body, Array.Empty<string>()); var faceRenderer = Skinned("Face", face, MascotFaceDriver.Shapes);

                var animator = model.AddComponent<Animator>(); animator.avatar = Avatar(spec, model, rig);
                animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                Accessories(spec, root, rig, mat);
                model.transform.localScale = Vector3.one * spec.scale;

                var library = AssetDatabase.LoadAssetAtPath<GameObject>(Chibi).GetComponent<ClipAnimator>();
                var player = root.AddComponent<ClipAnimator>(); player.animator = animator;
                player.idle = library.idle; player.walk = library.walk; player.run = library.run; player.carry = library.carry; player.gestures = library.gestures.ToArray();
                var binding = root.AddComponent<CharacterRigBinding>(); binding.animator = animator;
                binding.head = rig["Head"]; binding.chest = rig["UpperChest"]; binding.hips = rig["Hips"];
                binding.leftArm = rig["LeftUpperArm"]; binding.rightArm = rig["RightUpperArm"]; binding.leftForearm = rig["LeftLowerArm"]; binding.rightForearm = rig["RightLowerArm"];
                binding.leftLeg = rig["LeftUpperLeg"]; binding.rightLeg = rig["RightUpperLeg"]; binding.leftKnee = rig["LeftLowerLeg"]; binding.rightKnee = rig["RightLowerLeg"];
                binding.faceMeshes = Array.Empty<SkinnedMeshRenderer>(); binding.blinkShape = binding.mouthShape = binding.lookXShape = binding.lookYShape = "";
                binding.animationYawOffset = AnimationYaw(root, player);
                var driver = root.AddComponent<MascotFaceDriver>(); driver.face = faceRenderer; driver.leftEye = rig["LeftEye"]; driver.rightEye = rig["RightEye"];
                ContactShadow(root);
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 8;
                root.AddComponent<BodySystem>().rig = binding;
                Debug.Log("MASCOT " + spec.id + " yawOffset=" + binding.animationYawOffset + " tris=" + root.GetComponentsInChildren<SkinnedMeshRenderer>().Sum(r => r.sharedMesh.triangles.Length / 3));
                return PrefabUtility.SaveAsPrefabAsset(root, Output + "/" + spec.id + ".prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        // Replaces the asset outright: copying a mesh over an existing one can keep its old index buffer.
        static T Save<T>(T asset, string path) where T : UnityEngine.Object { AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(asset, path); return asset; }
        static Avatar Avatar(Spec spec, GameObject model, Rig rig)
        {
            var human = new List<HumanBone>();
            foreach (var name in rig.bone.Keys)
                if (Enum.TryParse<HumanBodyBones>(name, out var b)) human.Add(new HumanBone { boneName = name, humanName = HumanTrait.BoneName[(int)b], limit = new HumanLimit { useDefaultValues = true } });
            var skeleton = model.GetComponentsInChildren<Transform>(true).Select(t => new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray();
            var description = new HumanDescription { human = human.ToArray(), skeleton = skeleton, upperArmTwist = .5f, lowerArmTwist = .5f, upperLegTwist = .5f, lowerLegTwist = .5f, armStretch = .05f, legStretch = .05f, feetSpacing = 0, hasTranslationDoF = false };
            var avatar = AvatarBuilder.BuildHumanAvatar(model, description); avatar.name = spec.id + "_Avatar";
            if (!avatar.isValid || !avatar.isHuman) throw new Exception(spec.id + " avatar is not a valid humanoid");
            return Save(avatar, Output + "/" + spec.id + "_Avatar.asset");
        }
        // Plays the idle clip on a copy and checks which way the retargeted body faces.
        static float AnimationYaw(GameObject root, ClipAnimator player)
        {
            var copy = UnityEngine.Object.Instantiate(root);
            try
            {
                var a = copy.GetComponentInChildren<Animator>(); player.idle.SampleAnimation(a.gameObject, .5f);
                return a.GetBoneTransform(HumanBodyBones.LeftUpperArm).position.x > a.GetBoneTransform(HumanBodyBones.RightUpperArm).position.x ? 180 : 0;
            }
            finally { UnityEngine.Object.DestroyImmediate(copy); }
        }
        static void UpdateProfile(Spec spec, GameObject prefab)
        {
            string path = "Assets/Resources/Profiles/" + spec.id + ".asset";
            var profile = AssetDatabase.LoadAssetAtPath<CharacterProfile>(path) ?? throw new Exception("Missing profile " + path);
            profile.prefab = prefab; profile.displayName = spec.displayName; profile.personality = spec.personality; profile.personalSpace = Mathf.Max(profile.personalSpace, .8f);
            EditorUtility.SetDirty(profile);
        }
        static void ContactShadow(GameObject root)
        {
            var material = AssetDatabase.FindAssets("\"Contact shadow\" t:Material").Select(g => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault();
            if (!material) return;
            var shadow = new GameObject("Contact shadow", typeof(MeshFilter), typeof(MeshRenderer)); shadow.transform.SetParent(root.transform, false);
            shadow.transform.localPosition = new Vector3(0, .012f, 0); shadow.transform.localRotation = Quaternion.Euler(90, 0, 0); shadow.transform.localScale = new Vector3(.6f, .6f, 1);
            shadow.GetComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var r = shadow.GetComponent<MeshRenderer>(); r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        // ---------- review ----------
        // Batch: build, then render each mascot in the living room (lineup, front/back, head, expressions) for review.
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
                camera.fieldOfView = 40; camera.transform.position = new Vector3(0, .85f, -2.1f); camera.transform.LookAt(new Vector3(0, .5f, .3f));
                // New renderers have no probe lighting on their first frame; render once to settle it.
                ContentValidation.Render(camera, "../artifacts/previews/Mascot-lineup.png");
                ContentValidation.Render(camera, "../artifacts/previews/Mascot-lineup.png");
                foreach (var go in lineup) go.SetActive(false);
                foreach (var (spec, go) in Specs.Zip(lineup, (s, g) => (s, g)))
                {
                    go.SetActive(true); go.transform.position = new Vector3(0, 0, .3f);
                    var head = go.GetComponent<CharacterRigBinding>().head.position;
                    camera.fieldOfView = 36; camera.transform.position = new Vector3(.4f, .7f, -1.5f); camera.transform.LookAt(new Vector3(0, .45f, .3f));
                    ContentValidation.Render(camera, "../artifacts/previews/Mascot-" + spec.id + ".png");
                    go.transform.rotation = Quaternion.Euler(0, 30, 0); ContentValidation.Render(camera, "../artifacts/previews/Mascot-" + spec.id + "-back.png"); go.transform.rotation = Quaternion.Euler(0, 180, 0);
                    camera.fieldOfView = 26; camera.transform.position = head + new Vector3(.1f, .22f, -1.1f); camera.transform.LookAt(head + Vector3.up * .18f);
                    foreach (var emotion in new[] { Emotion.neutral, Emotion.happy, Emotion.excited, Emotion.sad, Emotion.concerned, Emotion.embarrassed, Emotion.curious })
                    {
                        Express(go, emotion); ContentValidation.Render(camera, "../artifacts/previews/Mascot-" + spec.id + "-" + emotion + ".png");
                    }
                    Express(go, Emotion.neutral);
                    foreach (var x in go.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Face preview").ToArray()) UnityEngine.Object.DestroyImmediate(x.gameObject);
                    foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) r.enabled = true;
                    go.SetActive(false);
                }
            }
            finally { EditorSettings.asyncShaderCompilation = async; }
            Debug.Log("MASCOTS_REVIEW_DONE");
        }
        static void Pose(GameObject go)
        {
            var player = go.GetComponent<ClipAnimator>(); var rig = go.GetComponent<CharacterRigBinding>();
            player.idle.SampleAnimation(player.animator.gameObject, .5f);
            if (rig.animationYawOffset != 0) rig.hips.rotation = Quaternion.AngleAxis(rig.animationYawOffset, go.transform.up) * rig.hips.rotation;
        }
        // Edit-mode preview of the runtime face with the weights MascotFaceDriver settles on for an emotion.
        static void Express(GameObject go, Emotion emotion)
        {
            var renderer = go.GetComponent<MascotFaceDriver>().face;
            for (int i = 0; i < MascotFaceDriver.Shapes.Length; i++) renderer.SetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex(MascotFaceDriver.Shapes[i]), MascotFaceDriver.Target(emotion, i) * 100);
            // Batch edit-mode renders do not re-deform skinned meshes after a weight change; show a baked copy instead.
            foreach (var old in go.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Face preview").ToArray()) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var baked = new Mesh(); renderer.BakeMesh(baked, true);
            var preview = new GameObject("Face preview", typeof(MeshFilter), typeof(MeshRenderer)); preview.transform.SetParent(renderer.transform.parent, false);
            preview.transform.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation);
            preview.GetComponent<MeshFilter>().sharedMesh = baked; var r = preview.GetComponent<MeshRenderer>(); r.sharedMaterials = renderer.sharedMaterials; r.probeAnchor = renderer.probeAnchor;
            renderer.enabled = false;
        }
    }
}
