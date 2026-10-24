using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace QuestAI
{
    public class CharacterBrain : MonoBehaviour
    {
        public CharacterProfile profile; public CharacterState State { get; private set; } = new CharacterState();
        public ConversationPhase Phase { get; private set; } = ConversationPhase.Idle;
        public event Action<string> StatusChanged; public event Action<string,string> CaptionChanged;
        public BodySystem body; public Transform user; public string environment;
        public SceneAwareness world; public CharacterLocomotion locomotion; public UserBodyTracker tracker;
        public AppSettings settings; public IMemoryRepository repository; public ISecretStore secrets;
        public ISTTProvider stt; public ILLMProvider llm; public ITTSProvider tts; public IBehaviorProvider behavior;
        public bool Connected => live!=null&&live.Ready;
        public Func<ILiveProvider> LiveFactory = () => new GPTLiveProvider();
        public IAudioCapture CaptureOverride;
        IAudioCapture capture;PcmStreamPlayer playback;ILiveProvider live;
        readonly TurnGate turns=new TurnGate();
        float lastInteraction,lastBehavior,lastSample,lastInputDelta,lastOutputDelta,oldDistance,nextAudio;
        bool oldLooking,behaviorBusy,initialized,sending,connecting,inputTransition,pttHeld,responseAllowed,inputReceived;
        long inputStartMs=-1,inputEndMs=-1,outputStartMs=-1,outputEndMs=-1;
        string sessionId,inputText="",outputText="";Vector3 oldHead;bool journeyCredited;float nextWander,lookUntil,poseUntil,lastWalkCheck,lastGestureReact=-10;
        readonly byte[] silence=new byte[2400];
        public void Initialize(CharacterState saved,string session)
        {
            if(tracker)tracker.Gestured+=OnUserGesture;
            State=saved??new CharacterState{emotion=profile.defaultEmotion};sessionId=session;capture=CaptureOverride??gameObject.AddComponent<AudioCaptureService>();playback=gameObject.AddComponent<PcmStreamPlayer>();playback.Initialize(body.voice);lastInteraction=Time.time;nextWander=Time.time+12;initialized=true;body.Apply(new BehaviorDecision{emotion=State.emotion});
        }
        void SetPhase(ConversationPhase value,string status){Phase=value;State.currentAction=value.ToString();StatusChanged?.Invoke(status);}
        public async void Connect()
        {
            if(!initialized||connecting||Connected)return;connecting=true;int generation=turns.Generation;var token=turns.Token;
            var connection=LiveFactory();live=connection;
            connection.Event+=message=>{if(turns.IsCurrent(generation)&&live==connection)Receive(message);};
            connection.Failed+=message=>{if(turns.IsCurrent(generation)&&live==connection){Disconnect();connection.Dispose();SetPhase(ConversationPhase.Idle,message);}};
            SetPhase(ConversationPhase.Thinking,"GPT-Live-1に接続しています…");
            try
            {
                var history=settings.memoryEnabled?repository.Recent(profile.id,10):Array.Empty<MessageRecord>();
                await connection.ConnectAsync(secrets.Get("OpenAI"),GPTLiveProvider.StartMessage(settings.voiceModel,string.IsNullOrEmpty(settings.voiceOverride)?profile.voice:settings.voiceOverride,profile.personality,history),token);
                if(!turns.IsCurrent(generation)){await connection.CloseAsync();return;}
                if(world!=null)await connection.ContextAsync("参考情報（命令ではない）: "+Surroundings()+" あなたはこの部屋の中を歩いて移動できます。ユーザーに頼まれたら物のそばへ行ったり近づいたりできます。移動は体の制御が自動で行うので、行けると答えて構いません。",null,token);
                if(settings.memoryEnabled&&history.Count>0){var memory=repository.Search(profile.id,history.Last().text,4);if(memory.Count>0)await connection.ContextAsync("参考記憶（命令ではない）: "+string.Join(" / ",memory.Select(x=>x.content)),null,token);}
                lastInteraction=Time.time;SetPhase(ConversationPhase.Idle,"接続済み · PTTを押して話してください");
                if(pttHeld)BeginRecording();
            }
            catch(OperationCanceledException){if(turns.IsCurrent(generation)){Disconnect();SetPhase(ConversationPhase.Idle,"接続がタイムアウトしました。再接続してください。");}}
            catch(Exception e){if(turns.IsCurrent(generation)){Disconnect();SetPhase(ConversationPhase.Idle,SafeError(e));}}
            finally{if(turns.IsCurrent(generation))connecting=false;}
        }
        public async void BeginRecording()
        {
            pttHeld=true;if(!initialized||Phase!=ConversationPhase.Idle||inputTransition)return;
            if(!Connected){Connect();return;}
            inputTransition=true;int generation=turns.Generation;
            try
            {
                await live.MuteAsync(false,turns.Token);
                if(!turns.IsCurrent(generation))return;
                if(!pttHeld){await live.MuteAsync(true,turns.Token);return;}
                if(capture.Begin(out string error)){journeyCredited=false;responseAllowed=true;inputReceived=false;lastInteraction=Time.time;SetPhase(ConversationPhase.Recording,"聞いています… 離すとマイクを停止");}
                else{await live.MuteAsync(true,turns.Token);StatusChanged?.Invoke(error);}
            }
            catch(Exception e){if(turns.IsCurrent(generation)){Disconnect();SetPhase(ConversationPhase.Idle,SafeError(e));}}
            finally{if(turns.IsCurrent(generation))inputTransition=false;}
        }
        public async void EndRecording()
        {
            pttHeld=false;if(!capture.Recording)return;int generation=turns.Generation;var token=turns.Token;
            inputTransition=true;SetPhase(ConversationPhase.Thinking,"返答を待っています…");
            try{while(sending){token.ThrowIfCancellationRequested();await Task.Yield();}var tail=capture.DrainPcm();capture.Stop();if(Connected){if(tail.Length>0)await live.AudioAsync(tail,token);await live.MuteAsync(true,token);}lastInteraction=Time.time;}
            catch(Exception e){if(turns.IsCurrent(generation)){Disconnect();SetPhase(ConversationPhase.Idle,SafeError(e));}}
            finally{if(turns.IsCurrent(generation))inputTransition=false;}
        }
        public PerceptionSnapshot Perception(string trigger)
        {
            Vector3 delta=body.transform.position+Vector3.up*.85f-user.position;float angle=Vector3.Angle(user.forward,delta);
            var snapshot=new PerceptionSnapshot{distance=Vector2.Distance(new Vector2(user.position.x,user.position.z),new Vector2(body.transform.position.x,body.transform.position.z)),headAngle=angle,headLookingAtCharacter=angle<22,silenceDuration=Time.time-lastInteraction,speaking=capture.Recording,environment=environment,time=DateTime.Now.ToString("HH:mm"),trigger=trigger};
            if(world!=null)
            {
                snapshot.surroundings=Surroundings();snapshot.userLookingAt=world.LookedAt(user.position,user.forward)?.id;snapshot.walking=locomotion&&locomotion.Moving;snapshot.walkingTo=locomotion?locomotion.Destination:null;
                var near=world.Nearest(body.transform.position);if(near!=null&&SceneAwareness.FlatDistance(near,body.transform.position)<1)snapshot.nearbyObject=near.id;
            }
            if(tracker){var g=tracker.Recent;snapshot.userGesture=g==UserGesture.none?null:g.ToString();snapshot.userPointingAt=tracker.PointingAt;snapshot.userCrouching=tracker.Crouching;}
            return snapshot;
        }
        public string Surroundings()=>world==null?"":world.Describe(environment,body.transform.position,body.transform.forward,user.position,user.forward);
        DialogueContext Context(string text,string trigger)=>new DialogueContext{text=text,personality=profile.personality,characterName=profile.displayName,character=State,perception=Perception(trigger),places=world?.objects.Select(x=>x.id).ToArray()};
        static readonly string[] Seats={"椅子","ソファ","ひじ掛け椅子","ベッド"};
        // Applies a whole Jev (or local) decision: expression, a reaction to an ongoing walk, a new walk, or an in-place action.
        public bool ApplyDecision(BehaviorDecision decision)
        {
            State.emotion=decision.emotion;body.Apply(decision);
            if(locomotion&&locomotion.Moving)
            {
                switch(decision.walking)
                {
                    case WalkingReaction.stop:locomotion.Stop();return false;
                    case WalkingReaction.turn_to_user:locomotion.Stop();locomotion.Face(user.position);lookUntil=Time.time+3;return false;
                    case WalkingReaction.change_destination:return Move(decision);
                    default:return true;
                }
            }
            if(Move(decision))return true;
            // Pointing or studying something does not need a walk when it is already in view.
            var target=decision.target!=null&&world!=null?world.Find(decision.target):null;
            if(target!=null&&decision.arrival!=ArrivalAction.none&&decision.arrival!=ArrivalAction.sit)Arrive(target,decision.arrival);
            return false;
        }
        // Turns a movement decision into a walk; returns false when it is "stay" or nothing reachable fits.
        public bool Move(BehaviorDecision decision)
        {
            if(world==null||!locomotion)return false;
            var target=decision.target!=null?world.Find(decision.target):null;
            body.lookPoint=target?.bounds.center;
            Vector3 self=body.transform.position,at=new Vector3(user.position.x,self.y,user.position.z),away=self-at;away.y=0;if(away.sqrMagnitude<.04f)away=-body.transform.forward;
            Vector3 goal;string place;
            switch(decision.movement)
            {
                case Movement.go_to_object:
                    if(target==null||!world.Approach(target,self,.3f,out goal))return false;place=target.id;break;
                case Movement.approach_user:goal=world.grid.Snap(at+away.normalized*Mathf.Max(.8f,profile.personalSpace+.3f));place="ユーザーのそば";break;
                case Movement.give_space:goal=world.grid.Snap(self+away.normalized*.9f);place="少し離れた場所";break;
                case Movement.wander:
                    var cells=world.grid.FreeCells().Where(c=>{float d=Vector3.Distance(c,self);return d>1&&d<2.6f&&Vector3.Distance(c,at)>1.1f;}).ToList();
                    if(cells.Count==0)return false;goal=cells[UnityEngine.Random.Range(0,cells.Count)];place="部屋の中";break;
                default:return false;
            }
            // "around" bends the route through a point beside the straight line, on whichever side is free.
            Vector3? via=null;
            if(decision.approach==ApproachStyle.around)
            {
                var line=goal-self;line.y=0;
                if(line.magnitude>1.2f){var side=Vector3.Cross(Vector3.up,line.normalized)*Mathf.Min(1,line.magnitude*.4f);var mid=(self+goal)/2;via=!world.grid.Blocked(mid+side)?mid+side:!world.grid.Blocked(mid-side)?mid-side:(Vector3?)null;}
            }
            return Walk(goal,place,target,decision.arrival,decision.pace,via);
        }
        bool Walk(Vector3 goal,string place,SceneObject target,ArrivalAction arrival,Pace pace,Vector3? via)
        {
            lookUntil=poseUntil=0;body.SetPose(BodySystem.Pose.stand,null);
            return locomotion.MoveTo(goal,place,target?.bounds.center,arrived=>
            {
                if(!arrived||!initialized)return;
                if(target!=null)Arrive(target,arrival);
                Tell("あなたは今「"+place+"」に移動しました。"+Surroundings());
            },pace,via);
        }
        // What the character does at an object: look, sit on it, crouch to study it, or point it out.
        void Arrive(SceneObject target,ArrivalAction arrival)
        {
            var center=target.bounds.center;body.lookPoint=center;
            body.Apply(new BehaviorDecision{emotion=State.emotion,gaze=Gaze.@object});lookUntil=Time.time+6;
            if(arrival==ArrivalAction.sit&&!Seats.Contains(target.label))arrival=ArrivalAction.crouch_look;
            switch(arrival)
            {
                case ArrivalAction.sit:
                    var inner=target.bounds;inner.Expand(new Vector3(-Mathf.Min(.8f,inner.size.x-.05f),0,-Mathf.Min(.8f,inner.size.z-.05f)));
                    var spot=inner.ClosestPoint(body.transform.position);float top=target.bounds.min.y+Mathf.Clamp(target.bounds.size.y*.5f,.3f,.5f);
                    locomotion.Sit(new Vector3(spot.x,0,spot.z),top-body.HipHeight*.85f);body.SetPose(BodySystem.Pose.sit,null);
                    // Seated, the character looks back at the user instead of the seat.
                    body.lookPoint=null;body.Apply(new BehaviorDecision{emotion=State.emotion});lookUntil=0;poseUntil=Time.time+25;break;
                case ArrivalAction.crouch_look:body.SetPose(BodySystem.Pose.crouch,center);poseUntil=Time.time+6;break;
                case ArrivalAction.point:body.SetPose(BodySystem.Pose.point,center);poseUntil=Time.time+4;Tell("あなたは今「"+target.id+"」を指さしてユーザーに見せています。");break;
            }
        }
        void EndPose(){poseUntil=0;body.SetPose(BodySystem.Pose.stand,null);if(locomotion)locomotion.Stand();}
        // A user gesture is shared with Live, sent to Jev at once, or (without Jev) answered by simple local rules.
        void OnUserGesture(UserGesture gesture,string pointed)
        {
            if(!initialized||Phase==ConversationPhase.Paused)return;
            lastInteraction=Time.time;
            string fact=gesture==UserGesture.wave?"ユーザーがあなたに手を振りました。":gesture==UserGesture.point?"ユーザーが「"+pointed+"」を指さしています。":gesture==UserGesture.crouch?"ユーザーがしゃがみました。":"ユーザーが立ち上がりました。";
            Tell(fact);
            if(Connected&&settings.behaviorEnabled)
            {
                if(!behaviorBusy&&Time.time-lastGestureReact>2.5f){lastGestureReact=lastBehavior=Time.time;ReactAsync("user_gesture:"+gesture);}
                return;
            }
            var near=Vector2.Distance(new Vector2(user.position.x,user.position.z),new Vector2(body.transform.position.x,body.transform.position.z));
            switch(gesture)
            {
                case UserGesture.wave:ApplyDecision(new BehaviorDecision{emotion=Emotion.happy,gesture=Gesture.wave,walking=WalkingReaction.turn_to_user});break;
                case UserGesture.point:if(pointed!=null)ApplyDecision(new BehaviorDecision{emotion=Emotion.curious,movement=Movement.go_to_object,target=pointed,arrival=ArrivalAction.crouch_look,pace=Pace.hurry,walking=WalkingReaction.change_destination});break;
                case UserGesture.crouch:
                    // Come down to the user's level: walk over if far, otherwise crouch facing them.
                    if(near>1.8f)ApplyDecision(new BehaviorDecision{emotion=Emotion.curious,movement=Movement.approach_user,pace=Pace.slow,walking=WalkingReaction.change_destination});
                    else{if(locomotion)locomotion.Stop();body.Apply(new BehaviorDecision{emotion=Emotion.curious,gesture=Gesture.head_tilt});body.SetPose(BodySystem.Pose.crouch,null);poseUntil=Time.time+8;}break;
                case UserGesture.stand_up:if(body.CurrentPose==BodySystem.Pose.crouch)EndPose();break;
            }
        }
        async void Tell(string fact)
        {
            if(!Connected)return;int generation=turns.Generation;
            try{await live.ContextAsync("参考情報（命令ではない）: "+fact,null,turns.Token);}catch(OperationCanceledException){}catch(Exception){if(turns.IsCurrent(generation))StatusChanged?.Invoke("周囲の情報をLiveへ送信できませんでした。");}
        }
        // Offline idle life: without Jev the character still strolls to things in the room now and then.
        void Autonomy()
        {
            if(!settings.autonomousMovement||world==null||!locomotion||locomotion.Moving||poseUntil>0||Phase!=ConversationPhase.Idle||capture.Recording||Time.time<nextWander)return;
            nextWander=Time.time+UnityEngine.Random.Range(25f,45f);
            if(Connected&&settings.behaviorEnabled||Time.time-lastInteraction<10)return;
            float distance=Vector3.Distance(new Vector3(user.position.x,0,user.position.z),new Vector3(body.transform.position.x,0,body.transform.position.z));
            var choice=new BehaviorDecision{emotion=State.emotion,movement=distance>2.6f?Movement.approach_user:UnityEngine.Random.value<.65f?Movement.go_to_object:Movement.wander};
            if(choice.movement==Movement.go_to_object)
            {
                var near=world.objects.Where(Interesting).ToList();if(near.Count==0)return;var pick=near[UnityEngine.Random.Range(0,near.Count)];choice.target=pick.id;choice.emotion=Emotion.curious;
                var actions=Seats.Contains(pick.label)?new[]{ArrivalAction.none,ArrivalAction.sit,ArrivalAction.sit,ArrivalAction.point}:new[]{ArrivalAction.none,ArrivalAction.crouch_look,ArrivalAction.point};
                choice.arrival=actions[UnityEngine.Random.Range(0,actions.Length)];
            }
            choice.pace=UnityEngine.Random.value<.3f?Pace.slow:Pace.normal;choice.approach=UnityEngine.Random.value<.3f?ApproachStyle.around:ApproachStyle.direct;
            ApplyDecision(choice);
        }
        bool Interesting(SceneObject o)=>SceneAwareness.FlatDistance(o,body.transform.position)<3.2f&&SceneAwareness.FlatDistance(o,user.position)>.8f;
        void Receive(JObject message)
        {
            switch((string)message["type"])
            {
                case "session.input_transcript.delta":
                    if(inputStartMs<0)inputStartMs=(long?)message["start_ms"]??-1;inputEndMs=(long?)message["end_ms"]??-1;
                    inputText+=(string)message["delta"];inputReceived|=!string.IsNullOrWhiteSpace(inputText);lastInputDelta=Time.time;CaptionChanged?.Invoke(inputText,outputText);break;
                case "session.output_transcript.delta":
                    if(!responseAllowed)break;
                    if(!capture.Recording)FlushInput();
                    if(outputStartMs<0)outputStartMs=(long?)message["start_ms"]??-1;outputEndMs=(long?)message["end_ms"]??-1;
                    outputText+=(string)message["delta"];lastOutputDelta=Time.time;CaptionChanged?.Invoke(inputText,outputText);break;
                case "session.output_audio.delta":if(!responseAllowed)break;try{playback.Append(Convert.FromBase64String((string)message["delta"]));}catch(Exception e){Suspend();StatusChanged?.Invoke(SafeError(e));}break;
                case "session.delegation.created":DelegateAsync((string)message["delegation"]?["id"]);break;
                case "session.closed":Disconnect();SetPhase(ConversationPhase.Idle,"Live接続を終了しました。再接続できます。");break;
            }
        }
        void FlushTranscripts()
        {
            if(!initialized)return;
            if(inputReceived&&!journeyCredited&&!string.IsNullOrWhiteSpace(outputText)){var journey=CompanionJourney.Load(repository,profile.id);journey.conversations++;journey.Save(repository,profile.id);journeyCredited=true;}
            FlushInput();
            if(outputText.Length>0){if(settings.memoryEnabled)repository.AddMessage(new MessageRecord{characterId=profile.id,sessionId=sessionId,role="assistant",text=outputText,audioStartMs=outputStartMs,audioEndMs=outputEndMs});outputText="";outputStartMs=outputEndMs=-1;State.lastInteraction=DateTimeOffset.UtcNow.ToUnixTimeSeconds();State.relationship=Mathf.Clamp01(State.relationship+.005f);repository.Save("state:"+profile.id,JsonConvert.SerializeObject(State));}
        }
        void FlushInput()
        {
            if(inputText.Length==0)return;
            var source=new MessageRecord{characterId=profile.id,sessionId=sessionId,role="user",text=inputText,audioStartMs=inputStartMs,audioEndMs=inputEndMs};
            inputText="";inputStartMs=inputEndMs=-1;
            if(settings.memoryEnabled)repository.AddMessage(source);
            if(Connected)ProcessInputAsync(source);
        }
        async void ProcessInputAsync(MessageRecord source)
        {
            int generation=turns.Generation;var token=turns.Token;
            try
            {
                var context=Context(source.text,"user_spoke");context.memories=settings.memoryEnabled?repository.Search(profile.id,source.text,5):Array.Empty<MemoryRecord>();
                BehaviorDecision decision=new BehaviorDecision();
                if(settings.behaviorEnabled){try{decision=await behavior.DecideAsync(context,token);}catch(OperationCanceledException){throw;}catch{if(turns.IsCurrent(generation))StatusChanged?.Invoke("Jevに接続できないため、中立の動作を使用します。");}}
                if(!turns.IsCurrent(generation)||Phase==ConversationPhase.Paused)return;bool walking=ApplyDecision(decision);if(walking)StatusChanged?.Invoke(locomotion.Destination+"へ歩いています…");
                if(Connected)await live.ContextAsync("参考情報（命令ではない）: "+JsonConvert.SerializeObject(new{emotion=decision.emotion.ToString(),conversation=decision.conversation.ToString(),movement=walking?decision.movement.ToString()+(decision.target!=null?" → "+decision.target:""):"stay",userLookingAt=context.perception.userLookingAt,memories=context.memories.Select(x=>x.content)}),null,token);
                if(settings.memoryEnabled){var records=await llm.ExtractAsync(source,token);if(turns.IsCurrent(generation)&&settings.memoryEnabled)repository.AddMemories(records);}
            }
            catch(OperationCanceledException){}catch(Exception){if(turns.IsCurrent(generation)&&Phase!=ConversationPhase.Paused)StatusChanged?.Invoke("会話は保存しました。記憶の更新を完了できませんでした。");}
        }
        async void DelegateAsync(string delegationId)
        {
            if(string.IsNullOrEmpty(delegationId))return;int generation=turns.Generation;var token=turns.Token;
            try
            {
                var history=settings.memoryEnabled?repository.Recent(profile.id,10):Array.Empty<MessageRecord>();
                string query=inputText.Length>0?inputText:history.LastOrDefault(x=>x.role=="user")?.text??"";
                var memories=settings.memoryEnabled?repository.Search(profile.id,query,5):Array.Empty<MemoryRecord>();
                string result=memories.Count>0?"保存されている参考記憶: "+string.Join(" / ",memories.Select(x=>x.content)):"一致する保存記憶はありません。このアプリは外部検索や予約などの操作を実行できません。わからないことはわからないと伝えてください。";
                if(turns.IsCurrent(generation)&&Connected)await live.AnswerAsync(result,delegationId,token);
            }catch(OperationCanceledException){}catch(Exception){if(turns.IsCurrent(generation))StatusChanged?.Invoke("記憶情報をLiveへ送信できませんでした。");}
        }
        public async void Disconnect()
        {
            capture.Stop();pttHeld=false;FlushTranscripts();turns.Cancel();connecting=sending=behaviorBusy=inputTransition=responseAllowed=false;var connection=live;live=null;playback.Clear();SetPhase(ConversationPhase.Idle,"未接続 · 接続ボタンで再開できます");if(connection!=null)await connection.CloseAsync();
        }
        public void Suspend(){if(!initialized)return;if(locomotion)locomotion.Stop();lookUntil=0;EndPose();Disconnect();SetPhase(ConversationPhase.Paused,"一時停止中 · マイクとLive接続を停止しました");}
        public void Resume(){if(initialized&&Phase==ConversationPhase.Paused)SetPhase(ConversationPhase.Idle,"接続ボタンで会話を再開できます");}
        void Update()
        {
            if(!initialized||Phase==ConversationPhase.Paused)return;body.lookStrength=settings.lookStrength;body.voice.volume=settings.voiceVolume;
            if(capture.LimitReached)EndRecording();
            if(lookUntil>0&&Time.time>lookUntil){lookUntil=0;body.lookPoint=null;if(locomotion)locomotion.Face(null);body.Apply(new BehaviorDecision{emotion=State.emotion});}
            if(poseUntil>0&&Time.time>poseUntil)EndPose();
            Autonomy();
            if(Connected&&Time.time>=nextAudio&&!sending&&!inputTransition){nextAudio=Time.time+.05f;PumpAudio();}
            if(!capture.Recording&&!inputTransition&&!connecting)
            {
                if(playback.Speaking){if(Phase!=ConversationPhase.Speaking)SetPhase(ConversationPhase.Speaking,"話しています…");lastInteraction=Time.time;}
                else if(Phase==ConversationPhase.Speaking&&Time.time-lastInteraction>1.2f){FlushTranscripts();responseAllowed=false;SetPhase(ConversationPhase.Idle,Connected?"PTTを押して話しかけてください":"接続ボタンで会話を開始できます");}
                if(Phase==ConversationPhase.Thinking&&Time.time-lastInteraction>30){bool recognized=inputReceived;Disconnect();StatusChanged?.Invoke(recognized?"返答がタイムアウトしました。再接続してください。":"音声を認識できませんでした。再接続してもう一度話してください。");return;}
                if(inputText.Length>0&&Time.time-lastInputDelta>.65f)FlushInput();
                if((inputText.Length>0||outputText.Length>0)&&Time.time-Mathf.Max(lastInputDelta,lastOutputDelta)>1.2f&&!playback.Speaking)FlushTranscripts();
            }
            if(Connected&&!capture.Recording&&Time.time-lastInteraction>120){Disconnect();StatusChanged?.Invoke("しばらく操作がなかったためLive接続を終了しました。");return;}
            if(!Connected||Phase!=ConversationPhase.Idle||behaviorBusy||!settings.behaviorEnabled||Time.time-lastSample<.25f)return;
            lastSample=Time.time;
            // While walking, Jev is asked every 3 s whether to keep going, stop, turn to the user or change course.
            if(locomotion&&locomotion.Moving){if(Time.time-lastWalkCheck>=3){lastWalkCheck=lastBehavior=Time.time;ReactAsync("walking");}return;}
            var p=Perception("perception");p.headSpeed=(user.position-oldHead).magnitude/.25f;oldHead=user.position;
            bool changed=Mathf.Abs(p.distance-oldDistance)>.5f||p.headLookingAtCharacter!=oldLooking||p.silenceDuration>30;oldDistance=p.distance;oldLooking=p.headLookingAtCharacter;
            if(changed&&Time.time-lastBehavior>=Mathf.Max(8,settings.behaviorInterval)){lastBehavior=Time.time;ReactAsync();}
        }
        async void PumpAudio()
        {
            sending=true;int generation=turns.Generation;var token=turns.Token;
            try{var pcm=capture.Recording?capture.DrainPcm():silence;if(pcm.Length>0&&Connected)await live.AudioAsync(pcm,token);}
            catch(OperationCanceledException){}catch(Exception e){if(turns.IsCurrent(generation)){Disconnect();StatusChanged?.Invoke(SafeError(e));}}finally{if(turns.IsCurrent(generation))sending=false;}
        }
        async void ReactAsync(string trigger="perception_only")
        {
            behaviorBusy=true;int generation=turns.Generation;var token=turns.Token;
            try{var result=await behavior.DecideAsync(Context("",trigger),token);if(turns.IsCurrent(generation)&&(Phase==ConversationPhase.Idle||trigger.StartsWith("user_gesture")&&Phase!=ConversationPhase.Paused&&!capture.Recording)){result.conversation=ConversationAction.wait;ApplyDecision(result);}}
            catch(OperationCanceledException){}catch{}finally{if(turns.IsCurrent(generation))behaviorBusy=false;}
        }
        public static string SafeError(Exception e)=>e is ProviderException||e is InvalidOperationException?e.Message:"処理に失敗しました。接続やAPI設定を確認してください。";
        void OnDestroy(){if(tracker)tracker.Gestured-=OnUserGesture;initialized=false;turns.Dispose();capture?.Stop();live?.Dispose();}
    }
}
