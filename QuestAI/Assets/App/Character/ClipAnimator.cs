using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace QuestAI
{
    [Serializable] public struct NamedClip { public string name; public AnimationClip clip; }
    // Plays authored clips for models that come with animations: idle/walk blended by speed plus one-shot gestures.
    public class ClipAnimator : MonoBehaviour
    {
        public Animator animator;
        public AnimationClip idle, walk;
        public NamedClip[] gestures;
        PlayableGraph graph; AnimationMixerPlayable mixer; AnimationClipPlayable gesture, walking;
        float speed, gestureWeight, gestureEnd = -1;
        public bool GestureActive => Time.time < gestureEnd;
        void Awake()
        {
            if (!animator) animator = GetComponentInChildren<Animator>();
            if (!animator || !idle || !walk) return;
            animator.applyRootMotion = false;
            graph = PlayableGraph.Create(name + " clips"); graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            mixer = AnimationMixerPlayable.Create(graph, 3);
            graph.Connect(AnimationClipPlayable.Create(graph, idle), 0, mixer, 0);
            walking = AnimationClipPlayable.Create(graph, walk); graph.Connect(walking, 0, mixer, 1);
            AnimationPlayableOutput.Create(graph, "Body", animator).SetSourcePlayable(mixer);
            mixer.SetInputWeight(0, 1); graph.Play();
        }
        public bool Has(string clipName) => Array.Exists(gestures ?? Array.Empty<NamedClip>(), g => g.name == clipName && g.clip);
        public bool Play(string clipName)
        {
            if (!graph.IsValid()) return false;
            var entry = Array.Find(gestures ?? Array.Empty<NamedClip>(), g => g.name == clipName);
            if (!entry.clip) return false;
            if (gesture.IsValid()) { graph.Disconnect(mixer, 2); gesture.Destroy(); }
            gesture = AnimationClipPlayable.Create(graph, entry.clip); gesture.SetTime(0);
            graph.Connect(gesture, 0, mixer, 2); gestureEnd = Time.time + entry.clip.length;
            return true;
        }
        public void SetSpeed(float value) => speed = Mathf.Clamp01(value);
        // Step rhythm follows the walking pace (slow stroll vs. hurry).
        public void SetPace(float scale) { if (walking.IsValid()) walking.SetSpeed(scale); }
        void Update()
        {
            if (!graph.IsValid()) return;
            gestureWeight = Mathf.MoveTowards(gestureWeight, GestureActive ? 1 : 0, Time.deltaTime / .25f);
            float body = 1 - gestureWeight;
            mixer.SetInputWeight(0, body * (1 - speed)); mixer.SetInputWeight(1, body * speed);
            if (gesture.IsValid()) mixer.SetInputWeight(2, gestureWeight);
        }
        void OnDestroy() { if (graph.IsValid()) graph.Destroy(); }
    }
}
