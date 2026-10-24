using NUnit.Framework;
namespace QuestAI.Tests
{
    public class TextInputTests
    {
        static string Type(params string[] keys){var value="";foreach(var key in keys)value=KanaInput.Press(value,key,10);return value;}
        [Test] public void KanaModifiersChangeOnlyTheLastCharacter()
        {
            Assert.That(Type("か","゛"),Is.EqualTo("が"));Assert.That(Type("か","゛","゛"),Is.EqualTo("か"));
            Assert.That(Type("は","゜"),Is.EqualTo("ぱ"));Assert.That(Type("は","゛","゜"),Is.EqualTo("ぱ"));Assert.That(Type("は","゜","゛"),Is.EqualTo("ば"));
            Assert.That(Type("つ","小"),Is.EqualTo("っ"));Assert.That(Type("き","よ","小"),Is.EqualTo("きょ"));
            Assert.That(Type("あ","゛"),Is.EqualTo("あ"));Assert.That(Type("゛"),Is.Empty);
        }
        [Test] public void KanaInputStopsAtMaximumLength()
        {
            var value="";for(int i=0;i<15;i++)value=KanaInput.Press(value,"あ",10);Assert.That(value.Length,Is.EqualTo(10));
        }
        [Test] public void TypedInstructionCarriesTheUserText()
        {
            var instruction=GPTLiveProvider.TypedInstruction("のどが痛い");
            StringAssert.Contains("「のどが痛い」",instruction);StringAssert.Contains("声で",instruction);
        }
    }
}
