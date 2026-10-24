using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;
namespace QuestAI.Tests
{
    public class MotionTests
    {
        static readonly MotionRequest Request = new MotionRequest { places = new[] { "ソファ", "暖炉" }, toys = new[] { "ボール1" } };
        [Test] public void PlannerAnswerIsParsedAndUntrustedValuesAreRemoved()
        {
            string json = @"{""summary"":""ボールを転がすね"",""steps"":[
                {""action"":""spawn_toy"",""target"":""ボール"",""toward"":"""",""direction"":""none"",""amount"":0,""pace"":""normal"",""gesture"":""none"",""emotion"":""happy"",""motion"":""custom"",""keys"":[]},
                {""action"":""roll"",""target"":""ボール1"",""toward"":""冷蔵庫"",""direction"":""to_user"",""amount"":0,""pace"":""hurry"",""gesture"":""none"",""emotion"":""excited"",""motion"":""custom"",""keys"":[]},
                {""action"":""move"",""target"":""宇宙"",""toward"":"""",""direction"":""forward"",""amount"":99,""pace"":""normal"",""gesture"":""none"",""emotion"":""neutral"",""motion"":""custom"",""keys"":[]},
                {""action"":""motion"",""target"":"""",""toward"":"""",""direction"":""none"",""amount"":1,""pace"":""normal"",""gesture"":""none"",""emotion"":""neutral"",""motion"":""custom"",""keys"":[
                    {""time"":1,""head_pitch"":0,""head_yaw"":0,""head_roll"":0,""chest_pitch"":0,""chest_yaw"":0,""chest_roll"":0,""left_arm_up"":400,""left_arm_out"":0,""right_arm_up"":170,""right_arm_out"":0,""left_elbow"":0,""right_elbow"":0,""left_leg"":0,""right_leg"":0,""left_knee"":0,""right_knee"":0,""hips_drop"":3,""lift"":-1},
                    {""time"":0,""head_pitch"":0,""head_yaw"":0,""head_roll"":0,""chest_pitch"":0,""chest_yaw"":0,""chest_roll"":0,""left_arm_up"":0,""left_arm_out"":0,""right_arm_up"":0,""right_arm_out"":0,""left_elbow"":0,""right_elbow"":0,""left_leg"":0,""right_leg"":0,""left_knee"":0,""right_knee"":0,""hips_drop"":0,""lift"":0}]}]}";
            var plan = MotionPlans.Parse(json, Request);
            Assert.That(plan.steps.Select(s => s.action), Is.EqualTo(new[] { MotionAction.spawn_toy, MotionAction.roll, MotionAction.move, MotionAction.motion }));
            Assert.That(plan.steps[0].target, Is.EqualTo("ボール"));
            Assert.That(plan.steps[1].target, Is.EqualTo("ボール1")); Assert.That(plan.steps[1].toward, Is.Empty, "unknown objects are dropped");
            Assert.That(plan.steps[1].pace, Is.EqualTo(Pace.hurry)); Assert.That(plan.steps[1].emotion, Is.EqualTo(Emotion.excited));
            Assert.That(plan.steps[2].target, Is.Empty); Assert.That(plan.steps[2].amount, Is.EqualTo(5));
            var keys = plan.steps[3].keys; Assert.That(keys[0].time, Is.EqualTo(0), "keys are sorted");
            Assert.That(keys[1].left_arm_up, Is.EqualTo(180)); Assert.That(keys[1].hips_drop, Is.EqualTo(1)); Assert.That(keys[1].lift, Is.EqualTo(0));
            Assert.That(plan.summary, Is.EqualTo("ボールを転がすね"));
        }
        [Test] public void PlanLimitsAndEmptyAnswers()
        {
            var many = new MotionPlan { steps = Enumerable.Range(0, 30).Select(_ => new MotionStep { action = MotionAction.wait, amount = float.NaN }).ToArray(), summary = new string('あ', 400) };
            var plan = MotionPlans.Validate(many, Request);
            Assert.That(plan.steps.Length, Is.EqualTo(MotionPlans.MaxSteps)); Assert.That(plan.steps[0].amount, Is.EqualTo(0)); Assert.That(plan.summary.Length, Is.EqualTo(120));
            Assert.True(MotionPlans.Validate(null, Request).Empty);
            Assert.True(MotionPlans.Parse(@"{""steps"":[],""summary"":""""}", Request).Empty);
        }
        [Test] public void SchemaIsStrictForEveryField()
        {
            var schema = JsonConvert.SerializeObject(MotionPlans.Schema());
            foreach (var field in MotionKey.Fields) StringAssert.Contains("\"" + field.Name + "\"", schema);
            foreach (var action in System.Enum.GetNames(typeof(MotionAction))) StringAssert.Contains("\"" + action + "\"", schema);
            StringAssert.Contains("\"additionalProperties\":false", schema);
        }
        [Test] public void StopRequestsAreRecognisedOnlyWhenShort()
        {
            Assert.True(CharacterBrain.IsStop("止まって！")); Assert.True(CharacterBrain.IsStop("ストップ"));
            Assert.False(CharacterBrain.IsStop("止まってから、ゆっくりソファまで歩いてきてくれる？")); Assert.False(CharacterBrain.IsStop("ジャンプして"));
        }
        [Test] public void PresetsStartAndEndAtRestAndSampleSmoothly()
        {
            foreach (MotionPreset preset in System.Enum.GetValues(typeof(MotionPreset)))
            {
                var keys = MotionLibrary.Get(preset);
                if (preset == MotionPreset.custom || preset == MotionPreset.spin) { Assert.That(keys, Is.Empty); continue; }
                Assert.That(keys.Length, Is.GreaterThan(2), preset.ToString());
                var last = keys[keys.Length - 1]; foreach (var f in MotionKey.Fields) if (f.Name != "time") Assert.That((float)f.GetValue(last), Is.EqualTo(0), preset + "." + f.Name);
                for (int i = 1; i < keys.Length; i++) Assert.That(keys[i].time, Is.GreaterThan(keys[i - 1].time), preset.ToString());
            }
            var bow = MotionLibrary.Get(MotionPreset.bow);
            Assert.That(MotionLibrary.Sample(bow, .3f).chest_pitch, Is.InRange(1f, 49f)); Assert.That(MotionLibrary.Sample(bow, 1).chest_pitch, Is.EqualTo(50).Within(.01f));
        }
        [Test] public void RobotVoiceChangesSpeechButKeepsSilenceAndLevel()
        {
            var robot = new RobotVoice(24000); var tone = Enumerable.Range(0, 4800).Select(i => Mathf.Sin(i * 2 * Mathf.PI * 220 / 24000) * .5f).ToArray(); var processed = (float[])tone.Clone();
            robot.Process(processed);
            Assert.That(processed.Max(x => Mathf.Abs(x)), Is.LessThanOrEqualTo(1)); Assert.That(processed.Max(x => Mathf.Abs(x)), Is.GreaterThan(.1f));
            Assert.That(tone.Zip(processed, (a, b) => Mathf.Abs(a - b)).Average(), Is.GreaterThan(.05f), "the effect must be audible");
            robot.Reset(); var silence = new float[2400]; robot.Process(silence); Assert.That(silence.Max(x => Mathf.Abs(x)), Is.EqualTo(0));
        }
        [Test] public void SightNeedsFieldOfViewAndAClearLine()
        {
            var eye = new Vector3(0, 1, 0); var wall = new[] { new Bounds(new Vector3(0, 1, 2), new Vector3(2, 2, .2f)) };
            Assert.True(CharacterVision.CanSee(eye, Vector3.forward, new Vector3(1, .3f, 3), 140, 7, new Bounds[0]));
            Assert.False(CharacterVision.CanSee(eye, Vector3.forward, new Vector3(0, .3f, -3), 140, 7, new Bounds[0]), "behind its back");
            Assert.False(CharacterVision.CanSee(eye, Vector3.forward, new Vector3(0, 1, 9), 140, 7, new Bounds[0]), "too far");
            Assert.False(CharacterVision.CanSee(eye, Vector3.forward, new Vector3(0, 1, 4), 140, 7, wall), "hidden behind the wall");
            Assert.True(CharacterVision.CanSee(eye, Vector3.forward, new Vector3(3, 1, 4), 140, 7, wall), "beside the wall");
        }
        [Test] public void BallRollsSlowsBouncesAndStops()
        {
            var floor = new Bounds(new Vector3(0, -.05f, 0), new Vector3(6, .1f, 6)); var box = new[] { new Bounds(new Vector3(0, .4f, 2), new Vector3(1, .8f, .4f)) };
            Vector3 p = new Vector3(0, CompanionPlayground.BallRadius, 0), v = new Vector3(0, 0, 3); int steps = 0;
            while (CompanionPlayground.StepBall(ref p, ref v, .02f, floor, box) && steps < 2000) steps++;
            Assert.That(steps, Is.LessThan(2000)); Assert.That(p.z, Is.LessThan(2 - .2f - CompanionPlayground.BallRadius + .01f), "bounced off the box");
            Assert.That(p.y, Is.EqualTo(CompanionPlayground.BallRadius).Within(.001f));
            p = new Vector3(-2, CompanionPlayground.BallRadius, -2); v = new Vector3(CompanionPlayground.SpeedFor(2), 0, 0);
            while (CompanionPlayground.StepBall(ref p, ref v, .01f, floor, new Bounds[0])) { }
            Assert.That(p.x, Is.EqualTo(0).Within(.15f), "launch speed matches the wanted distance");
            p = new Vector3(0, 1, 0); v = Vector3.zero; bool bounced = false;
            for (int i = 0; i < 400; i++) { CompanionPlayground.StepBall(ref p, ref v, .01f, floor, new Bounds[0]); bounced |= v.y > .5f; }
            Assert.True(bounced); Assert.That(p.y, Is.EqualTo(CompanionPlayground.BallRadius).Within(.01f));
            Assert.That(CompanionPlayground.KindOf("赤いボールで"), Is.EqualTo(CompanionPlayground.ToyKind.Ball)); Assert.That(CompanionPlayground.KindOf("つみきじゃなくて積み木"), Is.EqualTo(CompanionPlayground.ToyKind.Blocks));
        }
    }
}
