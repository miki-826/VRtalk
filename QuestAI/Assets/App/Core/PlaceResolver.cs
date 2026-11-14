using System.Linq;
using UnityEngine;

namespace QuestAI
{
    // Turns a place described in words (「ソファの後ろ」「私の右」「部屋の真ん中」「あそこ」) into a free spot on the floor.
    // Pure room geometry, so it runs the same for the on-device parser, the AI planner and the tests.
    public static class PlaceResolver
    {
        public struct Result
        {
            // A free floor cell the character can stand on.
            public Vector3 spot;
            // How the place reads in Japanese, e.g. 「ソファの後ろ」.
            public string name;
            // Why there is no spot (null on success), in words the character can pass on.
            public string error;
        }
        // Standing room kept from furniture (on top of the character's own radius) and from the user.
        public const float Clearance = .3f, UserDistance = 1f, UserGap = .55f, CornerInset = .9f, MaxMiss = 1.2f;

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
        static Vector3 Unit(Vector3 v, Vector3 fallback) { v = Flat(v); return v.sqrMagnitude > 1e-4f ? v.normalized : Flat(fallback).sqrMagnitude > 1e-4f ? Flat(fallback).normalized : Vector3.forward; }
        static string Who(string id) => id == "user" ? "あなた" : id;

        public static string Name(MotionPlace place, string target, string toward)
        {
            // The four sides without a target are the user's own.
            bool room = string.IsNullOrEmpty(target); string who = room ? "あなた" : Who(target);
            switch (place)
            {
                case MotionPlace.front: return who + "の前";
                case MotionPlace.behind: return who + "の後ろ";
                case MotionPlace.left: return who + "の左";
                case MotionPlace.right: return who + "の右";
                case MotionPlace.beside: return who + "の横";
                case MotionPlace.between: return Who(target) + "と" + Who(toward) + "の間";
                case MotionPlace.center: return "部屋の真ん中";
                case MotionPlace.corner: return room ? "部屋の隅" : target + "の近くの隅";
                case MotionPlace.far_side: return "部屋の奥";
                case MotionPlace.pointed: return "指さした場所";
                default: return room ? "指示された場所" : Who(target);
            }
        }

