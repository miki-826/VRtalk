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
        [Test] public void ConnectedCommandsKeepOrderAndIndependentPace()
        {
            var steps = Steps("ゆっくり2メートル前に歩いて右に90度回って早く3回ジャンプして");
            Assert.That(steps.Select(s => s.action), Is.EqualTo(new[] { MotionAction.move, MotionAction.turn, MotionAction.motion }));
            Assert.That(steps.Select(s => s.pace), Is.EqualTo(new[] { Pace.slow, Pace.normal, Pace.hurry }));
            Assert.That(steps[0].amount, Is.EqualTo(2)); Assert.That(steps[1].amount, Is.EqualTo(90)); Assert.That(steps[2].amount, Is.EqualTo(3));
            Assert.That(Steps("ボールを出してこっちに転がして").Select(s => s.action), Is.EqualTo(new[] { MotionAction.spawn_toy, MotionAction.roll }));
            Assert.That(Steps("ジャンプして踊ってお辞儀して").Select(s => s.motion), Is.EqualTo(new[] { MotionPreset.jump, MotionPreset.dance, MotionPreset.bow }));
            Assert.That(Steps("ジャンプしてみて").Length, Is.EqualTo(1));
        }
        [Test] public void SidesDurationsAndAnglesArePreserved()
        {
            var wave = Steps("左手だけを振って")[0]; Assert.That(wave.motion, Is.EqualTo(MotionPreset.custom));
            Assert.That(wave.keys.Max(k => k.left_arm_out), Is.GreaterThan(100)); Assert.That(wave.keys.Max(k => k.right_arm_out), Is.Zero);
            Assert.That(Steps("5秒間両手を上げて")[0].duration, Is.EqualTo(5));
            Assert.That(Steps("10秒踊って")[0].duration, Is.EqualTo(10));
            Assert.That(Steps("右手を90度上げて")[0].keys[1].right_arm_up, Is.EqualTo(90));
            var simultaneous = Steps("右手を上げたまま左手を振って")[0].keys;
            Assert.That(simultaneous[2].right_arm_up, Is.EqualTo(170), "The held arm must not swing with the other arm");
            Assert.That(Steps("三秒待って")[0].amount, Is.EqualTo(3));
            var turn = Steps("左に180度回って")[0]; Assert.That(turn.action, Is.EqualTo(MotionAction.turn)); Assert.That(turn.direction, Is.EqualTo(MotionDirection.left)); Assert.That(turn.amount, Is.EqualTo(180));
            Assert.False(Parse(string.Join("、", Enumerable.Repeat("ジャンプして", MotionPlans.MaxSteps + 1))).complete, "Never claim a truncated plan is complete");
        }
        [Test] public void HeldArmAndWalkingAreOneSimultaneousAction()
        {
            var step=Steps("右手を上げたまま2歩前に歩いて").Single();
            Assert.That(step.action,Is.EqualTo(MotionAction.move)); Assert.That(step.amount,Is.EqualTo(.8f).Within(.001f));
            Assert.That(step.keys,Has.Length.EqualTo(1)); Assert.That(step.keys[0].right_arm_up,Is.EqualTo(170)); Assert.True(MotionPlans.ArmsOnly(step.keys[0]));
            Assert.That(Steps("両手を広げたまま左に90度向いて").Single().keys[0].left_arm_out,Is.EqualTo(90));
            Assert.False(Parse("右手を上げたままジャンプして").complete,"Unsupported simultaneous motion must not silently lose the held pose");
        }
        [Test] public void NegatedLookingDoesNotBecomeAVisualCommand()
        {
            foreach(string text in new[]{"外を見ないで","景色を観察しないで","右手を上げないで"})
            { var result=Parse(text);Assert.True(result.plan.Empty,text);Assert.False(result.anyRequest,text); }
            var look=Steps("5秒ボールを見て").Single();Assert.That(look.target,Is.EqualTo("ボール1"));Assert.That(look.amount,Is.EqualTo(5));
            Assert.That(Steps("30秒床に座って").Single().amount,Is.EqualTo(30));
        }
        [Test] public void WavingKeepsTheRequestedArmUp()
        {
            var keys=Steps("左手だけを振って").Single().keys;
            foreach(var key in keys.Skip(1).Take(keys.Length-2))
            {Assert.That(key.left_arm_out,Is.GreaterThan(100));Assert.That(key.left_elbow,Is.InRange(0,120));Assert.That(key.right_arm_out,Is.Zero);}
        }
        [Test] public void DescribedPlacesBecomeWalkTargets()
        {
            var cases = new[] {
                ("ソファの後ろに行って", MotionPlace.behind, "ソファ", ""),
                ("暖炉の前に立って", MotionPlace.front, "暖炉", ""),
                ("テーブルの右に移動して", MotionPlace.right, "テーブル", ""),
                ("ソファの左側に行って", MotionPlace.left, "ソファ", ""),
                ("椅子の横に来て", MotionPlace.beside, "椅子1", ""),
                ("テーブルと暖炉の間に行って", MotionPlace.between, "テーブル", "暖炉"),
                ("私とソファの間に立って", MotionPlace.between, "user", "ソファ"),
                ("私の右に来て", MotionPlace.right, "user", ""),
                ("僕の後ろに回って", MotionPlace.behind, "user", ""),
                ("隣に来て", MotionPlace.beside, "user", ""),
                ("部屋の真ん中に行って", MotionPlace.center, "", ""),
                ("部屋の隅に行って", MotionPlace.corner, "", ""),
                ("部屋の奥に行って", MotionPlace.far_side, "", ""),
                ("あそこに行って", MotionPlace.pointed, "", ""),
                ("そこまで歩いて", MotionPlace.pointed, "", ""),
                ("指さしたところに行って", MotionPlace.pointed, "", "")
            };
            foreach (var item in cases)
            {
                var step = Steps(item.Item1)[0];
                Assert.That(step.action, Is.EqualTo(MotionAction.walk_to), item.Item1);
                Assert.That(step.place, Is.EqualTo(item.Item2), item.Item1);
                Assert.That(step.target, Is.EqualTo(item.Item3), item.Item1);
                Assert.That(step.toward, Is.EqualTo(item.Item4), item.Item1);
            }
        }

        [Test] public void NearbyPhrasesKeepTheirMeaningAndUnknownPlacesAreNotGuessed()
        {
            var close = Steps("目の前に来て")[0];
            Assert.That(close.action, Is.EqualTo(MotionAction.walk_to)); Assert.That(close.place, Is.EqualTo(MotionPlace.front)); Assert.That(close.target, Is.EqualTo("user"));
            var hide = Steps("ソファの後ろに隠れて")[0];
            Assert.That(hide.place, Is.EqualTo(MotionPlace.behind)); Assert.That(hide.target, Is.EqualTo("ソファ"));
            // The room has no wall or fridge object: the request is reported, not turned into a bare "stand" or a side of the user.
            foreach (var text in new[] { "壁の前に立って", "冷蔵庫の後ろに行って" })
            {
                var parsed = Parse(text); Assert.True(parsed.anyRequest, text); Assert.False(parsed.complete, text); Assert.True(parsed.plan.Empty, text);
            }
            // Older meanings that share words with places.
            Assert.That(Steps("右を向いて")[0].action, Is.EqualTo(MotionAction.turn));
            var sit = Steps("ソファに座って")[0]; Assert.That(sit.action, Is.EqualTo(MotionAction.sit)); Assert.That(sit.target, Is.EqualTo("ソファ"));
            Assert.That(Steps("椅子の前に座って")[0].action, Is.EqualTo(MotionAction.sit));
            Assert.That(Steps("ボールを私の右に転がして")[0].action, Is.EqualTo(MotionAction.roll));
            Assert.That(Steps("後ろに下がって")[0].action, Is.EqualTo(MotionAction.move));
            Assert.That(Steps("立って")[0].action, Is.EqualTo(MotionAction.stand));
            Assert.That(Steps("私のそばに来て")[0].action, Is.EqualTo(MotionAction.approach_user));
            Assert.That(Steps("ついてきて")[0].action, Is.EqualTo(MotionAction.follow_user));
        }
        [Test] public void PlacePhrasesFromTheSecondReview()
        {
            MotionStep Walk(string text) { var step = Steps(text)[0]; Assert.That(step.action, Is.EqualTo(MotionAction.walk_to), text); return step; }
            // Coming FROM a place is not going to it.
            Assert.That(Steps("ソファの後ろからこっちに来て")[0].action, Is.EqualTo(MotionAction.approach_user));
            Assert.That(Steps("部屋の隅からこっちに来て")[0].action, Is.EqualTo(MotionAction.approach_user));
            // Doing something AT a place: go there, then do it.
            var spin = Steps("ソファの前でくるっと回って");
            Assert.That(spin.Select(s => s.action), Is.EqualTo(new[] { MotionAction.walk_to, MotionAction.motion }));
            Assert.That(spin[0].place, Is.EqualTo(MotionPlace.front)); Assert.That(spin[0].target, Is.EqualTo("ソファ")); Assert.That(spin[1].motion, Is.EqualTo(MotionPreset.spin));
            var dance = Steps("部屋の真ん中で踊って");
            Assert.That(dance[0].place, Is.EqualTo(MotionPlace.center)); Assert.That(dance[1].motion, Is.EqualTo(MotionPreset.dance));
            var wave = Steps("暖炉の近くで手を振って");
            Assert.That(wave[0].action, Is.EqualTo(MotionAction.walk_to)); Assert.That(wave[0].target, Is.EqualTo("暖炉")); Assert.That(wave[1].action, Is.EqualTo(MotionAction.gesture));
            // 「そこで」 is where the character already is.
            var onTheSpot = Steps("そこでジャンプして"); Assert.That(onTheSpot.Length, Is.EqualTo(1)); Assert.That(onTheSpot[0].motion, Is.EqualTo(MotionPreset.jump));
            Assert.That(Steps("ジャンプしてくるっと回って").Select(s => s.motion), Is.EqualTo(new[] { MotionPreset.jump, MotionPreset.spin }));
            // A named thing wins over 「あそこ」.
            var sofa = Walk("あそこのソファに行って"); Assert.That(sofa.target, Is.EqualTo("ソファ")); Assert.That(sofa.place, Is.EqualTo(MotionPlace.none));
            var behind = Walk("あそこのソファの後ろに行って"); Assert.That(behind.target, Is.EqualTo("ソファ")); Assert.That(behind.place, Is.EqualTo(MotionPlace.behind));
            Assert.That(Walk("指さしている場所に行って").place, Is.EqualTo(MotionPlace.pointed));
            // Distances and 「真」 between the thing and its side.
            var metre = Walk("ソファの1メートル前に行って"); Assert.That(metre.place, Is.EqualTo(MotionPlace.front)); Assert.That(metre.amount, Is.EqualTo(1));
            var steps2 = Walk("私の2歩右に立って"); Assert.That(steps2.place, Is.EqualTo(MotionPlace.right)); Assert.That(steps2.target, Is.EqualTo("user")); Assert.That(steps2.amount, Is.EqualTo(.8f).Within(.001f));
            var right = Walk("私の真後ろに来て"); Assert.That(right.place, Is.EqualTo(MotionPlace.behind)); Assert.That(right.target, Is.EqualTo("user"));
            var round = Walk("後ろに回って"); Assert.That(round.place, Is.EqualTo(MotionPlace.behind)); Assert.That(round.target, Is.EqualTo("user"));
            var next = Walk("隣に行って"); Assert.That(next.place, Is.EqualTo(MotionPlace.beside)); Assert.That(next.target, Is.EqualTo("user"));
            // Near a thing is beside it, also with 「来て」.
            var near = Walk("ソファの近くに来て"); Assert.That(near.target, Is.EqualTo("ソファ")); Assert.That(near.place, Is.EqualTo(MotionPlace.none));
            // Two of the same thing, and a numbered one.
            var chairs = Walk("椅子と椅子の間に行って"); Assert.That(chairs.place, Is.EqualTo(MotionPlace.between)); Assert.That(new[] { chairs.target, chairs.toward }, Is.EquivalentTo(new[] { "椅子1", "椅子2" }));
            Assert.That(Walk("椅子2の横に来て").target, Is.EqualTo("椅子2"));
            // A bare direction with 「行って」 is a step that way.
            var move = Steps("右に行って")[0]; Assert.That(move.action, Is.EqualTo(MotionAction.move)); Assert.That(move.direction, Is.EqualTo(MotionDirection.right));
            Assert.That(Steps("前に行って")[0].direction, Is.EqualTo(MotionDirection.forward));
        }
        [Test] public void PlannerPlacesNeedARealAnchor()
        {
            // The AI names something that is not in the room: the step must fail visibly, not walk to a side of the user.
            var plan = MotionPlans.Validate(new MotionPlan { steps = new[] { new MotionStep { action = MotionAction.walk_to, target = "冷蔵庫", place = MotionPlace.behind } } }, Room);
            Assert.That(plan.steps[0].target, Is.Empty); Assert.That(plan.steps[0].place, Is.EqualTo(MotionPlace.none));
            var kept = MotionPlans.Validate(new MotionPlan { steps = new[] { new MotionStep { action = MotionAction.walk_to, target = "user", place = MotionPlace.right, amount = 9 } } }, Room);
            Assert.That(kept.steps[0].place, Is.EqualTo(MotionPlace.right)); Assert.That(kept.steps[0].amount, Is.EqualTo(3));
        }
        [Test] public void DescribedPlaceKeepsDistanceInMetres()
        {
            var step = Steps("ソファの前1メートルに立って")[0];
            Assert.That(step.action, Is.EqualTo(MotionAction.walk_to));
            Assert.That(step.place, Is.EqualTo(MotionPlace.front));
            Assert.That(step.target, Is.EqualTo("ソファ"));
            Assert.That(step.amount, Is.EqualTo(1));
        }

        [Test] public void OrdinaryDestinationsAndTurnsKeepTheirOriginalActions()
        {
            var destination = Steps("暖炉のところに行って")[0];
            Assert.That(destination.action, Is.EqualTo(MotionAction.walk_to));
            Assert.That(destination.target, Is.EqualTo("暖炉"));
            Assert.That(destination.place, Is.EqualTo(MotionPlace.none));
            Assert.That(Steps("こっちに来て")[0].action, Is.EqualTo(MotionAction.approach_user));
            var turn = Steps("右に回って")[0];
            Assert.That(turn.action, Is.EqualTo(MotionAction.turn));
            Assert.That(turn.place, Is.EqualTo(MotionPlace.none));
            var angled = Steps("右に90度回って")[0];
            Assert.That(angled.action, Is.EqualTo(MotionAction.turn));
            Assert.That(angled.direction, Is.EqualTo(MotionDirection.right));
            Assert.That(angled.amount, Is.EqualTo(90));
        }

        [Test] public void HereMeansApproachUnlessTheUserIsPointing()
        {
            Assert.That(Steps("ここに来て")[0].action, Is.EqualTo(MotionAction.approach_user));
            var request = new MotionRequest { places = Room.places, pointing = true };
            var parsed = LocalMotionParser.Parse("ここに来て", request);
            Assert.True(parsed.complete);
            Assert.That(parsed.plan.steps[0].action, Is.EqualTo(MotionAction.walk_to));
            Assert.That(parsed.plan.steps[0].place, Is.EqualTo(MotionPlace.pointed));
        }

        [Test] public void DescribedPlaceCanStartASequence()
        {
            var steps = Steps("ソファの後ろに行ってジャンプして");
            Assert.That(steps.Select(s => s.action), Is.EqualTo(new[] { MotionAction.walk_to, MotionAction.motion }));
            Assert.That(steps[0].place, Is.EqualTo(MotionPlace.behind));
            Assert.That(steps[0].target, Is.EqualTo("ソファ"));
            Assert.That(steps[1].motion, Is.EqualTo(MotionPreset.jump));
        }

        [Test] public void TimeOfDayCommandsSetTheRequestedTime()
        {
            var cases = new[] { ("夜にして", "夜"), ("朝に変えて", "朝"), ("夕方にして", "夕方"),
                ("明るくして", "昼"), ("暗くして", "夜") };
            foreach (var item in cases)
            {
                var step = Steps(item.Item1)[0];
                Assert.That(step.action, Is.EqualTo(MotionAction.set_time), item.Item1);
                Assert.That(step.target, Is.EqualTo(item.Item2), item.Item1);
            }
        }

        [Test] public void TimeOfDayQuestionIsChat()
        {
            Assert.False(Parse("夜は好き？").anyRequest);
        }

        [Test] public void ValidationKeepsOnlyWalkPlacesAndKnownTimes()
        {
            var plan = new MotionPlan { steps = new[] {
                new MotionStep { action = MotionAction.walk_to, target = "ソファ", place = MotionPlace.behind },
                new MotionStep { action = MotionAction.turn, place = MotionPlace.right },
                new MotionStep { action = MotionAction.set_time, target = "夜", place = MotionPlace.corner },
                new MotionStep { action = MotionAction.set_time, target = "深夜" }
            } };
            var steps = MotionPlans.Validate(plan, Room).steps;
            Assert.That(steps[0].place, Is.EqualTo(MotionPlace.behind));
            Assert.That(steps[1].place, Is.EqualTo(MotionPlace.none));
            Assert.That(steps[2].place, Is.EqualTo(MotionPlace.none));
            Assert.That(steps[2].target, Is.EqualTo("夜"));
            Assert.That(steps[3].target, Is.Empty);
        }

        [Test] public void PlannerSchemaIncludesPlacesAndFarSide()
        {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(MotionPlans.Schema());
            StringAssert.Contains("place", json);
            StringAssert.Contains("far_side", json);
        }
    }
}
