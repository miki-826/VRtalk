using NUnit.Framework;
using UnityEngine;
namespace QuestAI.Tests
{
    public class PlaygroundTests
    {
        [Test] public void PlacementRejectsWallsFurniturePeopleAndOtherToys()
        {
            var furniture=new SceneObject{id="desk",label="desk",bounds=new Bounds(Vector3.zero,Vector3.one),obstacle=true};
            var world=SceneAwareness.Build(new[]{furniture},new Bounds(Vector3.zero,new Vector3(10,.1f,10)));
            var character=new Vector3(-3,0,-3);var user=new Vector3(-3,0,3);
            bool Valid(Vector3 p)=>CompanionPlayground.CanPlace(world,p,character,user,new[]{new Vector3(3,0,3)});
            Assert.False(Valid(Vector3.zero));Assert.False(Valid(new Vector3(20,0,0)));
            Assert.False(Valid(character));Assert.False(Valid(user));Assert.False(Valid(new Vector3(3.2f,0,3)));
            Assert.True(Valid(new Vector3(2,0,-2)));
        }
        [Test] public void SpeechStyleSurvivesLegacyProfilesAndAllowsModelSpecificOverrides()
        {
            var profile=ScriptableObject.CreateInstance<CharacterProfile>();
            try {
                profile.id="robot";profile.personality="好奇心";
                StringAssert.Contains("ロボット",profile.SpeechInstructions);StringAssert.Contains("好奇心",profile.SpeechInstructions);
                profile.id="calm";StringAssert.Contains("穏やか",profile.SpeechInstructions);
                profile.id="bright";StringAssert.Contains("明るい",profile.SpeechInstructions);
                profile.speechStyle="低めでゆっくり";StringAssert.Contains(profile.speechStyle,profile.SpeechInstructions);
            }finally{Object.DestroyImmediate(profile);}
        }
    }
}
