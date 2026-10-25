using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace QuestAI.Tests
{
    public class MotionPerformerTests
    {
        static MotionStep Step(MotionAction action, string target = "", MotionDirection direction = MotionDirection.none, float amount = 0) => new MotionStep { action = action, target = target, direction = direction, amount = amount };
        static MotionPlan Plan(params MotionStep[] steps) => new MotionPlan { steps = steps, summary = "テスト" };
        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
        static Vector3 BakedCenter(SkinnedMeshRenderer renderer)
        {
            var mesh = new Mesh(); renderer.BakeMesh(mesh); var center = renderer.transform.TransformPoint(mesh.bounds.center); Object.Destroy(mesh); return center;
        }
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
                for (int i = 0; i < heights.Length; i++) Assert.That(heights[i], Is.EqualTo(CompanionPlayground.BlockSize / 2 + i * CompanionPlayground.BlockSize).Within(.025f), "level " + i + ": " + string.Join(",", heights));
                yield return new WaitForSeconds(1); Assert.True(blocks.stacked, "the physical tower stays up");
                yield return ApplicationTests.SaveImage(app.Head.GetComponent<Camera>(), "Play-blocks-stacked");

                performer.Run(Plan(Step(MotionAction.knock_down, "積み木")), "崩して");
                yield return Done(performer, 30); yield return new WaitForSeconds(2);
                Assert.False(blocks.stacked); Assert.That(blocks.pieces.Max(p => p.position.y) - floor, Is.LessThan(CompanionPlayground.BlockSize * 2.2f), "toppled");
                // Physics settles asynchronously; a fixed two-second delay depended on frame scheduling at 4x speed.
                wait = Time.realtimeSinceStartup + 5; while (blocks.Moving && Time.realtimeSinceStartup < wait) yield return null;
                Assert.False(blocks.Moving, "came to rest");

                // Picked up and thrown: it flies through the air, then bounces and rolls near the user.
                float highest = 0; performer.Run(Plan(Step(MotionAction.throw_ball, ball.item.id, MotionDirection.to_user)), "ボールを投げて");
                for (float end = Time.realtimeSinceStartup + 30; performer.Busy && Time.realtimeSinceStartup < end;) { highest = Mathf.Max(highest, ball.Center.y - floor); yield return null; }
                Assert.False(performer.Busy); Assert.That(highest, Is.GreaterThan(.6f), "thrown in an arc"); Assert.That(reports, Has.Some.Contains("投げました"));
                wait = Time.realtimeSinceStartup + 10; while (ball.Moving && Time.realtimeSinceStartup < wait) yield return null;
                Assert.That(Vector3.Distance(Flat(ball.Center), Flat(app.Head.position)), Is.LessThan(2.2f), "landed near the user");

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
                for (float end = Time.time + 2.6f; Time.time < end; ) { yield return null; highest = Mathf.Max(highest, rig.hips.position.y); }
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
        [UnityTest] public IEnumerator RealAnimationsPropsFurnitureAndTouch()
        {
            yield return Boot(); var app = AppRoot.Instance; Assert.NotNull(app);
            yield return Start(app, "calm", "LivingRoom");
            var brain = app.Brain; var performer = brain.performer; var play = brain.playground; var reports = new List<string>(); performer.Report = reports.Add;
            var clips = brain.GetComponent<ClipAnimator>(); Assert.NotNull(clips, "WhiteChibi plays the CC0 animation library"); Assert.True(clips.Humanoid);
            foreach (var name in new[] { "Idle_Loop", "Walk_Loop", "Jog_Fwd_Loop", "Walk_Carry_Loop", "OverhandThrow", "Dance_Loop", "Crouch_Idle_Loop", "Punch_Jab", "Death01" }) Assert.True(clips.Has(name), name);
            var dance = Step(MotionAction.motion, amount: 1); dance.motion = MotionPreset.dance;
            performer.Run(Plan(dance), "踊って"); yield return new WaitForSeconds(.6f);
            Assert.That(clips.Current, Is.EqualTo("Dance_Loop")); Assert.True(clips.GestureActive);
            yield return ApplicationTests.SaveImage(app.Head.GetComponent<Camera>(), "Anim-dance");
            performer.Stop(); Assert.False(clips.GestureActive);
            float floor = brain.world.floor.max.y;
            Time.timeScale = 3;
            try
            {
                performer.Run(Plan(Step(MotionAction.spawn_toy, "クッション"), Step(MotionAction.throw_ball, "クッション", MotionDirection.to_user)), "クッションを投げて");
                var cushion = (CompanionPlayground.Toy)null; float highest = 0;
                for (float end = Time.realtimeSinceStartup + 40; performer.Busy && Time.realtimeSinceStartup < end;) { cushion = cushion ?? play.Find("クッション"); if (cushion != null) highest = Mathf.Max(highest, cushion.Center.y - floor); yield return null; }
                Assert.False(performer.Busy); Assert.NotNull(cushion, string.Join(" / ", reports)); Assert.That(reports, Has.Some.Contains("投げました"), string.Join(" / ", reports)); Assert.That(highest, Is.GreaterThan(.5f));

                var furniture = brain.world.objects.Where(o => o.obstacle && o.bounds.max.y > floor + .3f && o.bounds.max.y < floor + 1 && o.bounds.size.x > .4f && o.bounds.size.z > .4f)
                    .OrderBy(o => SceneAwareness.FlatDistance(o, brain.transform.position)).First();
                var put = Step(MotionAction.put_on, "本"); put.toward = furniture.id;
                performer.Run(Plan(Step(MotionAction.spawn_toy, "本"), put), "本を" + furniture.id + "の上に置いて");
                yield return Done(performer, 60); yield return new WaitForSeconds(1.5f);
                var book = play.Find("本"); Assert.NotNull(book, string.Join(" / ", reports));
                Assert.That(book.item.bounds.min.y, Is.GreaterThan(furniture.bounds.max.y - .05f), "on top of " + furniture.id + ": " + string.Join(" / ", reports));
                yield return ApplicationTests.SaveImage(app.Head.GetComponent<Camera>(), "Prop-on-furniture");

                performer.Run(Plan(Step(MotionAction.touch, "user")), "握手して"); yield return Done(performer, 30);
                Assert.That(reports, Has.Some.Contains("触れました"));
                play.PickUp(book); Assert.True(book.carried);
                performer.Run(Plan(Step(MotionAction.wait, amount: .2f)), "そのまま待って");
                Assert.True(book.carried, "A new request preserves the prop held after the previous request");
                yield return Done(performer, 3); Assert.True(book.carried);
                performer.Stop(); Assert.False(book.carried, "Explicit stop releases held props");
            }
            finally { Time.timeScale = 1; }
        }
        [UnityTest] public IEnumerator AnimalCompanionsKeepAnimationAndRespectTiming()
        {
            yield return Boot(); var app = AppRoot.Instance;
            foreach (string id in new[] { "bunny", "cat" })
            {
                yield return Start(app, id, "LivingRoom");
                var brain = app.Brain; var rig = brain.body.rig; var secondary = brain.GetComponent<CompanionSecondaryMotion>();
                Assert.NotNull(secondary); Assert.NotNull(secondary.leftEar); Assert.NotNull(secondary.rightEar); Assert.NotNull(secondary.tail);
                Assert.True(brain.GetComponent<ClipAnimator>().Humanoid); Assert.That(rig.faceMeshes.Length, Is.GreaterThan(0));
                brain.performer.Run(Plan(Step(MotionAction.turn, direction: MotionDirection.to_user)), "こっちを向いて"); yield return Done(brain.performer, 5);
                brain.body.SetEmotion(Emotion.happy); yield return new WaitForSeconds(.5f);
                var headMesh = brain.GetComponentsInChildren<SkinnedMeshRenderer>().First(r => r.name.StartsWith("Head_"));
                var eyeMesh = brain.GetComponentsInChildren<SkinnedMeshRenderer>().First(r => r.name == "Eye.L");
                Assert.That(Vector3.Dot(BakedCenter(eyeMesh) - BakedCenter(headMesh), brain.transform.forward), Is.GreaterThan(.05f), "The animated face points in the requested direction");
                yield return ApplicationTests.SaveImage(app.Head.GetComponent<Camera>(), "Companion-" + id + "-session");
                var hold = LocalMotionParser.Parse("4秒両手を上げて", new MotionRequest()).plan;
                brain.performer.Run(hold, "4秒両手を上げて");
                yield return new WaitForSeconds(2.6f); Assert.True(brain.performer.Busy); Assert.NotNull(brain.body.Motion);
                yield return Done(brain.performer, 4); Assert.Null(brain.body.Motion);
                var dance = new MotionStep { action = MotionAction.motion, motion = MotionPreset.dance, duration = 1.2f, amount = 1 };
                float began = Time.time; brain.performer.Run(Plan(dance), "1.2秒踊って"); yield return Done(brain.performer, 3);
                Assert.That(Time.time - began, Is.InRange(1.1f, 2f), "clip follows requested duration");
                brain.performer.Run(Plan(Step(MotionAction.crouch, amount: 4), Step(MotionAction.turn, direction: MotionDirection.left)), "4秒しゃがんでから左を向いて");
                yield return new WaitForSeconds(3.3f); Assert.That(brain.performer.Current.action, Is.EqualTo(MotionAction.crouch));
                yield return Done(brain.performer, 5); Assert.That(brain.body.CurrentPose, Is.EqualTo(BodySystem.Pose.stand));
            }
        }
        [UnityTest] public IEnumerator UnreachableToyIsNotPickedUpAndStopCancelsIdleWalking()
        {
            yield return Boot(); var app = AppRoot.Instance; yield return Start(app, "cat", "LivingRoom");
            var brain = app.Brain; var play = brain.playground;
            var ball = play.Spawn(CompanionPlayground.ToyKind.Ball); Assert.NotNull(ball);
            // Force the walking request to fail while leaving the scene/prop model available.
            brain.locomotion.world = null;
            var distant = brain.transform.position + Vector3.right * 3 + Vector3.up * .15f;
            foreach (var piece in ball.pieces) piece.position = distant;
            ball.item.bounds = new Bounds(distant, Vector3.one * .3f);
            Assert.That(SceneAwareness.FlatDistance(ball.item, brain.transform.position), Is.GreaterThan(2));
            brain.performer.Run(Plan(Step(MotionAction.pick_up, ball.item.id)), "ボールを拾って"); yield return Done(brain.performer, 5);
            Assert.False(ball.carried, string.Join(" / ", brain.performer.Results)); Assert.That(brain.performer.Results, Has.Some.Contains("できませんでした"));
            Assert.That(brain.performer.Results, Has.None.Contains("持ち上げました"));
            brain.locomotion.world = brain.world;
            var goal = brain.world.grid.Snap(brain.transform.position + Vector3.left * 1.5f);
            Assert.True(brain.locomotion.MoveTo(goal, "test")); yield return null;
            Assert.True(brain.locomotion.Moving);
            brain.performer.Run(Plan(Step(MotionAction.stop)), "止まって");
            Assert.False(brain.locomotion.Moving); Assert.False(brain.locomotion.Frozen);
        }
        [UnityTest] public IEnumerator LocalControlsHoldArmsWhileWalkingAndKeepFloorPose()
        {
            yield return Boot(); var app=AppRoot.Instance;yield return Start(app,"bunny","LivingRoom");
            var brain=app.Brain;var performer=brain.performer;var rig=brain.body.rig;
            var statuses=new List<string>();brain.StatusChanged+=statuses.Add;
            app.UI.MotionLab();Assert.False(brain.Connected);Assert.That(app.Secrets.Get("OpenAI"),Is.Null.Or.Empty);
            yield return ApplicationTests.SaveImage(app.Head.GetComponent<Camera>(),"MotionLab-UI");
            float elbow=rig.rightForearm.position.y;var before=brain.transform.position;
            app.UI.Canvas.GetComponentsInChildren<Button>().First(b=>b.GetComponentInChildren<Text>().text=="右手を上げたまま歩く").onClick.Invoke();
            yield return new WaitForSeconds(.7f);Assert.True(performer.Busy);Assert.NotNull(brain.body.HeldArms);
            Assert.That(rig.rightForearm.position.y,Is.GreaterThan(elbow+.05f),"Raised arm remains up over the walk animation");
            yield return ApplicationTests.SaveImage(app.Head.GetComponent<Camera>(),"Motion-held-arm-walk");
            yield return Done(performer,15);Assert.That(Vector3.Distance(Flat(before),Flat(brain.transform.position)),Is.GreaterThan(.35f));
            Assert.Null(brain.body.HeldArms);Assert.That(statuses,Has.Some.Contains("できたよ"));

            float standing=rig.hips.position.y;
            Assert.True(brain.PerformLocalMotion("床に座って"));yield return Done(performer,3);yield return new WaitForSeconds(4.5f);
            Assert.That(brain.body.CurrentPose,Is.EqualTo(BodySystem.Pose.floor_sit));Assert.That(rig.hips.position.y,Is.LessThan(standing-.06f));
            Assert.That(rig.hips.position.y,Is.GreaterThan(brain.world.floor.max.y+.05f));
            foreach(var bone in new[]{HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot})Assert.That(rig.animator.GetBoneTransform(bone).position.y,Is.GreaterThan(brain.world.floor.max.y-.06f),"Seated foot stays above the floor");
            var viewRotation=app.Head.rotation;app.Head.LookAt(brain.transform.position+Vector3.up*.5f);
            yield return ApplicationTests.SaveImage(app.Head.GetComponent<Camera>(),"Motion-floor-sit");
            app.Head.rotation=viewRotation;
            Assert.True(brain.PerformLocalMotion("左手だけを振って"));yield return Done(performer,5);
            Assert.That(brain.body.CurrentPose,Is.EqualTo(BodySystem.Pose.floor_sit),"A seated greeting stays seated");
            app.UI.Canvas.GetComponentsInChildren<Button>().First(b=>b.GetComponentInChildren<Text>().text=="立ち上がる").onClick.Invoke();
            yield return null;Assert.That(rig.hips.position.y,Is.LessThan(standing-.03f),"Standing up unfolds instead of popping instantly");
            yield return Done(performer,3);Assert.That(brain.body.CurrentPose,Is.EqualTo(BodySystem.Pose.stand));

            Assert.True(brain.PerformLocalMotion("右手を上げたまま2歩前に歩いて"));yield return new WaitForSeconds(.4f);performer.Stop();
            Assert.False(performer.Busy);Assert.Null(brain.body.HeldArms);Assert.False(brain.locomotion.Moving);Assert.False(brain.locomotion.Frozen);
            Assert.False(brain.Connected,"Local controls never connect to paid services");
        }
        [UnityTest] public IEnumerator GazeFollowsItsMovingTargetAndTimedSittingEnds()
        {
            yield return Boot();var app=AppRoot.Instance;yield return Start(app,"cat","LivingRoom");var brain=app.Brain;
            brain.performer.Run(Plan(Step(MotionAction.look_at,"user",amount:1.5f)),"こっちを見て");yield return new WaitForSeconds(.2f);
            app.Head.position+=Vector3.right*.4f;yield return null;yield return null;
            Assert.True(brain.body.lookPoint.HasValue);Assert.That(Vector3.Distance(brain.body.lookPoint.Value,app.Head.position),Is.LessThan(.001f));
            yield return Done(brain.performer,3);Assert.Null(brain.body.lookPoint);
            Assert.True(brain.PerformLocalMotion("1秒床に座って"));yield return Done(brain.performer,3);
            Assert.That(brain.body.CurrentPose,Is.EqualTo(BodySystem.Pose.stand));
            Assert.True(brain.PerformLocalMotion("床に座って"));yield return Done(brain.performer,3);
            Assert.True(brain.PerformLocalMotion("ジャンプして"));Assert.That(brain.body.CurrentPose,Is.EqualTo(BodySystem.Pose.stand));
            yield return Done(brain.performer,5);
        }
        [UnityTest] public IEnumerator HeldArmsUseTheSameWorldDirectionOnEveryCharacter()
        {
            yield return Boot();var app=AppRoot.Instance;
            foreach(string id in new[]{"calm","bright","bunny","cat","robot"})
            {
                yield return Start(app,id,"LivingRoom");var brain=app.Brain;var rig=brain.body.rig;
                Assert.True(brain.PerformLocalMotion("右手を上げたまま2秒待って"));yield return new WaitForSeconds(.6f);
                var expected=Quaternion.AngleAxis(-170,brain.transform.right)*(-brain.transform.up);
                Assert.That(Vector3.Angle(rig.rightForearm.position-rig.rightArm.position,expected),Is.LessThan(8),id+" arm matches the requested direction");
                if(id=="bunny")yield return ApplicationTests.SaveImage(app.Head.GetComponent<Camera>(),"Motion-held-arms-accurate");
                brain.performer.Stop();
                Assert.True(brain.PerformLocalMotion("左手を横に広げたまま2秒待って"));yield return new WaitForSeconds(.6f);
                Assert.That(Vector3.Angle(rig.leftForearm.position-rig.leftArm.position,-brain.transform.right),Is.LessThan(8),id+" left arm is horizontal");
                brain.performer.Stop();Assert.Null(brain.body.HeldArms);
            }
        }
        [UnityTearDown] public IEnumerator Cleanup() { Time.timeScale = 1; if (AppRoot.Instance) Object.Destroy(AppRoot.Instance.gameObject); AppRoot.EditorRepositoryFactory = null; yield return null; }
    }
}
