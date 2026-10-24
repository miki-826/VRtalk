using System;
using System.IO;
using NUnit.Framework;
namespace QuestAI.Tests
{
    public class DesktopInputTests
    {
        [Test] public void PasteTrimsButNeverSilentlyTruncatesKeys()
        {
            Assert.True(ClipboardInput.TryNormalize("  sk-test\r\n",true,out var value));Assert.That(value,Is.EqualTo("sk-test"));
            Assert.False(ClipboardInput.TryNormalize("sk-\nother",true,out _));Assert.False(ClipboardInput.TryNormalize(new string('x',4097),true,out _));
            Assert.False(ClipboardInput.TryNormalize("",true,out _));Assert.True(ClipboardInput.TryNormalize("some value",false,out _));
        }
#if UNITY_EDITOR_WIN
        [Test] public void WindowsKeysPersistEncryptedAndDelete()
        {
            string path=Path.Combine(Path.GetTempPath(),"questai-secret-test-"+Guid.NewGuid());
            try
            {
                var store=new WindowsSecretStore(path);store.Set("OpenAI","fake-test-key");
                Assert.That(new WindowsSecretStore(path).Get("OpenAI"),Is.EqualTo("fake-test-key"));
                Assert.False(System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(path,"OpenAI.bin"))).Contains("fake-test-key"));
                store.Set("OpenAI","replacement");Assert.That(store.Get("OpenAI"),Is.EqualTo("replacement"));store.DeleteAll();Assert.That(store.Get("OpenAI"),Is.Empty);
            }
            finally{if(Directory.Exists(path)){new WindowsSecretStore(path).DeleteAll();Directory.Delete(path);}}
        }
#endif
    }
}
