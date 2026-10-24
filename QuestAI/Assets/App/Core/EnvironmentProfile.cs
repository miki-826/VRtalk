using UnityEngine;
namespace QuestAI
{
    [CreateAssetMenu(menuName="Quest AI/Environment")]
    public class EnvironmentProfile : ScriptableObject
    {
        public string id, displayName, sceneName;
        public Vector3 characterPosition = new Vector3(0,0,1.7f);
        public string[] semanticObjects;
    }
}
