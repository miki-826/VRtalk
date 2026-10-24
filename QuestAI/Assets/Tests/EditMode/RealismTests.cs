using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
namespace QuestAI.Tests
{
    public class RealismTests
    {
        [Test] public void TwoBoneIkPutsTheHandOnReachableTargets()
        {
            var shoulder = new GameObject("shoulder").transform; var elbow = new GameObject("elbow").transform; var hand = new GameObject("hand").transform;
            try
            {
                elbow.SetParent(shoulder); hand.SetParent(elbow); elbow.localPosition = new Vector3(0, -.3f, 0); hand.localPosition = new Vector3(0, -.28f, 0);
                foreach (var target in new[] { new Vector3(.3f, -.2f, .2f), new Vector3(0, .1f, .45f), new Vector3(-.2f, -.4f, -.1f), new Vector3(.1f, 0, .2f) })
                {
                    shoulder.rotation = Quaternion.identity; elbow.localRotation = Quaternion.identity;
                    BodySystem.SolveTwoBone(shoulder, elbow, hand, target, new Vector3(0, -.3f, -.3f), 1);
                    Assert.That(Vector3.Distance(hand.position, target), Is.LessThan(.01f), "target " + target);
                    Assert.That(Vector3.Distance(elbow.position, shoulder.position), Is.EqualTo(.3f).Within(.001f), "bones keep their length");
                }
                // Out of reach: the arm points straight at it.
                shoulder.rotation = Quaternion.identity; elbow.localRotation = Quaternion.identity;
                BodySystem.SolveTwoBone(shoulder, elbow, hand, new Vector3(2, 0, 0), Vector3.back, 1);
                Assert.That(Vector3.Angle(hand.position - shoulder.position, Vector3.right), Is.LessThan(2));
            }
            finally { Object.DestroyImmediate(shoulder.gameObject); }
        }
        [Test] public void ForbiddenResponsesApiFallsBackToChatCompletions()
        {
            var calls = new List<string>();
            var api = new OpenAIStructured((url, body, token) =>
            {
                calls.Add(url);
                if (url == OpenAIStructured.Responses) throw new ProviderException("OpenAI", 403);
                var sent = JObject.FromObject(body); Assert.That((string)sent["response_format"]["type"], Is.EqualTo("json_schema"));
                return Task.FromResult(JObject.Parse(@"{""choices"":[{""message"":{""content"":""{\""steps\"":[],\""summary\"":\""ok\"",\""done\"":true}""}}]}"));
            }, new AppSettings());
            var text = api.TextAsync("i", "ジャンプして", "motion_plan", MotionPlans.Schema(), 100, CancellationToken.None).Result;
            Assert.That(MotionPlans.Parse(text, new MotionRequest()).summary, Is.EqualTo("ok")); Assert.True(api.UseChat);
            api.TextAsync("i", "もう一回", "motion_plan", MotionPlans.Schema(), 100, CancellationToken.None).Wait();
            Assert.That(calls, Is.EqualTo(new[] { OpenAIStructured.Responses, OpenAIStructured.Chat, OpenAIStructured.Chat }), "Responses is not retried");
        }
        [Test] public void ImagesAreConvertedForChatCompletions()
        {
            JObject sent = null;
            var api = new OpenAIStructured((url, body, token) =>
            {
                if (url == OpenAIStructured.Responses) throw new ProviderException("OpenAI", 404);
                sent = JObject.FromObject(body); return Task.FromResult(JObject.Parse(@"{""choices"":[{""message"":{""content"":""赤いボール""}}]}"));
            }, new AppSettings());
            var content = new object[] { new { type = "input_text", text = "何が見える？" }, new { type = "input_image", image_url = "data:image/jpeg;base64,AAAA", detail = "low" } };
            Assert.That(api.TextAsync("i", content, null, null, 50, CancellationToken.None).Result, Is.EqualTo("赤いボール"));
            var parts = sent["messages"][1]["content"]; Assert.That((string)parts[0]["type"], Is.EqualTo("text")); Assert.That((string)parts[1]["image_url"]["url"], Does.StartWith("data:image/jpeg"));
            Assert.That(sent["response_format"], Is.Null);
        }
        [Test] public void AgentAnswersCarryDone()
        {
            var plan = MotionPlans.Parse(@"{""steps"":[{""action"":""wait"",""target"":"""",""toward"":"""",""direction"":""none"",""amount"":1,""pace"":""normal"",""gesture"":""none"",""emotion"":""neutral"",""motion"":""custom"",""keys"":[]}],""summary"":""待つ"",""done"":false}", new MotionRequest());
            Assert.False(plan.done); Assert.True(new MotionPlan().done, "local and one-shot plans finish by themselves");
            StringAssert.Contains("\"done\"", Newtonsoft.Json.JsonConvert.SerializeObject(MotionPlans.Schema()));
        }
        static readonly MotionRequest Room = new MotionRequest { places = new[] { "ソファ", "暖炉", "テーブル" }, toys = new[] { "ボール1" } };
        static MotionStep First(string text) { var r = LocalMotionParser.Parse(text, Room); Assert.True(r.complete, text + " → " + string.Join("/", r.unknown)); return r.plan.steps[0]; }
        [Test] public void NewInstructionsAreUnderstood()
        {
            Assert.That(First("パンチして").motion, Is.EqualTo(MotionPreset.punch));
            Assert.That(First("前転して").motion, Is.EqualTo(MotionPreset.forward_roll));
            Assert.That(First("床をごろごろ転がって").motion, Is.EqualTo(MotionPreset.forward_roll), "the character rolls, not the ball");
            Assert.That(First("死んだふりして").motion, Is.EqualTo(MotionPreset.die));
            Assert.That(First("電話してるふりして").motion, Is.EqualTo(MotionPreset.phone));
            Assert.That(First("腕を組んで").motion, Is.EqualTo(MotionPreset.fold_arms));
            Assert.That(First("薪割りして").motion, Is.EqualTo(MotionPreset.chop));
            Assert.That(First("何か食べて").motion, Is.EqualTo(MotionPreset.eat));
            var on = First("ボールをテーブルの上に置いて"); Assert.That(on.action, Is.EqualTo(MotionAction.put_on)); Assert.That(on.toward, Is.EqualTo("テーブル"));
            Assert.That(First("握手して").target, Is.EqualTo("user"));
            var touch = First("暖炉を触って"); Assert.That(touch.action, Is.EqualTo(MotionAction.touch)); Assert.That(touch.target, Is.EqualTo("暖炉"));
            Assert.That(First("クッションを投げて").target, Is.EqualTo("クッション"));
            Assert.That(First("ぬいぐるみを持ってきて").target, Is.EqualTo("ぬいぐるみ"));
            Assert.That(First("本を拾って").target, Is.EqualTo("本"));
            Assert.That(CompanionPlayground.KindOf("日本で遊ぼう"), Is.Null);
        }
    }
}
