using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QuestAI
{
    public enum Emotion { neutral, happy, sad, concerned, excited, embarrassed, curious }
    public enum Gesture { none, nod, big_nod, head_tilt, wave, look_away, laugh, shrug }
    public enum Gaze { user, @object, away }
    public enum ConversationAction { speak, wait, ask_question, change_topic }
    public enum Movement { stay, approach_user, give_space, go_to_object, wander }
    public enum Pace { normal, slow, hurry }
    public enum ApproachStyle { direct, around }
    public enum ArrivalAction { none, sit, crouch_look, point }
    public enum WalkingReaction { @continue, stop, turn_to_user, change_destination }
    public enum UserGesture { none, wave, point, crouch, stand_up }
    public enum ConversationPhase { Idle, Recording, Thinking, Speaking, Paused }
    public enum TimeMode { Auto, Morning, Day, Evening, Night }
    public enum SubtitleMode { Off, Minimal, Always }

    [Serializable] public class BehaviorDecision
    {
        public Emotion emotion;
        public Gesture gesture;
        public Gaze gaze;
        public ConversationAction conversation;
        public Movement movement = Movement.stay;
        // Scene object id for go_to_object (and gaze=object); null when there is none.
        public string target;
        public Pace pace = Pace.normal;
        public ApproachStyle approach = ApproachStyle.direct;
        public ArrivalAction arrival = ArrivalAction.none;
        // Only used while the character is already walking.
        public WalkingReaction walking = WalkingReaction.@continue;
    }
    [Serializable] public class CharacterState
    {
        public Emotion emotion;
        public float mood = .5f, energy = .7f, interest = .5f, relationship, curiosity = .5f;
        public string currentAction = "idle";
        public long lastInteraction;
    }
    [Serializable] public class PerceptionSnapshot
    {
        public float distance, headAngle, headSpeed, silenceDuration;
        public bool headLookingAtCharacter, speaking;
        public string environment, time, trigger;
        // Character-relative room description and what the user is currently facing (SceneAwareness).
        public string surroundings, userLookingAt, nearbyObject;
        public bool walking;
        public string walkingTo;
        // The user's latest body movement (UserBodyTracker) and what they point at, if anything.
        public string userGesture, userPointingAt;
        public bool userCrouching;
        // What the character's eyes currently see (CharacterVision) and the latest visual event, if any.
        public string[] visible;
        public string sawEvent;
    }
    [Serializable] public class AppSettings
    {
        public string voiceModel = "gpt-live-1";
        public string llmModel = "gpt-4.1-mini", behaviorModel = "jev-latest";
        public string characterId = "calm", environmentId = "LivingRoom", voiceOverride = "";
        public TimeMode timeMode = TimeMode.Auto;
        public SubtitleMode subtitles = SubtitleMode.Minimal;
        public float voiceVolume = .9f, lookStrength = 1, behaviorInterval = 12, uiScale = 1;
        public bool memoryEnabled = true, behaviorEnabled = true, autonomousMovement = true;
        // Instructions become body motion plans (OpenAI); sight reacts to what is in view; images of the view may be described by OpenAI.
        public bool motionPlanning = true, vision = true, visionImages = true;
        // An empty value restores the profile voice, even for saves with a legacy global override.
        public Dictionary<string,string> characterVoices = new Dictionary<string,string>();
        public string VoiceFor(CharacterProfile profile)
        {
            if(characterVoices!=null&&characterVoices.TryGetValue(profile.id,out var selected))
                return string.IsNullOrWhiteSpace(selected)?profile.voice:selected;
            return string.IsNullOrWhiteSpace(voiceOverride)?profile.voice:voiceOverride;
        }
        // Robotic voice effect: 0 = robot character only, 1 = every character, 2 = off.
        public int robotVoice = 1;
    }
    [Serializable] public class MessageRecord
    {
        public string id = Guid.NewGuid().ToString("N"), characterId, sessionId, role, text;
        public long createdAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        public long audioStartMs = -1, audioEndMs = -1;
    }
    [Serializable] public class MemoryRecord
    {
        public string id = Guid.NewGuid().ToString("N"), characterId, content, sourceMessageId;
        public float importance = .5f;
        public long createdAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), lastUsedAt;
    }
    [Serializable] public class SessionSave
    {
        public string characterId, environmentId, sessionId;
        public CharacterState state = new CharacterState();
    }
    public class DialogueContext
    {
        public string text, personality, characterName;
        public CharacterState character;
        public PerceptionSnapshot perception;
        public IReadOnlyList<MemoryRecord> memories;
        // Ids of scene objects the character may walk to or look at.
        public IReadOnlyList<string> places;
    }
    public interface ILLMProvider
    {
        Task<IReadOnlyList<MemoryRecord>> ExtractAsync(MessageRecord source, CancellationToken token);
    }
    public interface IBehaviorProvider { Task<BehaviorDecision> DecideAsync(DialogueContext context, CancellationToken token); }
    public interface ISecretStore { string Get(string name); void Set(string name, string value); void DeleteAll(); }
    public interface IMemoryRepository : IDisposable
    {
        void AddMessage(MessageRecord message);
        IReadOnlyList<MessageRecord> Recent(string characterId, int limit);
        IReadOnlyList<MemoryRecord> Search(string characterId, string text, int limit);
        void AddMemories(IEnumerable<MemoryRecord> records);
        void Save(string key, string json);
        string Load(string key);
        void DeleteMemories(string characterId);
        void DeleteAll();
    }
    public static class MemoryRanking
    {
        public static HashSet<string> Grams(string text)
        {
            var normalized = new string((text ?? "").Normalize(NormalizationForm.FormKC).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
            var result = new HashSet<string>();
            if (normalized.Length == 1) result.Add(normalized);
            for (int i = 0; i + 1 < normalized.Length; i++) result.Add(normalized.Substring(i, 2));
            return result;
        }
        public static IReadOnlyList<MemoryRecord> Rank(IEnumerable<MemoryRecord> source, string characterId, string query, int limit)
        {
            var q = Grams(query); long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return source.Where(m => m.characterId == characterId).Select(m => new { memory = m, overlap = Grams(m.content).Count(q.Contains) })
                .Where(x => x.overlap > 0).OrderByDescending(x => x.overlap / (double)Math.Max(1, q.Count) * 4 + x.memory.importance + .2 / (1 + Math.Max(0, now - x.memory.createdAt) / 86400.0))
                .Take(limit).Select(x => x.memory).ToArray();
        }
    }
    public sealed class TurnGate : IDisposable
    {
        CancellationTokenSource source = new CancellationTokenSource();
        public int Generation { get; private set; }
        public bool Busy { get; private set; }
        public CancellationToken Token => source.Token;
        public bool TryEnter(out int generation)
        {
            generation = Generation;
            if (Busy) return false;
            Busy = true; return true;
        }
        public bool IsCurrent(int generation) => generation == Generation && !source.IsCancellationRequested;
        public void Leave(int generation) { if (IsCurrent(generation)) Busy = false; }
        public void Cancel()
        {
            Generation++; source.Cancel(); source.Dispose(); source = new CancellationTokenSource(); Busy = false;
        }
        public void Dispose() { source.Cancel(); source.Dispose(); }
    }
}
