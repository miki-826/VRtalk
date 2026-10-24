using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace QuestAI
{
    [Serializable] public struct NamedClip { public string name; public AnimationClip clip; }
    // Plays authored clips for models that come with animations (the robot's own, or the CC0 humanoid library on WhiteChibi):
    // idle / walk / run / carry-walk blended by ground speed, one action clip at a time (once or looped), and a held pose
    // such as crouching. Walking clips are sped up or slowed down to match the real ground speed so the feet do not slide.
    public class ClipAnimator : MonoBehaviour
    {
        public Animator animator;
        public AnimationClip idle, walk, run, carry;
        public NamedClip[] gestures;
        PlayableGraph graph; AnimationMixerPlayable mixer; AnimationClipPlayable action, pose, walking, running, carrying;
        const int Idle = 0, Walk = 1, Run = 2, Carry = 3, Action = 4, Pose = 5;
        float speed, pace = 1, ground, actionWeight, poseWeight, actionEnd = -1;
        bool holding, posing;
        public string Current { get; private set; }
        public bool GestureActive => Time.time < actionEnd;
        public bool Posing => posing;
        public bool Humanoid => animator && animator.isHuman;
        void Awake()
        {
            if (!animator) animator = GetComponentInChildren<Animator>();
            if (!animator || !idle || !walk) return;
            animator.applyRootMotion = false;
            graph = PlayableGraph.Create(name + " clips"); graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            mixer = AnimationMixerPlayable.Create(graph, 6);
            graph.Connect(Clip(idle), 0, mixer, Idle);
            walking = Clip(walk); graph.Connect(walking, 0, mixer, Walk);
            running = Clip(run ? run : walk); graph.Connect(running, 0, mixer, Run);
            carrying = Clip(carry ? carry : walk); graph.Connect(carrying, 0, mixer, Carry);
            AnimationPlayableOutput.Create(graph, "Body", animator).SetSourcePlayable(mixer);
            mixer.SetInputWeight(Idle, 1); graph.Play();
        }
        AnimationClipPlayable Clip(AnimationClip clip)
        {
            var playable = AnimationClipPlayable.Create(graph, clip);
            // Humanoid feet are pinned to where the clip puts them, reducing sliding after retargeting.
            if (Humanoid) playable.SetApplyFootIK(true);
            return playable;
        }
        public AnimationClip Find(string clipName)
        {
            if (string.IsNullOrEmpty(clipName)) return null;
            foreach (var g in gestures ?? Array.Empty<NamedClip>()) if (g.clip && string.Equals(g.name, clipName, StringComparison.OrdinalIgnoreCase)) return g.clip;
            return null;
        }
        public bool Has(string clipName) => Find(clipName);
        public float Length(string clipName) { var clip = Find(clipName); return clip ? clip.length : 0; }
        // One action clip: played once (seconds 0) or looped for the given time; a new one replaces the old.
        public bool Play(string clipName, float seconds = 0, float rate = 1)
        {
            var clip = Find(clipName); if (!graph.IsValid() || !clip) return false;
            if (action.IsValid()) { graph.Disconnect(mixer, Action); action.Destroy(); }
            action = Clip(clip); action.SetTime(0); action.SetSpeed(rate); graph.Connect(action, 0, mixer, Action);
            actionEnd = Time.time + (seconds > 0 ? seconds : clip.length / Mathf.Max(.1f, rate)); Current = clipName;
            return true;
        }
        public void StopAction() { actionEnd = Time.time; }
        // A pose that stays until released (crouching, kneeling); actions and walking still play over it.
        public bool Hold(string clipName)
        {
            var clip = Find(clipName); if (!graph.IsValid() || !clip) return false;
            if (pose.IsValid()) { graph.Disconnect(mixer, Pose); pose.Destroy(); }
            pose = Clip(clip); graph.Connect(pose, 0, mixer, Pose); posing = true; return true;
        }
        public void Release() { posing = false; }
        public void SetSpeed(float value) => speed = Mathf.Clamp01(value);
        // Step rhythm follows the walking pace (slow stroll vs. hurry).
        public void SetPace(float scale) => pace = scale;
        public void SetGroundSpeed(float metersPerSecond) => ground = metersPerSecond;
        public void SetHolding(bool value) => holding = value;
        // How fast a clip moves its body in the world, scaled to this character.
        float Natural(AnimationClip clip, float fallback)
        {
            float scale = Humanoid ? animator.humanScale : 1;
            float clipSpeed = clip ? clip.averageSpeed.magnitude : 0;
            return Mathf.Max(.05f, (clipSpeed > .1f ? clipSpeed : fallback) * scale);
        }
        void Update()
        {
            if (!graph.IsValid()) return;
            actionWeight = Mathf.MoveTowards(actionWeight, GestureActive ? 1 : 0, Time.deltaTime / .2f);
            poseWeight = Mathf.MoveTowards(poseWeight, posing && speed < .3f ? 1 : 0, Time.deltaTime / .3f);
            float body = 1 - actionWeight, stand = body * (1 - poseWeight);
            bool hurry = pace > 1.3f && run;
            float moving = stand * speed;
            mixer.SetInputWeight(Idle, stand * (1 - speed));
            mixer.SetInputWeight(Walk, holding || hurry ? 0 : moving);
            mixer.SetInputWeight(Run, hurry && !holding ? moving : 0);
            mixer.SetInputWeight(Carry, holding ? moving : 0);
            mixer.SetInputWeight(Pose, body * poseWeight);
            if (action.IsValid()) mixer.SetInputWeight(Action, actionWeight);
            if (Humanoid && ground > .01f)
            {
                walking.SetSpeed(Mathf.Clamp(ground / Natural(walk, 1.3f), .3f, 2.5f));
                running.SetSpeed(Mathf.Clamp(ground / Natural(run ? run : walk, 3f), .3f, 2.5f));
                carrying.SetSpeed(Mathf.Clamp(ground / Natural(carry ? carry : walk, 1.1f), .3f, 2.5f));
            }
            else { walking.SetSpeed(pace); running.SetSpeed(pace); carrying.SetSpeed(pace); }
            if (!posing && poseWeight <= 0 && pose.IsValid()) { graph.Disconnect(mixer, Pose); pose.Destroy(); }
        }
        void OnDestroy() { if (graph.IsValid()) graph.Destroy(); }
    }
}
