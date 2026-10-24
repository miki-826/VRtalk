using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace QuestAI.Tests
{
    public class CoreTests
    {
        static JObject Behavior(string gesture="nod")=>JObject.Parse("{\"answers\":{\"emotion\":{\"type\":\"choice\",\"choice\":\"happy\"},\"gesture\":{\"type\":\"choice\",\"choice\":\""+gesture+"\"},\"gaze\":{\"type\":\"choice\",\"choice\":\"user\"},\"conversation\":{\"type\":\"choice\",\"choice\":\"speak\"}}}");
        [Test] public void BehaviorNeverAcceptsAnimationNamesOrNumericEnums(){foreach(var bad in new[]{"RunDangerousAnimation","999","1","small_nod"})Assert.Throws<InvalidOperationException>(()=>JevBehaviorProvider.Parse(Behavior(bad)));}
        [Test] public void BehaviorIsFiniteAndStationary(){var decision=JevBehaviorProvider.Parse(Behavior());Assert.That(decision.gesture,Is.EqualTo(Gesture.nod));Assert.That(decision.movement,Is.EqualTo(Movement.stay));}
        [Test] public void MissingBehaviorFieldsAreErrors(){Assert.Throws<InvalidOperationException>(()=>JevBehaviorProvider.Parse(new JObject()));}
        [Test] public void DuplicateTurnAndStaleCompletionAreRejected(){using var gate=new TurnGate();Assert.True(gate.TryEnter(out int first));Assert.False(gate.TryEnter(out _));var token=gate.Token;gate.Cancel();Assert.True(token.IsCancellationRequested);Assert.False(gate.IsCurrent(first));Assert.True(gate.TryEnter(out int second));gate.Leave(first);Assert.True(gate.Busy);gate.Leave(second);Assert.False(gate.Busy);}
        [Test] public async Task DelayedProviderCannotCommitAfterCancellation(){using var gate=new TurnGate();gate.TryEnter(out int old);var pending=new TaskCompletionSource<string>();int commits=0;async Task Work(){await pending.Task;if(gate.IsCurrent(old))commits++;}var work=Work();gate.Cancel();pending.SetResult("stale");await work;Assert.That(commits,Is.Zero);}
        [Test] public void JapaneseMemoryRetrievalIsCharacterScoped(){using var repo=new LocalRepository();repo.AddMemories(new[]{new MemoryRecord{characterId="calm",content="抹茶のアイスが好き"},new MemoryRecord{characterId="bright",content="抹茶が大好き"},new MemoryRecord{characterId="calm",content="東京に住んでいる"}});var matches=repo.Search("calm","抹茶が食べたい",5);Assert.That(matches.Count,Is.EqualTo(1));Assert.That(matches[0].content,Does.Contain("アイス"));}
        [Test] public void DuplicateMemoriesAreNotInserted(){using var repo=new LocalRepository();repo.AddMemories(new[]{new MemoryRecord{characterId="calm",content="猫が好き"},new MemoryRecord{characterId="calm",content="猫が好き"}});Assert.That(repo.Search("calm","猫が好き",5).Count,Is.EqualTo(1));}
        [Test] public void PersistenceAndDeletionRoundTrip(){string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");try{using(var repo=new LocalRepository(path)){repo.Save("continue","saved");repo.AddMessage(new MessageRecord{characterId="calm",role="user",text="hello"});}using(var repo=new LocalRepository(path)){Assert.That(repo.Load("continue"),Is.EqualTo("saved"));Assert.That(repo.Recent("calm",10).Count,Is.EqualTo(1));repo.DeleteAll();}using(var repo=new LocalRepository(path))Assert.That(repo.Load("continue"),Is.Null);}finally{File.Delete(path);}}
        [Test] public void LastMessagesRemainChronological(){using var repo=new LocalRepository();for(int i=0;i<12;i++)repo.AddMessage(new MessageRecord{characterId="calm",createdAt=1,text=i.ToString()});Assert.That(repo.Recent("calm",3).Select(x=>x.text),Is.EqualTo(new[]{"9","10","11"}));}
        [Test] public void DeleteCharacterMemoryKeepsOthers(){using var repo=new LocalRepository();repo.AddMessage(new MessageRecord{characterId="calm"});repo.AddMessage(new MessageRecord{characterId="bright"});repo.DeleteMemories("calm");Assert.That(repo.Recent("calm",10),Is.Empty);Assert.That(repo.Recent("bright",10).Count,Is.EqualTo(1));}
        [TestCase(4,3)][TestCase(5,0)][TestCase(10,1)][TestCase(16,2)][TestCase(20,3)]public void AutoTimeBoundaries(int hour,int phase)=>Assert.That(EnvironmentLighting.Resolve(TimeMode.Auto,hour),Is.EqualTo(phase));
        [Test] public void LiveUsesDedicatedProtocolAndRestoresRoles(){var request=GPTLiveProvider.StartMessage("gpt-live-1","marin","穏やか",new[]{new MessageRecord{role="user",text="猫が好き"},new MessageRecord{role="assistant",text="そうなんだ"}});Assert.That((string)request["type"],Is.EqualTo("session.start"));Assert.That((string)request["session"]["model"],Is.EqualTo("gpt-live-1"));Assert.That((bool)request["session"]["store"],Is.False);Assert.That((string)request["session"]["input"][1]["content"][0]["type"],Is.EqualTo("output_text"));Assert.That((int)request["session"]["audio"]["format"]["rate"],Is.EqualTo(24000));}
        [Test] public void ResponsesParserSkipsReasoningAndToolOutputs(){var response=JObject.Parse("{\"output\":[{\"type\":\"reasoning\",\"content\":[]},{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"こんにちは\"}]}]}");Assert.That(OpenAIProvider.ResponseText(response),Is.EqualTo("こんにちは"));}
        [Test] public void WavHasConsistentPcmHeader(){var wav=AudioCaptureService.EncodeWav(new[]{0f,1f,-1f},24000);Assert.That(wav.Length,Is.EqualTo(50));Assert.That(BitConverter.ToInt32(wav,40),Is.EqualTo(6));Assert.That(BitConverter.ToInt32(wav,24),Is.EqualTo(24000));}
    }
}
