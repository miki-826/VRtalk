using UnityEngine;
namespace QuestAI
{
    [CreateAssetMenu(menuName="Quest AI/Character")]
    public class CharacterProfile : ScriptableObject
    {
        public string id, displayName, voice = "marin";
        [TextArea] public string personality;
        public GameObject prefab;
        public Emotion defaultEmotion;
        public float personalSpace = .7f, initiative = .3f;
    }
}
