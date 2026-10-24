using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace QuestAI
{
    public sealed class SceneObject
    {
        public string id, label;
        public Bounds bounds;
        public bool obstacle;
    }
    // What the character knows about the room: labelled furniture from the loaded scene plus a walkable grid.
    public sealed class SceneAwareness
    {
        public readonly List<SceneObject> objects = new List<SceneObject>();
        public Bounds floor;
        public NavGrid grid;
        // Checked in order, so specific names ("loungeSofa", "Fire bed") win over generic ones ("lounge", "bed").
        static readonly (string key, string label)[] Labels = {
            ("distant",null),("pine",null),("tree",null),("snow",null),("beyond",null),("wind tree",null),("light",null),("sun",null),("reflection",null),("probe",null),("sparks",null),("glow",null),("shadow",null),("leaves",null),("_mat_",null),
            ("hearth","暖炉"),("chimney","暖炉"),("mantel","暖炉"),("fire bed","暖炉"),("flame","暖炉"),("log","暖炉"),
            ("sofa","ソファ"),("couch","ソファ"),("loungechair","ひじ掛け椅子"),("lounge chair","ひじ掛け椅子"),("lounge_","ひじ掛け椅子"),
            ("nightstand","ナイトテーブル"),("cabinetbed","ナイトテーブル"),("bed","ベッド"),
            ("coffeemachine","コーヒーマシン"),("coffee machine","コーヒーマシン"),("counter","カウンター"),
            ("bookcase","本棚"),("shelves","本棚"),("shelf","本棚"),("cabinet","棚"),("book","本"),
            ("lamp","ランプ"),("table","テーブル"),("chair","椅子"),("seat","椅子"),("plant","植物"),("vase","花瓶"),
            ("rug","ラグ"),("carpet","ラグ"),("curtain","窓"),("window","窓")};
        static readonly HashSet<string> Walkable = new HashSet<string> { "ラグ", "窓" };

        public static string Label(string name)
        {
            string lower = (name ?? "").ToLowerInvariant();
            if (lower.StartsWith("realistic ")) lower = lower.Substring(10);
            foreach (var (key, label) in Labels) if (lower.Contains(key)) return label;
            return null;
        }
        static bool IsFloor(string name) { string lower = name.ToLowerInvariant(); return lower == "oak floor" || lower.StartsWith("env_floor"); }

        public static SceneAwareness Scan(Scene scene, Vector3 fallbackCenter)
        {
            var parts = new List<SceneObject>(); var floors = new List<Bounds>();
            foreach (var root in scene.GetRootGameObjects()) Visit(root.transform, parts, floors);
            Bounds floor = floors.Count > 0 ? floors.Aggregate((a, b) => { a.Encapsulate(b); return a; }) : new Bounds(fallbackCenter, new Vector3(5, 0, 5));
            return Build(parts, floor);
        }
        static void Visit(Transform t, List<SceneObject> parts, List<Bounds> floors)
        {
            if (!t.gameObject.activeInHierarchy || t.GetComponent<BodySystem>()) return;
            if (IsFloor(t.name)) { if (TryBounds(t, out var b)) floors.Add(b); return; }
            string label = Label(t.name);
            if (label != null && TryBounds(t, out var bounds)) { parts.Add(new SceneObject { label = label, bounds = bounds }); return; }
            if (label == null && Labels.Any(x => x.label == null && t.name.ToLowerInvariant().Contains(x.key))) return;
            foreach (Transform child in t) Visit(child, parts, floors);
        }
        static bool TryBounds(Transform t, out Bounds bounds)
        {
            bounds = default; bool any = false;
            foreach (var r in t.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || r is ParticleSystemRenderer) continue;
                if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
            }
            if (any) return true;
            // Some rooms (Botanica) render merged meshes and keep the named parts only as simple box colliders.
            foreach (var c in t.GetComponentsInChildren<Collider>())
            {
                if (!c.enabled || c.isTrigger) continue;
                if (!any) { bounds = c.bounds; any = true; } else bounds.Encapsulate(c.bounds);
            }
            return any;
        }

        // Pieces of one thing (window panes, hearth stones, chair parts) are merged when they touch.
        public static SceneAwareness Build(IEnumerable<SceneObject> parts, Bounds floor)
        {
            var result = new SceneAwareness { floor = floor };
            foreach (var part in parts)
            {
                var grow = part.bounds; grow.Expand(.3f);
                var same = result.objects.FirstOrDefault(o => o.label == part.label && o.bounds.Intersects(grow));
                if (same != null) same.bounds.Encapsulate(part.bounds); else result.objects.Add(new SceneObject { label = part.label, bounds = part.bounds });
            }
            // Objects merged later may now touch each other.
            for (bool merged = true; merged;)
            {
                merged = false;
                for (int i = 0; i < result.objects.Count && !merged; i++) for (int j = i + 1; j < result.objects.Count && !merged; j++)
                {
                    var a = result.objects[i]; var b = result.objects[j]; var grow = b.bounds; grow.Expand(.3f);
                    if (a.label == b.label && a.bounds.Intersects(grow)) { a.bounds.Encapsulate(b.bounds); result.objects.RemoveAt(j); merged = true; }
                }
            }
            foreach (var group in result.objects.GroupBy(o => o.label))
            {
                int n = 0; bool many = group.Count() > 1;
                foreach (var o in group.OrderBy(o => o.bounds.center.x)) o.id = many ? o.label + (++n) : o.label;
            }
            foreach (var o in result.objects) o.obstacle = !Walkable.Contains(o.label) && o.bounds.min.y < .5f && o.bounds.size.y > .12f;
            result.grid = new NavGrid(floor, result.objects.Where(o => o.obstacle).Select(o => o.bounds));
            return result;
        }

        public SceneObject Find(string id) => objects.FirstOrDefault(o => o.id == id) ?? objects.FirstOrDefault(o => o.label == id);
        public SceneObject Nearest(Vector3 point) => objects.OrderBy(o => FlatDistance(o, point)).FirstOrDefault();
        public static float FlatDistance(SceneObject o, Vector3 point)
        {
            var p = o.bounds.ClosestPoint(new Vector3(point.x, o.bounds.center.y, point.z));
            return Vector2.Distance(new Vector2(p.x, p.z), new Vector2(point.x, point.z));
        }
        // The object nearest to the centre of a view ray, within a cone.
        public SceneObject LookedAt(Vector3 origin, Vector3 forward, float maxAngle = 14)
        {
            SceneObject best = null; float bestAngle = maxAngle;
            foreach (var o in objects)
            {
                float angle = Vector3.Angle(forward, o.bounds.center - origin);
                if (angle < bestAngle) { bestAngle = angle; best = o; }
            }
            return best;
        }
        public static string Direction(Vector3 from, Vector3 forward, Vector3 to)
        {
            Vector3 delta = to - from; delta.y = 0; forward.y = 0;
            if (delta.sqrMagnitude < .0001f) return "すぐそば";
            float angle = Vector3.SignedAngle(forward.normalized, delta.normalized, Vector3.up);
            string side = Mathf.Abs(angle) < 25 ? "正面" : Mathf.Abs(angle) > 135 ? "後ろ" : angle > 0 ? (angle < 70 ? "右前" : angle < 110 ? "右" : "右後ろ") : (angle > -70 ? "左前" : angle > -110 ? "左" : "左後ろ");
            return side + Mathf.Max(.1f, delta.magnitude).ToString("0.0") + "m";
        }
        // A short Japanese description of the room from the character's point of view, for Live and Jev.
        public string Describe(string room, Vector3 self, Vector3 selfForward, Vector3 user, Vector3 userForward)
        {
            var text = new StringBuilder();
            text.Append("部屋: ").Append(room).Append("（広さ約").Append(floor.size.x.ToString("0")).Append("m×").Append(floor.size.z.ToString("0")).Append("m）。");
            text.Append("あなたから見た配置: ");
            text.Append(string.Join("、", objects.OrderBy(o => FlatDistance(o, self)).Take(12).Select(o => o.id + "(" + Direction(self, selfForward, o.bounds.center) + ")")));
            text.Append("。ユーザーは").Append(Direction(self, selfForward, user)).Append("にいます。");
            var near = Nearest(self); if (near != null && FlatDistance(near, self) < 1) text.Append("あなたは").Append(near.id).Append("のそばにいます。");
            var seen = LookedAt(user, userForward); if (seen != null) text.Append("ユーザーは").Append(seen.id).Append("の方を見ています。");
            return text.ToString();
        }
        // A walkable spot beside an object, on the side closest to the character.
        public bool Approach(SceneObject target, Vector3 from, float clearance, out Vector3 spot)
        {
            spot = default; float best = float.MaxValue; bool found = false;
            foreach (var cell in grid.FreeCells())
            {
                float edge = FlatDistance(target, cell);
                if (edge < clearance || edge > clearance + .6f) continue;
                float score = edge * 2 + Vector3.Distance(cell, from) * .3f;
                if (score < best) { best = score; spot = cell; found = true; }
            }
            return found;
        }
    }

    // Coarse occupancy grid on the floor with 8-way A*; obstacles are inflated by the character's radius.
    public sealed class NavGrid
    {
        public readonly float cell, radius;
        readonly Vector3 origin; readonly int width, depth; readonly bool[] blocked;
        public int Width => width; public int Depth => depth;
        public NavGrid(Bounds floor, IEnumerable<Bounds> obstacles, float cell = .2f, float radius = .28f, float wallMargin = .35f)
        {
            this.cell = cell; this.radius = radius;
            origin = new Vector3(floor.min.x + wallMargin, floor.max.y, floor.min.z + wallMargin);
            width = Mathf.Max(1, Mathf.FloorToInt((floor.size.x - 2 * wallMargin) / cell) + 1);
            depth = Mathf.Max(1, Mathf.FloorToInt((floor.size.z - 2 * wallMargin) / cell) + 1);
            blocked = new bool[width * depth];
            foreach (var b in obstacles)
                for (int x = 0; x < width; x++) for (int z = 0; z < depth; z++)
                {
                    var p = Center(x, z);
                    float dx = Mathf.Max(0, Mathf.Abs(p.x - b.center.x) - b.extents.x), dz = Mathf.Max(0, Mathf.Abs(p.z - b.center.z) - b.extents.z);
                    if (dx * dx + dz * dz < radius * radius) blocked[x + z * width] = true;
                }
        }
        public Vector3 Center(int x, int z) => new Vector3(origin.x + x * cell, origin.y, origin.z + z * cell);
        public Vector2Int Cell(Vector3 p) => new Vector2Int(Mathf.Clamp(Mathf.RoundToInt((p.x - origin.x) / cell), 0, width - 1), Mathf.Clamp(Mathf.RoundToInt((p.z - origin.z) / cell), 0, depth - 1));
        public bool Inside(Vector3 p) => p.x >= origin.x - cell / 2 && p.z >= origin.z - cell / 2 && p.x <= origin.x + (width - .5f) * cell && p.z <= origin.z + (depth - .5f) * cell;
        public bool Blocked(Vector3 p) { if (!Inside(p)) return true; var c = Cell(p); return blocked[c.x + c.y * width]; }
        public IEnumerable<Vector3> FreeCells() { for (int z = 0; z < depth; z++) for (int x = 0; x < width; x++) if (!blocked[x + z * width]) yield return Center(x, z); }
        Vector2Int NearestFree(Vector2Int c)
        {
            if (!blocked[c.x + c.y * width]) return c;
            for (int r = 1; r < Mathf.Max(width, depth); r++)
                for (int dz = -r; dz <= r; dz++) for (int dx = -r; dx <= r; dx++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                    int x = c.x + dx, z = c.y + dz;
                    if (x >= 0 && z >= 0 && x < width && z < depth && !blocked[x + z * width]) return new Vector2Int(x, z);
                }
            return c;
        }
        public Vector3 Snap(Vector3 p) { var c = NearestFree(Cell(p)); return Center(c.x, c.y); }
        // Returns world waypoints (start excluded, goal included) or null when the goal cannot be reached.
        // `avoid` marks extra temporary blockers such as the user's standing spot.
        public List<Vector3> FindPath(Vector3 from, Vector3 to, Vector3? avoid = null, float avoidRadius = .45f)
        {
            var start = NearestFree(Cell(from)); var goal = NearestFree(Cell(to));
            bool Free(int x, int z)
            {
                if (x < 0 || z < 0 || x >= width || z >= depth || blocked[x + z * width]) return false;
                if (avoid.HasValue && (x != goal.x || z != goal.y)) { var p = Center(x, z); if (new Vector2(p.x - avoid.Value.x, p.z - avoid.Value.z).sqrMagnitude < avoidRadius * avoidRadius) return false; }
                return true;
            }
            int count = width * depth; var cost = new float[count]; var parent = new int[count]; var closed = new bool[count];
            for (int i = 0; i < count; i++) { cost[i] = float.MaxValue; parent[i] = -1; }
            int s = start.x + start.y * width, g = goal.x + goal.y * width; cost[s] = 0;
            var open = new SortedSet<(float f, int index)>(); open.Add((Heuristic(start, goal), s));
            while (open.Count > 0)
            {
                var current = open.Min; open.Remove(current); int i = current.index;
                if (closed[i]) continue; closed[i] = true;
                if (i == g) break;
                int cx = i % width, cz = i / width;
                for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue; int nx = cx + dx, nz = cz + dz;
                    if (!Free(nx, nz)) continue;
                    if (dx != 0 && dz != 0 && (!Free(cx + dx, cz) || !Free(cx, cz + dz))) continue; // no corner cutting
                    int n = nx + nz * width; float next = cost[i] + (dx != 0 && dz != 0 ? 1.4142f : 1);
                    if (next < cost[n]) { cost[n] = next; parent[n] = i; open.Add((next + Heuristic(new Vector2Int(nx, nz), goal), n)); }
                }
            }
            if (s != g && parent[g] < 0) return null;
            var cells = new List<int>(); for (int i = g; i != s && i >= 0; i = parent[i]) cells.Add(i); cells.Reverse();
            var points = cells.Select(i => Center(i % width, i / width)).ToList();
            return Smooth(Center(start.x, start.y), points, Free);
        }
        static float Heuristic(Vector2Int a, Vector2Int b) { int dx = Mathf.Abs(a.x - b.x), dz = Mathf.Abs(a.y - b.y); return Mathf.Max(dx, dz) + .4142f * Mathf.Min(dx, dz); }
        // Drops waypoints that are visible from an earlier one so the character walks in straight lines.
        List<Vector3> Smooth(Vector3 start, List<Vector3> points, Func<int, int, bool> free)
        {
            if (points.Count < 2) return points;
            var result = new List<Vector3>(); Vector3 anchor = start; int k = 0;
            while (k < points.Count)
            {
                int far = k;
                for (int j = points.Count - 1; j > k; j--) if (Clear(anchor, points[j], free)) { far = j; break; }
                result.Add(points[far]); anchor = points[far]; k = far + 1;
            }
            return result;
        }
        bool Clear(Vector3 a, Vector3 b, Func<int, int, bool> free)
        {
            int steps = Mathf.CeilToInt(Vector3.Distance(a, b) / (cell * .35f));
            for (int i = 1; i < steps; i++) { var c = Cell(Vector3.Lerp(a, b, i / (float)steps)); if (!free(c.x, c.y)) return false; }
            return true;
        }
    }
}
