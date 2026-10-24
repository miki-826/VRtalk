using System.Linq;
using NUnit.Framework;
namespace QuestAI.Tests
{
    public class LocalMotionParserTests
    {
        static readonly MotionRequest Room = new MotionRequest { places = new[] { "ソファ", "暖炉", "椅子1", "椅子2", "テーブル" }, toys = new[] { "ボール1", "積み木2" } };
        static LocalMotionParser.Result Parse(string text) => LocalMotionParser.Parse(text, Room);
        static MotionStep[] Steps(string text) { var r = Parse(text); Assert.True(r.complete, text + " → unknown: " + string.Join("/", r.unknown)); return r.plan.steps; }
        [Test] public void EverydayInstructionsBecomeSteps()
        {
            Assert.That(Steps("ジャンプして")[0].motion, Is.EqualTo(MotionPreset.jump));
            Assert.That(Steps("3回ジャンプして")[0].amount, Is.EqualTo(3));
            Assert.That(Steps("二回まわって")[0].motion, Is.EqualTo(MotionPreset.spin)); Assert.That(Steps("二回まわって")[0].amount, Is.EqualTo(2));
            Assert.That(Steps("お辞儀して")[0].motion, Is.EqualTo(MotionPreset.bow));
            Assert.That(Steps("踊って！")[0].motion, Is.EqualTo(MotionPreset.dance));
            Assert.That(Steps("バンザイ")[0].motion, Is.EqualTo(MotionPreset.banzai));
            Assert.That(Steps("手を振って")[0].gesture, Is.EqualTo(Gesture.wave));
            Assert.That(Steps("こっちに来て")[0].action, Is.EqualTo(MotionAction.approach_user));
            Assert.That(Steps("ついてきて")[0].action, Is.EqualTo(MotionAction.follow_user));
            var move = Steps("2メートル前に進んで")[0]; Assert.That(move.action, Is.EqualTo(MotionAction.move)); Assert.That(move.direction, Is.EqualTo(MotionDirection.forward)); Assert.That(move.amount, Is.EqualTo(2));
            var turn = Steps("右を向いて")[0]; Assert.That(turn.action, Is.EqualTo(MotionAction.turn)); Assert.That(turn.direction, Is.EqualTo(MotionDirection.right));
            var walk = Steps("暖炉のところに行って")[0]; Assert.That(walk.action, Is.EqualTo(MotionAction.walk_to)); Assert.That(walk.target, Is.EqualTo("暖炉"));
            var sit = Steps("座って")[0]; Assert.That(sit.action, Is.EqualTo(MotionAction.sit)); Assert.That(sit.target, Is.EqualTo("ソファ"), "the comfiest seat");
            Assert.That(Steps("椅子に座って")[0].target, Is.EqualTo("椅子1")); Assert.That(Steps("床に座って")[0].target, Is.Empty);
            Assert.That(Steps("止まって")[0].action, Is.EqualTo(MotionAction.stop));
            Assert.That(Steps("何が見える？")[0].action, Is.EqualTo(MotionAction.look_closely));
        }
        [Test] public void ToyPlayAndSequences()
        {
            var steps = Steps("ボールを出して、こっちに転がして");
            Assert.That(steps.Select(s => s.action), Is.EqualTo(new[] { MotionAction.spawn_toy, MotionAction.roll }));
            Assert.That(steps[1].direction, Is.EqualTo(MotionDirection.to_user));
            var kick = Steps("ボールをソファの方に蹴って")[0]; Assert.That(kick.action, Is.EqualTo(MotionAction.kick)); Assert.That(kick.direction, Is.EqualTo(MotionDirection.to_object)); Assert.That(kick.toward, Is.EqualTo("ソファ"));
            Assert.That(Steps("ボールを投げて")[0].action, Is.EqualTo(MotionAction.throw_ball));
            Assert.That(Steps("積み木を投げて")[0].target, Is.EqualTo("積み木"));
            Assert.That(Steps("積み木を5段積んでから崩して").Select(s => s.action), Is.EqualTo(new[] { MotionAction.stack, MotionAction.knock_down }));
            Assert.That(Steps("積み木を5段積んで")[0].amount, Is.EqualTo(5));
            Assert.That(Steps("ボール持ってきて")[0].action, Is.EqualTo(MotionAction.fetch));
            var sequence = Steps("ボールを拾って、それを投げて"); Assert.That(sequence.Select(s => s.action), Is.EqualTo(new[] { MotionAction.pick_up, MotionAction.throw_ball })); Assert.That(sequence[1].target, Is.EqualTo("ボール"));
            Assert.That(Steps("積み木を並べて")[0].action, Is.EqualTo(MotionAction.line_up));
            Assert.That(Steps("おもちゃを片づけて")[0].action, Is.EqualTo(MotionAction.put_away));
            Assert.That(Steps("ジャンプしてから右を向いて、手を振って").Select(s => s.action), Is.EqualTo(new[] { MotionAction.motion, MotionAction.turn, MotionAction.gesture }));
        }
        [Test] public void BodyPartsMakeFreePoses()
        {
            var pose = Steps("右手を上げて")[0]; Assert.That(pose.motion, Is.EqualTo(MotionPreset.custom)); Assert.That(pose.keys[1].right_arm_up, Is.EqualTo(170)); Assert.That(pose.keys[1].left_arm_up, Is.EqualTo(0));
            var both = Steps("右手を上げたまま左足を上げて")[0].keys[1]; Assert.That(both.right_arm_up, Is.EqualTo(170)); Assert.That(both.left_leg, Is.EqualTo(70));
            var wide = Steps("両手を広げて")[0].keys[1]; Assert.That(wide.left_arm_out, Is.EqualTo(90)); Assert.That(wide.right_arm_out, Is.EqualTo(90));
            Assert.That(Steps("両手を上げて")[0].keys[1].left_arm_up, Is.EqualTo(170));
            Assert.That(Steps("頭を右に傾けて")[0].keys[1].head_roll, Is.EqualTo(25));
            Assert.That(Steps("体を前に曲げて")[0].keys[1].chest_pitch, Is.EqualTo(50));
            Assert.That(Steps("片足で立って")[0].keys[1].left_leg, Is.GreaterThan(0));
            Assert.That(Steps("腕をぐるぐる回して")[0].keys.Length, Is.GreaterThan(4));
            var last = Steps("左腕を前に伸ばして")[0].keys.Last(); Assert.That(last.left_arm_up, Is.EqualTo(0), "returns to rest");
        }
        [Test] public void ChatIsNotACommandAndUnknownRequestsAreReported()
        {
            Assert.False(Parse("今日は楽しかったね").anyRequest);
            Assert.False(Parse("ジャンプって楽しいよね").anyRequest);
            Assert.False(Parse("走らないでね").complete);
            var odd = Parse("ジャンプして、宇宙人みたいな変なポーズして");
            Assert.True(odd.anyRequest); Assert.False(odd.complete); Assert.That(odd.plan.steps[0].motion, Is.EqualTo(MotionPreset.jump)); Assert.That(odd.unknown, Has.Count.EqualTo(1));
        }
    }
}
