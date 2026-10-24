using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace QuestAI.Tests
{
    public class PixelCharacterTests
    {
        [Test] public void ActiveModelPreservesRigAndAllFacialChannels()
        {
            var calm=Resources.Load<CharacterProfile>("Profiles/calm");
            var bright=Resources.Load<CharacterProfile>("Profiles/bright");
            Assert.That(calm.prefab.name,Is.EqualTo("WhiteChibi"));Assert.That(bright.prefab,Is.EqualTo(calm.prefab));
            Assert.That(calm.id,Is.Not.EqualTo(bright.id));Assert.That(calm.voice,Is.Not.EqualTo(bright.voice));
            var rig=calm.prefab.GetComponent<CharacterRigBinding>();Assert.NotNull(rig.head);Assert.NotNull(rig.chest);Assert.NotNull(rig.leftArm);Assert.NotNull(rig.rightArm);
            Assert.That(rig.faceMeshes.Length,Is.GreaterThan(0));
            foreach(var face in rig.faceMeshes)foreach(string channel in new[]{"Blink","MouthOpen","LookX","LookY","happy","sad","concerned","excited","embarrassed","curious"})
                Assert.That(face.sharedMesh.GetBlendShapeIndex(channel),Is.GreaterThanOrEqualTo(0),channel);
            foreach(var renderer in calm.prefab.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh=renderer.sharedMesh;Assert.That(mesh.boneWeights.Length,Is.EqualTo(mesh.vertexCount));
                Assert.That(mesh.vertices.All(v=>!float.IsNaN(v.x)&&!float.IsNaN(v.y)&&!float.IsNaN(v.z)),Is.True);
                foreach(var weight in mesh.boneWeights)Assert.That(weight.weight0+weight.weight1+weight.weight2+weight.weight3,Is.EqualTo(1).Within(.01));
            }
        }
    }
}
