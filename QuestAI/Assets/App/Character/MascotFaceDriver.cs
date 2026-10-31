using System.Linq;
using UnityEngine;

namespace QuestAI
{
    // Drives the generated mascots' face: one skinned face mesh with named blend shapes (bead eyes, mouth, blush). The eyes
    // are drawn on the head, so gaze slides them over its surface with the Look shapes. Only one eye shape (happy, wide,
    // sad) shows at a time: the old one closes before the new one opens, so the outlines never mix into odd in-between
    // shapes. Other shapes ease toward their targets.
    public sealed class MascotFaceDriver : FaceDriver
    {
        public SkinnedMeshRenderer face;
        // Gaze in degrees that moves the eyes fully to one side.
        public float eyeRange = 20;
        public static readonly string[] Shapes = { "Blink", "EyeHappy", "EyeWide", "EyeSad", "Smile", "Frown", "Blush", "MouthOpen", "MouthEe", "MouthOh", "LookRight", "LookLeft", "LookUp", "LookDown" };
        const int Blink = 0, EyeHappy = 1, EyeSad = 3, Smile = 4, Blush = 6, MouthOpen = 7, MouthEe = 8, MouthOh = 9, LookRight = 10;
        // Per emotion: eye shape (0 none, else its index) and amount; Smile, Frown, Blush; resting mouth opening before speech.
        struct Mood { public int eye; public float amount, smile, frown, blush, open; }
        static Mood For(Emotion emotion)
        {
            switch (emotion)
            {
                case Emotion.happy: return new Mood { smile = 1, blush = .4f, open = .1f };
                case Emotion.excited: return new Mood { eye = EyeHappy, amount = 1, smile = 1, blush = .7f, open = .45f };
                case Emotion.sad: return new Mood { eye = EyeSad, amount = 1, frown = .8f };
                case Emotion.concerned: return new Mood { eye = EyeSad, amount = .6f, frown = .4f };
                case Emotion.embarrassed: return new Mood { eye = EyeHappy, amount = 1, smile = .5f, blush = 1 };
                case Emotion.curious: return new Mood { eye = 2, amount = .8f, smile = .2f, blush = .1f, open = .15f };
                default: return new Mood { smile = .35f };
            }
        }
        static readonly Mood[] Moods = ((Emotion[])System.Enum.GetValues(typeof(Emotion))).Select(For).ToArray();
        // Mouth shape per spoken vowel (あいうえお): open, ee, oh. Rough share of each vowel in spoken Japanese.
        static readonly Vector3[] Vowels = { new Vector3(1, 0, 0), new Vector3(.25f, 1, 0), new Vector3(.3f, 0, 1), new Vector3(.55f, .7f, 0), new Vector3(.6f, 0, .8f) };
        static readonly float[] VowelOdds = { .32f, .2f, .2f, .12f, .16f };
        readonly int[] index = new int[Shapes.Length];
        readonly float[] weight = new float[Shapes.Length];
        int vowel; float nextVowel;
        void Awake() { if (face) for (int i = 0; i < Shapes.Length; i++) index[i] = face.sharedMesh.GetBlendShapeIndex(Shapes[i]); }
        // Settled weight (0..1) of a shape for an emotion with no speech, blink or gaze; the editor preview uses the same values.
        public static float Target(Emotion emotion, int shape)
        {
            var mood = Moods[Mathf.Clamp((int)emotion, 0, Moods.Length - 1)];
            if (shape >= EyeHappy && shape <= EyeSad) return mood.eye == shape ? mood.amount : 0;
            switch (shape) { case Smile: return mood.smile; case Smile + 1: return mood.frown; case Blush: return mood.blush; case MouthOpen: return mood.open; default: return 0; }
        }
        public override void Drive(Emotion emotion, float blink, float mouth, float yaw, float pitch)
        {
            if (!face) return;
            float dt = Time.deltaTime, t = 1 - Mathf.Exp(-6 * dt), fast = 1 - Mathf.Exp(-18 * dt);
            if (mouth > .08f && Time.time >= nextVowel)
            {
                float roll = Random.value; vowel = 0;
                for (float sum = VowelOdds[0]; vowel < Vowels.Length - 1 && roll > sum; sum += VowelOdds[++vowel]) { }
                nextVowel = Time.time + Random.Range(.08f, .16f);
            }
            // Eye shapes: any shape that is not wanted closes first; the wanted one opens only once the others are gone.
            var mood = Moods[Mathf.Clamp((int)emotion, 0, Moods.Length - 1)]; bool clear = true;
            for (int i = EyeHappy; i <= EyeSad; i++)
                if (i != mood.eye) { weight[i] = Mathf.MoveTowards(weight[i], 0, dt * 9); if (weight[i] > 0) clear = false; }
            if (mood.eye != 0) weight[mood.eye] = Mathf.MoveTowards(weight[mood.eye], clear ? mood.amount : 0, dt * 9);
            for (int i = Smile; i <= Blush; i++) weight[i] = Mathf.Lerp(weight[i], Target(emotion, i), t);
            float speak = Mathf.Clamp01(mouth * 1.15f); var v = Vowels[vowel] * speak;
            // Speech replaces the resting mouth; a smile stays on top of it.
            weight[MouthOpen] = Mathf.Lerp(weight[MouthOpen], Mathf.Max(mood.open * (1 - speak), v.x), fast);
            weight[MouthEe] = Mathf.Lerp(weight[MouthEe], v.y, fast); weight[MouthOh] = Mathf.Lerp(weight[MouthOh], v.z, fast);
            // Closed happy eyes cannot blink further.
            weight[Blink] = blink * (1 - weight[EyeHappy]);
            float x = Mathf.Clamp(yaw / eyeRange, -1, 1), y = Mathf.Clamp(pitch / eyeRange, -1, 1);
            weight[LookRight] = Mathf.Lerp(weight[LookRight], Mathf.Max(0, x), fast); weight[LookRight + 1] = Mathf.Lerp(weight[LookRight + 1], Mathf.Max(0, -x), fast);
            weight[LookRight + 2] = Mathf.Lerp(weight[LookRight + 2], Mathf.Max(0, y), fast); weight[LookRight + 3] = Mathf.Lerp(weight[LookRight + 3], Mathf.Max(0, -y), fast);
            for (int i = 0; i < Shapes.Length; i++) if (index[i] >= 0) face.SetBlendShapeWeight(index[i], weight[i] * 100);
        }
    }
}
