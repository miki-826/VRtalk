using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace QuestAI
{
    public interface ILiveProvider : IDisposable
    {
        event Action<JObject> Event;
        event Action<string> Failed;
        bool Ready { get; }
        Task ConnectAsync(string key,JObject start,CancellationToken token);
        Task MuteAsync(bool muted,CancellationToken token);
        Task AudioAsync(byte[] pcm,CancellationToken token);
        Task ContextAsync(string content,string delegationId,CancellationToken token);
        Task AnswerAsync(string content,string delegationId,CancellationToken token);
        Task TextAsync(string text,CancellationToken token);
        Task CloseAsync();
    }
    public interface IAudioCapture
    {
        bool Recording { get; }
        bool LimitReached { get; }
        bool Begin(out string error);
        byte[] DrainPcm();
        void Stop();
    }
}