        // target / toward: a scene object id, "user", or "" (the user for the four sides, nothing otherwise).
        // distance: metres from the target's edge (0 = close by). pointed: the spot the user points or looks at, if any.
        public static bool Resolve(SceneAwareness world, MotionPlace place, string target, string toward, float distance,
            Vector3 self, Vector3 user, Vector3 userForward, Vector3? pointed, out Result result)
        {
            result = new Result { name = Name(place, target, toward) };
            if (world == null || world.grid == null) { result.error = "この場所では歩けません。"; return false; }
            bool targetIsUser = target == "user", otherIsUser = toward == "user";
            var anchor = targetIsUser || string.IsNullOrEmpty(target) ? null : world.Find(target);
            var other = otherIsUser || string.IsNullOrEmpty(toward) ? null : world.Find(toward);
            if (!targetIsUser && !string.IsNullOrEmpty(target) && anchor == null) { result.error = "「" + target + "」が見つかりません。"; return false; }
            distance = Mathf.Clamp(float.IsNaN(distance) ? 0 : distance, 0, 3);
            Vector3 me = Flat(self), you = Flat(user), ideal;
            var floor = world.floor;
            switch (place)
            {
                case MotionPlace.pointed:
                    if (!pointed.HasValue) { result.error = "どこのことか分かりませんでした。行ってほしい床を指さすか、そこを見ながら言ってください。"; return false; }
                    ideal = Flat(pointed.Value); break;
                case MotionPlace.center: ideal = Flat(floor.center); break;
                case MotionPlace.far_side:
                    {
                        // Across the room from where the user stands, up to the wall; from the middle of the room, the way they face.
                        Vector3 centre = Flat(floor.center), across = centre - you;
                        Vector3 away = across.sqrMagnitude > 1 ? across.normalized : Unit(userForward, across);
                        float reachX = Mathf.Max(0, floor.extents.x - CornerInset), reachZ = Mathf.Max(0, floor.extents.z - CornerInset);
                        float x = Mathf.Abs(away.x) > 1e-3f ? (centre.x + Mathf.Sign(away.x) * reachX - you.x) / away.x : float.MaxValue;
                        float z = Mathf.Abs(away.z) > 1e-3f ? (centre.z + Mathf.Sign(away.z) * reachZ - you.z) / away.z : float.MaxValue;
                        ideal = you + away * Mathf.Max(0, Mathf.Min(x, z)); break;
                    }
                case MotionPlace.corner:
                    {
                        float x = Mathf.Max(0, floor.extents.x - CornerInset), z = Mathf.Max(0, floor.extents.z - CornerInset); Vector3 centre = Flat(floor.center);
                        var corners = new[] { new Vector3(x, 0, z), new Vector3(-x, 0, z), new Vector3(x, 0, -z), new Vector3(-x, 0, -z) }.Select(c => centre + c);
                        // Near the named object, otherwise the closest corner the character is not already standing in.
                        ideal = anchor != null ? corners.OrderBy(c => Vector3.Distance(c, Flat(anchor.bounds.center))).First()
                            : corners.OrderBy(c => Vector3.Distance(c, me) < 1 ? float.MaxValue : Vector3.Distance(c, me)).First();
                        break;
                    }
                case MotionPlace.between:
                    {
                        if (anchor == null && !targetIsUser || other == null && !otherIsUser) { result.error = "どことどこの間か分かりませんでした。"; return false; }
                        Vector3 a = targetIsUser ? you : Flat(anchor.bounds.center), b = otherIsUser ? you : Flat(other.bounds.center);
                        // Between the facing edges, not the centres, so a big sofa does not pull the spot inside itself.
                        if (anchor != null) a = Flat(anchor.bounds.ClosestPoint(new Vector3(b.x, anchor.bounds.center.y, b.z)));
                        if (other != null) b = Flat(other.bounds.ClosestPoint(new Vector3(a.x, other.bounds.center.y, a.z)));
                        ideal = (a + b) / 2; break;
                    }
                case MotionPlace.front: case MotionPlace.behind: case MotionPlace.left: case MotionPlace.right: case MotionPlace.beside:
                    if (anchor == null)
                    {
                        // The user's own sides.
                        Vector3 forward = Unit(userForward, Vector3.forward), right = Vector3.Cross(Vector3.up, forward);
                        Vector3 side = Vector3.Dot(me - you, right) >= 0 ? right : -right;
                        Vector3 direction = place == MotionPlace.front ? forward : place == MotionPlace.behind ? -forward : place == MotionPlace.left ? -right : place == MotionPlace.right ? right : side;
                        ideal = you + direction * (distance > 0 ? Mathf.Max(UserGap + .05f, distance) : UserDistance);
                    }
                    else
                    {
                        // Sides of a thing as the user sees it: its front faces the user, its back is the far side.
                        Vector3 centre = Flat(anchor.bounds.center), view = Unit(centre - you, userForward), right = Vector3.Cross(Vector3.up, view);
                        Vector3 side = Vector3.Dot(me - centre, right) >= 0 ? right : -right;
                        Vector3 direction = place == MotionPlace.front ? -view : place == MotionPlace.behind ? view : place == MotionPlace.left ? -right : place == MotionPlace.right ? right : side;
                        float edge = Mathf.Abs(direction.x) * anchor.bounds.extents.x + Mathf.Abs(direction.z) * anchor.bounds.extents.z;
                        ideal = centre + direction * (edge + world.grid.radius + Clearance + distance);
                    }
                    break;
                default: result.error = "行き先が分かりませんでした。"; return false;
            }
            // The free cell nearest to the described point, never on top of the user.
            float best = float.MaxValue; bool found = false;
            foreach (var cell in world.grid.FreeCells())
            {
                if (Vector3.Distance(Flat(cell), you) < UserGap) continue;
                float miss = Vector3.Distance(Flat(cell), ideal);
                if (miss < best) { best = miss; result.spot = cell; found = true; }
            }
            if (!found || best > MaxMiss) { result.error = result.name + "には立てる場所がありません。"; return false; }
            return true;
        }
    }
}
