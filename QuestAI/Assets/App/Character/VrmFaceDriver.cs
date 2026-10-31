using UniVRM10;
using UnityEngine;

namespace QuestAI
{
    // Drives a VRM 1.0 model through its own expressions and look-at. BodySystem runs before Vrm10Instance (order 11000),
    // which applies these weights, rotates the eye bones and then simulates the spring bones on the final pose.
    public sealed class VrmFaceDriver : FaceDriver
    {
        public Vrm10Instance vrm;
        // Expression weight per emotion. Joy closes the eyes on VRoid models, so it stays partial except when excited.
        static readonly (Emotion emotion, ExpressionPreset preset, float weight)[] Map = {
            (Emotion.neutral, ExpressionPreset.relaxed, .12f),
            (Emotion.happy, ExpressionPreset.happy, .4f), (Emotion.happy, ExpressionPreset.relaxed, .15f),
            (Emotion.excited, ExpressionPreset.happy, .85f), (Emotion.excited, ExpressionPreset.surprised, .2f),
            (Emotion.sad, ExpressionPreset.sad, .8f),
            (Emotion.concerned, ExpressionPreset.sad, .4f), (Emotion.concerned, ExpressionPreset.surprised, .12f),
            (Emotion.embarrassed, ExpressionPreset.happy, .3f), (Emotion.embarrassed, ExpressionPreset.sad, .25f),
            (Emotion.curious, ExpressionPreset.surprised, .35f), (Emotion.curious, ExpressionPreset.relaxed, .15f) };
        public static readonly ExpressionPreset[] Moods = { ExpressionPreset.happy, ExpressionPreset.sad, ExpressionPreset.relaxed, ExpressionPreset.surprised };
        public static float MoodWeight(Emotion emotion, ExpressionPreset preset)
        {
            float weight = 0; foreach (var m in Map) if (m.emotion == emotion && m.preset == preset) weight = Mathf.Max(weight, m.weight); return weight;
        }
        static readonly ExpressionPreset[] Vowels = { ExpressionPreset.aa, ExpressionPreset.ih, ExpressionPreset.ou, ExpressionPreset.ee, ExpressionPreset.oh };
        // Rough share of each vowel in spoken Japanese, so the mouth shape changes like speech rather than flapping.
        static readonly float[] VowelOdds = { .32f, .2f, .2f, .12f, .16f };
        readonly float[] mood = new float[4], vowel = new float[5];
        int currentVowel; float nextVowel;
        void Awake() { if (!vrm) vrm = GetComponentInChildren<Vrm10Instance>(); if (vrm) vrm.LookAtTargetType = VRM10ObjectLookAt.LookAtTargetTypes.YawPitchValue; }
        public override void Drive(Emotion emotion, float blink, float mouth, float yaw, float pitch)
        {
            if (!vrm || vrm.Vrm == null) return;
            var runtime = vrm.Runtime; var expression = runtime.Expression; float t = 1 - Mathf.Exp(-6 * Time.deltaTime);
            for (int i = 0; i < Moods.Length; i++)
            {
                mood[i] = Mathf.Lerp(mood[i], MoodWeight(emotion, Moods[i]), t); expression.SetWeight(ExpressionKey.CreateFromPreset(Moods[i]), mood[i]);
            }
            expression.SetWeight(ExpressionKey.Blink, blink);
            if (mouth > .08f && Time.time >= nextVowel)
            {
                float roll = Random.value; currentVowel = 0;
                for (float sum = VowelOdds[0]; currentVowel < Vowels.Length - 1 && roll > sum; sum += VowelOdds[++currentVowel]) { }
                nextVowel = Time.time + Random.Range(.08f, .16f);
            }
            float speak = Mathf.Clamp01(mouth * 1.15f), fast = 1 - Mathf.Exp(-18 * Time.deltaTime);
            for (int i = 0; i < Vowels.Length; i++)
            {
                vowel[i] = Mathf.Lerp(vowel[i], i == currentVowel ? speak * (i == 0 ? 1 : .8f) : 0, fast);
                expression.SetWeight(ExpressionKey.CreateFromPreset(Vowels[i]), vowel[i]);
            }
            runtime.LookAt.SetYawPitchManually(yaw, pitch);
        }
    }
}
