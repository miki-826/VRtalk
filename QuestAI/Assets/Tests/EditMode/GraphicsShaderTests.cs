using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace QuestAI.Tests
{
    public class GraphicsShaderTests
    {
        [TestCase("QuestAI/Hearth Fire")]
        [TestCase("QuestAI/Architectural Glass")]
        [TestCase("QuestAI/Natural Leaves")]
        [TestCase("QuestAI/Realistic Surface")]
        [TestCase("QuestAI/Hearth Embers")]
        [TestCase("QuestAI/Wind Impostor")]
        public void EffectsAreSupportedWithoutCompileErrors(string name)
        {
            var shader=Shader.Find(name);Assert.NotNull(shader);Assert.True(shader.isSupported,name);
            Assert.False(ShaderUtil.ShaderHasError(shader),name);
        }
    }
}
