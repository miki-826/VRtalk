using NUnit.Framework;
namespace QuestAI.Tests
{
    public class JourneyTests
    {
        [Test] public void RepeatedVisitsDoNotUnlockExplorer()
        {
            var journey=new CompanionJourney();journey.Visit("Cafe");journey.Visit("Cafe");journey.Visit("");
            Assert.That(journey.rooms.Count,Is.EqualTo(1));Assert.That(journey.Stamps,Is.Zero);
            journey.Visit("Bedroom");journey.Visit("LivingRoom");Assert.That(journey.Stamps,Is.EqualTo(1));
        }
        [Test] public void ProgressRestoresAndStaysCharacterScoped()
        {
            using var repository=new LocalRepository();var journey=new CompanionJourney{conversations=5,waved=true,memoryGame=true};
            journey.Save(repository,"calm");Assert.That(CompanionJourney.Load(repository,"calm").Stamps,Is.EqualTo(4));
            Assert.That(CompanionJourney.Load(repository,"bright").Stamps,Is.Zero);
            repository.DeleteAll();Assert.That(CompanionJourney.Load(repository,"calm").Stamps,Is.Zero);
        }
    }
}
