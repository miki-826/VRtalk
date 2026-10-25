using UnityEngine;

namespace QuestAI
{
    // Lightweight spring follow-through for the animal companions. No physics bodies or per-frame allocations.
    [DefaultExecutionOrder(80)]
    public sealed class CompanionSecondaryMotion : MonoBehaviour
    {
        public Transform leftEar, rightEar, tail;
        public bool rabbit;
        Quaternion leftRest, rightRest, tailRest;
        CharacterLocomotion legs; BodySystem body;
        float left, right, trailing, lv, rv, tv, previousYaw;
        void Awake()
        {
            if (leftEar) leftRest = leftEar.localRotation;
            if (rightEar) rightRest = rightEar.localRotation;
            if (tail) tailRest = tail.localRotation;
            legs = GetComponent<CharacterLocomotion>(); body = GetComponent<BodySystem>(); previousYaw = transform.eulerAngles.y;
        }
        void LateUpdate()
        {
            float dt = Mathf.Min(Time.deltaTime, .05f); if (dt <= 0) return;
            if (!legs) legs = GetComponent<CharacterLocomotion>();
            float walk = legs ? legs.Speed01 : 0;
            float yaw = transform.eulerAngles.y, turn = Mathf.Clamp(Mathf.DeltaAngle(previousYaw, yaw) / dt, -180, 180); previousYaw = yaw;
            float happy = body && (body.CurrentEmotion == Emotion.happy || body.CurrentEmotion == Emotion.excited) ? 1 : 0;
            float droop = body && (body.CurrentEmotion == Emotion.sad || body.CurrentEmotion == Emotion.concerned) ? 15 : 0;
            float bounce = Mathf.Sin(Time.time * (rabbit ? 7 : 6)) * (walk * 9 + happy * 3);
            left = Mathf.SmoothDampAngle(left, -turn * .07f + bounce + droop, ref lv, .16f, 180, dt);
            right = Mathf.SmoothDampAngle(right, -turn * .07f - bounce - droop, ref rv, .2f, 180, dt);
            trailing = Mathf.SmoothDampAngle(trailing, -turn * .12f + Mathf.Sin(Time.time * 2.6f) * (8 + happy * 12 + walk * 8), ref tv, .23f, 220, dt);
            if (leftEar) leftEar.localRotation = leftRest * Quaternion.Euler(left, 0, left * .35f);
            if (rightEar) rightEar.localRotation = rightRest * Quaternion.Euler(right, 0, right * .35f);
            if (tail) tail.localRotation = tailRest * Quaternion.Euler(0, trailing, trailing * .25f);
        }
    }
}
