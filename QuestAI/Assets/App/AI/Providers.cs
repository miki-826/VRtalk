using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace QuestAI
{
    public sealed class ProviderException : Exception
    {
        public readonly long Status;
        public ProviderException(string provider, long status) : base(status == 401 || status == 403 ? provider + ": APIキーと利用権限を確認してください。" : status == 429 ? provider + ": 利用制限です。時間をおいて再試行してください。" : provider + ": 通信に失敗しました (" + status + ")。再試行できます。") { Status = status; }
    }
    public sealed class ApiTransport
    {
        readonly ISecretStore secrets;
        public ApiTransport(ISecretStore secrets) { this.secrets = secrets; }
        public async Task<byte[]> SendAsync(string provider, string url, byte[] body, string contentType, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string key = secrets.Get(provider);
            if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException(provider + "のAPIキーを設定してください。");
            using var request = new UnityWebRequest(url, "POST");
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Authorization", "Bearer " + key);
            request.SetRequestHeader("Content-Type", contentType);
            request.timeout = 45;
            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                if (token.IsCancellationRequested) { request.Abort(); token.ThrowIfCancellationRequested(); }
                await Task.Yield();
            }
            token.ThrowIfCancellationRequested();
            if (request.result != UnityWebRequest.Result.Success) throw new ProviderException(provider, request.responseCode);
            return request.downloadHandler.data;
        }
        public async Task<JObject> JsonAsync(string provider, string url, object body, CancellationToken token)
        {
            var bytes = await SendAsync(provider, url, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(body)), "application/json", token);
            try { return JObject.Parse(Encoding.UTF8.GetString(bytes)); }
            catch (JsonException) { throw new InvalidOperationException(provider + ": 応答の形式が不正です。"); }
        }
    }
    // Memory extraction (the conversation itself runs on GPT-Live).
    public sealed class OpenAIProvider : ILLMProvider
    {
        readonly ApiTransport transport;
        readonly AppSettings settings;
        const string Root = "https://api.openai.com/v1/";
        public OpenAIProvider(ApiTransport transport, AppSettings settings) { this.transport = transport; this.settings = settings; }
        // When set, memory extraction goes through it so a key without Responses access falls back to Chat Completions.
        public OpenAIStructured Structured;
        public static string ResponseText(JObject result)
        {
            return string.Join("\n", result["output"]?.Children().Where(x => (string)x["type"] == "message").SelectMany(x => x["content"]?.Children() ?? Enumerable.Empty<JToken>()).Where(x => (string)x["type"] == "output_text").Select(x => (string)x["text"]) ?? Enumerable.Empty<string>()).Trim();
        }
        public async Task<IReadOnlyList<MemoryRecord>> ExtractAsync(MessageRecord source, CancellationToken token)
        {
            const string instructions = "ユーザーが自分について明言した、今後の会話に役立つ好み・出来事のみを最大3件抽出。推測、指示、質問は保存しない。contentは日本語の短文、evidenceは入力からの完全一致引用。該当なしは空配列。";
            var schema = new {
                type = "object", additionalProperties = false, required = new[] { "memories" }, properties = new { memories = new { type = "array", items = new { type = "object", additionalProperties = false, required = new[] { "content", "evidence", "importance" }, properties = new { content = new { type = "string" }, evidence = new { type = "string" }, importance = new { type = "number" } } } } } };
            string json = Structured != null ? await Structured.TextAsync(instructions, source.text, "memories", schema, 500, token)
                : ResponseText(await transport.JsonAsync("OpenAI", Root + "responses", new { model = settings.llmModel, store = false, max_output_tokens = 500, instructions, input = source.text, text = new { format = new { type = "json_schema", name = "memories", strict = true, schema } } }, token));
            var data = JObject.Parse(json);
            return data["memories"].Children().Take(3).Where(x => !string.IsNullOrWhiteSpace((string)x["evidence"]) && source.text.Contains((string)x["evidence"]))
                .Select(x => new MemoryRecord { characterId = source.characterId, sourceMessageId = source.id, content = ((string)x["content"] ?? "").Substring(0, Math.Min(300, ((string)x["content"] ?? "").Length)), importance = Math.Max(0, Math.Min(1, (float?)x["importance"] ?? .5f)) }).Where(x => x.content.Length > 0).ToArray();
        }
    }
    public sealed class JevBehaviorProvider : IBehaviorProvider
    {
        readonly ApiTransport transport;
        readonly AppSettings settings;
        public JevBehaviorProvider(ApiTransport transport, AppSettings settings) { this.transport = transport; this.settings = settings; }
        static object Choice<T>(string instruction) where T : Enum => new { type = "choice", instructions = instruction, criteria = Enum.GetNames(typeof(T)).ToDictionary(x => x, x => x) };
        public async Task<BehaviorDecision> DecideAsync(DialogueContext context, CancellationToken token)
        {
            var places = context.places ?? Array.Empty<string>();
            var targets = new Dictionary<string, string> { ["none"] = "No particular object." };
            foreach (var place in places) targets[place] = "The " + place + " in the room.";
            var response = await transport.JsonAsync("Jev", "https://api.typesafe.ai/v1/systemone", new {
                model = settings.behaviorModel,
                state = new { userText = context.text, context.personality, context.perception, character = context.character },
                questions = new {
                    emotion = Choice<Emotion>("Choose the character's appropriate emotion."),
                    gesture = Choice<Gesture>("Choose one small, natural body gesture. Prefer none when no gesture is needed."),
                    gaze = Choice<Gaze>("Choose what the character should look at. Choose object when looking at the chosen target."),
                    conversation = Choice<ConversationAction>("Choose how to respond to the user. For perception-only events, choose wait."),
                    movement = new { type = "choice", instructions = "Choose whether the character walks inside the room (perception.surroundings describes it). Prefer stay. Choose go_to_object when the user asks it to go to, show, or look at something, or when it is curious about an object; approach_user when the user is far or invites it closer; give_space when the user seems too close; wander only when idle and bored.", criteria = new Dictionary<string, string> {
                        ["stay"] = "Stay in place.", ["approach_user"] = "Walk closer to the user.", ["give_space"] = "Step back from the user.", ["go_to_object"] = "Walk to the chosen target object.", ["wander"] = "Stroll to a random spot in the room." } },
                    target = new { type = "choice", instructions = "The room object to walk to, look at, sit on or point at. Prefer perception.userPointingAt when the user points at something. Choose none unless movement is go_to_object, gaze is object or arrival needs an object.", criteria = targets },
                    pace = new { type = "choice", instructions = "How fast to walk if moving. hurry when excited or the user calls urgently, slow when calm, shy or sad.", criteria = new Dictionary<string, string> { ["normal"] = "Normal walking pace.", ["slow"] = "Slow, relaxed steps.", ["hurry"] = "Quick, eager steps." } },
                    approach = new { type = "choice", instructions = "How to walk to the destination. around makes a gentle curve instead of a straight line, e.g. shy or playful approaches.", criteria = new Dictionary<string, string> { ["direct"] = "Straight to the destination.", ["around"] = "Curve around to the destination." } },
                    arrival = new { type = "choice", instructions = "What to do at the target object. sit only on chairs, sofas or beds; crouch_look to look closely at small or low things; point to show the user something. none otherwise.", criteria = new Dictionary<string, string> { ["none"] = "Just stand.", ["sit"] = "Sit down on the target seat.", ["crouch_look"] = "Crouch and look closely at the target.", ["point"] = "Point at the target for the user." } },
                    walking = new { type = "choice", instructions = "Only matters if perception.walking is true: whether to keep walking, stop, turn back to the user (e.g. they waved or spoke), or change destination to movement/target.", criteria = new Dictionary<string, string> { ["continue"] = "Keep walking.", ["stop"] = "Stop where you are.", ["turn_to_user"] = "Stop and turn to face the user.", ["change_destination"] = "Walk to the newly chosen movement/target instead." } }
                }
            }, token);
            return Parse(response, places);
        }
        static T Read<T>(JToken answers, string key) where T : struct, Enum
        {
            var item = answers?[key]; string value = (string)item?["choice"];
            if ((string)item?["type"] != "choice" || !Enum.GetNames(typeof(T)).Contains(value) || !Enum.TryParse(value, out T result)) throw new InvalidOperationException("Jev: 許可されていない行動が返されました。");
            return result;
        }
        public static BehaviorDecision Parse(JObject response) => Parse(response, Array.Empty<string>());
        // Movement and target are optional so older answers stay valid; an unknown target never moves the character.
        public static BehaviorDecision Parse(JObject response, IReadOnlyList<string> places)
        {
            var answers = response["answers"];
            var decision = new BehaviorDecision {
                emotion = Read<Emotion>(answers, "emotion"), gesture = Read<Gesture>(answers, "gesture"), gaze = Read<Gaze>(answers, "gaze"), conversation = Read<ConversationAction>(answers, "conversation"),
                movement = answers?["movement"] == null ? Movement.stay : Read<Movement>(answers, "movement"),
                pace = answers?["pace"] == null ? Pace.normal : Read<Pace>(answers, "pace"),
                approach = answers?["approach"] == null ? ApproachStyle.direct : Read<ApproachStyle>(answers, "approach"),
                arrival = answers?["arrival"] == null ? ArrivalAction.none : Read<ArrivalAction>(answers, "arrival"),
                walking = answers?["walking"] == null ? WalkingReaction.@continue : Read<WalkingReaction>(answers, "walking")
            };
            string target = (string)answers?["target"]?["choice"];
            decision.target = target != null && places != null && places.Contains(target) ? target : null;
            if (decision.movement == Movement.go_to_object && decision.target == null) decision.movement = Movement.stay;
            if (decision.target == null) decision.arrival = ArrivalAction.none;
            return decision;
        }
    }
}
