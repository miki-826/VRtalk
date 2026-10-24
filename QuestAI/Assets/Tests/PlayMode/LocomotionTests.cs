using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace QuestAI.Tests
{
    public class LocomotionTests
    {
        static IEnumerator Start(AppRoot app, string character, string environment)
        {
            float ready = Time.realtimeSinceStartup + 20; while (app.IsChanging && Time.realtimeSinceStartup < ready) yield return null;
            app.Settings.characterId = character; app.Settings.environmentId = environment; app.Settings.autonomousMovement = false;
            app.StartSession(false); float deadline = Time.realtimeSinceStartup + 20;
            while ((app.IsChanging || !app.Brain || app.Brain.profile.id != character || app.Settings.environmentId != environment) && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
        }
        static IEnumerator WaitWhileMoving(AppRoot app, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (app.Brain.locomotion.Moving && Time.realtimeSinceStartup < deadline) yield return null;
        }
        [UnityTest] public IEnumerator CharactersWalkToRoomObjectsWithoutEnteringFurniture()
        {
            AppRoot.EditorRepositoryFactory = () => new LocalRepository();
            yield return SceneManager.LoadSceneAsync("Bootstrap");
            float deadline = Time.realtimeSinceStartup + 20;
            while ((!AppRoot.Instance || !AppRoot.Instance.UI || !SceneManager.GetSceneByName("MainMenu").isLoaded) && Time.realtimeSinceStartup < deadline) yield return null;
            var app = AppRoot.Instance; Assert.NotNull(app);
            Time.timeScale = 4;
            try
            {
                foreach (var (character, room, target) in new[] { ("robot", "LivingRoom", "暖炉"), ("calm", "Bedroom", "ベッド"), ("robot", "BotanicaCafe", "カウンター") })
                {
                    yield return Start(app, character, room);
                    var loaded = Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i)).Where(x => x.isLoaded).Select(x => x.name).ToArray();
                    Assert.That(loaded, Is.EquivalentTo(new[] { "Bootstrap", room }).Or.EquivalentTo(new[] { room }), "loaded scenes: " + string.Join(",", loaded));
                    var brain = app.Brain; Assert.NotNull(brain); Assert.NotNull(brain.world); Assert.NotNull(brain.locomotion);
                    Assert.That(brain.world.objects.Select(o => o.label), Does.Contain(target), room + " objects");
                    var perception = brain.Perception("test");
                    StringAssert.Contains(target, perception.surroundings);
                    var goal = brain.world.Find(target); float before = SceneAwareness.FlatDistance(goal, brain.body.transform.position);
                    Assert.True(brain.Move(new BehaviorDecision { movement = Movement.go_to_object, target = goal.id, gaze = Gaze.@object }), "no route to " + goal.id);
                    bool walked = false;
                    for (int i = 0; i < 60 && brain.locomotion.Moving; i++) { yield return null; walked |= brain.locomotion.Speed01 > .5f; }
                    Assert.True(walked, "walk speed never rose");
                    yield return WaitWhileMoving(app, 30);
                    Assert.False(brain.locomotion.Moving, "did not arrive at " + goal.id);
                    var position = brain.body.transform.position; float after = SceneAwareness.FlatDistance(goal, position);
                    Assert.That(after, Is.LessThan(Mathf.Min(before, 1.1f)), goal.id + " distance");
                    foreach (var o in brain.world.objects.Where(o => o.obstacle)) Assert.False(o.bounds.Contains(new Vector3(position.x, o.bounds.center.y, position.z)), "stands inside " + o.id);
                    yield return new WaitForSeconds(1);
                    Assert.That(Vector3.Angle(brain.body.transform.forward, Vector3.ProjectOnPlane(goal.bounds.center - position, Vector3.up)), Is.LessThan(30), "faces " + goal.id);
                    yield return ApplicationTests.SaveImage(app.Head.GetComponent<Camera>(), "Walk-" + character + "-" + room);
                    // Coming back to the user ends within reach but outside personal space.
                    Assert.True(brain.Move(new BehaviorDecision { movement = Movement.approach_user }));
                    yield return WaitWhileMoving(app, 30);
                    float distance = Vector2.Distance(new Vector2(app.Head.position.x, app.Head.position.z), new Vector2(brain.body.transform.position.x, brain.body.transform.position.z));
                    Assert.That(distance, Is.InRange(.5f, 1.6f), "distance to user after approach");
                }
            }
            finally { Time.timeScale = 1; }
            Object.Destroy(app.gameObject); AppRoot.EditorRepositoryFactory = null; yield return null;
        }
        [UnityTest] public IEnumerator SitsOnSofaThenReactsToCrouchAndPointing()
        {
            AppRoot.EditorRepositoryFactory = () => new LocalRepository();
            yield return SceneManager.LoadSceneAsync("Bootstrap");
            float deadline = Time.realtimeSinceStartup + 20;
            while ((!AppRoot.Instance || !AppRoot.Instance.UI || !SceneManager.GetSceneByName("MainMenu").isLoaded) && Time.realtimeSinceStartup < deadline) yield return null;
            var app = AppRoot.Instance; Time.timeScale = 4;
            try
            {
                yield return Start(app, "robot", "LivingRoom");
                var brain = app.Brain; var sofa = brain.world.Find("ソファ");
                Assert.True(brain.ApplyDecision(new BehaviorDecision { movement = Movement.go_to_object, target = sofa.id, arrival = ArrivalAction.sit, pace = Pace.hurry, approach = ApproachStyle.around }));
                Assert.That(brain.locomotion.PaceScale, Is.GreaterThan(1.5f));
                yield return WaitWhileMoving(app, 30); yield return new WaitForSeconds(1);
                Assert.True(brain.locomotion.Seated, "seated"); Assert.That(brain.body.CurrentPose, Is.EqualTo(BodySystem.Pose.sit));
                Assert.That(brain.body.transform.position.y, Is.GreaterThan(.02f), "root lifted onto the seat");
                yield return ApplicationTests.SaveImage(app.Head.GetComponent<Camera>(), "Pose-robot-sit");
                // The user crouches (PC: C key lowers the view): far away, the robot gets up and comes over slowly.
                var head = app.Head.localPosition; app.Head.localPosition = new Vector3(head.x, .9f, head.z);
                deadline = Time.realtimeSinceStartup + 5; while (!brain.locomotion.Moving && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.True(brain.tracker.Crouching); Assert.True(brain.locomotion.Moving, "reacts to crouch"); Assert.False(brain.locomotion.Seated);
                Assert.That(brain.locomotion.PaceScale, Is.LessThan(1));
                yield return WaitWhileMoving(app, 30);
                app.Head.localPosition = head; yield return new WaitForSeconds(.5f);
                // Pointing at the hearth (PC: F key) sends the robot to crouch and look at it.
                var hearth = brain.world.Find("暖炉");
                app.Body.Pointer(new Ray(app.Head.position, (hearth.bounds.center - app.Head.position).normalized), true); yield return null;
                Assert.That(brain.tracker.PointingAt, Is.EqualTo(hearth.id)); Assert.That(brain.locomotion.Destination, Is.EqualTo(hearth.id));
                yield return WaitWhileMoving(app, 30); yield return new WaitForSeconds(.5f);
                Assert.That(brain.body.CurrentPose, Is.EqualTo(BodySystem.Pose.crouch));
                yield return ApplicationTests.SaveImage(app.Head.GetComponent<Camera>(), "Pose-robot-crouch");
            }
            finally { Time.timeScale = 1; }
            Object.Destroy(app.gameObject); AppRoot.EditorRepositoryFactory = null; yield return null;
        }
        [UnityTearDown] public IEnumerator Cleanup() { Time.timeScale = 1; if (AppRoot.Instance) Object.Destroy(AppRoot.Instance.gameObject); AppRoot.EditorRepositoryFactory = null; yield return null; }
    }
}
