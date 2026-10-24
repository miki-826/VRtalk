using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace QuestAI
{
    /// <summary>GPT-Live wire protocol. No Realtime response.create/input_audio_buffer events.</summary>
    public sealed class GPTLiveProvider : ILiveProvider
    {
        readonly ClientWebSocket socket=new ClientWebSocket();
        readonly SemaphoreSlim sends=new SemaphoreSlim(1,1);
        readonly CancellationTokenSource lifetime=new CancellationTokenSource();
        readonly TaskCompletionSource<bool> started=new TaskCompletionSource<bool>();
        readonly TaskCompletionSource<bool> closed=new TaskCompletionSource<bool>();
        readonly System.Collections.Generic.Dictionary<string,TaskCompletionSource<bool>> acknowledgements=new System.Collections.Generic.Dictionary<string,TaskCompletionSource<bool>>();
        public event Action<JObject> Event;
        public event Action<string> Failed;
        public bool Ready { get; private set; }
        public bool Finalized { get; private set; }
        public double UsageSeconds { get; private set; }
        Task receiver;bool disposed,closing;
        public static JObject StartMessage(string model,string voice,string personality,System.Collections.Generic.IReadOnlyList<MessageRecord> history)
        {
            var input=new JArray();foreach(var item in history)input.Add(new JObject{["type"]="message",["role"]=item.role,["content"]=new JArray(new JObject{["type"]=item.role=="assistant"?"output_text":"input_text",["text"]=item.text})});
            return new JObject{["type"]="session.start",["event_id"]="start_"+Guid.NewGuid().ToString("N"),["session"]=new JObject{
                ["model"]=model,["store"]=false,["instructions"]="あなたはVR空間のAI会話相手です。日本語で短く自然に1〜3文で話します。ユーザーが話しかけるまで待ち、沈黙時に自分から話題を始めません。記憶や外部の事実が必要な場合だけバックエンドへ委譲してください。身体動作のト書きは読み上げません。性格: "+personality,
                ["input"]=input,["audio"]=new JObject{["format"]=new JObject{["type"]="audio/pcm",["rate"]=24000},["output"]=new JObject{["voice"]=voice}},["delegation"]=new JObject{["type"]="client"}
            }};
        }
        public async Task ConnectAsync(string key,JObject start,CancellationToken token)
        {
            if(string.IsNullOrWhiteSpace(key))throw new InvalidOperationException("OpenAIのAPIキーを設定してください。");
            socket.Options.SetRequestHeader("Authorization","Bearer "+key);socket.Options.KeepAliveInterval=TimeSpan.FromSeconds(20);
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token,lifetime.Token);timeout.CancelAfter(30000);
            try{await socket.ConnectAsync(new Uri("wss://api.openai.com/v1/live/sessions"),timeout.Token);receiver=ReceiveAsync();await SendAsync(start,timeout.Token);using(timeout.Token.Register(()=>started.TrySetCanceled()))await started.Task;await MuteAsync(true,timeout.Token);Ready=true;}
            catch{Dispose();throw;}
        }
        public async Task SendAsync(object data,CancellationToken token)
        {
            using var linked=CancellationTokenSource.CreateLinkedTokenSource(token,lifetime.Token);
            await sends.WaitAsync(linked.Token);
            try{if(socket.State!=WebSocketState.Open)throw new InvalidOperationException("GPT-Live接続が閉じています。再接続してください。");byte[] bytes=Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data));await socket.SendAsync(new ArraySegment<byte>(bytes),WebSocketMessageType.Text,true,linked.Token);}finally{sends.Release();}
        }
        public async Task MuteAsync(bool muted,CancellationToken token)
        {
            string id=Guid.NewGuid().ToString("N");var accepted=new TaskCompletionSource<bool>();acknowledgements[id]=accepted;
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token,lifetime.Token);timeout.CancelAfter(8000);
            try{await SendAsync(new{type=muted?"session.input_audio.mute":"session.input_audio.unmute",event_id=id},timeout.Token);using(timeout.Token.Register(()=>accepted.TrySetCanceled()))await accepted.Task;}
            finally{acknowledgements.Remove(id);}
        }
        public Task AudioAsync(byte[] pcm,CancellationToken token)=>SendAsync(new{type="session.input_audio.append",audio=Convert.ToBase64String(pcm)},token);
        static string Brief(string content)=>content.Length>350?content.Substring(0,350):content;
        public Task ContextAsync(string content,string delegationId,CancellationToken token)=>SendAsync(new{type="session.thinking.append",event_id=Guid.NewGuid().ToString("N"),delegation_id=delegationId,content=Brief(content)},token);
        public Task AnswerAsync(string content,string delegationId,CancellationToken token)=>SendAsync(new{type="session.commentary.append",event_id=Guid.NewGuid().ToString("N"),delegation_id=delegationId,content=Brief(content)},token);
        // GPT-Live has no typed user turn, so typed input goes in as an instruction to answer it aloud.
        public static string TypedInstruction(string text)=>"ユーザーは声を出せないため、次の発言を文字で入力しました。これをユーザーの発言として扱い、いつも通り声で短く返答してください。入力文を読み上げ直さないでください。ユーザーの入力: 「"+text+"」";
        public Task TextAsync(string text,CancellationToken token)=>SendAsync(new{type="session.instructions.append",event_id=Guid.NewGuid().ToString("N"),delegation_id=(string)null,content=TypedInstruction(text)},token);
        async Task ReceiveAsync()
        {
            var buffer=new byte[32768];
            try
            {
                while(!lifetime.IsCancellationRequested&&socket.State==WebSocketState.Open)
                {
                    using var stream=new MemoryStream();WebSocketReceiveResult part;
                    do{part=await socket.ReceiveAsync(new ArraySegment<byte>(buffer),lifetime.Token);if(part.MessageType==WebSocketMessageType.Close)throw new IOException("Closed");stream.Write(buffer,0,part.Count);if(stream.Length>8*1024*1024)throw new IOException("Oversized Live event");}while(!part.EndOfMessage);
                    var message=JObject.Parse(Encoding.UTF8.GetString(stream.ToArray()));string type=(string)message["type"];
                    if(type=="session.started")started.TrySetResult(true);
                    if(type=="session.input_audio.muted"||type=="session.input_audio.unmuted"){string id=(string)message["client_event_id"];if(id!=null&&acknowledgements.TryGetValue(id,out var accepted))accepted.TrySetResult(true);}
                    if(type=="session.usage.updated"||type=="session.closed")UsageSeconds=(double?)message["usage"]?["seconds"]??UsageSeconds;
                    if(type=="session.closed"){Finalized=true;Ready=false;closed.TrySetResult(true);Event?.Invoke(message);break;}
                    if(type=="error")throw new InvalidOperationException(ErrorMessage((string)message["error"]?["code"]));
                    Event?.Invoke(message);
                }
            }
            catch(OperationCanceledException){started.TrySetCanceled();}
            catch(Exception e){Ready=false;started.TrySetException(e);if(!closing)Failed?.Invoke(e is InvalidOperationException?e.Message:"GPT-Liveの接続が切れました。再接続してください。");}
        }
        public async Task CloseAsync()
        {
            if(disposed||closing)return;closing=true;Ready=false;
            using var timeout=new CancellationTokenSource(5000);
            try{if(socket.State==WebSocketState.Open){await SendAsync(new{type="session.close"},timeout.Token);using(timeout.Token.Register(()=>closed.TrySetCanceled()))await closed.Task;}}
            catch{}finally{Dispose();}
        }
        public void Dispose(){if(disposed)return;disposed=true;Ready=false;lifetime.Cancel();socket.Abort();socket.Dispose();}
        public static string ErrorMessage(string code)
        {
            // Never expose the raw server message: it can contain request contents.
            switch(code)
            {
                case "invalid_api_key":case "authentication_error":return "OpenAIのAPIキーを確認してください。設定 > AI接続で入れ直せます。";
                case "insufficient_quota":return "OpenAI APIの利用枠を確認してください。課金設定・残高・利用上限が原因の可能性があります。";
                case "rate_limit_exceeded":return "OpenAIの接続上限に達しました。他の接続を閉じ、少し待って再接続してください。";
                case "model_not_found":return "指定の会話モデルを利用できません。モデル名とAPIプロジェクトの利用権限を確認してください。";
                default:return "GPT-Liveがリクエストを受け付けませんでした。設定の接続テストでAPIキー・モデル・音声設定を確認してください。";
            }
        }
    }
}
