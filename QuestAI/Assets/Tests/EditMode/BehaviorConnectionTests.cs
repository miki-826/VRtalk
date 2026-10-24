using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
namespace QuestAI.Tests
{
    public class BehaviorConnectionTests
    {
        [Test] public async Task OverlappingRequestsKeepBothDecisionsWithoutRevivingStaleStatus()
        {
            var status=new BehaviorConnectionStatus(()=>"test-value",()=>"jev-latest");
            var first=new TaskCompletionSource<BehaviorDecision>();var second=new TaskCompletionSource<BehaviorDecision>();
            int calls=0;var provider=status.Observe(new Fake{Run=()=>++calls==1?first.Task:second.Task});
            var a=provider.DecideAsync(new DialogueContext(),CancellationToken.None);var b=provider.DecideAsync(new DialogueContext(),CancellationToken.None);
            second.SetResult(new BehaviorDecision());await b;first.SetResult(new BehaviorDecision());await a;
            Assert.AreEqual(2,status.Successes);Assert.AreEqual(BehaviorConnectionStatus.State.Verified,status.Current);
        }
        [Test] public void UnreadableSavedKeyIsAnErrorNotAnAuthenticatedState()
        {
            var status=new BehaviorConnectionStatus(()=>throw new InvalidOperationException(),()=>"jev-latest");
            Assert.AreEqual(BehaviorConnectionStatus.State.Failed,status.Current);StringAssert.Contains("読み取れません",status.Label);
        }
        sealed class Fake : IBehaviorProvider
        {
            public int calls;
            public Func<Task<BehaviorDecision>> Run=()=>Task.FromResult(new BehaviorDecision());
            public Task<BehaviorDecision> DecideAsync(DialogueContext c,CancellationToken t){calls++;return Run();}
        }
        [Test] public void StoredKeyIsNotAuthenticationAndMissingKeyDoesNotCallProvider()
        {
            string key="";var status=new BehaviorConnectionStatus(()=>key,()=>"jev-latest");var fake=new Fake();var provider=status.Observe(fake);
            Assert.AreEqual(BehaviorConnectionStatus.State.MissingKey,status.Current);
            Assert.ThrowsAsync<InvalidOperationException>(async()=>await provider.DecideAsync(new DialogueContext(),CancellationToken.None));
            Assert.AreEqual(0,fake.calls);Assert.AreEqual(0,status.Successes);
            key="test-value";Assert.AreEqual(BehaviorConnectionStatus.State.Untested,status.Current);
        }
        [Test] public async Task OnlyValidatedResponsesCountAndFailuresReplaceSuccess()
        {
            var status=new BehaviorConnectionStatus(()=>"test-value",()=>"jev-latest");var fake=new Fake();var provider=status.Observe(fake);
            await provider.DecideAsync(new DialogueContext(),CancellationToken.None);
            Assert.AreEqual(BehaviorConnectionStatus.State.Verified,status.Current);Assert.AreEqual(1,status.Successes);
            fake.Run=()=>throw new ProviderException("Jev",401);
            Assert.ThrowsAsync<ProviderException>(async()=>await provider.DecideAsync(new DialogueContext(),CancellationToken.None));
            Assert.AreEqual(BehaviorConnectionStatus.State.Failed,status.Current);Assert.AreEqual(1,status.Successes);
            fake.Run=()=>Task.FromResult(JevBehaviorProvider.Parse(new JObject()));
            Assert.ThrowsAsync<InvalidOperationException>(async()=>await provider.DecideAsync(new DialogueContext(),CancellationToken.None));
            Assert.AreEqual(BehaviorConnectionStatus.State.Failed,status.Current);Assert.AreEqual(1,status.Successes);
        }
        [Test] public void ChangedKeyAndCancellationCannotReportOldResponseAsSuccess()
        {
            string key="old";var status=new BehaviorConnectionStatus(()=>key,()=>"jev-latest");var pending=new TaskCompletionSource<BehaviorDecision>();
            var fake=new Fake{Run=()=>pending.Task};var provider=status.Observe(fake);
            var task=provider.DecideAsync(new DialogueContext(),CancellationToken.None);
            Assert.AreEqual(BehaviorConnectionStatus.State.Requesting,status.Current);key="new";pending.SetResult(new BehaviorDecision());
            Assert.CatchAsync<OperationCanceledException>(async()=>await task);
            Assert.AreEqual(BehaviorConnectionStatus.State.Untested,status.Current);Assert.AreEqual(0,status.Successes);
            var cancel=new CancellationTokenSource();fake.Run=()=>{cancel.Cancel();return Task.FromResult(new BehaviorDecision());};
            Assert.CatchAsync<OperationCanceledException>(async()=>await provider.DecideAsync(new DialogueContext(),cancel.Token));
            Assert.AreEqual(BehaviorConnectionStatus.State.Cancelled,status.Current);Assert.AreEqual(0,status.Successes);
        }
    }
}
