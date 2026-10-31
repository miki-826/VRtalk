using System.Linq;
using UnityEngine;

namespace QuestAI
{
    // Drives the generated companions' face: one skinned face mesh with named blend shapes (eyes, brows, mouth, blush) and
    // two eye bones that turn the irises. Shapes ease toward their targets so expressions never pop.
    public sealed class AnimeFaceDriver : FaceDriver
    {
        public SkinnedMeshRenderer face;
        public Transform leftEye, rightEye;
        // Largest iris turn in degrees; the irises are drawn on the face, so they only need a small swing.
        public float eyeRange = 9;
        public static readonly string[] Shapes = { "Blink", "EyeHappy", "EyeWide", "BrowSad", "BrowUp", "Smile", "Frown", "Blush", "MouthOpen", "MouthEe", "MouthOh" };
        const int Blink = 0, MouthOpen = 8, MouthEe = 9, MouthOh = 10;
        // Mood weights per emotion over Shapes[1..7]: EyeHappy, EyeWide, BrowSad, BrowUp, Smile, Frown, Blush; then the resting
        // mouth opening before speech (a smiling "excited" face shows its open mouth).
        static float[] Mood(Emotion emotion)
        {
            switch (emotion)
            {
                case Emotion.happy: return new[] { .45f, 0, 0, .15f, 1, 0, .35f, .08f };
                case Emotion.excited: return new[] { .9f, 0, 0, .5f, 1, 0, .6f, .35f };
                case Emotion.sad: return new[] { .1f, 0, 1, 0, 0, .8f, 0, 0 };
                case Emotion.concerned: return new[] { 0f, .1f, .7f, 0, 0, .35f, 0, 0 };
                case Emotion.embarrassed: return new[] { .3f, 0, .45f, 0, .45f, 0, 1, 0 };
                case Emotion.curious: return new[] { 0f, .55f, 0, .7f, .15f, 0, .1f, .12f };
                default: return new[] { 0f, 0, 0, 0, .3f, 0, 0, 0 };
            }
        }
        static readonly float[][] Moods = ((Emotion[])System.Enum.GetValues(typeof(Emotion))).Select(Mood).ToArray();
        // Mouth shape per spoken vowel (あいうえお): open, ee, oh. Rough share of each vowel in spoken Japanese.
        static readonly Vector3[] Vowels = { new Vector3(1, 0, 0), new Vector3(.25f, 1, 0), new Vector3(.3f, 0, 1), new Vector3(.55f, .7f, 0), new Vector3(.6f, 0, .8f) };
        static readonly float[] VowelOdds = { .32f, .2f, .2f, .12f, .16f };
        readonly int[] index = new int[Shapes.Length];
        readonly float[] weight = new float[Shapes.Length];
        Quaternion leftRest = Quaternion.identity, rightRest = Quaternion.identity;
        int vowel; float nextVowel;
        void Awake()
        {
            if (!face) return;
            for (int i = 0; i < Shapes.Length; i++) index[i] = face.sharedMesh.GetBlendShapeIndex(Shapes[i]);
            if (leftEye) leftRest = leftEye.localRotation; if (rightEye) rightRest = rightEye.localRotation;
        }
        // Target weight (0..1) of every shape for an emotion with no speech or blink; the editor preview uses the same values.
        public static float Target(Emotion emotion, int shape)
        {
            var mood = Moods[Mathf.Clamp((int)emotion, 0, Moods.Length - 1)];
            if (shape >= 1 && shape <= 7) return mood[shape - 1];
            return shape == MouthOpen ? mood[7] : 0;
        }
        public override void Drive(Emotion emotion, float blink, float mouth, float yaw, float pitch)
        {
            if (!face) return;
            float t = 1 - Mathf.Exp(-6 * Time.deltaTime), fast = 1 - Mathf.Exp(-18 * Time.deltaTime);
            if (mouth > .08f && Time.time >= nextVowel)
            {
                float roll = Random.value; vowel = 0;
                for (float sum = VowelOdds[0]; vowel < Vowels.Length - 1 && roll > sum; sum += VowelOdds[++vowel]) { }
                nextVowel = Time.time + Random.Range(.08f, .16f);
            }
            float speak = Mathf.Clamp01(mouth * 1.15f); var v = Vowels[vowel] * speak;
            for (int i = 1; i <= 7; i++) weight[i] = Mathf.Lerp(weight[i], Target(emotion, i), t);
            // Speech replaces the resting mouth; a smile stays on top of it.
            weight[MouthOpen] = Mathf.Lerp(weight[MouthOpen], Mathf.Max(Target(emotion, MouthOpen) * (1 - speak), v.x), fast);
            weight[MouthEe] = Mathf.Lerp(weight[MouthEe], v.y, fast); weight[MouthOh] = Mathf.Lerp(weight[MouthOh], v.z, fast);
            // Closed happy eyes cannot blink further; sad eyes rest a little lowered.
            weight[Blink] = Mathf.Max(blink * (1 - weight[1]), emotion == Emotion.sad ? .2f : 0);
            for (int i = 0; i < Shapes.Length; i++) if (index[i] >= 0) face.SetBlendShapeWeight(index[i], weight[i] * 100);
            var turn = Quaternion.Euler(-Mathf.Clamp(pitch, -eyeRange, eyeRange) * .7f, Mathf.Clamp(yaw, -eyeRange, eyeRange), 0);
            if (leftEye) leftEye.localRotation = leftRest * turn; if (rightEye) rightEye.localRotation = rightRest * turn;
        }
    }
}
