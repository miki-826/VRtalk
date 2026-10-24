using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace QuestAI.Tests
{
    public class SceneAwarenessTests
    {
        static SceneObject Part(string label, Vector3 center, Vector3 size) => new SceneObject { label = label, bounds = new Bounds(center, size) };
        static Bounds Floor => new Bounds(new Vector3(0, 0, 0), new Vector3(8, 0, 7));
        static SceneAwareness Room() => SceneAwareness.Build(new[] {
            Part("ソファ", new Vector3(2.5f, .4f, .5f), new Vector3(.9f, .8f, 2.4f)),
            Part("窓", new Vector3(-3.9f, 1.9f, -2), new Vector3(.1f, 2.4f, 1.6f)), Part("窓", new Vector3(-3.9f, 1.9f, -.3f), new Vector3(.1f, 2.4f, 1.6f)),
            Part("椅子", new Vector3(-2, .4f, 2), new Vector3(.5f, .8f, .5f)), Part("椅子", new Vector3(-1, .4f, 2), new Vector3(.5f, .8f, .5f)),
            Part("ラグ", new Vector3(0, .01f, 0), new Vector3(3, .02f, 3.5f)) }, Floor);

        [TestCase("Realistic loungeSofa", "ソファ")][TestCase("Realistic lampRoundTable", "ランプ")][TestCase("Realistic cabinetBedDrawer", "ナイトテーブル")]
        [TestCase("Fire bed", "暖炉")][TestCase("Reflective window pane", "窓")][TestCase("Window sunlight", null)][TestCase("Distant pine", null)]
        [TestCase("Realistic floor plant", "植物")][TestCase("Counter_Body", "カウンター")][TestCase("Back wall", null)]
        public void NamesMapToJapaneseLabels(string name, string label) => Assert.That(SceneAwareness.Label(name), Is.EqualTo(label));

        [Test] public void TouchingPiecesMergeAndSeparateOnesGetIds()
        {
            var room = Room();
            Assert.That(room.objects.Count(o => o.label == "窓"), Is.EqualTo(1));
            Assert.That(room.objects.Where(o => o.label == "椅子").Select(o => o.id), Is.EquivalentTo(new[] { "椅子1", "椅子2" }));
            Assert.False(room.Find("ラグ").obstacle); Assert.True(room.Find("ソファ").obstacle); Assert.False(room.Find("窓").obstacle);
        }
        [Test] public void PathGoesAroundFurnitureAndNeverThroughIt()
        {
            var room = Room();
            var path = room.grid.FindPath(new Vector3(1.2f, 0, .5f), new Vector3(3.5f, 0, .5f));
            Assert.That(path, Is.Not.Null.And.Not.Empty);
            var points = new[] { new Vector3(1.2f, 0, .5f) }.Concat(path).ToArray();
            for (int i = 1; i < points.Length; i++) for (int k = 0; k <= 20; k++)
                Assert.False(room.Find("ソファ").bounds.Contains(new Vector3(0, .4f, 0) + Vector3.Lerp(points[i - 1], points[i], k / 20f)), "walked through the sofa");
            Assert.That(Vector3.Distance(path.Last(), room.grid.Snap(new Vector3(3.5f, 0, .5f))), Is.LessThan(.01f));
        }
        [Test] public void PathAvoidsTheUserSpot()
        {
            var room = Room(); var user = new Vector3(0, 1.5f, -1);
            var path = room.grid.FindPath(new Vector3(0, 0, .8f), new Vector3(0, 0, -2.6f), user);
            Assert.That(path, Is.Not.Null);
            foreach (var p in path.Take(path.Count - 1)) Assert.That(new Vector2(p.x - user.x, p.z - user.z).magnitude, Is.GreaterThan(.4f));
        }
        [Test] public void ApproachSpotIsFreeAndBesideTarget()
        {
            var room = Room(); var sofa = room.Find("ソファ");
            Assert.True(room.Approach(sofa, Vector3.zero, .3f, out var spot));
            Assert.False(room.grid.Blocked(spot)); Assert.That(SceneAwareness.FlatDistance(sofa, spot), Is.InRange(.3f, .9f));
        }
        [Test] public void DescriptionIsCharacterRelative()
        {
            var room = Room();
            // Character at the centre facing the user (-z): +x is on its left.
            string text = room.Describe("リビング", new Vector3(0, 0, .6f), Vector3.back, new Vector3(0, 1.5f, -1.8f), Vector3.forward);
            StringAssert.Contains("ソファ(左", text); StringAssert.Contains("ユーザーは正面", text);
            Assert.That(SceneAwareness.Direction(Vector3.zero, Vector3.forward, new Vector3(1, 0, 0)), Does.StartWith("右"));
        }
        [Test] public void LookedAtPicksObjectInViewCone()
        {
            var room = Room();
            Assert.That(room.LookedAt(new Vector3(0, .5f, .5f), Vector3.right)?.id, Is.EqualTo("ソファ"));
            Assert.That(room.LookedAt(new Vector3(0, .5f, .5f), Vector3.down), Is.Null);
        }
        static JObject Answer(string movement, string target) => JObject.Parse("{\"answers\":{\"emotion\":{\"type\":\"choice\",\"choice\":\"curious\"},\"gesture\":{\"type\":\"choice\",\"choice\":\"none\"},\"gaze\":{\"type\":\"choice\",\"choice\":\"object\"},\"conversation\":{\"type\":\"choice\",\"choice\":\"speak\"},\"movement\":{\"type\":\"choice\",\"choice\":\"" + movement + "\"},\"target\":{\"type\":\"choice\",\"choice\":\"" + target + "\"}}}");
        [Test] public void JevMovementTargetsMustBeRealPlaces()
        {
            var places = new[] { "窓", "ソファ" };
            var go = JevBehaviorProvider.Parse(Answer("go_to_object", "窓"), places);
            Assert.That(go.movement, Is.EqualTo(Movement.go_to_object)); Assert.That(go.target, Is.EqualTo("窓"));
            var unknown = JevBehaviorProvider.Parse(Answer("go_to_object", "冷蔵庫"), places);
            Assert.That(unknown.movement, Is.EqualTo(Movement.stay)); Assert.That(unknown.target, Is.Null);
            Assert.Throws<InvalidOperationException>(() => JevBehaviorProvider.Parse(Answer("teleport", "none"), places));
        }
        static JObject Full(string extra) => JObject.Parse("{\"answers\":{\"emotion\":{\"type\":\"choice\",\"choice\":\"happy\"},\"gesture\":{\"type\":\"choice\",\"choice\":\"none\"},\"gaze\":{\"type\":\"choice\",\"choice\":\"user\"},\"conversation\":{\"type\":\"choice\",\"choice\":\"speak\"}" + extra + "}}");
        static string Choice(string key, string value) => ",\"" + key + "\":{\"type\":\"choice\",\"choice\":\"" + value + "\"}";
        [Test] public void JevStyleFieldsDefaultAndValidate()
        {
            var plain = JevBehaviorProvider.Parse(Full(""), new[] { "ソファ" });
            Assert.That((plain.pace, plain.approach, plain.arrival, plain.walking), Is.EqualTo((Pace.normal, ApproachStyle.direct, ArrivalAction.none, WalkingReaction.@continue)));
            var styled = JevBehaviorProvider.Parse(Full(Choice("movement", "go_to_object") + Choice("target", "ソファ") + Choice("pace", "hurry") + Choice("approach", "around") + Choice("arrival", "sit") + Choice("walking", "turn_to_user")), new[] { "ソファ" });
            Assert.That((styled.pace, styled.approach, styled.arrival, styled.walking), Is.EqualTo((Pace.hurry, ApproachStyle.around, ArrivalAction.sit, WalkingReaction.turn_to_user)));
            Assert.That(JevBehaviorProvider.Parse(Full(Choice("arrival", "point") + Choice("target", "none")), new[] { "ソファ" }).arrival, Is.EqualTo(ArrivalAction.none), "arrival needs a real target");
            Assert.Throws<InvalidOperationException>(() => JevBehaviorProvider.Parse(Full(Choice("pace", "teleport")), new[] { "ソファ" }));
        }
        static readonly Vector3 Head = new Vector3(0, 1.6f, 0);
        [Test] public void CrouchAndStandUpAreDetectedFromHeadHeight()
        {
            var d = new GestureDetector(); var seen = new System.Collections.Generic.List<UserGesture>();
            for (float t = 0; t < 3; t += .05f) { float h = t < 1 ? 1.6f : t < 2 ? 1.0f : 1.6f; var g = d.Step(t, h, new Vector3(0, h, 0), Vector3.right, null, null); if (g != UserGesture.none) seen.Add(g); }
            Assert.That(seen, Is.EqualTo(new[] { UserGesture.crouch, UserGesture.stand_up }));
        }
        [Test] public void RaisedSideToSideHandIsAWaveButLoweredHandIsNot()
        {
            UserGesture Run(float handY) { var d = new GestureDetector(); for (float t = 0; t < 2.5f; t += .05f) { var hand = new Ray(new Vector3(Mathf.Sin(t * 12) * .18f, handY, .3f), Vector3.forward); var g = d.Step(t, 1.6f, Head, Vector3.right, hand, null); if (g != UserGesture.none) return g; } return UserGesture.none; }
            Assert.That(Run(1.75f), Is.EqualTo(UserGesture.wave)); Assert.That(Run(1.0f), Is.EqualTo(UserGesture.none));
        }
        [Test] public void SteadyExtendedArmPointsAtObject()
        {
            var room = Room(); var d = new GestureDetector(); var hand = new Ray(new Vector3(.5f, 1.3f, .5f), (room.Find("ソファ").bounds.center - new Vector3(.5f, 1.3f, .5f)).normalized);
            UserGesture result = UserGesture.none; float when = -1;
            for (float t = 0; t < 2 && result == UserGesture.none; t += .05f) { result = d.Step(t, 1.6f, Head, Vector3.right, hand, room); when = t; }
            Assert.That(result, Is.EqualTo(UserGesture.point)); Assert.That(d.PointingAt, Is.EqualTo("ソファ")); Assert.That(when, Is.GreaterThan(.75f), "needs a steady hold");
            var desk = new GestureDetector(); Assert.That(desk.Step(0, 1.6f, Head, Vector3.right, new Ray(Head, (room.Find("ソファ").bounds.center - Head).normalized), room, true), Is.EqualTo(UserGesture.point), "PC key points at once");
        }
    }
}
