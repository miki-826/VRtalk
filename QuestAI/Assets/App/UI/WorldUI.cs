using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace QuestAI
{
    public class WorldUI : MonoBehaviour
    {
        AppRoot app;Canvas canvas;RectTransform content;Font font;Text status,captions;
        MicrophoneCheck microphoneCheck;
        Text jevStatus;float nextJevStatus;
        public bool IsEditing { get; private set; }
        bool secretEditing;Action refreshEdit,submitEdit;
        // Quest's own keyboard (kana-kanji conversion, letters, dictation) stands in for the PC keyboard and IME.
        TouchScreenKeyboard systemKeyboard;int editLimit=4096;
        static bool SystemKeyboard=>TouchScreenKeyboard.isSupported;
        float cursor,captionTime;bool sessionPage;Action returnPage;string editValue;Text editText;
        static readonly Color Ink=new Color(.91f,.94f,.90f),Muted=new Color(.64f,.74f,.70f),Panel=new Color(.045f,.095f,.087f,.97f),Accent=new Color(.88f,.69f,.41f);
        public Canvas Canvas => canvas;
        public void Initialize(AppRoot root)
        {
            app=root;font=Resources.Load<Font>("Fonts/NotoSansJP");
            var go=new GameObject("Conversation UI",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));go.transform.SetParent(transform,false);canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=app.Head.GetComponent<Camera>();canvas.sortingOrder=10;
            var rect=go.GetComponent<RectTransform>();rect.sizeDelta=new Vector2(960,1000);rect.localScale=Vector3.one*.0012f;
            var background=go.AddComponent<Image>();background.color=Panel;
            content=Rect("Content",rect,36,26,888,948);Place(false);
        }
        RectTransform Rect(string name,Transform parent,float x,float y,float width,float height)
        {
            var go=new GameObject(name,typeof(RectTransform));var r=go.GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(width,height);return r;
        }
        Text TextAt(string value,Transform parent,float x,float y,float width,float height,int size,Color color)
        {
            var text=Rect("Text",parent,x,y,width,height).gameObject.AddComponent<Text>();text.font=font;text.text=value;text.fontSize=size;text.color=color;text.alignment=TextAnchor.MiddleLeft;text.horizontalOverflow=HorizontalWrapMode.Wrap;text.verticalOverflow=VerticalWrapMode.Truncate;text.raycastTarget=false;return text;
        }
        void Clear(string eyebrow,string title,string description,bool small=false)
        {
            IsEditing=false;editValue="";editText=null;refreshEdit=submitEdit=null;Input.imeCompositionMode=IMECompositionMode.Auto;CloseSystemKeyboard();
            if(microphoneCheck)microphoneCheck.Stop();
            foreach(Transform child in content){child.gameObject.SetActive(false);Destroy(child.gameObject);}sessionPage=small;
            TextAt(eyebrow,content,0,0,888,34,21,Accent);TextAt(title,content,0,44,888,80,48,Ink);TextAt(description,content,0,131,888,65,24,Muted);cursor=214;
            status=TextAt("",content,0,853,888,83,23,Accent);Place(small);
            jevStatus=TextAt("",content,0,815,888,34,19,Muted);nextJevStatus=0;
        }
        void Place(bool small)
        {
            if(!app.Head)return;
            Vector3 forward=Vector3.ProjectOnPlane(app.Head.forward,Vector3.up).normalized;if(forward.sqrMagnitude<.1f)forward=Vector3.forward;
            Vector3 right=Vector3.Cross(Vector3.up,forward);
            canvas.transform.position=app.Head.position+forward*(small?1.55f:1.8f)+right*(small?-1.05f:0)-Vector3.up*(small?.18f:.05f);
            canvas.transform.rotation=Quaternion.LookRotation(canvas.transform.position-app.Head.position,Vector3.up);
            canvas.transform.localScale=Vector3.one*.0012f*app.Settings.uiScale*(small?.68f:1);
        }
        public void Recenter()=>Place(sessionPage);
        Button ButtonAt(string text,float x,float y,float width,float height,Action onClick,bool primary=false)
        {
            var rect=Rect("Button "+text,content,x,y,width,height);var image=rect.gameObject.AddComponent<Image>();image.color=primary?Accent:new Color(.11f,.20f,.18f);var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=image;var colors=button.colors;colors.highlightedColor=new Color(1,.92f,.77f);colors.pressedColor=new Color(.65f,.79f,.72f);button.colors=colors;
            var label=TextAt(text,rect,20,0,width-40,height,height<55?23:29,primary?Panel:Ink);label.alignment=TextAnchor.MiddleCenter;
            if(onClick!=null)button.onClick.AddListener(()=>onClick());return button;
        }
        Button Button(string text,Action action,bool primary=false){var button=ButtonAt(text,0,cursor,888,70,action,primary);cursor+=86;return button;}
        void Note(string text,int height=62){TextAt(text,content,0,cursor,888,height,24,Muted);cursor+=height+12;}
        void Back(Action action)=>ButtonAt("← 戻る",0,778,250,58,action);
        public void MainMenu()
        {
            Clear("WHITE ROOM  /  VR COMPANION","ひと息、いっしょに。","いつもの部屋で、いつもの相手と。声でつながる小さな時間。");
            var previous=Button("つづきから",()=>app.StartSession(true));previous.interactable=app.HasSave;
            Button("キャラクター  /  "+(app.characters.FirstOrDefault(x=>x.id==app.Settings.characterId)?.displayName??"穏やか"),Characters);
            Button("お部屋  /  "+(app.environments.FirstOrDefault(x=>x.id==app.Settings.environmentId)?.displayName??"リビング"),Environments);
            Button("設定",SettingsMenu);Button("この部屋ではじめる",()=>app.StartSession(false),true);
            ShowStatus("音声はAIが生成します。会話にはインターネット接続が必要です。");
        }
        public void Session()
        {
            if(app.Brain)app.Brain.Resume();Clear("TOGETHER","声をかけてみよう","「ソファの後ろに行って」「踊って」など、話した通りに動きます。",true);
            Button("会話に接続  /  GPT-Live-1",()=>app.Brain?.Connect(),true);
            var ptt=ButtonAt("押して話す  /  PTT",0,cursor,430,70,null);var hold=ptt.gameObject.AddComponent<HoldToTalk>();hold.Down=()=>app.Brain?.BeginRecording();hold.Up=()=>app.Brain?.EndRecording();
            ButtonAt("文字で話す  ›",454,cursor,434,70,TextChat);cursor+=86;
            ButtonAt("接続を終了",0,cursor,430,64,()=>app.Brain?.Disconnect());ButtonAt("設定",454,cursor,434,64,SettingsMenu);cursor+=84;
            ButtonAt("おもちゃで遊ぶ",0,cursor,280,64,Playground);ButtonAt("動きを試す",304,cursor,280,64,MotionLab);ButtonAt("メインメニューへ",608,cursor,280,64,app.ReturnMenu);cursor+=84;
            captions=TextAt("",content,0,cursor,888,214,28,Ink);ShowStatus("PC: U長押し ／ Quest: A・Xボタン ／ 画面: PTT長押し");
        }
        static readonly string[] QuickPhrases={"うん","ううん","ありがとう","もう一回言って","ちょっと待って"};
        // Short labels for the quick buttons and the request each one sends.
        static readonly string[] QuickOrders={"真ん中へ","こっちへ","踊って","手を振って","止まって"},QuickOrderTexts={"部屋の真ん中に行って","こっちに来て","踊って","手を振って","止まって"};
        // Ready-made requests that run on the device, plus free wording: places said in words, body movements and toys.
        public void MotionLab()
        {
            if(!app.Brain)return;
            Clear("MOTION","言葉で動かしてみよう","接続・キーなしで試せます。行き先も言葉で伝えられます。",true);
            string[] labels={"部屋の真ん中へ行く","あなたの右に来る","見ている床へ行く","部屋の奥へ行く","右手を上げたまま歩く","左手だけを振る","床に座る","立ち上がる","3回ジャンプ","ボールを転がす"};
            string[] commands={"部屋の真ん中に行って","私の右に来て","あそこに行って","部屋の奥に行って","右手を上げたまま2歩前に歩いて","左手だけを振って","床に座って","立って","3回ジャンプして","ボールを出してこっちに転がして"};
            for(int i=0;i<labels.Length;i++)
            {
                string command=commands[i];
                ButtonAt(labels[i],i%2*454,cursor+i/2*76,434,64,()=>app.Brain?.PerformLocalMotion(command));
            }
            cursor+=380;
            ButtonAt("言葉で指示する  ›",0,cursor,434,64,InstructionInput);ButtonAt("動きを止める",454,cursor,434,64,()=>app.Brain?.performer?.Stop(),true);cursor+=80;
            Note("「見ている床へ行く」は、床を見ながら（または右グリップ／Fキーで指さしてから）押します。左右はキャラクター自身の左右です。",78);
            Back(Session);
        }
        // Typing for when the user cannot speak: quick phrases send at once, the kana keys build a message. PC keyboards (with IME) also work.
        public void TextChat()=>TextInput(false);
        // The same keyboard, but the words go straight to the body (no voice connection): 「窓の前に立って手を振って」.
        public void InstructionInput()=>TextInput(true);
        void TextInput(bool order)
        {
            if(!app.Brain)return;
            string keyboard=SystemKeyboard?"「キーボード」で漢字・英数字や音声入力も使えます。":"PCはキーボード入力・Enterで送信。";
            if(order)Clear("ORDER","言葉で指示する","動きや行き先を書くと、その通りに動きます。");
            else Clear("TEXT","文字で話しかける","声を出せない時用。返事は声と字幕で届きます。");
            Action back=order?(Action)MotionLab:Session;Action<string> send=order?(Action<string>)SendOrder:SendTyped;
            editValue="";IsEditing=true;secretEditing=false;editLimit=CharacterBrain.MaxTypedLength;returnPage=back;Input.imeCompositionMode=IMECompositionMode.On;
            editText=TextAt("",content,10,196,868,74,28,Ink);
            Action refresh=()=>editText.text=editValue.Length>0?editValue:"（ここに入力した文字が出ます）";refreshEdit=refresh;refresh();submitEdit=()=>send(editValue);
            var quick=order?QuickOrders:QuickPhrases;
            for(int i=0;i<quick.Length;i++){string phrase=order?QuickOrderTexts[i]:quick[i];ButtonAt(quick[i],i*177.6f,278,171,48,()=>send(phrase));}
            for(int r=0;r<KanaInput.Rows.Length;r++)for(int i=0;i<KanaInput.Rows[r].Length;i++){string key=KanaInput.Rows[r][i].ToString();ButtonAt(key=="　"?"空白":key,i*88.8f,336+r*52,82.8f,46,()=>{editValue=KanaInput.Press(editValue,key,CharacterBrain.MaxTypedLength);refresh();});}
            EditRow(656,refresh,"送信",()=>send(editValue));Back(back);ShowStatus(keyboard);
        }
        void SendTyped(string text)
        {
            if(string.IsNullOrWhiteSpace(text)){ShowStatus("文字を入力してください。");return;}
            if(app.Brain&&app.Brain.SendText(text))Session();
        }
        void SendOrder(string text)
        {
            if(string.IsNullOrWhiteSpace(text)){ShowStatus("してほしいことを入力してください。");return;}
            if(!app.Brain)return;
            // Back to the motion page first, so the progress of the request shows there.
            MotionLab();app.Brain.Instruct(text);
        }
        public void Playground()
        {
            if(!app.Brain)return;app.Brain.Resume();
            Clear("ROOM PLAY","置いて、頼んで、遊ぼう","会話はつながったまま。「ボールをこっちに転がして」「積み木を積んで」と声や文字で頼めます。",true);
            var play=app.Brain.playground;if(!play)return;play.Status=ShowStatus;
            ButtonAt("ボールを置く",0,cursor,280,70,()=>play.BeginPlacement(CompanionPlayground.ToyKind.Ball));
            ButtonAt("積み木を置く",300,cursor,280,70,()=>play.BeginPlacement(CompanionPlayground.ToyKind.Blocks));
            ButtonAt("回転おもちゃ",600,cursor,288,70,()=>play.BeginPlacement(CompanionPlayground.ToyKind.Spinner));cursor+=86;
            Note("床をクリックで配置。置いたおもちゃをクリックすると自分で転がす・崩す・回せます。",70);
            ButtonAt("選んだ物で遊ぶ",0,cursor,430,70,()=>play.PlaySelected(),true);ButtonAt("次のおもちゃを選ぶ",454,cursor,434,70,play.Next);cursor+=86;
            var ptt=ButtonAt("押して話す  /  PTT",0,cursor,430,70,null);var hold=ptt.gameObject.AddComponent<HoldToTalk>();hold.Down=()=>app.Brain?.BeginRecording();hold.Up=()=>app.Brain?.EndRecording();
            ButtonAt("文字で話す  ›",454,cursor,434,70,TextChat);cursor+=86;
            ButtonAt("動きを止める",0,cursor,430,64,()=>{play.Cancel();ShowStatus("止めました。");});ButtonAt("すべて片づける",454,cursor,434,64,play.ClearToys);
            Back(Session);ShowStatus("選択: "+play.SelectedName);
        }
        int characterPage;
        void Characters()
        {
            Clear("CHARACTER","今日の話し相手","それぞれの性格・声・記憶を持っています。「マイモデル」はあなたが追加したVRMです。");
            // Two columns, four rows a page; a page button fills the last row when there are more.
            const int perPage=8;int pages=Mathf.Max(1,(app.characters.Length+perPage-1)/perPage);characterPage=Mathf.Clamp(characterPage,0,pages-1);
            var shown=app.characters.Skip(characterPage*perPage).Take(perPage).ToArray();
            for(int i=0;i<shown.Length;i++){var p=shown[i];ButtonAt((p.id==app.Settings.characterId?"● ":"")+p.displayName,i%2*454,cursor+i/2*76,434,64,()=>{app.Settings.characterId=p.id;app.SaveSettings();Characters();},p.id==app.Settings.characterId);}
            cursor+=4*76;
            if(pages>1){ButtonAt("前のページ",0,cursor,434,58,()=>{characterPage=(characterPage+pages-1)%pages;Characters();});ButtonAt("次のページ  ("+(characterPage+1)+" / "+pages+")",454,cursor,434,58,()=>{characterPage=(characterPage+1)%pages;Characters();});cursor+=72;}
            var selected=app.characters.FirstOrDefault(p=>p.id==app.Settings.characterId);
            if(selected)Note(selected.personality,pages>1?100:150);
            Back(MainMenu);ButtonAt("自分のモデルを追加  ›",270,778,618,58,MyModels);
        }
        // How to add a VRM: the folder to put it in on this device (or a download link), then a rescan.
        void MyModels()
        {
            Clear("MY MODEL","自分のモデルを追加","VRM（.vrm）の人型モデルを、話し相手として追加できます。");
            Note("VRoid Studioなどで作ったVRM（0.x / 1.0）を下のフォルダに入れて「一覧を更新」、または「URLから追加」で .vrm への直接リンクを入力します。歩く・身振り・表情・まばたき・口パク・髪の揺れで動きます。",126);
            Note("フォルダ: "+CustomModels.Folder,70);
#if UNITY_ANDROID && !UNITY_EDITOR
            Note("USBの場合: QuestをPCにつなぎ、Quest内の Android/data/"+Application.identifier+"/files/Models にコピーします。",70);
#endif
            Note("同じ名前の .txt で名前・性格・声を設定できます。1行目は名前、次に「声: gleam」または「声: marin」、残りは性格を書きます。省略時は名前と性格から声を選びます。",100);
            float w=SystemFolder?280:434,gap=SystemFolder?24:20;
            ButtonAt("一覧を更新",0,cursor,w,70,()=>{int count=app.RefreshCustomModels();MyModels();ShowStatus(count>0?"マイモデル "+count+" 体を読み込みました。キャラクター一覧から選べます。":"VRMファイルが見つかりません。フォルダを確認してください。");},true);
            ButtonAt("URLから追加  ›",w+gap,cursor,w,70,()=>{returnPage=MyModels;Keyboard(".vrm のURL","",DownloadModel,false);});
            if(SystemFolder)ButtonAt("フォルダを開く",2*(w+gap),cursor,888-2*(w+gap),70,()=>Application.OpenURL(new Uri(CustomModels.Folder).AbsoluteUri));
            cursor+=86;Back(Characters);
        }
#if UNITY_ANDROID && !UNITY_EDITOR
        const bool SystemFolder=false;
#else
        const bool SystemFolder=true;
#endif
        async void DownloadModel(string url)
        {
            if(string.IsNullOrWhiteSpace(url))return;
            // Let the input page close first so the status shows on the page it returns to.
            await System.Threading.Tasks.Task.Yield();ShowStatus("ダウンロードしています…");
            try{var path=await CustomModels.Download(url);app.RefreshCustomModels();ShowStatus("「"+System.IO.Path.GetFileNameWithoutExtension(path)+"」を追加しました。キャラクター一覧から選べます。");}
            catch(InvalidOperationException e){ShowStatus(e.Message);}
            catch(Exception){ShowStatus("ダウンロードに失敗しました。URLと接続を確認してください。");}
        }
        void Environments()
        {
            Clear("ENVIRONMENT","落ち着く場所へ","木のぬくもりと、やわらかな灯り。");
            foreach(var environment in app.environments){var e=environment;Button((e.id==app.Settings.environmentId?"● ":"")+e.displayName,()=>{app.Settings.environmentId=e.id;app.SaveSettings();Environments();},e.id==app.Settings.environmentId);}
            Button("時間帯  /  "+TimeLabel(app.Settings.timeMode),()=>{app.Settings.timeMode=(TimeMode)(((int)app.Settings.timeMode+1)%5);app.SaveSettings();Environments();});Back(MainMenu);
        }
        static string TimeLabel(TimeMode mode)=>new[]{"自動","朝","昼","夕方","夜"}[(int)mode];
        public void SettingsMenu()
        {
            if(app.Brain)app.Brain.Suspend();Clear("SETTINGS","心地よく話すために","設定はQuest本体に保存します。");
            string[] names={"AI","Speech / 話しかけ方","Voice / 声","Character / 視線","Behavior / 反応","Memory / 記憶","VR / 表示","Privacy / プライバシー"};
            for(int i=0;i<names.Length;i++){int index=i;ButtonAt(names[i],i%2*454,214+i/2*104,434,82,()=>SettingsPage(index));}
            Back(()=>{app.SaveSettings();if(app.Brain)Session();else MainMenu();});
        }
        void SettingsPage(int page)
        {
            Clear("SETTINGS",new[]{"AI接続","話しかけ方","声と音量","キャラクター","身体の反応","会話と記憶","VR表示","プライバシー"}[page],"変更は自動的に保存されます。");returnPage=()=>SettingsPage(page);
            switch(page)
            {
                case 0:
                    EditButton("OpenAI API Key", "",v=>app.Secrets.Set("OpenAI",v),true);
                    EditButton("Liveモデル",app.Settings.voiceModel,v=>app.Settings.voiceModel=v);
                    EditButton("動作計画・記憶のモデル",app.Settings.llmModel,v=>app.Settings.llmModel=v);
                    Button("GPT-Live接続テスト",()=>app.TestProvider("OpenAI"));Note("会話本体: GPT-Live-1 / 言葉からの動作計画・目で見て説明・記憶抽出: 上のモデル（OpenAI API）");break;
                case 1:Note("PTT（Quest: A・Xボタン ／ PC: Uキー ／ 画面のPTTボタン）を押している間だけマイクを送信します。1回の録音は最大60秒です。",90);
                    Note("Questの操作（カッコ内はPCのキー）\n移動: 左スティック（WASD）／ 向き: 右スティックを左右\nテレポート: 右スティックを前に倒して離す（Tを押して離す）\n指さし: 右グリップ（F）。床を指すとキャラクターがそこへ歩きます\n手を振る: Yボタン（H）／ しゃがむ・立つ: 左スティック押し込み（C）",230);
                    Button("マイク・再生を確認（4秒）",()=>{if(!microphoneCheck)microphoneCheck=gameObject.AddComponent<MicrophoneCheck>();microphoneCheck.Begin(app.Settings.voiceVolume,ShowStatus);});Note("録音してその場で再生します。外部送信・ファイル保存は行いません。",62);break;
                case 2:
                    var profile=app.characters.FirstOrDefault(p=>p.id==app.Settings.characterId);
                    if(profile!=null)
                    {
                        string selected=app.Settings.VoiceFor(profile);
                        Note(profile.displayName+"の声: "+selected,56);
                        string[] voices={"","marin","gleam"};
                        string[] labels={"キャラ既定","marin","gleam"};
                        for(int i=0;i<voices.Length;i++)
                        {
                            string voice=voices[i];
                            bool active=app.Settings.characterVoices!=null&&app.Settings.characterVoices.TryGetValue(profile.id,out var chosen)
                                ? chosen==voice : (voice.Length==0&&string.IsNullOrEmpty(app.Settings.voiceOverride));
                            ButtonAt((active?"● ":"")+labels[i],i*300,cursor,288,64,()=>{SetCharacterVoice(profile,voice);SettingsPage(page);},active);
                        }
                        cursor+=80;
                        Button("音声名を入力  ›",()=>Keyboard("Live音声名",selected,v=>SetCharacterVoice(profile,v),false));
                    }
                    Button("音量: "+Mathf.RoundToInt(app.Settings.voiceVolume*100)+"%",()=>{app.Settings.voiceVolume=app.Settings.voiceVolume>=.99f?0:Mathf.Min(1,app.Settings.voiceVolume+.1f);app.SaveSettings();SettingsPage(page);});
                    Button("ロボット声: "+new[]{"ロボのみ","全員","OFF"}[Mathf.Clamp(app.Settings.robotVoice,0,2)],()=>{app.Settings.robotVoice=(Mathf.Clamp(app.Settings.robotVoice,0,2)+1)%3;app.SaveSettings();SettingsPage(page);});
                    Note("声の種類はキャラクターごとに保存します。変更は次回接続時、ロボット声の加工はすぐに反映します。",90);break;
                case 3:Button("視線の追従: "+Mathf.RoundToInt(app.Settings.lookStrength*100)+"%",()=>{app.Settings.lookStrength=app.Settings.lookStrength>=1?0:app.Settings.lookStrength+.25f;app.SaveSettings();SettingsPage(page);});Note("HMDの方向で注視を判定します。瞬き・呼吸・口パクは端末内で動きます。",70);
                    Button("指示どおりに動く: "+(app.Settings.motionPlanning?"ON":"OFF"),()=>{app.Settings.motionPlanning=!app.Settings.motionPlanning;app.SaveSettings();SettingsPage(page);});
                    Button("視覚（見えた物に反応）: "+(app.Settings.vision?"ON":"OFF"),()=>{app.Settings.vision=!app.Settings.vision;app.SaveSettings();SettingsPage(page);});
                    Button("目で見て説明（画像をOpenAIへ）: "+(app.Settings.visionImages?"ON":"OFF"),()=>{app.Settings.visionImages=!app.Settings.visionImages;app.SaveSettings();SettingsPage(page);});
                    Note("指示の動作計画と「目で見て説明」はOpenAIキーを使用します。",62);break;
                case 4:
                    EditButton("Jev API Key","",v=>app.Secrets.Set("Jev",v),true);EditButton("Jevモデル",app.Settings.behaviorModel,v=>app.Settings.behaviorModel=v);
                    Button("Jev利用設定: "+(app.Settings.behaviorEnabled?"ON（認証状態は下部）":"OFF"),()=>{app.Settings.behaviorEnabled=!app.Settings.behaviorEnabled;app.SaveSettings();SettingsPage(page);});Button("Jev接続テスト",()=>app.TestProvider("Jev"));
                    Button("ローカル散歩: "+(app.Settings.autonomousMovement?"ON":"OFF"),()=>{app.Settings.autonomousMovement=!app.Settings.autonomousMovement;app.SaveSettings();SettingsPage(page);});
                    Note("散歩・しぐさ・おもちゃはキーなしでも動きます。Jevの認証とは別です。指示どおりの動き・視覚は「キャラクター」設定。",88);break;
                case 5:
                    Button("会話・記憶の保存: "+(app.Settings.memoryEnabled?"ON":"OFF"),()=>{app.Settings.memoryEnabled=!app.Settings.memoryEnabled;app.SaveSettings();SettingsPage(page);});
                    Note("保存済みの会話・好み・出来事は、選択したキャラクターだけが参照します。OFF中は新しい会話を保存しません。",120);
                    Button("このキャラクターの記憶を削除",()=>Confirm("このキャラクターの会話と記憶を削除します。",app.DeleteMemory,()=>SettingsPage(page)));break;
                case 6:
                    Button("字幕: "+app.Settings.subtitles,()=>{app.Settings.subtitles=(SubtitleMode)(((int)app.Settings.subtitles+1)%3);app.SaveSettings();SettingsPage(page);});
                    Button("表示サイズ: "+Mathf.RoundToInt(app.Settings.uiScale*100)+"%",()=>{app.Settings.uiScale=app.Settings.uiScale>1.2f?.8f:app.Settings.uiScale+.1f;app.SaveSettings();SettingsPage(page);});
                    Button("時間帯: "+TimeLabel(app.Settings.timeMode),()=>{app.Settings.timeMode=(TimeMode)(((int)app.Settings.timeMode+1)%5);app.SaveSettings();SettingsPage(page);});Button("パネルを目の前へ",Recenter);break;
                case 7:
                    Note("音声はOpenAIへ、文字と状態はOpenAI／Jevへ送信します。「目で見て説明」ではキャラクター視点のCG画像をOpenAIへ送ります。録音音声は端末へ保存しません。",112);
                    Note("Live接続は終了操作・一時停止・120秒の無操作で切断します。利用料金は接続時間などに応じて発生します。",112);
                    Button("APIキーをすべて削除",()=>Confirm("保存済みのAPIキーを削除します。",app.DeleteKeys,()=>SettingsPage(page)));
                    Button("全ローカルデータを削除",()=>Confirm("会話・記憶・設定・APIキーを削除します。",app.DeleteAllData,MainMenu));break;
            }
            Back(SettingsMenu);
        }
        void SetCharacterVoice(CharacterProfile profile,string voice)
        {
            if(app.Settings.characterVoices==null)app.Settings.characterVoices=new System.Collections.Generic.Dictionary<string,string>();
            app.Settings.characterVoices[profile.id]=voice;
            app.SaveSettings();
        }
        void EditButton(string label,string value,Action<string> save,bool secret=false)=>Button(label+"  ›",()=>Keyboard(label,value,save,secret));
        void Keyboard(string label,string value,Action<string> save,bool secret)
        {
            Clear("INPUT",label,SystemKeyboard?"入力後に「保存」。「キーボード」でQuestのキーボードと貼り付けも使えます。":"貼り付け、または入力後に「保存」。PCではCtrl+Vも使えます。");editValue=value;IsEditing=true;secretEditing=secret;editLimit=512;
            editText=TextAt(secret?new string('●',editValue.Length):editValue,content,10,208,868,85,28,Ink);
            string[] rows={"1234567890","qwertyuiop","asdfghjkl","zxcvbnm","QWERTYUIOP","ASDFGHJKL","ZXCVBNM_-./+=:?&%"};
            Action refresh=()=>editText.text=secret?new string('●',Math.Min(100,editValue.Length)):editValue;
            refreshEdit=refresh;
            for(int r=0;r<rows.Length;r++)for(int i=0;i<rows[r].Length;i++){string ch=rows[r][i].ToString();float step=Mathf.Min(72,888f/rows[r].Length);ButtonAt(ch,i*step,306+r*54,step-6,48,()=>{if(editValue.Length<512)editValue+=ch;refresh();});}
            EditRow(697,refresh,"保存",()=>{try{save(editValue.Trim());app.SaveSettings();editValue="";returnPage();}catch{ShowStatus("保存できませんでした。もう一度お試しください。");}});Back(()=>{editValue="";returnPage();});
            ButtonAt("貼り付け",290,778,270,58,()=>{try{PasteText(GUIUtility.systemCopyBuffer);}catch{ShowStatus("クリップボードを読み取れませんでした。");}});
        }
        // Delete / clear / (Quest keyboard) / confirm along the bottom of an input page.
        void EditRow(float y,Action refresh,string confirm,Action done)
        {
            float w=SystemKeyboard?200:270,gap=SystemKeyboard?10:20;
            ButtonAt("1文字消す",0,y,w,60,()=>{if(editValue.Length>0)editValue=editValue.Substring(0,editValue.Length-1);refresh();});
            ButtonAt("すべて消す",w+gap,y,w,60,()=>{editValue="";refresh();});
            if(SystemKeyboard)ButtonAt("キーボード",2*(w+gap),y,w,60,OpenSystemKeyboard);
            float x=SystemKeyboard?3*(w+gap):580;ButtonAt(confirm,x,y,888-x,60,done,true);
        }
        public void OpenSystemKeyboard()
        {
            if(!IsEditing||!SystemKeyboard)return;CloseSystemKeyboard();
            systemKeyboard=TouchScreenKeyboard.Open(editValue,secretEditing?TouchScreenKeyboardType.ASCIICapable:TouchScreenKeyboardType.Default,!secretEditing,false,secretEditing);
            if(systemKeyboard!=null)systemKeyboard.characterLimit=editLimit;
        }
        void CloseSystemKeyboard(){if(systemKeyboard==null)return;if(systemKeyboard.active)systemKeyboard.active=false;systemKeyboard=null;}
        // Mirrors the Quest keyboard into the input field; its Done key sends the message like Enter on PC.
        void PollSystemKeyboard()
        {
            if(systemKeyboard==null)return;
            if(!IsEditing){CloseSystemKeyboard();return;}
            var text=systemKeyboard.text??"";if(secretEditing)text=text.Trim();
            if(text.Length>editLimit)text=text.Substring(0,editLimit);
            if(text!=editValue){editValue=text;refreshEdit?.Invoke();}
            var state=systemKeyboard.status;
            if(state==TouchScreenKeyboard.Status.Visible)return;
            systemKeyboard=null;
            if(state==TouchScreenKeyboard.Status.Done&&submitEdit!=null)submitEdit();
        }
        public void PasteText(string text)
        {
            if(!IsEditing)return;
            if(!ClipboardInput.TryNormalize(text,secretEditing,out var value)){ShowStatus("空欄・長すぎる文字列・空白を含むキーは貼り付けできません。");return;}
            editValue=value;refreshEdit?.Invoke();ShowStatus("貼り付けました。「保存」で確定してください。");
        }
        void Confirm(string message,Action confirm,Action back){Clear("CONFIRM","削除の確認",message);Button("削除する",()=>{confirm();back();},true);Button("キャンセル",back);}
        public void ShowStatus(string value){if(status)status.text=value;}
        public void SetCaptions(string input,string output){captionTime=Time.time;if(captions)captions.text=app.Settings.subtitles==SubtitleMode.Off?"":(output.Length>0?output:input);}
        void Update()
        {
            if(jevStatus&&app.JevStatus!=null&&Time.unscaledTime>=nextJevStatus){nextJevStatus=Time.unscaledTime+.5f;jevStatus.text=app.JevStatus.Label+(app.Settings.behaviorEnabled?"":" / 自動判断OFF");}
            if(captions&&app.Settings.subtitles==SubtitleMode.Minimal&&Time.time-captionTime>8)captions.text="";
            PollSystemKeyboard();
#if UNITY_EDITOR || UNITY_STANDALONE
            if(!IsEditing)return;
            bool command=Input.GetKey(KeyCode.LeftControl)||Input.GetKey(KeyCode.RightControl)||Input.GetKey(KeyCode.LeftCommand)||Input.GetKey(KeyCode.RightCommand);
            if(command){if(Input.GetKeyDown(KeyCode.V))PasteText(GUIUtility.systemCopyBuffer);return;}
            foreach(char c in Input.inputString){if((c=='\r'||c=='\n')&&submitEdit!=null){submitEdit();return;}if(c=='\b'){if(editValue.Length>0)editValue=editValue.Substring(0,editValue.Length-1);}else if(!char.IsControl(c)&&editValue.Length<4096&&(!secretEditing||!char.IsWhiteSpace(c)))editValue+=c;}
            refreshEdit?.Invoke();
#endif
        }
    }
    // Hiragana keys with voiced, semi-voiced and small-kana modifiers applied to the last character.
    public static class KanaInput
    {
        public static readonly string[] Rows={"あかさたなはまやらわ","いきしちにひみゆりを","うくすつぬふむよるん","えけせてねへめ、れー","おこそとのほも。ろ？","゛゜小！〜　"};
        const string Plain="かきくけこさしすせそたちつてとはひふへほう",Voiced="がぎぐげござじずぜぞだぢづでどばびぶべぼゔ",Half="はひふへほ",HalfVoiced="ぱぴぷぺぽ",Large="あいうえおつやゆよわ",Small="ぁぃぅぇぉっゃゅょゎ";
        public static string Press(string value,string key,int max)
        {
            if(key=="゛"||key=="゜"||key=="小")
            {
                if(value.Length==0)return value;char last=value[value.Length-1],next=Modify(last,key);
                return next==last?value:value.Substring(0,value.Length-1)+next;
            }
            return value.Length<max?value+key:value;
        }
        static char Modify(char c,string key)
        {
            int i;
            if(key=="゛"){if((i=Plain.IndexOf(c))>=0)return Voiced[i];if((i=Voiced.IndexOf(c))>=0)return Plain[i];if((i=HalfVoiced.IndexOf(c))>=0)return Voiced[Plain.IndexOf(Half[i])];}
            else if(key=="゜"){if((i=Half.IndexOf(c))>=0)return HalfVoiced[i];if((i=HalfVoiced.IndexOf(c))>=0)return Half[i];if((i=Voiced.IndexOf(c))>=0&&(i=Half.IndexOf(Plain[i]))>=0)return HalfVoiced[i];}
            else{if((i=Large.IndexOf(c))>=0)return Small[i];if((i=Small.IndexOf(c))>=0)return Large[i];}
            return c;
        }
    }
    public class HoldToTalk : MonoBehaviour,IPointerDownHandler,IPointerUpHandler
    {
        public Action Down,Up;bool held;
        public void OnPointerDown(PointerEventData e){held=true;Down?.Invoke();}
        public void OnPointerUp(PointerEventData e){if(held){held=false;Up?.Invoke();}}
        void OnDisable(){if(held){held=false;Up?.Invoke();}}
    }
}
