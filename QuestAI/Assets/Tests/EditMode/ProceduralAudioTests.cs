using NUnit.Framework;
using UnityEngine;

namespace QuestAI.Tests
{
    public class ProceduralAudioTests
    {
        [Test]
        public void AmbientClipsAreAudibleAndBounded()
        {
            foreach(var clip in new[]{ProceduralAudio.Wind(),ProceduralAudio.Crackle(),ProceduralAudio.Chirp(0),ProceduralAudio.Chirp(5)})
            {
                Assert.NotNull(clip);Assert.Greater(clip.length,.2f,clip.name);
                var data=new float[clip.samples];clip.GetData(data,0);float peak=0;foreach(float s in data)peak=Mathf.Max(peak,Mathf.Abs(s));
                Assert.Greater(peak,.01f,clip.name+" is silent");Assert.LessOrEqual(peak,1.5f,clip.name+" clips");
            }
        }
    }
}
