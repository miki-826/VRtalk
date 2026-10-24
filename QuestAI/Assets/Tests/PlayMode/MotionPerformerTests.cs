using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace QuestAI.Tests
{
    public class MotionPerformerTests
    {
        static MotionStep Step(MotionAction action, string target = "", MotionDirection direction = MotionDirection.none, float amount = 0) => new MotionStep { action = action, target = target, direction = direction, amount = amount };
        static MotionPlan Plan(params MotionStep[] steps) => new MotionPlan { steps = steps, summary = "テスト" };
        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
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
        [UnityTest] public IEnumerator ToyInstructionsRollStackKnockDownAndFetch()
        {
            yield return Boot(); var app = AppRoot.Instance; Assert.NotNull(app);
            yield return Start(app, "robot", "LivingRoom");
            var brain = app.Brain; var performer = brain.performer; var play = brain.playground; var reports = new List<string>(); performer.Report = reports.Add;
            float floor = brain.world.floor.max.y;
            Time.timeScale = 4;
            try
            {
                performer.Run(Plan(Step(MotionAction.spawn_toy, "ボール"), Step(MotionAction.roll, "ボール", MotionDirection.to_user)), "ボールを出してこっちに転がして");
                yield return Done(performer, 40);
                var ball = play.Find("ボール"); Assert.NotNull(ball, string.Join(" / ", reports));
                Assert.That(reports, Has.Some.Contains("転がしました"), string.Join(" / ", reports));
                float wait = Time.realtimeSinceStartup + 10; while (ball.rolling && Time.realtimeSinceStartup < wait) yield return null;
                Assert.False(ball.rolling); Assert.That(ball.Center.y, Is.EqualTo(floor + CompanionPlayground.BallRadius).Within(.03f));
                Assert.That(Vector3.Distance(Flat(ball.Center), Flat(app.Head.position)), Is.LessThan(1.6f), "rolled to the user");

                // Sight: the ball counts only while it is inside the field of view.
                brain.body.lookPoint = null;
                brain.transform.rotation = Quaternion.LookRotation(Flat(ball.Center - brain.transform.position).normalized); brain.sight.Scan();
                Assert.True(brain.sight.Sees(ball.item.id)); Assert.That(brain.sight.VisibleSummary(), Has.Some.StartsWith(ball.item.id));
                brain.transform.rotation = Quaternion.LookRotation(-Flat(ball.Center - brain.transform.position).normalized); brain.sight.Scan();
                Assert.False(brain.sight.Sees(ball.item.id), "behind its back");

                performer.Run(Plan(Step(MotionAction.spawn_toy, "積み木"), Step(MotionAction.stack, "積み木")), "積み木を積んで");
                yield return Done(performer, 40);
                var blocks = play.Find("積み木"); Assert.NotNull(blocks, string.Join(" / ", reports)); Assert.True(blocks.stacked);
                var heights = blocks.pieces.Select(p => p.position.y - floor).OrderBy(y => y).ToArray();
                for (int i = 0; i < heights.Length; i++) Assert.That(heights[i], Is.EqualTo(CompanionPlayground.BlockSize / 2 + i * CompanionPlayground.BlockSize).Within(.01f), "level " + i);
                yield return ApplicationTests.SaveImage(app.Head.GetComponent<Camera>(), "Play-blocks-stacked");

                performer.Run(Plan(Step(MotionAction.knock_down, "積み木")), "崩して");
                yield return Done(performer, 30); yield return new WaitForSeconds(2);
                Assert.False(blocks.stacked); foreach (var piece in blocks.pieces) Assert.That(piece.position.y - floor, Is.EqualTo(CompanionPlayground.BlockSize / 2).Within(.02f));

                performer.Run(Plan(Step(MotionAction.fetch, ball.item.id)), "ボール持ってきて");
                yield return Done(performer, 40);
                Assert.False(ball.carried); Assert.That(Vector3.Distance(Flat(ball.Center), Flat(app.Head.position)), Is.LessThan(1.6f), string.Join(" / ", reports));
                Assert.That(reports, Has.Some.Contains("持ってきました"));

                var picture = brain.sight.CaptureAsync(); while (!picture.IsCompleted) yield return null;
                Assert.That(picture.Result.Length, Is.GreaterThan(2000));
                Directory.CreateDirectory("../artifacts/previews"); File.WriteAllBytes("../artifacts/previews/Vision-eye.jpg", picture.Result);
            }
            finally { Time.timeScale = 1; }
        }
        [UnityTest] public IEnumerator BodyMotionsFollowInstructions()
        {
            yield return Boot(); var app = AppRoot.Instance; Assert.NotNull(app);
            yield return Start(app, "calm", "LivingRoom");
            var brain = app.Brain; var performer = brain.performer; var rig = brain.body.rig; yield return new WaitForSeconds(.5f);
            float elbow = rig.rightForearm.position.y;
            var up = new MotionKey { time = .4f, left_arm_up = 170, right_arm_up = 170 }; var hold = new MotionKey { time = 2, left_arm_up = 170, right_arm_up = 170 };
            var custom = Step(MotionAction.motion); custom.motion = MotionPreset.custom; custom.keys = new[] { new MotionKey(), up, hold, new MotionKey { time = 2.4f } };
            performer.Run(Plan(custom), "両手を上げて"); yield return new WaitForSeconds(1);
            Assert.That(rig.rightForearm.position.y, Is.GreaterThan(elbow + .02f), "arm raised");
            yield return ApplicationTests.SaveImage(app.Head.GetComponent<Camera>(), "Motion-arms-up");
            yield return Done(performer, 10); Assert.Null(brain.body.Motion);

            if (rig.hips)
            {
                float rest = rig.hips.position.y, highest = rest; var jump = Step(MotionAction.motion); jump.motion = MotionPreset.jump;
                performer.Run(Plan(jump), "ジャンプして");
                for (float end = Time.time + 1.3f; Time.time < end; ) { yield return null; highest = Mathf.Max(highest, rig.hips.position.y); }
                Assert.That(highest, Is.GreaterThan(rest + .1f), "jumped");
                yield return Done(performer, 5);
            }
            float yaw = brain.transform.eulerAngles.y; var spin = Step(MotionAction.motion, amount: 1); spin.motion = MotionPreset.spin;
            performer.Run(Plan(spin), "一回転して"); yield return Done(performer, 10);
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(yaw, brain.transform.eulerAngles.y)), Is.LessThan(5));
            performer.Run(Plan(Step(MotionAction.turn, direction: MotionDirection.right, amount: 90)), "右を向いて"); yield return Done(performer, 10);
            Assert.That(Mathf.DeltaAngle(yaw, brain.transform.eulerAngles.y), Is.EqualTo(90).Within(5));
            Time.timeScale = 3;
            try
            {
                var before = brain.transform.position;
                performer.Run(Plan(Step(MotionAction.move, direction: MotionDirection.forward, amount: 1)), "1メートル前に進んで"); yield return Done(performer, 20);
                Assert.That(Vector3.Distance(Flat(before), Flat(brain.transform.position)), Is.GreaterThan(.5f));
            }
            finally { Time.timeScale = 1; }
            performer.Run(Plan(Step(MotionAction.follow_user, amount: 20)), "ついてきて"); yield return null; Assert.True(performer.Busy);
            performer.Run(Plan(Step(MotionAction.stop)), "止まって"); Assert.False(performer.Busy); Assert.False(brain.locomotion.Moving); Assert.False(brain.locomotion.Frozen);
        }
        [UnityTearDown] public IEnumerator Cleanup() { Time.timeScale = 1; if (AppRoot.Instance) Object.Destroy(AppRoot.Instance.gameObject); AppRoot.EditorRepositoryFactory = null; yield return null; }
    }
}
