using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QuestAI
{
    // Evidence of completed, validated requests, never inferred from an enabled toggle or stored key.
    public sealed class BehaviorConnectionStatus
    {
        public enum State { MissingKey, Untested, Requesting, Verified, Failed, Cancelled }
        readonly Func<string> key, model;
        string signature;
        State state;
        int requestVersion, configurationVersion;
        public int Successes { get; private set; }
        public string LastError { get; private set; }
        public BehaviorConnectionStatus(Func<string> key,Func<string> model){this.key=key;this.model=model;}
        void Refresh()
        {
            string value;
            try {value=key()??"";}
            catch {signature=null;requestVersion++;configurationVersion++;Successes=0;state=State.Failed;LastError="保存キーを読み取れません。設定し直してください。";return;}
            using var sha=SHA256.Create();
            string current=model()+":"+Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
            if(current==signature)return;
            signature=current;requestVersion++;configurationVersion++;Successes=0;LastError=null;
            state=string.IsNullOrWhiteSpace(value)?State.MissingKey:State.Untested;
        }
        public State Current { get {Refresh();return state;} }
        public string Label
        {
            get {
                switch(Current){
                    case State.MissingKey:return "Jev: キー未設定（API未使用）";
                    case State.Untested:return "Jev: キー保存済み・未検証";
                    case State.Requesting:return "Jev: API応答待ち";
                    case State.Verified:return "Jev: 有効なAPI応答を受信（今回 "+Successes+"回）";
                    case State.Failed:return "Jev: 失敗 / "+LastError;
                    default:return "Jev: 通信中止（成功未確認）";
                }
            }
        }
        public IBehaviorProvider Observe(IBehaviorProvider provider)=>new Observed(this,provider);
        sealed class Observed : IBehaviorProvider
        {
            readonly BehaviorConnectionStatus owner;readonly IBehaviorProvider inner;
            public Observed(BehaviorConnectionStatus owner,IBehaviorProvider inner){this.owner=owner;this.inner=inner;}
            public async Task<BehaviorDecision> DecideAsync(DialogueContext context,CancellationToken token)
            {
                owner.Refresh();token.ThrowIfCancellationRequested();
                if(owner.state==State.MissingKey)throw new InvalidOperationException("JevのAPIキーを設定してください。");
                int version=++owner.requestVersion,configuration=owner.configurationVersion;owner.state=State.Requesting;
                try {
                    var result=await inner.DecideAsync(context,token);token.ThrowIfCancellationRequested();
                    if(result==null)throw new InvalidOperationException("Jev: 有効な行動が返されませんでした。");
                    owner.Refresh();
                    if(configuration!=owner.configurationVersion)throw new OperationCanceledException("Jev設定が変更されたため応答を破棄しました。");
                    owner.Successes++;
                    if(version==owner.requestVersion){owner.state=State.Verified;owner.LastError=null;}
                    return result;
                }
                catch(OperationCanceledException){owner.Refresh();if(version==owner.requestVersion)owner.state=State.Cancelled;throw;}
                catch(Exception error){owner.Refresh();if(version==owner.requestVersion){owner.state=State.Failed;owner.LastError=CharacterBrain.SafeError(error);}throw;}
            }
        }
    }
}
