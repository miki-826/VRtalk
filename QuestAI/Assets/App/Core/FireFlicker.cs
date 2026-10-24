using UnityEngine;

namespace QuestAI
{
    // Realtime hearth light layered on top of the baked glow: irregular flicker and a small wandering
    // light centre so shadows and highlights in the room move like firelight.
    [RequireComponent(typeof(Light))]
    public class FireFlicker : MonoBehaviour
    {
        public float baseIntensity = 1.1f;
        public float flicker = .45f;
        public float wander = .05f;
        Light fireLight; Vector3 origin; float seed;
        void Awake(){fireLight=GetComponent<Light>();origin=transform.localPosition;seed=Random.value*100;}
        void Update()
        {
            float t=Time.time;
            float slow=Mathf.PerlinNoise(seed,t*1.3f),fast=Mathf.PerlinNoise(seed+7,t*9f);
            fireLight.intensity=baseIntensity*(1-flicker*.5f+flicker*(slow*.7f+fast*.3f));
            transform.localPosition=origin+new Vector3(Mathf.PerlinNoise(seed+3,t*2)-.5f,Mathf.PerlinNoise(seed+5,t*2.4f)-.5f,0)*wander*2;
        }
    }
}
