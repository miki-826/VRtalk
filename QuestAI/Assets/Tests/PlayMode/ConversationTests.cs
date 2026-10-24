using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace QuestAI.Tests
{
    public class ConversationTests
    {
        sealed class Live : ILiveProvider
        {
            public event Action<JObject> Event;
            public event Action<string> Failed;
            public bool Ready {get;private set;}
            public readonly List<string> calls=new List<string>();
            public TaskCompletionSource<bool> wait;
            public TaskCompletionSource<bool> muteWait;
            public async Task ConnectAsync(string k,JObject s,CancellationToken t){calls.Add("connect");if(wait!=null)await wait.Task;Ready=true;}
            public Task MuteAsync(bool m,CancellationToken t){calls.Add(m?"mute":"unmute");return muteWait?.Task??Task.CompletedTask;}
            public Task AudioAsync(byte[] b,CancellationToken t){calls.Add("audio");return Task.CompletedTask;}
            public Task ContextAsync(string c,string d,CancellationToken t)=>Task.CompletedTask;
            public Task AnswerAsync(string c,string d,CancellationToken t)=>Task.CompletedTask;
            public readonly List<string> texts=new List<string>();
            public Task TextAsync(string text,CancellationToken t){calls.Add("text");texts.Add(text);return Task.CompletedTask;}
            public Task CloseAsync(){Dispose();return Task.CompletedTask;}
            public void Dispose(){Ready=false;calls.Add("close");}
            public void Emit(string type,string delta)=>Event?.Invoke(new JObject{["type"]=type,["delta"]=delta,["start_ms"]=123,["end_ms"]=456});
            public void Fail()=>Failed?.Invoke("simulated disconnect");
        }
        sealed class Capture : IAudioCapture
        {
            public bool Recording{get;private set;}
            public bool LimitReached=>false;public int starts;
            public bool Begin(out string error){error="";starts++;Recording=true;return true;}
            public byte[] DrainPcm()=>new byte[2400];
            public void Stop()=>Recording=false;
        }
        sealed class Model : ILLMProvider,IBehaviorProvider
        {
            public Task<string> ReplyAsync(DialogueContext c,CancellationToken t)=>Task.FromResult("unused");
            public Task<BehaviorDecision> DecideAsync(DialogueContext c,CancellationToken t)=>Task.FromResult(new BehaviorDecision{emotion=Emotion.happy,gesture=Gesture.nod});
            public Task<IReadOnlyList<MemoryRecord>> ExtractAsync(MessageRecord s,CancellationToken t)=>Task.FromResult<IReadOnlyList<MemoryRecord>>(new[]{new MemoryRecord{characterId=s.characterId,sourceMessageId=s.id,content=s.text}});
        }
        sealed class Planner : IMotionPlanner,IVisionProvider
        {
            public readonly List<MotionRequest> requests=new List<MotionRequest>();
            public Task<MotionPlan> PlanAsync(MotionRequest r,CancellationToken t){requests.Add(r);return Task.FromResult(new MotionPlan{summary="うなずくね",steps=new[]{new MotionStep{action=MotionAction.gesture,gesture=Gesture.nod,amount=2}}});}
            public Task<string> DescribeAsync(byte[] jpeg,string q,CancellationToken t)=>Task.FromResult("赤いボールが見える");
        }
        GameObject avatar,head;CharacterBrain brain;Capture capture;Live live;LocalRepository repo;
        [SetUp] public void Setup()
        {
            var profile=Resources.Load<CharacterProfile>("Profiles/calm");Assert.NotNull(profile);
            avatar=UnityEngine.Object.Instantiate(profile.prefab);head=new GameObject("Test head");head.transform.position=new Vector3(0,1.55f,-2);
            brain=avatar.AddComponent<CharacterBrain>();brain.profile=profile;brain.body=avatar.GetComponent<BodySystem>();brain.user=head.transform;brain.body.user=head.transform;brain.settings=new AppSettings();repo=new LocalRepository();brain.repository=repo;brain.secrets=new SessionSecrets();var model=new Model();brain.llm=model;brain.behavior=model;capture=new Capture();live=new Live();brain.CaptureOverride=capture;brain.LiveFactory=()=>live;brain.Initialize(null,"test-session");
        }
        [TearDown] public void Cleanup(){UnityEngine.Object.DestroyImmediate(avatar);UnityEngine.Object.DestroyImmediate(head);repo.Dispose();}
        [UnityTest] public IEnumerator PttIsExclusiveAndCommitsSourceBeforeMemory()
        {
            brain.Connect();brain.BeginRecording();brain.BeginRecording();Assert.That(capture.starts,Is.EqualTo(1));Assert.That(brain.Phase,Is.EqualTo(ConversationPhase.Recording));
            brain.EndRecording();Assert.False(capture.Recording);Assert.That(live.calls,Is.EqualTo(new[]{"connect","unmute","audio","mute"}));
            live.Emit("session.input_transcript.delta","猫が好き");live.Emit("session.output_transcript.delta","覚えておくね");
            Assert.That(repo.Recent("calm",10)[0].text,Is.EqualTo("猫が好き"));Assert.That(repo.Recent("calm",10)[0].audioStartMs,Is.EqualTo(123));Assert.That(repo.Search("calm","猫が好き",4)[0].sourceMessageId,Is.EqualTo(repo.Recent("calm",10)[0].id));
            brain.Disconnect();Assert.That(repo.Recent("calm",10).Count,Is.EqualTo(2));yield return null;
        }
        [UnityTest] public IEnumerator PauseRejectsLateEventsAndResumeDoesNotRecord()
        {
            brain.Connect();brain.BeginRecording();brain.Suspend();live.Emit("session.input_transcript.delta","古い応答");live.Emit("session.output_transcript.delta","古い返答");
            Assert.False(capture.Recording);Assert.That(brain.Phase,Is.EqualTo(ConversationPhase.Paused));brain.Resume();Assert.False(brain.Connected);Assert.False(capture.Recording);Assert.That(repo.Recent("calm",10),Is.Empty);yield return null;
        }
        [UnityTest] public IEnumerator DelayedConnectCannotReplaceNewSession()
        {
            var old=live;old.wait=new TaskCompletionSource<bool>();brain.Connect();brain.Suspend();brain.Resume();live=new Live();brain.Connect();Assert.True(brain.Connected);
            old.wait.SetResult(true);yield return null;Assert.True(brain.Connected);Assert.That(brain.Phase,Is.EqualTo(ConversationPhase.Idle));Assert.False(old.Ready);
        }
        [UnityTest] public IEnumerator ConnectionFailureCanRetryWithoutMicrophoneRestart()
        {
            brain.Connect();brain.BeginRecording();live.Fail();Assert.False(capture.Recording);Assert.False(brain.Connected);live=new Live();brain.Connect();Assert.True(brain.Connected);Assert.False(capture.Recording);yield return null;
        }
        [UnityTest] public IEnumerator RemoteCloseStopsRecordingAndRejectsLateEvents()
        {
            brain.Connect();brain.BeginRecording();live.Emit("session.closed","");
            Assert.False(capture.Recording);Assert.False(brain.Connected);Assert.That(brain.Phase,Is.EqualTo(ConversationPhase.Idle));
            live.Emit("session.input_transcript.delta","終了後の発言");Assert.That(repo.Recent("calm",10),Is.Empty);
            live=new Live();brain.Connect();brain.BeginRecording();Assert.True(capture.Recording);yield return null;
        }
        [UnityTest] public IEnumerator OldMuteFailureCannotStopNewRecording()
        {
            brain.Connect();var old=live;old.muteWait=new TaskCompletionSource<bool>();brain.BeginRecording();
            brain.Suspend();brain.Resume();live=new Live();brain.Connect();brain.BeginRecording();Assert.True(capture.Recording);
            old.muteWait.SetException(new InvalidOperationException("old request failed"));yield return null;
            Assert.True(capture.Recording);Assert.True(brain.Connected);Assert.That(brain.Phase,Is.EqualTo(ConversationPhase.Recording));
        }
        [UnityTest] public IEnumerator MuteFailureDisconnectsAndAllowsRetry()
        {
            brain.Connect();live.muteWait=new TaskCompletionSource<bool>();live.muteWait.SetException(new InvalidOperationException("mute failed"));brain.BeginRecording();
            Assert.False(brain.Connected);Assert.False(capture.Recording);
            live=new Live();brain.Connect();brain.BeginRecording();Assert.True(capture.Recording);yield return null;
        }
        [UnityTest] public IEnumerator HeldPttStartsAfterConnection()
        {
            live.wait=new TaskCompletionSource<bool>();brain.BeginRecording();Assert.False(capture.Recording);
            live.wait.SetResult(true);yield return null;
            Assert.True(capture.Recording);Assert.That(capture.starts,Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator ReleasedPttDuringConnectionDoesNotRecord()
        {
            live.wait=new TaskCompletionSource<bool>();brain.BeginRecording();brain.EndRecording();
            live.wait.SetResult(true);yield return null;
            Assert.True(brain.Connected);Assert.False(capture.Recording);Assert.That(capture.starts,Is.Zero);
        }
        [UnityTest] public IEnumerator InstructionBecomesMotionPlanAndStopEndsIt()
        {
            var planner=new Planner();brain.planner=planner;brain.Connect();
            Assert.True(brain.SendText("うなずいて"));yield return null;yield return null;
            Assert.That(planner.requests.Select(r=>r.text),Is.EqualTo(new[]{"うなずいて"}));Assert.That(planner.requests[0].characterName,Is.EqualTo(brain.profile.displayName));
            Assert.True(brain.performer.Busy);Assert.That(brain.performer.Request,Is.EqualTo("うなずいて"));
            Assert.False(brain.ApplyDecision(new BehaviorDecision{emotion=Emotion.sad,movement=Movement.wander}),"Jev cannot move the body during an instruction");Assert.That(brain.State.emotion,Is.EqualTo(Emotion.sad));
            brain.settings.motionPlanning=false;yield return new WaitForSeconds(5.2f);Assert.False(brain.performer.Busy);
            brain.performer.Run(brain.performer.Last,"again");Assert.True(brain.performer.Busy);brain.Suspend();Assert.False(brain.performer.Busy);
        }
        [UnityTest] public IEnumerator RobotVoiceFollowsTheSetting()
        {
            var player=brain.GetComponent<PcmStreamPlayer>();yield return null;Assert.False(player.Robot,"calm keeps its own voice by default");
            brain.settings.robotVoice=1;yield return null;Assert.True(player.Robot);brain.settings.robotVoice=2;yield return null;Assert.False(player.Robot);
        }
        [UnityTest] public IEnumerator TypedTextConnectsSendsAndIsSavedBeforeReply()
        {
            live.wait=new TaskCompletionSource<bool>();Assert.True(brain.SendText("  声が出せないの  "));Assert.That(live.texts,Is.Empty);
            live.wait.SetResult(true);yield return null;
            Assert.That(live.texts,Is.EqualTo(new[]{"声が出せないの"}));Assert.False(capture.Recording);Assert.That(brain.Phase,Is.EqualTo(ConversationPhase.Thinking));
            Assert.False(brain.SendText("二重送信"));Assert.That(live.texts.Count,Is.EqualTo(1));
            live.Emit("session.output_transcript.delta","わかった、文字でどうぞ");brain.Disconnect();
            var recent=repo.Recent("calm",10);Assert.That(recent.Count,Is.EqualTo(2));Assert.That(recent[0].text,Is.EqualTo("声が出せないの"));Assert.That(recent[0].role,Is.EqualTo("user"));Assert.That(recent[1].role,Is.EqualTo("assistant"));
            Assert.False(brain.SendText("   "));live=new Live();Assert.That(brain.SendText(new string('あ',300)),Is.True);yield return null;Assert.That(live.texts.Last().Length,Is.EqualTo(CharacterBrain.MaxTypedLength));
        }
    }
}
