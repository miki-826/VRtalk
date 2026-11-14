using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace QuestAI.Tests
{
    public class PlaceTests
    {
        static readonly Vector3 User = new Vector3(0, 1.6f, -2.5f);
        static SceneObject Part(string label, Vector3 center, Vector3 size) => new SceneObject { label = label, bounds = new Bounds(center, size) };
        static SceneAwareness Room() => SceneAwareness.Build(new[] {
            Part("ソファ", new Vector3(2.5f, .4f, .5f), new Vector3(.9f, .8f, 2.4f)),
            Part("窓", new Vector3(-3.9f, 1.9f, -2), new Vector3(.1f, 2.4f, 1.6f)),
            Part("窓", new Vector3(-3.9f, 1.9f, -.3f), new Vector3(.1f, 2.4f, 1.6f)),
            Part("椅子", new Vector3(-2, .4f, 2), new Vector3(.5f, .8f, .5f)),
            Part("椅子", new Vector3(-1, .4f, 2), new Vector3(.5f, .8f, .5f)),
            Part("ラグ", new Vector3(0, .01f, 0), new Vector3(3, .02f, 3.5f))
        }, new Bounds(Vector3.zero, new Vector3(8, 0, 7)));

        static PlaceResolver.Result Resolve(SceneAwareness room, MotionPlace place, string target = "", string toward = "", float distance = 0,
            Vector3? self = null, Vector3? user = null, Vector3? pointed = null, Vector3? forward = null)
        {
            Assert.True(PlaceResolver.Resolve(room, place, target, toward, distance, self ?? Vector3.zero,
                user ?? User, forward ?? Vector3.forward, pointed, out var result), result.error);
            Assert.False(room.grid.Blocked(result.spot));
            return result;
        }

        static float FlatDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        [Test] public void CenterIsAFreeCellNearTheFloorCenter()
        {
            var room = Room(); var result = Resolve(room, MotionPlace.center);
            Assert.That(FlatDistance(result.spot, room.floor.center), Is.LessThan(.3f));
            Assert.That(result.name, Is.EqualTo("部屋の真ん中"));
        }

        [Test] public void ObjectSidesAreRelativeToTheUser()
        {
            var room = Room(); var sofa = room.Find("ソファ").bounds; var user = new Vector3(-1, 1.6f, .5f);
            var behind = Resolve(room, MotionPlace.behind, "ソファ", user: user, forward: Vector3.right).spot;
            var front = Resolve(room, MotionPlace.front, "ソファ", user: user, forward: Vector3.right).spot;
            var left = Resolve(room, MotionPlace.left, "ソファ", user: user, forward: Vector3.right).spot;
            var right = Resolve(room, MotionPlace.right, "ソファ", user: user, forward: Vector3.right).spot;
            Assert.That(behind.x, Is.GreaterThan(sofa.max.x));
            Assert.False(sofa.Contains(new Vector3(behind.x, sofa.center.y, behind.z)));
            Assert.That(front.x, Is.LessThan(sofa.min.x));
            Assert.That(left.z, Is.GreaterThan(sofa.max.z));
            Assert.That(right.z, Is.LessThan(sofa.min.z));
        }

        [Test] public void UserSidesRespectDirectionAndDistance()
        {
            // Far enough from the back wall that a metre behind the user is still walkable floor.
            var room = Room(); var User = new Vector3(0, 1.6f, -1.5f);
            var right = Resolve(room, MotionPlace.right, "user", user: User).spot;
            var behind = Resolve(room, MotionPlace.behind, "user", user: User).spot;
            var front = Resolve(room, MotionPlace.front, "user", user: User).spot;
            var farRight = Resolve(room, MotionPlace.right, "user", distance: 2, user: User).spot;
            Assert.That(right.x, Is.GreaterThan(User.x));
            Assert.That(behind.z, Is.LessThan(User.z));
            Assert.That(front.z, Is.GreaterThan(User.z));
            Assert.That(FlatDistance(right, User), Is.EqualTo(PlaceResolver.UserDistance).Within(.3f));
            Assert.That(FlatDistance(behind, User), Is.EqualTo(PlaceResolver.UserDistance).Within(.3f));
            Assert.That(FlatDistance(front, User), Is.EqualTo(PlaceResolver.UserDistance).Within(.3f));
            Assert.That(FlatDistance(farRight, User), Is.EqualTo(2).Within(.3f));
        }

        [Test] public void BesideChoosesTheSideCloserToTheCharacter()
        {
            var room = Room(); var sofa = room.Find("ソファ").bounds;
            var left = Resolve(room, MotionPlace.beside, "ソファ", self: new Vector3(2.5f, 0, 3)).spot;
            var right = Resolve(room, MotionPlace.beside, "ソファ", self: new Vector3(2.5f, 0, -2)).spot;
            var view = sofa.center - User; view.y = 0; view.Normalize();
            var userRight = Vector3.Cross(Vector3.up, view);
            Assert.That(Vector3.Dot(left - sofa.center, userRight), Is.LessThan(0));
            Assert.That(Vector3.Dot(right - sofa.center, userRight), Is.GreaterThan(0));
        }

        [Test] public void BetweenUsesTheMidpointOfFacingEdges()
        {
            var room = Room(); var a = room.Find("ソファ").bounds; var b = room.Find("椅子2").bounds;
            var first = a.ClosestPoint(new Vector3(b.center.x, a.center.y, b.center.z));
            var second = b.ClosestPoint(new Vector3(first.x, b.center.y, first.z));
            var midpoint = (first + second) / 2;
            var spot = Resolve(room, MotionPlace.between, "ソファ", "椅子2").spot;
            Assert.That(FlatDistance(spot, midpoint), Is.LessThan(.4f));
        }

        [Test] public void BetweenRequiresASecondAnchor()
        {
            Assert.False(PlaceResolver.Resolve(Room(), MotionPlace.between, "ソファ", "", 0, Vector3.zero, User,
                Vector3.forward, null, out var result));
            Assert.That(result.error, Is.Not.Null.And.Not.Empty);
        }

        [Test] public void CornerIsNearAnInsetFloorCornerAndCanFollowAnObject()
        {
            var room = Room(); float x = room.floor.extents.x - PlaceResolver.CornerInset, z = room.floor.extents.z - PlaceResolver.CornerInset;
            var corners = new[] { new Vector3(x, 0, z), new Vector3(-x, 0, z), new Vector3(x, 0, -z), new Vector3(-x, 0, -z) };
            var spot = Resolve(room, MotionPlace.corner).spot;
            Assert.That(corners.Min(c => FlatDistance(c, spot)), Is.LessThan(1.3f));
            var nearSofa = Resolve(room, MotionPlace.corner, "ソファ").spot;
            var nearest = corners.OrderBy(c => FlatDistance(c, room.Find("ソファ").bounds.center)).First();
            Assert.That(FlatDistance(nearSofa, nearest), Is.LessThan(1.3f));
        }

        [Test] public void FarSideIsAcrossTheRoomFromTheUser()
        {
            var room = Room(); var spot = Resolve(room, MotionPlace.far_side).spot;
            Assert.That(spot.z, Is.GreaterThan(1.5f));
            Assert.That(FlatDistance(spot, User), Is.GreaterThan(FlatDistance(room.floor.center, User)));
        }

        [Test] public void PointedSpotSnapsToAFreeCellAndKeepsUserGap()
        {
            var room = Room(); var pointed = new Vector3(.7f, 0, .7f);
            var spot = Resolve(room, MotionPlace.pointed, pointed: pointed).spot;
            Assert.That(FlatDistance(spot, pointed), Is.LessThan(.3f));
            var atFeet = Resolve(room, MotionPlace.pointed, pointed: User).spot;
            Assert.That(FlatDistance(atFeet, User), Is.GreaterThanOrEqualTo(PlaceResolver.UserGap));
        }

        [Test] public void PointedWithoutSpotReportsAnError()
        {
            Assert.False(PlaceResolver.Resolve(Room(), MotionPlace.pointed, "", "", 0, Vector3.zero, User,
                Vector3.forward, null, out var result));
            Assert.That(result.error, Is.Not.Null.And.Not.Empty);
        }

        [Test] public void UnknownTargetIdIsNamedInTheError()
        {
            const string id = "冷蔵庫";
            Assert.False(PlaceResolver.Resolve(Room(), MotionPlace.behind, id, "", 0, Vector3.zero, User,
                Vector3.forward, null, out var result));
            StringAssert.Contains(id, result.error);
        }

        [Test] public void NoStandingRoomBeyondMaxMissReportsAnError()
        {
            var room = SceneAwareness.Build(new[] {
                Part("大きな棚", new Vector3(0, .5f, 2), new Vector3(8, 1, 3))
            }, new Bounds(Vector3.zero, new Vector3(8, 0, 7)));
            Assert.False(PlaceResolver.Resolve(room, MotionPlace.behind, "大きな棚", "", 0, Vector3.zero, User,
                Vector3.forward, null, out var result));
            Assert.That(result.error, Is.Not.Null.And.Not.Empty);
        }

        [Test] public void NamesDescribeObjectUserBetweenAndPointedPlaces()
        {
            Assert.That(PlaceResolver.Name(MotionPlace.front, "ソファ", ""), Is.EqualTo("ソファの前"));
            Assert.That(PlaceResolver.Name(MotionPlace.right, "user", ""), Is.EqualTo("あなたの右"));
            Assert.That(PlaceResolver.Name(MotionPlace.between, "テーブル", "窓"), Is.EqualTo("テーブルと窓の間"));
            Assert.That(PlaceResolver.Name(MotionPlace.pointed, "", ""), Is.EqualTo("指さした場所"));
        }

        [Test] public void FloorPointAcceptsOnlyUnobstructedHitsInsideTheFloor()
        {
            var room = Room(); var origin = new Vector3(0, 1.5f, -2);
            var open = new Ray(origin, new Vector3(0, -1, 1).normalized);
            Assert.True(room.FloorPoint(open, out var point));
            Assert.That(Vector3.Distance(point, new Vector3(0, room.floor.max.y, -.5f)), Is.LessThan(.001f));
            Assert.False(room.FloorPoint(new Ray(origin, Vector3.up), out _));
            Assert.False(room.FloorPoint(new Ray(origin, new Vector3(0, -1, 8).normalized), out _));
            var sofa = room.Find("ソファ").bounds;
            var throughSofa = new Ray(new Vector3(sofa.center.x, 1.5f, sofa.center.z), Vector3.down);
            Assert.False(room.FloorPoint(throughSofa, out _));
            Assert.True(room.FloorPoint(new Ray(new Vector3(0, 1.5f, 0), Vector3.down), out var rugPoint));
            Assert.That(rugPoint.y, Is.EqualTo(room.floor.max.y).Within(.001f));
        }

        [Test] public void RayHitReturnsNearestSolidObjectAndIgnoresFlatRug()
        {
            var room = Room(); var sofa = room.Find("ソファ").bounds;
            var ray = new Ray(new Vector3(sofa.center.x, 1.5f, sofa.center.z), Vector3.down);
            Assert.That(room.RayHit(ray, out var distance)?.id, Is.EqualTo("ソファ"));
            Assert.That(distance, Is.GreaterThan(0));
            Assert.That(room.RayHit(new Ray(new Vector3(0, 1.5f, 0), Vector3.down), out _), Is.Null);
            var layered = SceneAwareness.Build(new[] {
                Part("近い箱", new Vector3(0, .4f, 0), new Vector3(.5f, .8f, .5f)),
                Part("遠い棚", new Vector3(0, .6f, 1.2f), new Vector3(.5f, 1.2f, .5f)),
                Part("壁の棚", new Vector3(0, 1.1f, -.6f), new Vector3(.5f, .3f, .5f)),
                Part("窓", new Vector3(0, 1, -.9f), new Vector3(3, 1.6f, .1f))
            }, new Bounds(Vector3.zero, new Vector3(4, 0, 4)));
            // Along the floor from behind the window and the wall shelf: neither is in the way, the nearer box is hit first.
            var along = new Ray(new Vector3(0, .5f, -1.5f), Vector3.forward);
            Assert.That(layered.RayHit(along, out float reach)?.id, Is.EqualTo("近い箱")); Assert.That(reach, Is.EqualTo(1.25f).Within(.01f));
            // Standing inside a thing's bounds does not block every ray.
            Assert.That(layered.RayHit(new Ray(new Vector3(0, .4f, 0), Vector3.forward), out _)?.id, Is.EqualTo("遠い棚"));
            // A window or a shelf on the wall never hides the floor behind it.
            Assert.True(layered.FloorPoint(new Ray(new Vector3(0, 1.5f, -1.6f), new Vector3(0, -1.5f, 1).normalized), out var floorPoint));
            Assert.That(floorPoint.z, Is.EqualTo(-.6f).Within(.01f));
        }
        [Test] public void FarSideFromTheMiddleOfTheRoomIsWhereTheUserFaces()
        {
            // Just past the centre, facing +x: the far side is still ahead, not the wall behind the user.
            var room = Room(); var user = new Vector3(.1f, 1.6f, .1f);
            var spot = Resolve(room, MotionPlace.far_side, user: user, forward: Vector3.right, self: new Vector3(-2, 0, -2)).spot;
            Assert.That(spot.x, Is.GreaterThan(1.5f));
            // From one end of the room it is the other end, whichever way the user faces.
            var fromEnd = Resolve(room, MotionPlace.far_side, user: new Vector3(-3, 1.6f, 0), forward: Vector3.left).spot;
            Assert.That(fromEnd.x, Is.GreaterThan(1.5f));
        }

        [Test] public void CanStandRejectsWallsAndFurnitureButAllowsRug()
        {
            var room = Room();
            Assert.True(room.CanStand(new Vector3(0, 0, -2)));
            Assert.False(room.CanStand(new Vector3(3.8f, 0, 0)));
            Assert.False(room.CanStand(room.Find("ソファ").bounds.center));
            Assert.True(room.CanStand(Vector3.zero));
        }

        [Test] public void DeliberateFloorPointRecordsSpotNotObject()
        {
            var room = Room(); var detector = new GestureDetector(); var head = new Vector3(0, 1.6f, -2);
            var ray = new Ray(head, new Vector3(0, -1, 1).normalized);
            Assert.That(detector.Step(0, 1.6f, head, Vector3.right, ray, room, deliberatePoint: true), Is.EqualTo(UserGesture.point));
            Assert.True(detector.PointingSpot.HasValue);
            Assert.That(Vector3.Distance(detector.PointingSpot.Value, new Vector3(0, 0, -.4f)), Is.LessThan(.05f));
            Assert.That(detector.PointingAt, Is.Null);
        }

        [Test] public void DeliberateObjectPointRecordsObjectNotSpot()
        {
            var room = Room(); var detector = new GestureDetector(); var head = new Vector3(0, 1.6f, -2);
            var sofa = room.Find("ソファ"); var ray = new Ray(head, (sofa.bounds.center - head).normalized);
            Assert.That(detector.Step(0, 1.6f, head, Vector3.right, ray, room, deliberatePoint: true), Is.EqualTo(UserGesture.point));
            Assert.That(detector.PointingAt, Is.EqualTo("ソファ"));
            Assert.That(detector.PointingSpot, Is.Null);
        }

        [Test] public void OrdinaryFloorRayDoesNotRecordAPointingSpot()
        {
            var room = Room(); var detector = new GestureDetector(); var head = new Vector3(0, 1.6f, -2);
            var ray = new Ray(head, new Vector3(0, -1, 1).normalized);
            for (float t = 0; t < 1.5f; t += .05f) detector.Step(t, 1.6f, head, Vector3.right, ray, room);
            Assert.That(detector.PointingSpot, Is.Null);
        }
    }
}
