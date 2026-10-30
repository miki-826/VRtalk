using UnityEngine;

namespace QuestAI
{
    // Models whose face is not a set of named blend shapes (VRM expressions, eye bones) take BodySystem's face state here.
    public abstract class FaceDriver : MonoBehaviour
    {
        // blink and mouth 0..1, gaze in degrees relative to the head (yaw right, pitch up).
        public abstract void Drive(Emotion emotion, float blink, float mouth, float yaw, float pitch);
    }
}
