using System.Linq;
using NUnit.Framework;
using UniVRM10;
using UnityEngine;

namespace QuestAI.Tests
{
    public class PixelCharacterTests
    {
        // しろ・こはく・もも・むぎ are separate CC0 VRoid models driven through their VRM expressions, eye bones and spring bones.
        [Test] public void ActiveModelsKeepRigFaceAndSkinning()
        {
            var profiles=new[]{"calm","bright","bunny","cat"}.Select(id=>Resources.Load<CharacterProfile>("Profiles/"+id)).ToArray();
            Assert.That(profiles.Select(p=>p.prefab).Distinct().Count(),Is.EqualTo(4),"Each companion has its own model");
            Assert.That(profiles[0].voice,Is.Not.EqualTo(profiles[1].voice));
            foreach(var profile in profiles)
            {
                var prefab=profile.prefab;
                var rig=prefab.GetComponent<CharacterRigBinding>();Assert.NotNull(rig.head,profile.id);Assert.NotNull(rig.chest);Assert.NotNull(rig.leftArm);Assert.NotNull(rig.rightArm);Assert.NotNull(rig.hips);
                Assert.True(rig.animator.isHuman,profile.id);Assert.NotNull(rig.animator.GetBoneTransform(HumanBodyBones.LeftEye),profile.id+" eye bones for look-at");
                var clips=prefab.GetComponent<ClipAnimator>();Assert.NotNull(clips.idle);Assert.NotNull(clips.walk);Assert.That(clips.gestures.Length,Is.GreaterThan(50));
                var face=prefab.GetComponent<VrmFaceDriver>();Assert.NotNull(face,profile.id);Assert.NotNull(face.vrm);Assert.NotNull(face.vrm.Vrm,profile.id+" VRM object");
                var expression=face.vrm.Vrm.Expression;
                foreach(var clip in new[]{expression.Happy,expression.Sad,expression.Relaxed,expression.Surprised,expression.Aa,expression.Ih,expression.Ou,expression.Ee,expression.Oh,expression.Blink})
                    Assert.That(clip.MorphTargetBindings.Length,Is.GreaterThan(0),profile.id+" "+clip.name);
                Assert.That(face.vrm.SpringBone.Springs.Count,Is.GreaterThan(0),profile.id+" hair and clothes sway");
                foreach(var renderer in prefab.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var mesh=renderer.sharedMesh;Assert.That(mesh.boneWeights.Length,Is.EqualTo(mesh.vertexCount));
                    Assert.That(mesh.vertices.All(v=>!float.IsNaN(v.x)&&!float.IsNaN(v.y)&&!float.IsNaN(v.z)),Is.True);
                }
                float eyes=rig.animator.GetBoneTransform(HumanBodyBones.LeftEye).position.y-prefab.transform.position.y;
                Assert.That(eyes,Is.InRange(1.2f,1.5f),profile.id+" companion-sized");
            }
            foreach(var id in new[]{"bunny","cat"})
            {
                var secondary=Resources.Load<CharacterProfile>("Profiles/"+id).prefab.GetComponent<CompanionSecondaryMotion>();
                Assert.NotNull(secondary.leftEar);Assert.NotNull(secondary.rightEar);Assert.NotNull(secondary.tail);
            }
        }
    }
}
