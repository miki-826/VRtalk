using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace QuestAI
{
    public enum MotionAction { walk_to, approach_user, move, turn, look_at, sit, crouch, stand, point, gesture, motion, follow_user, wait, stop, spawn_toy, roll, kick, throw_ball, bounce, fetch, pick_up, put_down, stack, knock_down, line_up, spin_toy, put_away, look_closely }
    public enum MotionDirection { none, forward, back, left, right, to_user, away_from_user, to_object }
    public enum MotionPreset { custom, jump, bow, banzai, clap, dance, stretch, spin, shake_head, nod, wave_both, sit_floor, think, cheer }
    // One keyframe of a whole-body motion, in degrees relative to a relaxed standing pose (see the planner instructions).
    [Serializable] public class MotionKey
    {
        public float time, head_pitch, head_yaw, head_roll, chest_pitch, chest_yaw, chest_roll;
        public float left_arm_up, left_arm_out, right_arm_up, right_arm_out, left_elbow, right_elbow;
        public float left_leg, right_leg, left_knee, right_knee, hips_drop, lift;
        public static MotionKey Lerp(MotionKey a, MotionKey b, float t)
        {
            var result = new MotionKey();
            foreach (var field in Fields) field.SetValue(result, Mathf.Lerp((float)field.GetValue(a), (float)field.GetValue(b), t));
            return result;
        }
        public static readonly System.Reflection.FieldInfo[] Fields = typeof(MotionKey).GetFields().Where(f => f.FieldType == typeof(float)).ToArray();
    }
    [Serializable] public class MotionStep
    {
        public MotionAction action;
        public string target = "", toward = "";
        public MotionDirection direction;
        public float amount;
        public Pace pace = Pace.normal;
        public Gesture gesture;
        public Emotion emotion;
        public MotionPreset motion;
        public MotionKey[] keys = Array.Empty<MotionKey>();
    }
    [Serializable] public class MotionPlan
    {
        public MotionStep[] steps = Array.Empty<MotionStep>();
        public string summary = "";
        public bool Empty => steps == null || steps.Length == 0;
    }
    public class MotionRequest
    {
        public string text, characterName, surroundings, pose;
        public IReadOnlyList<string> places = Array.Empty<string>(), toys = Array.Empty<string>(), toyStates = Array.Empty<string>(), visible = Array.Empty<string>();
    }
    public interface IMotionPlanner { Task<MotionPlan> PlanAsync(MotionRequest request, CancellationToken token); }
    public interface IVisionProvider { Task<string> DescribeAsync(byte[] jpeg, string question, CancellationToken token); }

    public static class MotionPlans
    {
        public const int MaxSteps = 10, MaxKeys = 16;
        public static readonly string[] ToyKinds = { "ボール", "積み木", "回転おもちゃ" };
        static readonly JsonSerializerSettings Json = new JsonSerializerSettings { Converters = { new StringEnumConverter() } };
        public static MotionPlan Parse(string json, MotionRequest request) => Validate(JsonConvert.DeserializeObject<MotionPlan>(json, Json), request);
        // Never trusts the model: unknown ids are dropped, amounts and angles are clamped, keyframes are ordered.
        public static MotionPlan Validate(MotionPlan plan, MotionRequest request)
        {
            plan = plan ?? new MotionPlan();
            var known = new HashSet<string>((request?.places ?? Array.Empty<string>()).Concat(request?.toys ?? Array.Empty<string>()).Concat(ToyKinds)) { "user" };
            plan.summary = (plan.summary ?? "").Trim(); if (plan.summary.Length > 120) plan.summary = plan.summary.Substring(0, 120);
            plan.steps = (plan.steps ?? Array.Empty<MotionStep>()).Where(s => s != null).Take(MaxSteps).ToArray();
            foreach (var step in plan.steps)
            {
                step.target = known.Contains(step.target ?? "") ? step.target : "";
                step.toward = known.Contains(step.toward ?? "") ? step.toward : "";
                if (float.IsNaN(step.amount) || float.IsInfinity(step.amount)) step.amount = 0;
                float max = step.action == MotionAction.move ? 5 : step.action == MotionAction.turn ? 1080 : step.action == MotionAction.wait || step.action == MotionAction.follow_user || step.action == MotionAction.look_at || step.action == MotionAction.crouch ? 60 : step.action == MotionAction.stack ? 5 : 10;
                step.amount = Mathf.Clamp(step.amount, 0, max);
                step.keys = (step.keys ?? Array.Empty<MotionKey>()).Where(k => k != null).Take(MaxKeys).Select(Clamp).OrderBy(k => k.time).ToArray();
            }
            return plan;
        }
        static MotionKey Clamp(MotionKey key)
        {
            foreach (var field in MotionKey.Fields)
            {
                float value = (float)field.GetValue(key); if (float.IsNaN(value) || float.IsInfinity(value)) value = 0;
                float limit = field.Name == "time" ? 10 : field.Name == "hips_drop" ? 1 : field.Name == "lift" ? .6f : 180;
                field.SetValue(key, Mathf.Clamp(value, field.Name == "time" || field.Name == "hips_drop" || field.Name == "lift" ? 0 : -limit, limit));
            }
            return key;
        }
        public static object Schema()
        {
            object Enum<T>() => new { type = "string", @enum = System.Enum.GetNames(typeof(T)) };
            var number = new { type = "number" };
            var keyProperties = MotionKey.Fields.ToDictionary(f => f.Name, f => (object)number);
            var key = new { type = "object", additionalProperties = false, required = keyProperties.Keys.ToArray(), properties = keyProperties };
            var step = new
            {
                type = "object", additionalProperties = false,
                required = new[] { "action", "target", "toward", "direction", "amount", "pace", "gesture", "emotion", "motion", "keys" },
                properties = new Dictionary<string, object>
                {
                    ["action"] = Enum<MotionAction>(), ["target"] = new { type = "string" }, ["toward"] = new { type = "string" }, ["direction"] = Enum<MotionDirection>(),
                    ["amount"] = number, ["pace"] = Enum<Pace>(), ["gesture"] = Enum<Gesture>(), ["emotion"] = Enum<Emotion>(), ["motion"] = Enum<MotionPreset>(),
                    ["keys"] = new { type = "array", items = key }
                }
            };
            return new { type = "object", additionalProperties = false, required = new[] { "steps", "summary" }, properties = new { steps = new { type = "array", items = step }, summary = new { type = "string" } } };
        }
        public const string Instructions =
@"You turn a Japanese request from the user into body actions for a small 3D character standing in a VR room. Return steps=[] when the utterance is ordinary chat that does not ask the character to move, act, play or look at something. Otherwise return up to 10 steps that run in order, doing exactly and completely what was asked; split compound requests into several steps (e.g. 「ボールを出してこっちに転がして」 = spawn_toy ボール, then roll toward the user).
target / toward: an id from places or toys, ""user"", or """" for none. For spawn_toy, target is ボール, 積み木 or 回転おもちゃ.
Actions:
- walk_to: walk beside target. approach_user: come close to the user. move: step in direction (forward/back/left/right are the character's own, to_user, away_from_user, to_object with toward) by amount metres (default 1).
- turn: direction left/right (amount degrees, default 90), back, to_user or to_object.
- look_at: look at target for amount seconds. sit: sit on target seat (no target: sit on the floor). crouch (amount seconds), stand, point at target.
- gesture: one gesture value, amount repetitions.
- motion: whole-body movement. Use a preset when it fits: jump, bow, banzai, clap, dance, stretch, spin (turn around on the spot), shake_head, nod, wave_both, sit_floor, think, cheer; amount = repetitions. For any other movement use motion=custom and write keys: 2-12 keyframes with time in seconds (ascending, starting at 0), angles in degrees relative to relaxed standing: head_pitch (+looks down), head_yaw (+right), head_roll (+tilts right), chest_pitch (+bends forward), chest_yaw (+twists right), chest_roll (+leans right), left/right_arm_up (0 hanging, 90 forward and level, 180 straight up), left/right_arm_out (0 at the side, 90 level to the side), left/right_elbow (0 straight, 140 fully bent), left/right_leg (+lifts the leg forward), left/right_knee (+bends), hips_drop (0 standing, 1 deep squat), lift (metres the whole body rises, 0.3 = a hop). Left and right are the character's own. End near all zeros unless the pose should stay.
- follow_user: follow the user around for amount seconds (default 20). wait: pause amount seconds. stop: stop everything (alone, when told to stop).
- Toys: spawn_toy (put a new toy near the character), roll (roll the ball; direction/toward give where to, to_user by default), kick (kick the ball hard), throw_ball (toss it in an arc), bounce (bounce it on the spot), fetch (bring target toy to the user), pick_up, put_down (put the carried toy down), stack (build a tower with the blocks, amount blocks), knock_down (knock the blocks over), line_up (line the blocks up), spin_toy (spin the top), put_away (tidy all toys away).
- look_closely: really look with its eyes and tell what it sees (asked what it sees, 「これ見て」, 「何が見える」, to check something).
If a toy that the request needs is missing, spawn it first. pace: slow/normal/hurry for walking. emotion: the feeling to show. Unused fields: target """", toward """", direction none, amount 0, gesture none, motion custom, keys []. summary: one short Japanese sentence in the character's voice saying what it will do, or why it cannot.";
    }

    // Instruction → motion plan with OpenAI structured output, and descriptions of what the character's eye camera sees.
    public sealed class OpenAIMotionPlanner : IMotionPlanner, IVisionProvider
    {
        readonly ApiTransport transport; readonly AppSettings settings;
        const string Url = "https://api.openai.com/v1/responses";
        public OpenAIMotionPlanner(ApiTransport transport, AppSettings settings) { this.transport = transport; this.settings = settings; }
        public async Task<MotionPlan> PlanAsync(MotionRequest request, CancellationToken token)
        {
            var input = JsonConvert.SerializeObject(new { request = request.text, character = request.characterName, pose = request.pose, places = request.places, toys = request.toyStates, visible = request.visible, room = request.surroundings });
            var result = await transport.JsonAsync("OpenAI", Url, new
            {
                model = settings.llmModel, store = false, max_output_tokens = 2000, instructions = MotionPlans.Instructions, input,
                text = new { format = new { type = "json_schema", name = "motion_plan", strict = true, schema = MotionPlans.Schema() } }
            }, token);
            try { return MotionPlans.Parse(OpenAIProvider.ResponseText(result), request); }
            catch (JsonException) { throw new InvalidOperationException("動きの指示を解析できませんでした。"); }
        }
        public async Task<string> DescribeAsync(byte[] jpeg, string question, CancellationToken token)
        {
            var content = new object[] { new { type = "input_text", text = "ユーザーの言葉: " + question }, new { type = "input_image", image_url = "data:image/jpeg;base64," + Convert.ToBase64String(jpeg), detail = "low" } };
            var result = await transport.JsonAsync("OpenAI", Url, new
            {
                model = settings.llmModel, store = false, max_output_tokens = 300,
                instructions = "画像は、VRの部屋にいるキャラクターの目に今映っている光景（CGの部屋で、現実の映像ではない）です。キャラクター自身の視点で、見えている物・人・動き・色を日本語2文以内で具体的に述べてください。写っていない物を推測しないこと。",
                input = new object[] { new { role = "user", content } }
            }, token);
            string text = OpenAIProvider.ResponseText(result);
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("見えたものを説明できませんでした。");
            return text.Length > 300 ? text.Substring(0, 300) : text;
        }
    }
}
