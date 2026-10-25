using System;
using UnityEngine;
namespace QuestAI
{
    [Serializable] public struct EmotionShape { public Emotion emotion; public string shape; public float weight; }
    public class CharacterRigBinding : MonoBehaviour
    {
        public Animator animator;
        public Transform head, chest, leftArm, rightArm, leftForearm, rightForearm;
        public Transform leftLeg, rightLeg, leftKnee, rightKnee, hips;
        // Corrects a retargeted library whose body faces opposite to this model's authored forward axis.
        public float animationYawOffset;
        public SkinnedMeshRenderer[] faceMeshes;
        public Vector3 leftArmRestEuler = new Vector3(0,0,-65), rightArmRestEuler = new Vector3(0,0,65);
        public string blinkShape = "Blink", mouthShape = "MouthOpen", lookXShape = "LookX", lookYShape = "LookY";
        // Empty: each Emotion drives the blend shape with the same name at 70 (WhiteChibi). Other models map their own shapes.
        public EmotionShape[] emotionShapes = Array.Empty<EmotionShape>();
    }
}
