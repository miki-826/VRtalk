using System.Linq;
using System.IO;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

namespace QuestAI.Tests
{
    public class MascotModelTests
    {
        [Test] public void CharacterVoicesPersistIndependentlyWithRobotEffectEnabled()
        {
            var calm=Resources.Load<CharacterProfile>("Profiles/calm");
            var bright=Resources.Load<CharacterProfile>("Profiles/bright");
            var settings=new AppSettings();
            Assert.That(settings.robotVoice,Is.EqualTo(1));
            Assert.That(settings.VoiceFor(calm),Is.EqualTo(calm.voice));
            settings.characterVoices[calm.id]="gleam";
            var restored=JsonConvert.DeserializeObject<AppSettings>(JsonConvert.SerializeObject(settings));
            Assert.That(restored.VoiceFor(calm),Is.EqualTo("gleam"));
            Assert.That(restored.VoiceFor(bright),Is.EqualTo(bright.voice));
            restored.voiceOverride="marin";
            restored.characterVoices[bright.id]="";
            Assert.That(restored.VoiceFor(bright),Is.EqualTo(bright.voice));
        }
        [Test] public void UploadedAvatarVoiceUsesDescriptionUnlessTextOverridesIt()
        {
            string path=Path.Combine(Path.GetTempPath(),"元気な子_"+System.Guid.NewGuid().ToString("N")+".vrm");
            string notes=Path.ChangeExtension(path,".txt");
            CharacterProfile suggested=null, described=null, explicitVoice=null;
            try
            {
                suggested=CustomModels.Profile(path);
                Assert.That(suggested.voice,Is.EqualTo("gleam"));
                File.WriteAllText(notes,"静かな子\n落ち着いて話す。");
                described=CustomModels.Profile(path);
                Assert.That(described.voice,Is.EqualTo("marin"));
                File.WriteAllText(notes,"静かな子\n声: gleam\n落ち着いて話す。");
                explicitVoice=CustomModels.Profile(path);
                Assert.That(explicitVoice.voice,Is.EqualTo("gleam"));
                Assert.That(explicitVoice.personality,Is.EqualTo("落ち着いて話す。"));
            }
            finally
            {
                if(suggested)Object.DestroyImmediate(suggested);
                if(described)Object.DestroyImmediate(described);
                if(explicitVoice)Object.DestroyImmediate(explicitVoice);
                if(File.Exists(notes))File.Delete(notes);
            }
        }
        // しろ（いぬ）・こはく（ロボ）・もも（うさぎ）・むぎ（ねこ） are chibi mascots generated from scratch (MascotCharacterBuilder).
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
                var face=prefab.GetComponent<MascotFaceDriver>();Assert.NotNull(face,profile.id);Assert.NotNull(face.face);
                foreach(var shape in MascotFaceDriver.Shapes){int index=face.face.sharedMesh.GetBlendShapeIndex(shape);Assert.That(index,Is.GreaterThanOrEqualTo(0),profile.id+" "+shape);var deltas=new Vector3[face.face.sharedMesh.vertexCount];face.face.sharedMesh.GetBlendShapeFrameVertices(index,0,deltas,null,null);Assert.That(deltas.Any(d=>d.sqrMagnitude>0),profile.id+" "+shape+" moves the face");}
                foreach(var renderer in prefab.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var mesh=renderer.sharedMesh;Assert.That(mesh.boneWeights.Length,Is.EqualTo(mesh.vertexCount));
                    Assert.That(mesh.vertices.All(v=>!float.IsNaN(v.x)&&!float.IsNaN(v.y)&&!float.IsNaN(v.z)),Is.True);
                }
                foreach(var filter in prefab.GetComponentsInChildren<MeshFilter>())Assert.That(filter.sharedMesh.vertices.All(v=>!float.IsNaN(v.x)&&!float.IsNaN(v.y)&&!float.IsNaN(v.z)),Is.True,profile.id+" "+filter.name);
                float eyes=rig.animator.GetBoneTransform(HumanBodyBones.LeftEye).position.y-prefab.transform.position.y;
                Assert.That(eyes,Is.InRange(.6f,1f),profile.id+" chibi-sized");
            }
            foreach(var id in new[]{"calm","bunny","cat"})
            {
                var secondary=Resources.Load<CharacterProfile>("Profiles/"+id).prefab.GetComponent<CompanionSecondaryMotion>();
                Assert.NotNull(secondary.leftEar);Assert.NotNull(secondary.rightEar);Assert.NotNull(secondary.tail);
            }
        }
    }
}
