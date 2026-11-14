using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace QuestAI.Tests
{
    public class ApplicationTests
    {
        [UnityTest] public IEnumerator MenuRoomsExpressionsAndContinue()
        {
            AppRoot.EditorRepositoryFactory=()=>new LocalRepository();
            yield return SceneManager.LoadSceneAsync("Bootstrap");
            float deadline=Time.realtimeSinceStartup+20;
            while((!AppRoot.Instance||!AppRoot.Instance.UI||!SceneManager.GetSceneByName("MainMenu").isLoaded)&&Time.realtimeSinceStartup<deadline)yield return null;
            var app=AppRoot.Instance;Assert.NotNull(app);Assert.NotNull(app.UI);yield return null;
            Assert.That(app.characters.Select(p=>p.id),Is.EquivalentTo(new[]{"calm","bright","robot","bunny","cat"}));Assert.That(app.environments.Length,Is.EqualTo(4));
            Assert.True(app.UI.Canvas.GetComponentsInChildren<Text>().Any(t=>t.text.Contains("ひと息")));
            yield return SaveImage(app.Head.GetComponent<Camera>(),"MainMenu-UI");
            app.UI.SettingsMenu();Click(app,"AI");Click(app,"OpenAI API Key",true);
            app.UI.PasteText("  sk-T  ");Assert.True(app.UI.IsEditing);
            Assert.False(app.UI.Canvas.GetComponentsInChildren<Text>().Any(t=>t.text.Contains("sk-T")),"Secret input must stay masked");
            yield return SaveImage(app.Head.GetComponent<Camera>(),"Keyboard-UI");
            Click(app,"保存");Assert.That(app.Secrets.Get("OpenAI"),Is.EqualTo("sk-T"));app.Secrets.DeleteAll();app.UI.MainMenu();
            foreach(var env in app.environments)
            {
                app.Settings.environmentId=env.id;app.StartSession(false);deadline=Time.realtimeSinceStartup+20;
                while((!app.Brain||app.Brain.environment!=env.displayName)&&Time.realtimeSinceStartup<deadline)yield return null;
                yield return null;Assert.NotNull(app.Brain);Assert.That(app.Brain.environment,Is.EqualTo(env.displayName));
                var binding=app.Brain.GetComponent<CharacterRigBinding>();Assert.NotNull(binding.head);Assert.That(binding.animator.GetBoneTransform(HumanBodyBones.LeftFoot).position.y,Is.GreaterThan(-.05f));Assert.NotNull(app.Brain.GetComponent<FaceDriver>(),"The face follows emotion, blink, mouth and gaze");
                var lighting=Object.FindAnyObjectByType<EnvironmentLighting>();Assert.That(lighting.bakes.Length,Is.EqualTo(4));foreach(var bake in lighting.bakes)Assert.That(bake.maps.Length,Is.GreaterThan(0));
                app.Brain.body.Apply(new BehaviorDecision{emotion=Emotion.happy,gesture=Gesture.wave});yield return null;yield return SaveImage(app.Head.GetComponent<Camera>(),env.id+"-Session");
            }
            // The session page leads straight to toy play and to the motion page with its typed-instruction input.
            Click(app,"おもちゃで遊ぶ");Assert.False(app.Brain.Connected);Assert.True(app.UI.Canvas.GetComponentsInChildren<Text>().Any(t=>t.text=="ボールを置く"));
            yield return SaveImage(app.Head.GetComponent<Camera>(),"Playground-UI");
            Click(app,"← 戻る");Click(app,"動きを試す");Click(app,"言葉で指示する  ›");Assert.True(app.UI.IsEditing);
            yield return SaveImage(app.Head.GetComponent<Camera>(),"Instruction-UI");
            Click(app,"← 戻る");Assert.False(app.UI.IsEditing);Click(app,"← 戻る");
            // Exercise visible poses, not just the presence of BlendShape channel names.
            foreach(Emotion emotion in System.Enum.GetValues(typeof(Emotion)))
            {
                app.Brain.body.Apply(new BehaviorDecision{emotion=emotion,gesture=emotion==Emotion.happy?Gesture.wave:Gesture.none});
                yield return new WaitForSeconds(.35f);
                foreach(var renderer in app.Brain.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var posed=new Mesh();renderer.BakeMesh(posed);
                    Assert.True(posed.vertices.All(v=>!float.IsNaN(v.x)&&!float.IsNaN(v.y)&&!float.IsNaN(v.z)),emotion.ToString());
                    Assert.That(posed.bounds.size.magnitude,Is.LessThan(3),"Deformed character bounds: "+emotion);Object.Destroy(posed);
                }
                yield return SaveImage(app.Head.GetComponent<Camera>(),"Character-"+emotion);
            }
            app.Brain.State.relationship=.42f;string character=app.Brain.profile.id;string environment=app.Settings.environmentId;app.SaveSession();Assert.True(app.HasSave);
            app.ReturnMenu();deadline=Time.realtimeSinceStartup+20;while((app.IsChanging||app.Brain||!SceneManager.GetSceneByName("MainMenu").isLoaded)&&Time.realtimeSinceStartup<deadline)yield return null;Assert.False(app.IsChanging);
            app.Settings.characterId="bright";app.StartSession(true);deadline=Time.realtimeSinceStartup+20;while((app.IsChanging||!app.Brain)&&Time.realtimeSinceStartup<deadline)yield return null;Assert.NotNull(app.Brain);Assert.That(app.Brain.profile.id,Is.EqualTo(character));Assert.That(app.Settings.environmentId,Is.EqualTo(environment));Assert.That(app.Brain.State.relationship,Is.EqualTo(.42f));Assert.False(app.Brain.Connected);
            Object.Destroy(app.gameObject);AppRoot.EditorRepositoryFactory=null;yield return null;
        }
        static void Click(AppRoot app,string label,bool prefix=false)
        {
            var button=app.UI.Canvas.GetComponentsInChildren<Button>().First(b=>prefix?b.GetComponentInChildren<Text>().text.StartsWith(label):b.GetComponentInChildren<Text>().text==label);button.onClick.Invoke();
        }
        public static IEnumerator SaveImage(Camera camera,string name)
        {
            Directory.CreateDirectory("../artifacts/previews");var target=new RenderTexture(1600,1000,24);target.Create();camera.targetTexture=target;for(int frame=0;frame<5;frame++)yield return null;var old=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(1600,1000,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1600,1000),0,0);image.Apply();if(name.Contains("UI")||name.Contains("Session")){var pixels=image.GetPixels32();float clipped=pixels.Count(p=>p.r>250&&p.g>250&&p.b>250)/(float)pixels.Length;Assert.That(clipped,Is.LessThan(.25f),"Room lighting is overexposed: "+name);}
            File.WriteAllBytes("../artifacts/previews/"+name+".png",image.EncodeToPNG());RenderTexture.active=old;camera.targetTexture=null;Object.Destroy(image);target.Release();Object.Destroy(target);
        }
        [UnityTearDown] public IEnumerator Cleanup(){if(AppRoot.Instance)Object.Destroy(AppRoot.Instance.gameObject);AppRoot.EditorRepositoryFactory=null;yield return null;}
    }
}

