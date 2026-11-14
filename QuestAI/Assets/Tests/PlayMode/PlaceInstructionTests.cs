using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace QuestAI.Tests
{
    // Places said in words, the looked-at / pointed-at floor spot, the time of day and the user's own teleport, in a real room.
    public class PlaceInstructionTests
    {
        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
        static float FlatDistance(Vector3 a, Vector3 b) => Vector3.Distance(Flat(a), Flat(b));
        static IEnumerator Boot()
        {
            AppRoot.EditorRepositoryFactory = () => new LocalRepository();
            yield return SceneManager.LoadSceneAsync("Bootstrap");
            float deadline = Time.realtimeSinceStartup + 30;
            while ((!AppRoot.Instance || !AppRoot.Instance.UI || AppRoot.Instance.IsChanging) && Time.realtimeSinceStartup < deadline) yield return null;
        }
        static IEnumerator Start(AppRoot app, string character, string environment)
        {
            app.Settings.characterId = character; app.Settings.environmentId = environment; app.Settings.autonomousMovement = false;
            app.StartSession(false); float deadline = Time.realtimeSinceStartup + 30;
            while ((app.IsChanging || !app.Brain || app.Brain.profile.id != character) && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null; Assert.NotNull(app.Brain);
        }
        static IEnumerator Done(MotionPerformer performer, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (performer.Busy && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.False(performer.Busy, "timed out during " + performer.Current?.action);
        }
        static bool MarkerShown => Object.FindObjectsByType<FloorMarker>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Any(m => m.name == "Destination marker");
        // A free floor cell the head can see directly, away from both the character and the user.
        static Vector3 OpenSpot(CharacterBrain brain, Transform head, Vector3? not = null)
        {
            var world = brain.world;
            return world.grid.FreeCells().Where(c => FlatDistance(c, brain.transform.position) > 1.4f && FlatDistance(c, head.position) > 1.2f && FlatDistance(c, head.position) < 4
                && (!not.HasValue || FlatDistance(c, not.Value) > 1.2f) && world.CanStand(c)
                && world.FloorPoint(new Ray(head.position, (c - head.position).normalized), out var seen) && FlatDistance(seen, c) < .05f)
                .OrderBy(c => FlatDistance(c, brain.transform.position)).First();
        }

        [UnityTest] public IEnumerator SpokenPlacesGazeAndPointingSendTheCharacterToThatSpot()
        {
            yield return Boot(); var app = AppRoot.Instance; Time.timeScale = 4;
            try
            {
                yield return Start(app, "calm", "LivingRoom");
                var brain = app.Brain; var world = brain.world; var performer = brain.performer; var head = app.Head;

                // 「部屋の真ん中」: the free spot nearest the middle of the floor.
                Assert.True(brain.PerformLocalMotion("部屋の真ん中に行って")); Assert.True(performer.Busy);
                yield return null; Assert.True(MarkerShown, "a ring marks where it is heading");
                yield return Done(performer, 40);
                Assert.That(FlatDistance(brain.transform.position, world.floor.center), Is.LessThan(PlaceResolver.MaxMiss + .3f), "room centre");
                Assert.False(world.grid.Blocked(brain.transform.position)); Assert.False(MarkerShown, "the ring goes away on arrival");

                // 「私の右」: beside the user on their right, not in front or behind.
                Assert.True(brain.PerformLocalMotion("私の右に来て")); yield return Done(performer, 40);
                var offset = Flat(brain.transform.position - head.position);
                Assert.That(Vector3.Dot(offset, Flat(head.right).normalized), Is.InRange(.45f, 1.7f), "to the user's right");
                Assert.That(Mathf.Abs(Vector3.Dot(offset, Flat(head.forward).normalized)), Is.LessThan(.8f), "level with the user");
                yield return new WaitForSeconds(1.5f);
                Assert.That(Vector3.Angle(brain.transform.forward, Flat(head.position - brain.transform.position)), Is.LessThan(40), "turns to the user after arriving");

                // 「暖炉の前」: next to the hearth, on the side the user sees.
                var hearth = world.Find("暖炉"); Assert.NotNull(hearth);
                Assert.True(brain.PerformLocalMotion("暖炉の前に行って")); yield return Done(performer, 40);
                Assert.That(SceneAwareness.FlatDistance(hearth, brain.transform.position), Is.LessThan(1.4f), "beside the hearth");
                Assert.That(FlatDistance(brain.transform.position, head.position), Is.LessThan(FlatDistance(hearth.bounds.center, head.position)), "on the user's side of the hearth");
                foreach (var o in world.objects.Where(o => o.obstacle)) Assert.False(o.bounds.Contains(new Vector3(brain.transform.position.x, o.bounds.center.y, brain.transform.position.z)), "stands inside " + o.id);

                // 「あそこ」 with nothing pointed at: the floor the user is looking at.
                var looked = OpenSpot(brain, head); var facing = head.rotation;
                head.rotation = Quaternion.LookRotation(looked - head.position); yield return null;
                Assert.False(brain.PointedRecently);
                Assert.True(brain.PerformLocalMotion("あそこに行って")); yield return Done(performer, 40);
                Assert.That(FlatDistance(brain.transform.position, looked), Is.LessThan(.35f), "the looked-at floor spot");
                head.rotation = facing; yield return null;

                // A deliberate point at open floor (right grip / F key) is an instruction by itself.
                var pointedAt = OpenSpot(brain, head, looked);
                app.Body.Pointer(new Ray(head.position, (pointedAt - head.position).normalized), true);
                float deadline = Time.realtimeSinceStartup + 3; while (!performer.Busy && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.True(brain.tracker.PointingSpot.HasValue, "the floor spot is remembered"); Assert.Null(brain.tracker.PointingAt);
                Assert.True(performer.Busy, "walks to the pointed spot");
                yield return Done(performer, 40);
                Assert.That(FlatDistance(brain.transform.position, pointedAt), Is.LessThan(.35f), "the pointed floor spot");
                // Once reached, the spot is used up: 「あそこ」 means where the user looks again.
                Assert.False(brain.PointedRecently); Assert.False(brain.tracker.PointingSpot.HasValue);
                // While a spot is being pointed at, 「ここ」 means that spot instead of the user's side.
                var parsed = LocalMotionParser.Parse("ここに来て", new MotionRequest { pointing = true });
                Assert.That(parsed.plan.steps[0].place, Is.EqualTo(MotionPlace.pointed));

                // Stopping forgets the plan, so nothing that was waiting to continue it can start it again.
                Assert.True(brain.PerformLocalMotion("部屋の奥に行って")); int running = performer.Version; yield return null;
                performer.Stop(); Assert.False(performer.Busy); Assert.Null(performer.Last); Assert.That(performer.Version, Is.Not.EqualTo(running)); Assert.False(MarkerShown);

                // Words alone also change the room.
                Assert.True(brain.PerformLocalMotion("夜にして")); yield return Done(performer, 10);
                Assert.That(app.Settings.timeMode, Is.EqualTo(TimeMode.Night));

                // Typed instructions run without a voice connection or key; what is not understood is said so.
                string status = null; brain.StatusChanged += s => status = s;
                brain.Instruct("部屋の隅に行って手を振って"); Assert.True(performer.Busy); Assert.False(brain.Connected);
                Assert.That(performer.Last.steps.Select(s => s.action), Is.EqualTo(new[] { MotionAction.walk_to, MotionAction.gesture }));
                yield return Done(performer, 60);
                brain.Instruct("ふんわりしたことをお願い"); yield return null;
                Assert.False(performer.Busy); StringAssert.Contains("分かりませんでした", status);
                yield return ApplicationTests.SaveImage(head.GetComponent<Camera>(), "Place-instruction");
            }
            finally { Time.timeScale = 1; }
        }

        [UnityTest] public IEnumerator UserTeleportsOnlyOntoFreeFloorAndWalkingSlidesAlongObstacles()
        {
            yield return Boot(); var app = AppRoot.Instance;
            yield return Start(app, "calm", "LivingRoom");
            var brain = app.Brain; var world = brain.world; var head = app.Head; var input = app.GetComponent<XRPointerInput>();
            Vector3 character = brain.transform.position; float height = head.position.y;
            var free = OpenSpot(brain, head);
            Assert.True(XRPointerInput.CanUserStand(world, free, character));
            input.TeleportTo(free, head);
            Assert.That(FlatDistance(head.position, free), Is.LessThan(.01f), "the viewpoint is above the chosen spot"); Assert.That(head.position.y, Is.EqualTo(height).Within(.001f));
            // Not onto the character, into furniture or out of the room.
            Assert.False(XRPointerInput.CanUserStand(world, character, character));
            var sofa = world.objects.First(o => o.obstacle);
            Assert.False(XRPointerInput.CanUserStand(world, sofa.bounds.center, character), "inside " + sofa.id);
            Assert.False(XRPointerInput.CanUserStand(world, world.floor.max + Vector3.one, character));
            // Walking: a step into furniture is refused, a step out of it (room-scale users) is always allowed.
            Vector3 edge = new Vector3(sofa.bounds.min.x - .05f, 0, sofa.bounds.center.z), inside = new Vector3(sofa.bounds.min.x + .05f, 0, sofa.bounds.center.z);
            if (world.CanStand(edge)) Assert.False(XRPointerInput.CanUserStep(world, edge, inside, character));
            Assert.True(XRPointerInput.CanUserStep(world, inside, edge, character));
            // Next to the character: cannot step closer, can always step away.
            Vector3 near = character + Vector3.right * .4f;
            Assert.False(XRPointerInput.CanUserStep(world, near, character + Vector3.right * .3f, character));
            Assert.True(XRPointerInput.CanUserStep(world, near, character + Vector3.right * .45f, character));
        }
        [UnityTearDown] public IEnumerator Cleanup() { Time.timeScale = 1; if (AppRoot.Instance) Object.Destroy(AppRoot.Instance.gameObject); AppRoot.EditorRepositoryFactory = null; yield return null; }
    }
}
