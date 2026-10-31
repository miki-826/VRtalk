using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace QuestAI
{
    public class AppRoot : MonoBehaviour
    {
        public static AppRoot Instance { get; private set; }
        public static Func<IMemoryRepository> EditorRepositoryFactory;
        public GameObject xrRigPrefab;
        public CharacterProfile[] characters;
        CharacterProfile[] builtIn = Array.Empty<CharacterProfile>();
        public EnvironmentProfile[] environments;
        public AppSettings Settings { get; private set; }
        public IMemoryRepository Repository { get; private set; }
        public ISecretStore Secrets { get; private set; }
        public BehaviorConnectionStatus JevStatus { get; private set; }
        public CharacterBrain Brain { get; private set; }
        public Transform Head { get; private set; }
        public WorldUI UI { get; private set; }
        public Transform TrackingSpace { get; private set; }
        public UserBodyTracker Body { get; private set; }
        public bool IsChanging => changing;
        public bool HasSave => !string.IsNullOrEmpty(Repository?.Load("continue"));
        bool changing, focused = true, paused;
        string loadedScene; CancellationTokenSource operations = new CancellationTokenSource();
        async void Start()
        {
            if(Instance && Instance!=this){Destroy(gameObject);return;}Instance=this;DontDestroyOnLoad(gameObject);
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                var storage=new AndroidStorage();Repository=storage;Secrets=storage;
#else
                System.IO.Directory.CreateDirectory(Application.persistentDataPath);
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                bool smoke=Array.IndexOf(Environment.GetCommandLineArgs(),"--desktop-smoke")>=0;
                Repository=smoke?new LocalRepository():new LocalRepository(System.IO.Path.Combine(Application.persistentDataPath,"desktop-data.json"));Secrets=smoke?(ISecretStore)new SessionSecrets():new WindowsSecretStore(System.IO.Path.Combine(Application.persistentDataPath,"secrets"));
#else
                Repository=EditorRepositoryFactory?.Invoke()??new LocalRepository(System.IO.Path.Combine(Application.persistentDataPath,"editor-data.json"));Secrets=new SessionSecrets();
#endif
#endif
                Settings=JsonConvert.DeserializeObject<AppSettings>(Repository.Load("settings")??"null")??new AppSettings();
                JevStatus=new BehaviorConnectionStatus(()=>Secrets.Get("Jev"),()=>Settings.behaviorModel);
                Settings.voiceVolume=Mathf.Clamp01(Settings.voiceVolume);Settings.lookStrength=Mathf.Clamp01(Settings.lookStrength);Settings.uiScale=Mathf.Clamp(Settings.uiScale,.8f,1.3f);
                builtIn=Resources.LoadAll<CharacterProfile>("Profiles").OrderBy(x=>x.id).ToArray();RefreshCustomModels();environments=Resources.LoadAll<EnvironmentProfile>("Profiles").OrderBy(x=>x.id).ToArray();
                CreateRig();Body=gameObject.AddComponent<UserBodyTracker>();Body.head=Head;Body.trackingSpace=TrackingSpace;UI=gameObject.AddComponent<WorldUI>();UI.Initialize(this);gameObject.AddComponent<XRPointerInput>().Initialize(this);
                Application.targetFrameRate=72;
                // Rooms cannot be started until the menu scene is fully registered.
                changing=true;try{await LoadScene("MainMenu");UI.MainMenu();}finally{changing=false;}
            }
            catch(Exception e){Debug.LogError("QuestAI initialization failed: "+e.GetType().Name);if(UI)UI.ShowStatus("初期化に失敗しました。ビルド構成を確認してください。");}
        }
        void CreateRig()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var rig=xrRigPrefab?Instantiate(xrRigPrefab,transform):new GameObject("Quest Rig");rig.transform.SetParent(transform,false);
            var cameraRig=rig.GetComponentInChildren<OVRCameraRig>();
            if(!cameraRig)cameraRig=rig.AddComponent<OVRCameraRig>();
            var manager=rig.GetComponentInChildren<OVRManager>();if(!manager)manager=rig.AddComponent<OVRManager>();
            manager.trackingOriginType=OVRManager.TrackingOrigin.FloorLevel;
            Head=cameraRig.centerEyeAnchor;TrackingSpace=cameraRig.trackingSpace;
            Head.GetComponent<Camera>().tag="MainCamera";
#else
            var rig=new GameObject("Editor camera rig");rig.transform.SetParent(transform,false);TrackingSpace=rig.transform;
            var camera=new GameObject("Head").AddComponent<Camera>();camera.tag="MainCamera";camera.transform.SetParent(rig.transform,false);camera.transform.localPosition=new Vector3(0,1.55f,0);camera.fieldOfView=80;camera.nearClipPlane=.05f;camera.farClipPlane=60;camera.backgroundColor=new Color(.09f,.12f,.16f);camera.gameObject.AddComponent<AudioListener>();Head=camera.transform;
#endif
            TrackingSpace.parent.position=new Vector3(0,0,-1.8f);
            if(!Head.GetComponent<AudioListener>())Head.gameObject.AddComponent<AudioListener>();
            // Filmic tone mapping, gentle bloom and grading; the profile is generated by RealisticUpgrade.
            var grade=Resources.Load<VolumeProfile>("Graphics/CinematicGrade");
            if(grade){Head.GetComponent<Camera>().GetUniversalAdditionalCameraData().renderPostProcessing=true;var volume=gameObject.AddComponent<Volume>();volume.isGlobal=true;volume.sharedProfile=grade;}
        }
        // Each room starts from the same spot: undoes stick/WASD walking, snap turns and a seated crouch from the last room.
        void ResetViewpoint()
        {
            var rig=TrackingSpace.parent;if(rig){if(Body&&Body.SeatedCrouch)Body.SeatedCrouch=false;rig.SetPositionAndRotation(new Vector3(0,0,-1.8f),Quaternion.identity);}
#if !UNITY_ANDROID || UNITY_EDITOR
            Head.localPosition=new Vector3(0,Head.localPosition.y,0);
#endif
        }
        async Task LoadScene(string name)
        {
            // Unload every content scene, not only the one recorded last, so an interrupted switch cannot leave two rooms loaded.
            for(int i=SceneManager.sceneCount-1;i>=0;i--)
            {
                var scene=SceneManager.GetSceneAt(i);if(!scene.isLoaded||scene.name=="Bootstrap"||scene==gameObject.scene)continue;
                var unload=SceneManager.UnloadSceneAsync(scene);while(unload!=null&&!unload.isDone)await Task.Yield();
            }
            var load=SceneManager.LoadSceneAsync(name,LoadSceneMode.Additive);while(!load.isDone)await Task.Yield();loadedScene=name;
            SceneManager.SetActiveScene(SceneManager.GetSceneByName(name));
            if(LightmapSettings.lightProbes!=null&&LightmapSettings.lightProbes.count>0)LightProbes.TetrahedralizeAsync();
        }
        // Built-in characters followed by the user's VRM files (CustomModels.Folder). Returns how many models were found.
        public int RefreshCustomModels(){var custom=CustomModels.Scan();characters=builtIn.Concat(custom).ToArray();return custom.Length;}
        // The shared humanoid animation library, taken from a built-in character that uses it.
        public ClipAnimator AnimationLibrary=>builtIn.Select(x=>x.prefab?x.prefab.GetComponent<ClipAnimator>():null).FirstOrDefault(x=>x&&x.animator&&x.animator.avatar&&x.animator.avatar.isHuman);
        public async void StartSession(bool resume)
        {
            if(changing)return;changing=true;UI.ShowStatus("部屋を準備しています…");CancelOperations();GameObject custom=null;
            try
            {
                SaveSession();if(Brain){Destroy(Brain.gameObject);Brain=null;}
                var save=resume?JsonConvert.DeserializeObject<SessionSave>(Repository.Load("continue")??"null"):null;
                if(save!=null){Settings.characterId=save.characterId;Settings.environmentId=save.environmentId;}
                var profile=characters.FirstOrDefault(x=>x.id==Settings.characterId)??characters[0];
                var environment=environments.FirstOrDefault(x=>x.id==Settings.environmentId)??environments[0];
                Settings.characterId=profile.id;Settings.environmentId=environment.id;SaveSettings();
                // A user model is loaded before leaving the current scene, so a bad file leaves the menu as it was.
                if(!profile.prefab){UI.ShowStatus("モデルを読み込んでいます…");custom=await CustomModels.Load(profile,AnimationLibrary,operations.Token);DontDestroyOnLoad(custom);}
                await LoadScene(environment.sceneName);ResetViewpoint();
                var world=SceneAwareness.Scan(SceneManager.GetSceneByName(environment.sceneName),environment.characterPosition);
                GameObject avatar;
                if(custom){SceneManager.MoveGameObjectToScene(custom,SceneManager.GetActiveScene());custom.transform.SetPositionAndRotation(environment.characterPosition,Quaternion.Euler(0,180,0));custom.SetActive(true);avatar=custom;custom=null;}
                else avatar=Instantiate(profile.prefab,environment.characterPosition,Quaternion.Euler(0,180,0));
                var body=avatar.GetComponent<BodySystem>();body.user=Head;
                var locomotion=avatar.AddComponent<CharacterLocomotion>();locomotion.world=world;locomotion.user=Head;
                Brain=avatar.AddComponent<CharacterBrain>();Brain.profile=profile;Brain.body=body;Brain.user=Head;Brain.environment=environment.displayName;Brain.world=world;Brain.locomotion=locomotion;Brain.tracker=Body;Body.world=world;Brain.settings=Settings;Brain.repository=Repository;Brain.secrets=Secrets;
                var transport=new ApiTransport(Secrets);var openAI=new OpenAIProvider(transport,Settings);Brain.stt=openAI;Brain.llm=openAI;Brain.tts=openAI;Brain.behavior=JevStatus.Observe(new JevBehaviorProvider(transport,Settings));
                var structured=new OpenAIStructured(transport,Settings);openAI.Structured=structured;var planner=new OpenAIMotionPlanner(structured);Brain.planner=planner;Brain.vision=planner;
                string session=save?.sessionId??Guid.NewGuid().ToString("N");
                Brain.Initialize(save?.state??JsonConvert.DeserializeObject<CharacterState>(Repository.Load("state:"+profile.id)??"null"),session);
                Repository.Save("character:"+profile.id,JsonConvert.SerializeObject(new{id=profile.id,name=profile.displayName}));Repository.Save("session:"+session,JsonConvert.SerializeObject(new{character=profile.id,environment=environment.id,started=DateTimeOffset.UtcNow.ToUnixTimeSeconds()}));
                currentSession=session;Brain.StatusChanged+=UI.ShowStatus;Brain.CaptionChanged+=UI.SetCaptions;
                var journey=CompanionJourney.Load(Repository,profile.id);journey.Visit(environment.id);journey.Save(Repository,profile.id);
                ApplyTime();UI.Session();if(paused||!focused)Brain.Suspend();SaveSession();
            }
            catch(OperationCanceledException){}
            catch(Exception e){UI.ShowStatus(CharacterBrain.SafeError(e));}
            finally{if(custom)Destroy(custom);changing=false;}
        }
        string currentSession;
        public async void ReturnMenu()
        {
            if(changing)return;changing=true;CancelOperations();
            try{SaveSession();if(Brain){Destroy(Brain.gameObject);Brain=null;}if(Body)Body.world=null;await LoadScene("MainMenu");UI.MainMenu();}catch(Exception e){UI.ShowStatus(CharacterBrain.SafeError(e));}finally{changing=false;}
        }
        void CancelOperations(){operations.Cancel();operations.Dispose();operations=new CancellationTokenSource();if(Brain)Brain.Suspend();}
        public void SaveSettings(){Repository.Save("settings",JsonConvert.SerializeObject(Settings));ApplyTime();}
        public void SaveSession()
        {
            if(Repository==null||Settings==null)return;SaveSettings();if(!Brain)return;
            Repository.Save("state:"+Brain.profile.id,JsonConvert.SerializeObject(Brain.State));
            Repository.Save("relationship:"+Brain.profile.id,JsonConvert.SerializeObject(new{value=Brain.State.relationship}));
            Repository.Save("continue",JsonConvert.SerializeObject(new SessionSave{characterId=Brain.profile.id,environmentId=Settings.environmentId,sessionId=currentSession,state=Brain.State}));
        }
        public void ApplyTime(){foreach(var lighting in FindObjectsByType<EnvironmentLighting>(FindObjectsSortMode.None))lighting.Apply(Settings.timeMode);}
        public void DeleteKeys(){CancelOperations();Secrets.DeleteAll();if(Brain)Brain.Resume();UI.ShowStatus("APIキーを削除しました。");}
        public void DeleteMemory(){CancelOperations();Repository.DeleteMemories(Settings.characterId);if(Brain)Brain.Resume();UI.ShowStatus("このキャラクターの会話と記憶を削除しました。");}
        public async void DeleteAllData()
        {
            if(changing)return;changing=true;CancelOperations();
            try{if(Brain){Destroy(Brain.gameObject);Brain=null;}currentSession=null;Repository.DeleteAll();Secrets.DeleteAll();Settings=new AppSettings();await LoadScene("MainMenu");UI.MainMenu();UI.ShowStatus("すべてのローカルデータを削除しました。");}catch(Exception e){UI.ShowStatus(CharacterBrain.SafeError(e));}finally{changing=false;}
        }
        public async void TestProvider(string provider)
        {
            var token=operations.Token;UI.ShowStatus(provider+"の接続を確認しています…");
            try
            {
                var transport=new ApiTransport(Secrets);var context=new DialogueContext{text="こんにちは",personality="短く挨拶する",character=new CharacterState(),perception=new PerceptionSnapshot()};
                if(provider=="Jev")await JevStatus.Observe(new JevBehaviorProvider(transport,Settings)).DecideAsync(context,token);
                else {using var connection=new GPTLiveProvider();await connection.ConnectAsync(Secrets.Get("OpenAI"),GPTLiveProvider.StartMessage(Settings.voiceModel,"marin","接続確認。発話せず待つ。",Array.Empty<MessageRecord>()),token);await connection.CloseAsync();}
                token.ThrowIfCancellationRequested();UI.ShowStatus(provider+"接続成功");
            }catch(OperationCanceledException){}catch(Exception e){if(!token.IsCancellationRequested)UI.ShowStatus(CharacterBrain.SafeError(e));}
        }
        void OnApplicationPause(bool value){paused=value;Lifecycle();}
        void OnApplicationFocus(bool value){focused=value;Lifecycle();}
        void Lifecycle(){if(Repository==null)return;if(paused||!focused){CancelOperations();SaveSession();}else if(Brain)Brain.Resume();}
        void OnApplicationQuit(){SaveSession();}
        void OnDestroy(){if(Instance!=this)return;operations.Cancel();operations.Dispose();if(Brain){Brain.Suspend();Destroy(Brain.gameObject);Brain=null;}Repository?.Dispose();Instance=null;}
    }
}
