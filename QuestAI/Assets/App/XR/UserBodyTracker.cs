using System;
using UnityEngine;

namespace QuestAI
{
    // Turns raw head/hand samples into discrete user gestures. Pure logic so it can be tested without XR devices.
    public sealed class GestureDetector
    {
        public float standingHeight;
        public bool Crouching { get; private set; }
        public string PointingAt { get; private set; }
        float lowSince = -1, highSince = -1;
        // Wave: hand above shoulder height swinging side to side (direction reversals of at least 6 cm).
        float lastExtreme, lastSwingTime = -10; int swingDirection, reversals;
        // Point: an extended arm resting on the same object.
        string candidate; float candidateSince, previousTime = -1;
        public UserGesture Step(float time, float headHeight, Vector3 head, Vector3 headRight, Ray? hand, SceneAwareness world, bool deliberatePoint = false)
        {
            // Standing height follows the highest recent head position, decaying slowly so a new user can be shorter.
            float dt = previousTime < 0 ? 0 : Mathf.Max(0, time - previousTime); previousTime = time;
            standingHeight = Mathf.Max(headHeight, standingHeight - .01f * dt);
            if (standingHeight < .5f) standingHeight = headHeight;
            if (!Crouching)
            {
                lowSince = headHeight < standingHeight * .72f ? (lowSince < 0 ? time : lowSince) : -1;
                if (lowSince >= 0 && time - lowSince > .4f) { Crouching = true; highSince = -1; return UserGesture.crouch; }
            }
            else
            {
                highSince = headHeight > standingHeight * .85f ? (highSince < 0 ? time : highSince) : -1;
                if (highSince >= 0 && time - highSince > .3f) { Crouching = false; lowSince = -1; return UserGesture.stand_up; }
            }
            if (hand.HasValue)
            {
                var origin = hand.Value.origin;
                if (origin.y > head.y - .15f)
                {
                    float x = Vector3.Dot(origin - head, headRight);
                    int direction = x > lastExtreme + .06f ? 1 : x < lastExtreme - .06f ? -1 : 0;
                    if (direction != 0)
                    {
                        if (swingDirection != 0 && direction != swingDirection) { reversals = time - lastSwingTime < .8f ? reversals + 1 : 1; lastSwingTime = time; }
                        swingDirection = direction; lastExtreme = x;
                    }
                    if (reversals >= 3) { reversals = 0; swingDirection = 0; return UserGesture.wave; }
                }
                else { reversals = 0; swingDirection = 0; lastExtreme = Vector3.Dot(origin - head, headRight); }
            }
            if (world != null && hand.HasValue)
            {
                var ray = hand.Value; var target = world.LookedAt(ray.origin, ray.direction, 10);
                bool extended = deliberatePoint || (Vector3.Distance(ray.origin, head) > .4f && ray.origin.y > head.y - .6f);
                string id = extended ? target?.id : null;
                if (deliberatePoint && id != null) { candidate = id; PointingAt = id; return UserGesture.point; }
                if (id != candidate) { candidate = id; candidateSince = time; if (id == null) PointingAt = null; }
                else if (id != null && id != PointingAt && time - candidateSince > .8f) { PointingAt = id; return UserGesture.point; }
            }
            return UserGesture.none;
        }
    }

    public class UserBodyTracker : MonoBehaviour
    {
        public Transform head, trackingSpace;
        public SceneAwareness world;
        public event Action<UserGesture, string> Gestured;
        readonly GestureDetector detector = new GestureDetector();
        Ray? hand, deliberate; bool waveRequested;
        public bool Crouching => detector.Crouching;
        public string PointingAt => detector.PointingAt;
        public UserGesture Last { get; private set; }
        float lastTime = -10;
        public UserGesture Recent => Time.time - lastTime < 5 ? Last : UserGesture.none;
        // Called every frame by XRPointerInput with the controller/hand ray (null when untracked).
        // A deliberate point (PC key) keeps its own ray so a later per-frame update cannot replace it before it is read.
        public void Pointer(Ray? ray, bool deliberatePoint) { hand = ray; if (deliberatePoint && ray.HasValue) deliberate = ray; }
        public void Wave() => waveRequested = true;
        void Update()
        {
            if (!head) return;
            float height = trackingSpace ? trackingSpace.InverseTransformPoint(head.position).y : head.position.y;
            var gesture = detector.Step(Time.time, height, head.position, head.right, deliberate ?? hand, world, deliberate.HasValue);
            deliberate = null;
            if (waveRequested) { waveRequested = false; if (gesture == UserGesture.none) gesture = UserGesture.wave; }
            if (gesture == UserGesture.none) return;
            Last = gesture; lastTime = Time.time;
            Gestured?.Invoke(gesture, gesture == UserGesture.point ? detector.PointingAt : null);
        }
    }
}
