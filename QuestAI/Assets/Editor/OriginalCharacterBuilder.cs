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
    // しろ・こはく・もも・むぎ are made from scratch by this script; no third-party model is used. It builds the skeleton and its
    // humanoid avatar, a skinned body (head, limbs, dress, socks, shoes), a face of blend-shape decals projected onto the head
    // (eyes, irises on eye bones, lashes, brows, mouth, blush), hair strands grown around the head and body with spring
    // chains, painted textures and toon materials (the MToon10 shader from UniVRM). The shared CC0 animation library on
    // WhiteChibi retargets onto the avatar. Measurements are metres for a 1.43 m figure facing +Z; each character is scaled.
    // Batch: -executeMethod QuestAI.Editor.OriginalCharacterBuilder.BuildAndReview
    public static class OriginalCharacterBuilder
    {
        const string Output = "Assets/Generated/OriginalCharacters", Chibi = "Assets/Generated/WhiteChibi.prefab";
        enum Hair { Long, Buns, Twin, Bob }
        sealed class Spec
        {
            public string id, name; public float scale = 1, hem = .6f, socks = .4f; public Hair hair; public bool bunny, cat, ahoge;
            public Color skin = new Color(1f, .92f, .86f), hairColor, hairTip, irisL, irisR, dress, trim = new Color(.99f, .98f, .96f), ribbon, sock = new Color(.98f, .98f, .99f), shoe;
        }
        static readonly Spec[] Specs = {
            new Spec { id = "calm", name = "Shiro", scale = 1.02f, hair = Hair.Long, hem = .54f, socks = .56f, hairColor = new Color(.93f, .94f, .98f), hairTip = new Color(.8f, .83f, .96f),
                irisL = new Color(.3f, .62f, .95f), irisR = new Color(.96f, .74f, .28f), dress = new Color(.42f, .52f, .78f), ribbon = new Color(.28f, .36f, .66f), shoe = new Color(.24f, .26f, .38f) },
            new Spec { id = "bright", name = "Kohaku", scale = .97f, hair = Hair.Buns, ahoge = true, hairColor = new Color(.88f, .54f, .24f), hairTip = new Color(.98f, .74f, .4f),
                irisL = new Color(.92f, .5f, .16f), irisR = new Color(.92f, .5f, .16f), dress = new Color(.99f, .8f, .36f), ribbon = new Color(.9f, .3f, .26f), shoe = new Color(.5f, .3f, .2f) },
            new Spec { id = "bunny", name = "MomoBunny", hair = Hair.Twin, bunny = true, socks = .56f, hairColor = new Color(.98f, .7f, .79f), hairTip = new Color(1f, .88f, .92f),
                irisL = new Color(.9f, .34f, .5f), irisR = new Color(.9f, .34f, .5f), dress = new Color(.99f, .95f, .97f), trim = new Color(.98f, .62f, .74f), ribbon = new Color(.97f, .48f, .64f), shoe = new Color(.96f, .72f, .8f) },
            new Spec { id = "cat", name = "MugiCat", scale = 1.03f, hair = Hair.Bob, cat = true, ahoge = true, hairColor = new Color(.95f, .74f, .4f), hairTip = new Color(.99f, .87f, .6f),
                irisL = new Color(.36f, .72f, .32f), irisR = new Color(.36f, .72f, .32f), dress = new Color(.36f, .55f, .42f), trim = new Color(.98f, .95f, .86f), ribbon = new Color(.95f, .52f, .28f), sock = new Color(.3f, .26f, .24f), shoe = new Color(.42f, .28f, .2f) } };

        // Skeleton in the bind (T) pose: name, parent, position. Right-side bones mirror the left ones.
        static readonly (string name, string parent, Vector3 at)[] Bones = {
            ("Hips", null, new Vector3(0, .8f, 0)), ("Spine", "Hips", new Vector3(0, .88f, 0)), ("Chest", "Spine", new Vector3(0, .97f, 0)), ("UpperChest", "Chest", new Vector3(0, 1.05f, 0)),
            ("Neck", "UpperChest", new Vector3(0, 1.14f, -.01f)), ("Head", "Neck", new Vector3(0, 1.19f, -.005f)), ("LeftEye", "Head", new Vector3(-.036f, 1.273f, .027f)),
            ("LeftShoulder", "UpperChest", new Vector3(-.03f, 1.11f, -.01f)), ("LeftUpperArm", "LeftShoulder", new Vector3(-.14f, 1.1f, -.01f)),
            ("LeftLowerArm", "LeftUpperArm", new Vector3(-.355f, 1.1f, -.01f)), ("LeftHand", "LeftLowerArm", new Vector3(-.555f, 1.1f, -.01f)),
            ("LeftUpperLeg", "Hips", new Vector3(-.085f, .76f, 0)), ("LeftLowerLeg", "LeftUpperLeg", new Vector3(-.08f, .43f, .005f)),
            ("LeftFoot", "LeftLowerLeg", new Vector3(-.075f, .075f, -.01f)), ("LeftToes", "LeftFoot", new Vector3(-.075f, .02f, .085f)) };
        static readonly Vector3 HeadCenter = new Vector3(0, 1.288f, .005f);
        const float EyeY = 1.273f, EyeX = .036f, MouthY = 1.224f;

        [MenuItem("Quest AI/Build original characters")]
        public static void Build()
        {
            foreach (var folder in new[] { "", "/Materials", "/Meshes", "/Textures" }) Directory.CreateDirectory(Output + folder);
            AssetDatabase.Refresh();
            foreach (var spec in Specs) UpdateProfile(spec, BuildPrefab(spec));
            AssetDatabase.SaveAssets();
            Debug.Log("ORIGINAL_CHARACTERS_READY " + string.Join(",", Specs.Select(s => s.id)));
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
            // Smooth blend along a chain of (bone, height) stations, e.g. the spine.
            public BoneWeight Along(float y, params (string bone, float at)[] chain)
            {
                if (y <= chain[0].at) return W(chain[0].bone);
                for (int i = 0; i < chain.Length - 1; i++) if (y < chain[i + 1].at) return W(chain[i].bone, chain[i + 1].bone, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(chain[i].at, chain[i + 1].at, y)));
                return W(chain[chain.Length - 1].bone);
            }
        }
        struct Ring { public Vector3 c, a, b; public BoneWeight w; public float v; public Ring(Vector3 c, Vector3 a, Vector3 b, BoneWeight w, float v = 0) { this.c = c; this.a = a; this.b = b; this.w = w; this.v = v; } }
        // Rings of `sides` points c + a·cos θ + b·sin θ (+ an optional radial offset) joined by quads, with optional fan caps.
        static void Loft(M m, int s, IList<Ring> rings, int sides, bool capStart, bool capEnd, Func<int, float, float> radius = null, Func<int, int, BoneWeight> weight = null)
        {
            int start = m.v.Count;
            for (int i = 0; i < rings.Count; i++)
                for (int j = 0; j < sides; j++)
                {
                    float th = j * Mathf.PI * 2 / sides, k = radius?.Invoke(i, th) ?? 1; var r = rings[i];
                    m.Add(r.c + (r.a * Mathf.Cos(th) + r.b * Mathf.Sin(th)) * k, new Vector2((float)j / sides, r.v), weight?.Invoke(i, j) ?? r.w);
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
                int center = m.Add(rings[ring].c, new Vector2(.5f, rings[ring].v), rings[ring].w);
                for (int j = 0; j < sides; j++) m.Tri(s, center, start + ring * sides + j, start + ring * sides + (j + 1) % sides, outward);
            }
            if (capStart) Cap(0, rings[0].c - rings[1].c);
            if (capEnd) Cap(rings.Count - 1, rings[rings.Count - 1].c - rings[rings.Count - 2].c);
        }
        // A lat-long sphere through point(direction); triangles whose corners all pass `keep` are kept. Returns their corners.
        static List<Vector3> Ball(M m, int s, int lat, int lon, Func<Vector3, Vector3> point, Func<Vector3, BoneWeight> weight, Vector3 center, Func<Vector3, bool> keep = null, Func<Vector3, float> v = null)
        {
            var index = new int[lat + 1, lon]; var dir = new Vector3[lat + 1, lon]; var corners = new List<Vector3>();
            for (int i = 0; i <= lat; i++)
                for (int j = 0; j < lon; j++)
                {
                    float th = Mathf.PI * i / lat, ph = Mathf.PI * 2 * j / lon; var d = new Vector3(Mathf.Sin(th) * Mathf.Sin(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Cos(ph));
                    dir[i, j] = d;
                    if ((i == 0 || i == lat) && j > 0) { index[i, j] = index[i, 0]; continue; }
                    index[i, j] = m.Add(point(d), new Vector2((float)j / lon, v?.Invoke(d) ?? 1 - (float)i / lat), weight(d));
                }
            void Triangle(int i0, int j0, int i1, int j1, int i2, int j2)
            {
                int a = index[i0, j0], b = index[i1, j1], c = index[i2, j2]; if (a == b || b == c || a == c) return;
                if (keep != null && !(keep(dir[i0, j0]) && keep(dir[i1, j1]) && keep(dir[i2, j2]))) return;
                m.Tri(s, a, b, c, (m.v[a] + m.v[b] + m.v[c]) / 3 - center); corners.Add(m.v[a]); corners.Add(m.v[b]); corners.Add(m.v[c]);
            }
            for (int i = 0; i < lat; i++)
                for (int j = 0; j < lon; j++) { int n = (j + 1) % lon; Triangle(i, j, i, n, i + 1, n); Triangle(i, j, i + 1, n, i + 1, j); }
            return corners;
        }

        // ---------- head and face ----------
        // The head: a rounded skull over a small tapered jaw and pointed chin, flatter in front.
        static Vector3 HeadPoint(Vector3 d, float grow = 0)
        {
            float front = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-.2f, .8f, d.z)), low = Mathf.Max(0, -d.y);
            float wx = .088f * (1 - Mathf.Pow(low, 1.4f) * Mathf.Lerp(.25f, .5f, front));
            float hy = d.y > 0 ? .105f : Mathf.Lerp(.088f, .108f, front), dz = d.z > 0 ? .088f : .1f;
            return HeadCenter + new Vector3(d.x * (wx + grow), d.y * (hy + grow), d.z * (dz + grow));
        }
        static List<Vector3> headSurface;
        // Front of the head at (x, y): a ray along -Z against the head triangles.
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
        static Vector3 OnFace(Vector2 p, float lift) => new Vector3(p.x, p.y, SurfaceZ(p.x, p.y) + lift);
        // A grid decal laid on the face, lifted off the skin, with blend shapes that move its points to other face positions.
        static void Decal(M m, int s, int cols, int rows, float lift, BoneWeight w, Func<float, float, Vector2> at, params (string name, Func<float, float, Vector2> at)[] shapes)
        {
            int start = m.v.Count;
            for (int r = 0; r <= rows; r++)
                for (int c = 0; c <= cols; c++)
                {
                    float u = (float)c / cols, v = (float)r / rows; var p = OnFace(at(u, v), lift); int i = m.Add(p, new Vector2(u, v), w);
                    foreach (var shape in shapes) { var q = OnFace(shape.at(u, v), lift); if ((q - p).sqrMagnitude > 1e-12f) m.Shape(shape.name, i, q - p); }
                }
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    int a = start + r * (cols + 1) + c, b = a + 1, d = a + cols + 1, e = d + 1;
                    m.Tri(s, a, b, e, Vector3.forward); m.Tri(s, a, e, d, Vector3.forward);
                }
        }
        // Eye outline in eye units: h runs from the inner (-1) to the outer (+1) corner.
        static float Top(float h) { h = Mathf.Clamp(h, -1, 1); return .021f * Mathf.Pow(Mathf.Max(0, 1 - Mathf.Pow(Mathf.Abs(h), 2.4f)), 1 / 2.4f) + .0025f * h; }
        static float Bottom(float h) { h = Mathf.Clamp(h, -1, 1); return -.015f * Mathf.Sqrt(Mathf.Max(0, 1 - h * h)) + .0015f * h; }
        static float Closed(float h) => Mathf.Lerp(Bottom(h), Top(h), .28f);
        static float Arch(float h) { h = Mathf.Clamp(h, -1, 1); return .001f + .008f * (1 - h * h) + .0015f * h; }
        const float EyeHalf = .021f;

        static void Face(M m, Rig rig, Spec spec, Dictionary<string, Material> mat)
        {
            var head = rig.W("Head");
            for (int side = -1; side <= 1; side += 2)
            {
                float ex = side * EyeX;
                // side -1 is the character's left eye (on -X); h = +1 is always the outer corner.
                Vector2 P(float h, float y) => new Vector2(ex + side * h * EyeHalf, EyeY + y);
                Vector2 Wide(float h, float y) => new Vector2(ex + side * h * EyeHalf * 1.03f, EyeY + y * 1.16f + .001f);
                Decal(m, m.Sub(mat["EyeWhite"]), 16, 6, .0008f, head, (u, v) => { float h = u * 2 - 1; return P(h, Mathf.Lerp(Bottom(h), Top(h), v)); },
                    ("Blink", (u, v) => P(u * 2 - 1, Closed(u * 2 - 1))), ("EyeHappy", (u, v) => P(u * 2 - 1, Arch(u * 2 - 1))),
                    ("EyeWide", (u, v) => { float h = u * 2 - 1; return Wide(h, Mathf.Lerp(Bottom(h), Top(h), v)); }));
                // Iris: a cut-out painted disc on the eye bone; it squashes with the lids.
                float ix = .013f, iy = .0168f, icy = -.001f;
                // Not mirrored, so both eyes catch the light on the same side.
                Vector2 IrisAt(float u, float v) => new Vector2(ex + side * .0015f + (u * 2 - 1) * ix, EyeY + icy + (v * 2 - 1) * iy);
                float H(float u) => (.0015f + side * (u * 2 - 1) * ix) / EyeHalf;
                Decal(m, m.Sub(mat[side < 0 ? "IrisL" : "IrisR"]), 8, 8, .0016f, rig.W(side < 0 ? "LeftEye" : "RightEye"), IrisAt,
                    ("Blink", (u, v) => new Vector2(IrisAt(u, v).x, EyeY + Closed(H(u)))), ("EyeHappy", (u, v) => new Vector2(IrisAt(u, v).x, EyeY + Arch(H(u)))),
                    ("EyeWide", (u, v) => { var p = IrisAt(u, v); return new Vector2(ex + (p.x - ex) * 1.03f, EyeY + (p.y - EyeY) * 1.16f + .001f); }));
                // Upper lash line: thick at the outer corner, ending in a small flick; it becomes the closed-eye line.
                var line = m.Sub(mat["Line"]);
                float LashH(float u) => Mathf.Lerp(-1.06f, 1.16f, u);
                float Thick(float h) => Mathf.Lerp(.0016f, .0046f, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-1, .7f, h))) * (h > 1 ? Mathf.Lerp(1, .15f, (h - 1) / .16f) : 1) * (h < -.95f ? .6f : 1);
                float LashY(float h) => h > 1 ? Top(1) + (h - 1) * .025f : Top(h);
                Decal(m, line, 20, 1, .0024f, head, (u, v) => { float h = LashH(u); return P(h, LashY(h) - .0006f + v * Thick(h)); },
                    ("Blink", (u, v) => { float h = LashH(u); return P(h, Closed(h) + (h > 1 ? (h - 1) * .01f : 0) + v * Thick(h) * .75f); }),
                    ("EyeHappy", (u, v) => { float h = LashH(u); return P(h, Arch(h) + (h > 1 ? -(h - 1) * .03f : 0) + v * Thick(h) * .8f); }),
                    ("EyeWide", (u, v) => { float h = LashH(u); var p = P(h, LashY(h) - .0006f + v * Thick(h)); return Wide(h, p.y - EyeY); }));
                // A short lower lash on the outer half.
                Decal(m, line, 8, 1, .0022f, head, (u, v) => { float h = Mathf.Lerp(.25f, 1, u); return P(h, Bottom(h) - v * .0011f); },
                    ("Blink", (u, v) => { float h = Mathf.Lerp(.25f, 1, u); return P(h, Closed(h) - v * .0008f); }),
                    ("EyeHappy", (u, v) => { float h = Mathf.Lerp(.25f, 1, u); return P(h, Arch(h) - .0004f - v * .0008f); }),
                    ("EyeWide", (u, v) => { float h = Mathf.Lerp(.25f, 1, u); return Wide(h, Bottom(h) - v * .0011f); }));
                // Brow: a soft tapered arc above the eye.
                Vector2 Brow(float u, float v, float sad, float up)
                {
                    float h = u * 2 - 1, y = .03f + .0035f * (1 - h * h) + up * .006f + sad * (.006f * (1 - u) - .002f * u), thick = .0021f * (1 - .55f * u);
                    return new Vector2(ex + side * (.002f + h * .0185f), EyeY + y + (v - .5f) * thick);
                }
                Decal(m, line, 10, 1, .0018f, head, (u, v) => Brow(u, v, 0, 0), ("BrowSad", (u, v) => Brow(u, v, 1, 0)), ("BrowUp", (u, v) => Brow(u, v, 0, 1)));
                // Blush: hidden (collapsed) until the Blush shape spreads it over the cheek.
                Vector2 Blush(float u, float v, float k) { float a = u * Mathf.PI * 2, r = v; return new Vector2(side * .05f + Mathf.Cos(a) * r * .017f * k, EyeY - .029f + Mathf.Sin(a) * r * .0085f * k); }
                Decal(m, m.Sub(mat["Blush"]), 16, 2, .0012f, head, (u, v) => Blush(u, v, .02f), ("Blush", (u, v) => Blush(u, v, 1)));
            }
            // Nose: a tiny shaded tick.
            Decal(m, m.Sub(mat["Nose"]), 2, 2, .0008f, head, (u, v) => new Vector2(.002f + (u - .5f) * .0024f * (1 - v * .6f), 1.245f + (v - .5f) * .003f));
            // Mouth: a thin smile line that opens into vowel shapes; the texture shades lip line, mouth and tongue from top to bottom.
            Vector2 Mouth(float u, float v, float width, float smile, float open, float top, float down)
            {
                float h = u * 2 - 1, round = Mathf.Sqrt(Mathf.Max(0, 1 - h * h));
                float c = .0012f * h * h + smile * (.003f * h * h - .0006f) - down * .0025f * h * h, upper = c + .0007f + top * round, lower = c - .0007f - open * Mathf.Pow(round, .7f);
                return new Vector2(h * .0085f * width, MouthY + Mathf.Lerp(upper, lower, v));
            }
            Decal(m, m.Sub(mat["Mouth"]), 12, 4, .0009f, head, (u, v) => Mouth(u, v, 1, 0, 0, 0, 0),
                ("Smile", (u, v) => Mouth(u, v, 1.15f, 1, 0, 0, 0)), ("Frown", (u, v) => Mouth(u, v, .9f, 0, 0, 0, 1)),
                ("MouthOpen", (u, v) => Mouth(u, v, .92f, 0, .011f, .0012f, 0)), ("MouthEe", (u, v) => Mouth(u, v, 1.2f, 0, .004f, .0004f, 0)),
                ("MouthOh", (u, v) => Mouth(u, v, .55f, 0, .009f, .002f, 0)));
        }

        // ---------- body and clothes ----------
        static readonly (string bone, float at)[] Spine = { ("Hips", .78f), ("Spine", .88f), ("Chest", .97f), ("UpperChest", 1.05f), ("Neck", 1.15f) };
        static void Body(M m, Rig rig, Spec spec, Dictionary<string, Material> mat)
        {
            int skin = m.Sub(mat["Skin"]), dress = m.Sub(mat["Dress"]), trim = m.Sub(mat["Trim"]), ribbon = m.Sub(mat["Ribbon"]), sock = m.Sub(mat["Sock"]), shoe = m.Sub(mat["Shoe"]);
            headSurface = Ball(m, skin, 28, 36, d => HeadPoint(d), d => rig.W("Head"), HeadCenter);
            // Neck.
            var neck = new List<Ring>(); foreach (var y in new[] { 1.1f, 1.16f, 1.2f, 1.24f }) neck.Add(new Ring(new Vector3(0, y, -.008f), Vector3.right * .029f, Vector3.forward * .028f, rig.Along(y, ("Neck", 1.15f), ("Head", 1.21f))));
            Loft(m, skin, neck, 14, false, false);
            // Bodice: hips to the neck base, shoulders wide enough to cover the arm roots.
            var torso = new List<Ring>();
            foreach (var (y, rx, rz, z) in new[] { (.7f, .105f, .085f, 0f), (.78f, .116f, .09f, 0f), (.86f, .098f, .077f, 0f), (.93f, .099f, .079f, .003f), (.99f, .106f, .087f, .01f), (1.04f, .108f, .082f, .006f), (1.08f, .118f, .072f, -.004f), (1.115f, .1f, .062f, -.008f), (1.135f, .056f, .045f, -.01f), (1.15f, .033f, .032f, -.009f) })
                torso.Add(new Ring(new Vector3(0, y, z), Vector3.right * rx, Vector3.forward * rz, rig.Along(y, Spine)));
            Loft(m, dress, torso, 28, true, false);
            // Collar resting on the shoulders, belt and a bow at the chest.
            Loft(m, trim, new[] { new Ring(new Vector3(0, 1.148f, -.008f), Vector3.right * .04f, Vector3.forward * .038f, rig.W("Neck")), new Ring(new Vector3(0, 1.112f, -.004f), Vector3.right * .092f, Vector3.forward * .074f, rig.W("UpperChest")) }, 28, false, false);
            Loft(m, ribbon, new[] { new Ring(new Vector3(0, .868f, 0), Vector3.right * .1f, Vector3.forward * .079f, rig.W("Spine")), new Ring(new Vector3(0, .895f, 0), Vector3.right * .1f, Vector3.forward * .079f, rig.W("Spine")) }, 28, false, false, (i, th) => 1.03f);
            var chest = rig.W("UpperChest"); var bow = new Vector3(0, 1.098f, .066f);
            for (int s = -1; s <= 1; s += 2)
            {
                Ball(m, ribbon, 8, 12, d => bow + new Vector3(s * .02f, 0, 0) + Vector3.Scale(d, new Vector3(.019f, .011f, .007f)), d => chest, bow + new Vector3(s * .02f, 0, 0));
                Loft(m, ribbon, new[] { new Ring(bow + new Vector3(s * .004f, -.004f, .002f), Vector3.right * .006f, Vector3.forward * .003f, chest), new Ring(bow + new Vector3(s * .014f, -.032f, .006f), Vector3.right * .008f, Vector3.forward * .002f, chest) }, 8, false, true);
            }
            Ball(m, ribbon, 6, 10, d => bow + new Vector3(0, 0, .004f) + d * .0075f, d => chest, bow);
            // Skirt: pleated cone from the waist; its lower front and back follow the thighs so walking does not cut through it.
            var skirt = new List<Ring>(); int rows = 8;
            for (int i = 0; i <= rows; i++)
            {
                float t = (float)i / rows, y = Mathf.Lerp(.885f, spec.hem, t), flare = Mathf.Pow(t, .8f);
                skirt.Add(new Ring(new Vector3(0, y, -.008f * t), Vector3.right * Mathf.Lerp(.103f, .215f, flare), Vector3.forward * Mathf.Lerp(.081f, .19f, flare), rig.W("Hips"), t));
            }
            Func<int, float, float> pleats = (i, th) => 1 + .045f * Mathf.Pow((float)Math.Min(i, rows) / rows, 1.2f) * (1 - Mathf.Cos(th * 14)) * .5f;
            Func<int, int, BoneWeight> drape = (i, j) =>
            {
                float t = (float)Math.Min(i, rows) / rows, th = j * Mathf.PI * 2 / 56;
                // θ = 0 is the right side (+X).
                float follow = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.15f, 1, t)) * .55f * Mathf.Abs(Mathf.Sin(th)) * Mathf.Clamp01(Mathf.Abs(Mathf.Cos(th)) * 2.5f + .3f);
                return rig.W("Hips", Mathf.Cos(th) > 0 ? "RightUpperLeg" : "LeftUpperLeg", follow);
            };
            Loft(m, m.Sub(mat["Skirt"]), skirt, 56, false, false, pleats, drape);
            var hem = skirt[rows]; float hemDrop = .014f;
            var band = new[] { new Ring(hem.c + Vector3.up * hemDrop, hem.a * .985f * 1.01f, hem.b * .985f * 1.01f, hem.w), new Ring(hem.c - Vector3.up * .002f, hem.a * 1.012f, hem.b * 1.012f, hem.w) };
            Loft(m, m.Sub(mat["SkirtTrim"]), band, 56, false, false, (i, th) => pleats(rows, th), (i, j) => drape(rows, j));
            for (int side = -1; side <= 1; side += 2)
            {
                string S(string bone) => (side < 0 ? "Left" : "Right") + bone;
                // Arm along ±X: puff sleeve, skin, mitten hand and thumb.
                var arm = new List<Ring>();
                foreach (var (x, r) in new[] { (.1f, .034f), (.16f, .032f), (.25f, .028f), (.33f, .025f), (.355f, .0235f), (.38f, .024f), (.45f, .023f), (.53f, .0185f), (.555f, .0175f) })
                    arm.Add(new Ring(new Vector3(side * x, 1.1f, -.01f), Vector3.up * r, Vector3.forward * r * 1.05f, rig.Along(x, (S("UpperArm"), .32f), (S("LowerArm"), .385f), (S("Hand"), .56f))));
                Loft(m, skin, arm, 14, false, false);
                var sleeve = new List<Ring>();
                foreach (var (x, r) in new[] { (.105f, .046f), (.12f, .054f), (.15f, .058f), (.19f, .05f), (.21f, .037f) })
                    sleeve.Add(new Ring(new Vector3(side * x, 1.1f, -.01f), Vector3.up * r, Vector3.forward * r, rig.W(S("UpperArm"))));
                Loft(m, dress, sleeve, 20, false, false, (i, th) => 1 + .05f * Mathf.Pow(Mathf.Sin(th * 5), 2));
                Loft(m, trim, new[] { new Ring(new Vector3(side * .2f, 1.1f, -.01f), Vector3.up * .043f, Vector3.forward * .043f, rig.W(S("UpperArm"))), new Ring(new Vector3(side * .214f, 1.1f, -.01f), Vector3.up * .035f, Vector3.forward * .035f, rig.W(S("UpperArm"))) }, 20, false, false);
                var hand = new List<Ring>(); var hw = rig.W(S("Hand"));
                foreach (var (x, ry, rz) in new[] { (.55f, .016f, .022f), (.58f, .0145f, .029f), (.62f, .012f, .031f), (.66f, .011f, .027f), (.69f, .008f, .019f), (.704f, .004f, .009f) })
                    hand.Add(new Ring(new Vector3(side * x, 1.1f, -.012f), Vector3.up * ry, Vector3.forward * rz, hw));
                Loft(m, skin, hand, 12, false, true);
                Loft(m, skin, new[] { new Ring(new Vector3(side * .575f, 1.096f, .006f), Vector3.up * .009f, new Vector3(side, 0, -.6f).normalized * .009f, hw),
                    new Ring(new Vector3(side * .6f, 1.094f, .03f), Vector3.up * .0075f, new Vector3(side, 0, -.4f).normalized * .0075f, hw), new Ring(new Vector3(side * .62f, 1.094f, .045f), Vector3.up * .006f, Vector3.right * .006f, hw) }, 8, false, true);
                // Leg from inside the skirt to the ankle; socks below spec.socks.
                var leg = new List<(float y, float x, float z, float r)> { (.8f, .088f, 0, .066f), (.7f, .087f, 0, .062f), (.58f, .085f, .002f, .053f), (.47f, .082f, .005f, .043f), (.43f, .08f, .006f, .04f), (.37f, .079f, .002f, .041f), (.28f, .078f, -.003f, .042f), (.18f, .077f, -.006f, .032f), (.1f, .076f, -.008f, .026f), (.07f, .075f, -.01f, .025f) };
                Ring LegRing(float y, float x, float z, float r) => new Ring(new Vector3(side * x, y, z), Vector3.right * r, Vector3.forward * r, rig.Along(-y, (S("UpperLeg"), -.47f), (S("LowerLeg"), -.39f), (S("Foot"), -.1f)));
                var rings = leg.Select(p => LegRing(p.y, p.x, p.z, p.r)).ToList();
                // Split exactly at the sock top so skin and sock meet on one ring; the sock gets a small cuff.
                int below = leg.FindIndex(p => p.y < spec.socks);
                if (below > 0)
                {
                    var (a, b) = (leg[below - 1], leg[below]); float k = Mathf.InverseLerp(a.y, b.y, spec.socks);
                    rings.Insert(below, LegRing(spec.socks, Mathf.Lerp(a.x, b.x, k), Mathf.Lerp(a.z, b.z, k), Mathf.Lerp(a.r, b.r, k)));
                }
                var skinLeg = rings.Where(r => r.c.y >= spec.socks - 1e-4f).ToList();
                var sockLeg = rings.Where(r => r.c.y <= spec.socks + 1e-4f).Select(r => new Ring(r.c, r.a * 1.05f, r.b * 1.05f, r.w)).ToList();
                sockLeg.Insert(0, new Ring(sockLeg[0].c + Vector3.up * .007f, sockLeg[0].a, sockLeg[0].b, sockLeg[0].w));
                sockLeg[1] = new Ring(sockLeg[1].c, sockLeg[1].a * 1.03f, sockLeg[1].b * 1.03f, sockLeg[1].w);
                if (skinLeg.Count > 1) Loft(m, skin, skinLeg, 16, false, false);
                Loft(m, sock, sockLeg, 16, false, false);
                // Shoe along +Z from heel to toe, with a cuff around the ankle.
                var foot = new List<Ring>();
                foreach (var (z, y, rx, ry) in new[] { (-.06f, .045f, .03f, .043f), (-.03f, .05f, .036f, .05f), (.02f, .04f, .038f, .039f), (.07f, .03f, .036f, .029f), (.105f, .026f, .03f, .022f), (.125f, .024f, .017f, .014f) })
                    foot.Add(new Ring(new Vector3(side * .075f, y, z), Vector3.right * rx, Vector3.up * ry, rig.W(S("Foot"), S("Toes"), Mathf.InverseLerp(.05f, .1f, z))));
                Loft(m, shoe, foot, 16, true, true);
                Loft(m, shoe, new[] { new Ring(new Vector3(side * .075f, .085f, -.012f), Vector3.right * .031f, Vector3.forward * .031f, rig.W(S("Foot"))), new Ring(new Vector3(side * .075f, .11f, -.01f), Vector3.right * .029f, Vector3.forward * .029f, rig.W(S("LowerLeg"), S("Foot"), .4f)) }, 16, false, false);
            }
        }

        // ---------- hair ----------
        // Collision hull of head and body the strands drape over: piecewise ellipsoids (each axis has its own +/- radius).
        static readonly (Vector3 c, Vector3 pos, Vector3 neg)[] Hull = {
            (HeadCenter, new Vector3(.101f, .12f, .1f), new Vector3(.101f, .125f, .115f)),
            (new Vector3(0, .98f, -.01f), new Vector3(.125f, .2f, .1f), new Vector3(.125f, .2f, .1f)),
            (new Vector3(0, 1.1f, -.012f), new Vector3(.165f, .045f, .085f), new Vector3(.165f, .045f, .09f)),
            (new Vector3(0, .8f, -.01f), new Vector3(.16f, .12f, .13f), new Vector3(.16f, .12f, .13f)) };
        static Vector3 Clear(Vector3 p)
        {
            for (int pass = 0; pass < 3; pass++)
                foreach (var (c, pos, neg) in Hull)
                {
                    var d = p - c; var r = new Vector3(d.x > 0 ? pos.x : neg.x, d.y > 0 ? pos.y : neg.y, d.z > 0 ? pos.z : neg.z);
                    var q = new Vector3(d.x / r.x, d.y / r.y, d.z / r.z); if (q.sqrMagnitude < 1) p = c + Vector3.Scale(q.normalized, r);
                }
            return p;
        }
        // Grows a strand from a root: each step bends toward gravity, then is pushed back out of the hull. stopY cuts it short.
        static List<Vector3> Grow(Vector3 root, Vector3 dir, float length, float droop, int steps = 14, float stopY = float.NegativeInfinity)
        {
            var path = new List<Vector3> { root }; var p = root; var d = dir.normalized; float step = length / steps;
            for (int i = 0; i < steps; i++)
            {
                d = (d + Vector3.down * droop).normalized; var next = Clear(p + d * step);
                if (next.y < stopY) { float k = Mathf.InverseLerp(p.y, next.y, stopY); path.Add(Vector3.Lerp(p, next, k)); break; }
                d = (next - p).normalized; p = next; path.Add(p);
            }
            return path;
        }
        // A flat, pointed lock along a path. Its width wraps horizontally around the head and body and is carried smoothly from
        // ring to ring, so the ribbon never flips or twists where the strand turns.
        static void Strand(M m, int s, List<Vector3> path, float width, float thickness, Func<float, BoneWeight> weight)
        {
            if (path.Count < 3) return;
            var rings = new List<Ring>(); int n = path.Count; var previous = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / (n - 1); var tangent = (path[Math.Min(n - 1, i + 1)] - path[Math.Max(0, i - 1)]).normalized;
                var axis = new Vector3(0, 0, path[i].y > 1.2f ? HeadCenter.z : -.01f); var outward = path[i] - axis; outward.y = 0;
                var around = outward.sqrMagnitude > 1e-6f ? Vector3.Cross(Vector3.up, outward).normalized : Vector3.right;
                var side = around - tangent * Vector3.Dot(around, tangent);
                if (side.sqrMagnitude < .05f && i > 0) side = previous - tangent * Vector3.Dot(previous, tangent);
                side.Normalize(); if (i > 0 && Vector3.Dot(side, previous) < 0) side = -side; previous = side;
                var normal = Vector3.Cross(side, tangent).normalized;
                float w = width * Mathf.Lerp(.8f, 1, Mathf.Clamp01(t / .2f)) * Mathf.Max(.02f, 1 - Mathf.Pow(t, 2.2f));
                rings.Add(new Ring(path[i], side * w * .5f, normal * thickness * .5f * (1 - .6f * t), weight(t), Mathf.Lerp(.12f, 1, t)));
            }
            Loft(m, s, rings, 6, false, false);
        }
        sealed class SpringChain { public Transform[] joints; public float phi; }
        // Turns a representative path into a chain of spring joints under the head.
        static SpringChain Chain(Rig rig, string name, List<Vector3> path, int count, float phi)
        {
            var joints = new Transform[count]; var parent = rig["Head"];
            for (int k = 0; k < count; k++) { var at = path[Mathf.RoundToInt((float)k / (count - 1) * (path.Count - 1))]; joints[k] = parent = rig.Add(name + "_" + k, parent, at); }
            return new SpringChain { joints = joints, phi = phi };
        }
        // Weight along a chain for a point a fraction t down a strand; the root stays on the head.
        static BoneWeight OnChain(Rig rig, SpringChain chain, float t)
        {
            if (t < .1f) return rig.W("Head", chain.joints[0].name, t / .1f);
            float k = Mathf.Clamp(t * (chain.joints.Length - 1), 0, chain.joints.Length - 1.001f); int i = (int)k;
            return rig.W(chain.joints[i].name, chain.joints[i + 1].name, k - i);
        }
        static Vector3 Around(float phiDegrees, float y) { float a = phiDegrees * Mathf.Deg2Rad, r = Mathf.Sqrt(1 - y * y); return new Vector3(Mathf.Sin(a) * r, y, Mathf.Cos(a) * r); }

        static List<SpringChain> HairMesh(M m, Rig rig, Spec spec, Material hairMat, Material tieMat)
        {
            int s = m.Sub(hairMat); var head = rig.W("Head"); var chains = new List<SpringChain>();
            // Cap over the skull down to a hairline: high on the forehead, above the ears, low at the nape.
            Ball(m, s, 24, 36, d => HeadPoint(d, .009f), d => head, HeadCenter, d => d.y > Mathf.Lerp(-.62f, .36f, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-.35f, .55f, d.z))), d => Mathf.Lerp(0, .5f, (1 - d.y) * .5f));
            // Bangs: locks from the front of the crown falling over the forehead to just above the brows; the outer ones longer.
            int bangs = 13;
            for (int i = 0; i < bangs; i++)
            {
                float f = Mathf.Lerp(-1, 1, (float)i / (bangs - 1)), edge = Mathf.Abs(f);
                var root = HeadPoint(new Vector3(f * .5f, .84f, .45f).normalized, .006f);
                float stop = EyeY + .036f + (i % 2 == 0 ? 0 : .006f) - (edge > .85f ? .03f : 0) - (Mathf.Abs(f) < .15f ? .004f : 0);
                Strand(m, s, Grow(root, new Vector3(f * .3f, -.55f, .85f), .2f, .1f, 16, stop), .036f, .007f, t => head);
            }
            float side = spec.hair == Hair.Long ? .3f : spec.hair == Hair.Bob ? .13f : spec.hair == Hair.Twin ? .24f : .2f;
            float back = spec.hair == Hair.Long ? .6f : spec.hair == Hair.Bob ? .17f : spec.hair == Hair.Twin ? .25f : .21f;
            // Side locks framing the face, each on its own spring chain.
            for (int k = -1; k <= 1; k += 2)
            {
                var lead = Grow(HeadPoint(new Vector3(k * .88f, .28f, .38f).normalized, .006f), new Vector3(k * .2f, -1, .25f), side, .05f);
                var chain = Chain(rig, k < 0 ? "Hair_SideL" : "Hair_SideR", lead, 3, k * 90); chains.Add(chain);
                Strand(m, s, lead, .03f, .009f, t => OnChain(rig, chain, t));
                Strand(m, s, Grow(HeadPoint(new Vector3(k * .93f, .14f, .2f).normalized, .006f), new Vector3(k * .3f, -1, .05f), side * .9f, .05f), .032f, .01f, t => OnChain(rig, chain, t));
            }
            // Back hair in two layers; strands share a few spring chains by their angle around the head.
            var backChains = new List<SpringChain>(); float[] anchors = spec.hair == Hair.Long ? new[] { 110f, 145f, 180f, 215f, 250f } : new[] { 125f, 180f, 235f };
            foreach (var phi in anchors)
            {
                var lead = Grow(HeadPoint(Around(phi, .45f), .006f), Around(phi, -.8f), back, .22f, 16);
                backChains.Add(Chain(rig, "Hair_Back" + Mathf.RoundToInt(phi), lead, spec.hair == Hair.Long ? 5 : 3, phi));
            }
            void Layer(float y, float from, float to, float step, float length, float width)
            {
                for (float phi = from; phi <= to + .01f; phi += step)
                {
                    var chain = backChains.OrderBy(c => Mathf.Abs(Mathf.DeltaAngle(c.phi, phi))).First();
                    var root = HeadPoint(Around(phi, y), .006f); var dir = Around(phi, -.8f);
                    Strand(m, s, Grow(root, dir, length * Mathf.Lerp(.92f, 1.04f, Mathf.PerlinNoise(phi * .1f, y * 7)), .22f, 16), width, .012f, t => OnChain(rig, chain, t));
                }
            }
            Layer(.62f, 62, 298, 13, back, .06f);
            Layer(.28f, 80, 280, 12.5f, back * .97f, .058f);
            if (spec.hair == Hair.Bob) Layer(.05f, 100, 260, 20, back * .7f, .045f);
            chains.AddRange(backChains);
            if (spec.hair == Hair.Buns)
                for (int k = -1; k <= 1; k += 2)
                {
                    var c = HeadPoint(new Vector3(k * .55f, .74f, -.38f).normalized, .03f);
                    Ball(m, s, 12, 16, d => c + Vector3.Scale(d, new Vector3(.043f, .04f, .042f)) * (1 + .06f * Mathf.Sin(Mathf.Atan2(d.x, d.z) * 6 + d.y * 4)), d => head, c, null, d => .3f + d.y * .1f);
                    Ball(m, m.Sub(tieMat), 6, 16, d => c + new Vector3(0, -.03f, 0) + Vector3.Scale(d, new Vector3(.03f, .008f, .03f)), d => head, c);
                }
            if (spec.hair == Hair.Twin)
                for (int k = -1; k <= 1; k += 2)
                {
                    // Low twin tails tied behind the ears.
                    var tie = HeadPoint(new Vector3(k * .78f, -.05f, -.6f).normalized, .02f);
                    Ball(m, m.Sub(tieMat), 8, 14, d => tie + Vector3.Scale(d, new Vector3(.018f, .018f, .018f)), d => head, tie);
                    var lead = Grow(tie, new Vector3(k * .35f, -1, -.25f), .42f, .04f, 16);
                    var chain = Chain(rig, k < 0 ? "Hair_TailL" : "Hair_TailR", lead, 5, k * 120); chains.Add(chain);
                    for (int j = 0; j < 5; j++)
                    {
                        float a = j * Mathf.PI * 2 / 5; var offset = new Vector3(Mathf.Cos(a) * .012f, Mathf.Sin(a) * .006f, Mathf.Sin(a) * .012f);
                        Strand(m, s, Grow(tie + offset, new Vector3(k * .35f + Mathf.Cos(a) * .2f, -1, -.25f + Mathf.Sin(a) * .2f), .42f * Mathf.Lerp(.9f, 1.05f, j / 4f), .04f, 16), .036f, .016f, t => OnChain(rig, chain, t));
                    }
                }
            if (spec.ahoge)
            {
                var top = HeadPoint(new Vector3(0, 1, .1f).normalized, .006f);
                Strand(m, s, new List<Vector3> { top, top + new Vector3(0, .025f, .005f), top + new Vector3(.006f, .045f, .02f), top + new Vector3(.018f, .052f, .04f), top + new Vector3(.03f, .046f, .052f) }, .014f, .004f, t => head);
            }
            return chains;
        }

        // ---------- textures and materials ----------
        static Texture2D Paint(string name, int width, int height, Func<float, float, Color> paint, bool alpha)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) pixels[y * width + x] = paint((x + .5f) / width, (y + .5f) / height);
            tex.SetPixels(pixels); string path = Output + "/Textures/" + name + ".png"; File.WriteAllBytes(path, tex.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path); ti.wrapMode = TextureWrapMode.Clamp; ti.alphaIsTransparency = alpha; ti.mipmapEnabled = true; ti.sRGBTexture = true;
            var android = ti.GetPlatformTextureSettings("Android"); android.overridden = true; android.maxTextureSize = 256; android.format = TextureImporterFormat.ASTC_4x4; ti.SetPlatformTextureSettings(android);
            ti.SaveAndReimport(); return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        // 0 at a, 1 at b, eased (a may be above b).
        static float Step(float a, float b, float x) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(a, b, x));
        static float Disc(float x, float y, float cx, float cy, float rx, float ry, float soft = .08f) { float r = Mathf.Sqrt(Mathf.Pow((x - cx) / rx, 2) + Mathf.Pow((y - cy) / ry, 2)); return 1 - Step(1 - soft, 1 + soft, r); }
        // Anime iris: dark top shading into a bright lower glow, darker rim, soft pupil, fine streaks and two catchlights.
        static Texture2D Iris(string name, Color iris)
        {
            return Paint(name, 128, 128, (u, v) =>
            {
                float x = u * 2 - 1, y = v * 2 - 1, r = Mathf.Sqrt(x * x + y * y); if (r > 1.02f) return new Color(0, 0, 0, 0);
                float a = Mathf.Atan2(y, x), glow = Step(.2f, -.85f, y);
                var c = Color.Lerp(iris * .42f, Color.Lerp(iris, Color.white, .22f) * 1.1f, Step(.75f, -.6f, y));
                c = Color.Lerp(c, Color.Lerp(iris, Color.white, .45f), glow * .35f * Step(.95f, .5f, r));
                c *= 1 + .07f * Mathf.Sin(a * 22) * Step(.3f, .8f, r);
                c = Color.Lerp(c, iris * .22f, Step(.8f, .97f, r));
                c = Color.Lerp(c, iris * .14f, Disc(x, y, 0, .05f, .36f, .46f, .12f));
                c = Color.Lerp(c, Color.white, Disc(x, y, -.34f, .42f, .22f, .26f, .1f));
                c = Color.Lerp(c, Color.white, Disc(x, y, .36f, -.36f, .1f, .1f, .15f) * .9f);
                c.a = 1 - Step(.97f, 1.02f, r); return c;
            }, true);
        }
        static Material Toon(string name, Color color, Color shade, float outline = .0011f, Texture texture = null, MToon10AlphaMode alpha = MToon10AlphaMode.Opaque, bool twoSided = false, float shift = 0, float alphaValue = 1)
        {
            string path = Output + "/Materials/" + name + ".mat"; var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat) { mat = new Material(Shader.Find("VRM10/Universal Render Pipeline/MToon10")); AssetDatabase.CreateAsset(mat, path); }
            color.a = alphaValue;
            var c = new MToon10Context(mat)
            {
                AlphaMode = alpha, AlphaCutoff = .5f, DoubleSidedMode = twoSided ? MToon10DoubleSidedMode.On : MToon10DoubleSidedMode.Off, BaseColorFactorSrgb = color, BaseColorTexture = texture, ShadeColorFactorSrgb = shade, ShadeColorTexture = texture,
                ShadingToonyFactor = .92f, ShadingShiftFactor = shift, GiEqualizationFactor = .9f, ParametricRimColorFactorSrgb = new Color(.16f, .14f, .14f), ParametricRimFresnelPowerFactor = 4, RimLightingMixFactor = 1,
                OutlineWidthMode = outline > 0 ? MToon10OutlineMode.World : MToon10OutlineMode.None, OutlineWidthFactor = outline, OutlineColorFactorSrgb = Color.Lerp(shade * .45f, new Color(.25f, .15f, .15f), .4f), OutlineLightingMixFactor = 1
            };
            c.Validate(); EditorUtility.SetDirty(mat); return mat;
        }
        static Color Shade(Color c, float k = .78f) => Color.Lerp(c * k, new Color(.82f, .55f, .66f), .16f);
        static Dictionary<string, Material> Materials(Spec spec)
        {
            string p = spec.id + "_"; var m = new Dictionary<string, Material>();
            var hair = Paint(p + "Hair", 4, 256, (u, v) =>
            {
                var c = Color.Lerp(spec.hairColor * .9f, spec.hairColor, Step(0, .12f, v));
                c = Color.Lerp(c, Color.Lerp(spec.hairColor, Color.white, .45f), Mathf.Exp(-Mathf.Pow((v - .19f) / .025f, 2)) * .55f);
                return Color.Lerp(c, spec.hairTip, Step(.5f, 1, v));
            }, false);
            m["Hair"] = Toon(p + "Hair", Color.white, Shade(Color.white, .8f), .0012f, hair, shift: -.05f);
            m["Skin"] = Toon(p + "Skin", spec.skin, new Color(.98f, .76f, .72f), .0009f, shift: .15f);
            m["Dress"] = Toon(p + "Dress", spec.dress, Shade(spec.dress));
            m["Skirt"] = Toon(p + "Skirt", spec.dress, Shade(spec.dress), twoSided: true);
            m["Trim"] = Toon(p + "Trim", spec.trim, Shade(spec.trim, .85f));
            m["SkirtTrim"] = Toon(p + "SkirtTrim", spec.trim, Shade(spec.trim, .85f), twoSided: true);
            m["Ribbon"] = Toon(p + "Ribbon", spec.ribbon, Shade(spec.ribbon));
            m["Sock"] = Toon(p + "Sock", spec.sock, Shade(spec.sock, .82f));
            m["Shoe"] = Toon(p + "Shoe", spec.shoe, Shade(spec.shoe, .7f));
            m["EyeWhite"] = Toon(p + "EyeWhite", new Color(1f, .995f, .99f), new Color(.86f, .85f, .95f), 0, shift: .4f);
            m["IrisL"] = Toon(p + "IrisL", Color.white, Color.white, 0, Iris(p + "IrisL", spec.irisL), MToon10AlphaMode.Cutout, shift: .6f);
            m["IrisR"] = spec.irisR == spec.irisL ? m["IrisL"] : Toon(p + "IrisR", Color.white, Color.white, 0, Iris(p + "IrisR", spec.irisR), MToon10AlphaMode.Cutout, shift: .6f);
            var line = Color.Lerp(spec.hairColor * .25f, new Color(.2f, .1f, .1f), .6f);
            m["Line"] = Toon(p + "Line", line, line, 0, shift: .6f);
            var mouth = Paint(p + "Mouth", 4, 64, (u, v) => Color.Lerp(Color.Lerp(new Color(.36f, .13f, .13f), new Color(.55f, .17f, .2f), Step(.12f, .3f, v)), new Color(.94f, .52f, .56f), Step(.6f, .78f, v)), false);
            m["Mouth"] = Toon(p + "Mouth", Color.white, Color.white, 0, mouth, shift: .6f);
            m["Nose"] = Toon(p + "Nose", new Color(.93f, .68f, .64f), new Color(.93f, .68f, .64f), 0, shift: .6f);
            m["Blush"] = Toon(p + "Blush", new Color(1f, .5f, .55f), new Color(1f, .5f, .55f), 0, alpha: MToon10AlphaMode.Transparent, shift: .6f, alphaValue: .42f);
            m["Tie"] = Toon(p + "Tie", spec.ribbon, Shade(spec.ribbon));
            return m;
        }

        // ---------- prefab ----------
        static GameObject BuildPrefab(Spec spec)
        {
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
                var hair = new M(); var chains = HairMesh(hair, rig, spec, mat["Hair"], mat["Tie"]);
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
                Skinned("Body", body, Array.Empty<string>()); var faceRenderer = Skinned("Face", face, AnimeFaceDriver.Shapes); Skinned("Hair", hair, Array.Empty<string>());

                var animator = model.AddComponent<Animator>(); animator.avatar = Avatar(spec, model, rig);
                animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
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
                var driver = root.AddComponent<AnimeFaceDriver>(); driver.face = faceRenderer; driver.leftEye = rig["LeftEye"]; driver.rightEye = rig["RightEye"]; driver.eyeRange = 6;
                var spring = root.AddComponent<HairSpring>();
                spring.chains = chains.Select(c => new HairSpring.Chain { joints = c.joints, stiffness = c.joints.Length > 3 ? .9f : 1.3f, drag = .4f, gravity = .3f }).ToArray();
                spring.colliders = new[] { new HairSpring.Sphere { bone = rig["Head"], offset = HeadCenter - rig["Head"].position / spec.scale, radius = .1f }, new HairSpring.Sphere { bone = rig["UpperChest"], offset = new Vector3(0, -.03f, -.012f), radius = .085f } };
                if (spec.bunny || spec.cat) Accessories(spec, root, rig);
                ContactShadow(root);
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 8;
                root.AddComponent<BodySystem>().rig = binding;
                Debug.Log("ORIGINAL_CHARACTER " + spec.id + " yawOffset=" + binding.animationYawOffset + " tris=" + root.GetComponentsInChildren<SkinnedMeshRenderer>().Sum(r => r.sharedMesh.triangles.Length / 3) + " bones=" + bones.Length + " chains=" + chains.Count);
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
            string path = Output + "/" + spec.id + "_Avatar.asset"; AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(avatar, path); return avatar;
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
            profile.prefab = prefab; profile.personalSpace = Mathf.Max(profile.personalSpace, .85f);
            switch (spec.id)
            {
                case "calm": profile.personality = "銀白色の長い髪と、青と金の色違いの瞳を持つ、落ち着いた少女のしろ。優しく穏やかに話し、相手のペースを大事にする。短く自然な言葉で寄り添う。"; break;
                case "bright": profile.personality = "琥珀色の髪をお団子にまとめた、明るく好奇心旺盛な少女のこはく。身近な小さな楽しみを見つけ、一緒に喜ぶ。押しつけず相手を尊重する。"; break;
                case "bunny": profile.personality = "桃色の髪をふたつ結びにして、ふわふわのうさ耳を持つ女の子のもも。やさしく人なつっこく、一緒に遊ぶと耳まで嬉しそうに揺れる。相手の気持ちを大切にして短く話す。"; break;
                case "cat": profile.personality = "はちみつ色のボブヘアに猫耳と、しなやかなしっぽを持つむぎ。好奇心旺盛で少しマイペース。気に入ったものを見つけるのが上手で、親しく自然に話す。"; break;
            }
            EditorUtility.SetDirty(profile);
        }

        // ---------- animal ears and tails ----------
        static void Accessories(Spec spec, GameObject root, Rig rig)
        {
            Color coat = spec.bunny ? new Color(1f, .96f, .94f) : Color.Lerp(spec.hairColor, new Color(.96f, .72f, .47f), .4f), inner = new Color(1f, .66f, .74f);
            var coatMat = Toon(spec.id + "_Coat", coat, Shade(coat, .8f)); var innerMat = Toon(spec.id + "_Inner", inner, Shade(inner, .85f));
            var secondary = root.AddComponent<CompanionSecondaryMotion>(); secondary.rabbit = spec.bunny;
            // One mesh per shape, shared by both sides.
            Mesh ear = spec.bunny ? BunnyEar("BunnyEar", .2f, .052f, .03f) : CatEar("CatEar", .085f, .048f, .03f), innerEar = spec.bunny ? BunnyEar("BunnyInnerEar", .165f, .03f, .012f) : CatEar("CatInnerEar", .062f, .03f, .01f);
            for (int side = -1; side <= 1; side += 2)
            {
                // Bunny ears rise from the crown; cat ears sit further out on the sides of the head.
                var basePoint = HeadPoint(new Vector3(side * (spec.bunny ? .3f : .55f), spec.bunny ? .95f : .82f, spec.bunny ? -.05f : .02f).normalized, spec.bunny ? 0 : .002f);
                var pivot = new GameObject(side < 0 ? "Left ear spring" : "Right ear spring").transform; pivot.SetParent(rig["Head"], false);
                pivot.position = basePoint; pivot.rotation = Quaternion.AngleAxis(-side * (spec.bunny ? 9 : 24), Vector3.forward) * Quaternion.AngleAxis(spec.bunny ? -10 : -4, Vector3.right);
                Part(spec.bunny ? "Velvet ear" : "Cat ear", pivot, Vector3.zero, ear, coatMat);
                Part(spec.bunny ? "Rose inner ear" : "Pink inner ear", pivot, spec.bunny ? new Vector3(0, .012f, .011f) : new Vector3(0, .008f, .012f), innerEar, innerMat);
                if (side < 0) secondary.leftEar = pivot; else secondary.rightEar = pivot;
            }
            var tail = new GameObject("Tail spring").transform; tail.SetParent(rig["Hips"], false);
            tail.position = new Vector3(0, .72f, spec.bunny ? -.155f : -.11f); tail.rotation = Quaternion.identity; secondary.tail = tail;
            if (spec.bunny) Part("Cotton tail", tail, new Vector3(0, 0, -.035f), Pompom(), coatMat);
            else Part("Plush tail", tail, Vector3.zero, CatTail(), coatMat);
        }
        static void Part(string name, Transform parent, Vector3 local, Mesh mesh, Material material)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(parent, false); go.transform.localPosition = local;
            go.GetComponent<MeshFilter>().sharedMesh = mesh; var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.On;
        }
        // A rounded bunny ear: an elliptic section that swells then narrows to a soft tip, bending slightly back.
        static Mesh BunnyEar(string name, float length, float width, float depth)
        {
            return Sweep(name, 24, 16, t =>
            {
                float profile = Mathf.Pow(Mathf.Sin(Mathf.Lerp(.35f, Mathf.PI, t)), .55f);
                return (new Vector3(0, t * length, -Mathf.Pow(t, 2) * length * .12f), width * .5f * Mathf.Max(.05f, profile * (1 - .25f * t)), depth * .5f * Mathf.Max(.15f, profile));
            });
        }
        // A cat ear: a cone with an elliptic base that is flatter front to back, rounded at the tip.
        static Mesh CatEar(string name, float height, float width, float depth)
        {
            return Sweep(name, 14, 16, t =>
            {
                float profile = Mathf.Pow(Mathf.Max(0, Mathf.Cos(t * Mathf.PI * .5f)), .85f);
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
        static Mesh CatTail() => Sweep("CatTail", 36, 14, t => { float r = Mathf.Max(.004f, Mathf.Lerp(.03f, .022f, t) * Mathf.Min(1, Mathf.Sin(Mathf.Lerp(.25f, Mathf.PI, t)) * 3.5f)); return (TailPoint(t), r, r); });
        static Mesh Pompom() => Sweep("BunnyTail", 12, 16, t => { float a = t * Mathf.PI; return (new Vector3(0, -Mathf.Cos(a) * .045f, 0), Mathf.Max(.002f, Mathf.Sin(a) * .05f), Mathf.Max(.002f, Mathf.Sin(a) * .045f)); });
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
            return Save(mesh, Output + "/Meshes/" + name + ".asset");
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

        // ---------- review ----------
        // Batch: build, then render each character in the living room (lineup, full body front/back, head, expressions) for review.
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
                ContentValidation.Render(camera, "../artifacts/previews/Original-lineup.png");
                ContentValidation.Render(camera, "../artifacts/previews/Original-lineup.png");
                foreach (var go in lineup) go.SetActive(false);
                foreach (var (spec, go) in Specs.Zip(lineup, (s, g) => (s, g)))
                {
                    go.SetActive(true); go.transform.position = new Vector3(0, 0, .3f);
                    var head = go.GetComponent<CharacterRigBinding>().head.position;
                    camera.fieldOfView = 36; camera.transform.position = new Vector3(.45f, head.y * .62f, -2.1f); camera.transform.LookAt(new Vector3(0, head.y * .52f, .3f));
                    ContentValidation.Render(camera, "../artifacts/previews/Original-" + spec.id + ".png");
                    go.transform.rotation = Quaternion.Euler(0, 20, 0); ContentValidation.Render(camera, "../artifacts/previews/Original-" + spec.id + "-back.png"); go.transform.rotation = Quaternion.Euler(0, 180, 0);
                    camera.fieldOfView = 26; camera.transform.position = head + new Vector3(.45f, .12f, -.8f); camera.transform.LookAt(head + Vector3.up * .08f);
                    ContentValidation.Render(camera, "../artifacts/previews/Original-" + spec.id + "-head.png");
                    camera.fieldOfView = 20; camera.transform.position = head + new Vector3(.1f, .06f, -.75f); camera.transform.LookAt(head + Vector3.up * .07f);
                    foreach (var emotion in new[] { Emotion.neutral, Emotion.happy, Emotion.excited, Emotion.sad, Emotion.concerned, Emotion.embarrassed, Emotion.curious })
                    {
                        Express(go, emotion); ContentValidation.Render(camera, "../artifacts/previews/Original-" + spec.id + "-" + emotion + ".png");
                    }
                    Express(go, Emotion.neutral);
                    foreach (var x in go.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Face preview").ToArray()) UnityEngine.Object.DestroyImmediate(x.gameObject);
                    foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) r.enabled = true;
                    go.SetActive(false);
                }
            }
            finally { EditorSettings.asyncShaderCompilation = async; }
            Debug.Log("ORIGINAL_CHARACTERS_REVIEW_DONE");
        }
        static void Pose(GameObject go)
        {
            var player = go.GetComponent<ClipAnimator>(); var rig = go.GetComponent<CharacterRigBinding>();
            player.idle.SampleAnimation(player.animator.gameObject, .5f);
            if (rig.animationYawOffset != 0) rig.hips.rotation = Quaternion.AngleAxis(rig.animationYawOffset, go.transform.up) * rig.hips.rotation;
        }
        // Edit-mode preview of the runtime face with the weights AnimeFaceDriver settles on for an emotion.
        static void Express(GameObject go, Emotion emotion)
        {
            var renderer = go.GetComponent<AnimeFaceDriver>().face;
            for (int i = 0; i < AnimeFaceDriver.Shapes.Length; i++) renderer.SetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex(AnimeFaceDriver.Shapes[i]), AnimeFaceDriver.Target(emotion, i) * 100);
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
