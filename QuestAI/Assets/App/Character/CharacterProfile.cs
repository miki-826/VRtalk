using UnityEngine;
namespace QuestAI
{
    [CreateAssetMenu(menuName="Quest AI/Character")]
    public class CharacterProfile : ScriptableObject
    {
        public string id, displayName, voice = "marin";
        [TextArea] public string personality;
        [TextArea] public string speechStyle;
        public string SpeechInstructions => personality + "\n話し方: " + (string.IsNullOrWhiteSpace(speechStyle) ? DefaultSpeechStyle(id) : speechStyle);
        public static string DefaultSpeechStyle(string characterId) => characterId == "robot"
            ? "小さな好奇心旺盛なロボットとして、少し機械的で抑揚をおさえた、区切りのはっきりした日本語。一定のテンポで話し、ときどき語尾を「〜デス」「〜マス」と固めに言う。発見には温かな驚きを込める。電子音のまねは連発せず、短い言葉で話す。"
            : characterId == "bright"
            ? "明るい、弾むような元気な声。やや高めの響きで、楽しそうな抑揚をつける。早口や叫び声を避け、自然な日本語で一度に1〜3文話す。"
            : "穏やかで柔らかく温かな声。落ち着いた抑揚で、少しゆっくり、親しい相手に話す自然な日本語。ささやきすぎず、一度に1〜3文話す。";
        public GameObject prefab;
        // A user-added VRM (CustomModels) instead of a prefab.
        public string modelPath;
        public Emotion defaultEmotion;
        public float personalSpace = .7f, initiative = .3f;
    }
}
