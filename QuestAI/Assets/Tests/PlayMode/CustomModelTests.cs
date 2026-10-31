using System;
using System.Collections;
using System.Net;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace QuestAI.Tests
{
    // A VRM dropped into the Models folder shows up as a character and moves like the built-in ones.
    public class CustomModelTests
    {
        const string Fixture = "Assets/Tests/Fixtures/MascotDog.vrm.bytes";
        string model, notes;
        [UnityTest] public IEnumerator AddedVrmBecomesAMovingCharacter()
        {
            Directory.CreateDirectory(CustomModels.Folder);
            model = Path.Combine(CustomModels.Folder, "TestDog.vrm"); notes = Path.ChangeExtension(model, ".txt");
            File.Copy(Fixture, model, true); File.WriteAllText(notes, "テストわんこ\nのんびりした犬。");
            AppRoot.EditorRepositoryFactory = () => new LocalRepository();
            yield return SceneManager.LoadSceneAsync("Bootstrap");
            float deadline = Time.realtimeSinceStartup + 30;
            while ((!AppRoot.Instance || !AppRoot.Instance.UI || AppRoot.Instance.IsChanging) && Time.realtimeSinceStartup < deadline) yield return null;
            var app = AppRoot.Instance; app.RefreshCustomModels();
            var profile = app.characters.FirstOrDefault(c => c.id == CustomModels.Prefix + "TestDog");
            Assert.NotNull(profile, "The VRM is listed"); Assert.That(profile.displayName, Does.StartWith("テストわんこ")); Assert.That(profile.personality, Is.EqualTo("のんびりした犬。"));

            app.Settings.characterId = profile.id; app.Settings.environmentId = "LivingRoom"; app.Settings.autonomousMovement = false;
            app.StartSession(false); deadline = Time.realtimeSinceStartup + 30;
            while ((app.IsChanging || !app.Brain || app.Brain.profile.id != profile.id) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.NotNull(app.Brain, "The session starts with the added model");
            var brain = app.Brain; var rig = brain.body.rig;
            Assert.True(brain.GetComponent<ClipAnimator>().Humanoid); Assert.NotNull(brain.GetComponent<VrmFaceDriver>());
            Assert.That(brain.gameObject.scene, Is.EqualTo(SceneManager.GetActiveScene()), "The model belongs to the room and unloads with it");
            yield return new WaitForSeconds(.5f);
            float feet = Mathf.Min(rig.animator.GetBoneTransform(HumanBodyBones.LeftFoot).position.y, rig.animator.GetBoneTransform(HumanBodyBones.RightFoot).position.y) - brain.transform.position.y;
            Assert.That(feet, Is.InRange(-.05f, .25f), "Stands on the floor");
            Assert.That(rig.head.position.y - brain.transform.position.y, Is.InRange(.4f, 2f));
            // Instructed motions drive it like the built-in characters: a wave lifts and moves the right hand.
            var hand = rig.animator.GetBoneTransform(HumanBodyBones.RightHand); var before = hand.position;
            Assert.True(brain.PerformLocalMotion("右手を振って"));
            float moved = 0; for (float end = Time.time + 2.5f; Time.time < end; ) { moved = Mathf.Max(moved, Vector3.Distance(before, hand.position)); yield return null; }
            yield return ApplicationTests.SaveImage(app.Head.GetComponent<Camera>(), "CustomModel-Wave");
            brain.performer.Stop(); yield return null;
            Assert.That(moved, Is.GreaterThan(.03f), "Gestures animate the model");
            // Conversation gestures the clip library has no clip for (wave, shrug) still move the arms.
            foreach (var gesture in new[] { Gesture.wave, Gesture.shrug })
            {
                yield return new WaitForSeconds(.4f); before = hand.position; brain.body.Apply(new BehaviorDecision { emotion = Emotion.happy, gesture = gesture });
                moved = 0; for (float end = Time.time + 2f; Time.time < end; ) { moved = Mathf.Max(moved, Vector3.Distance(before, hand.position)); yield return null; }
                Assert.That(moved, Is.GreaterThan(.05f), gesture + " moves the arm");
            }
            Assert.True(brain.PerformLocalMotion("2歩前に歩いて")); var start = brain.transform.position; yield return new WaitForSeconds(3f);
            Assert.That(Vector3.Distance(start, brain.transform.position), Is.GreaterThan(.2f), "It walks");
            yield return ApplicationTests.SaveImage(app.Head.GetComponent<Camera>(), "CustomModel-Session");

            app.ReturnMenu(); deadline = Time.realtimeSinceStartup + 20;
            while ((app.IsChanging || app.Brain) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.Null(app.Brain);
        }
        [UnityTest] public IEnumerator BrokenFileKeepsTheMenu()
        {
            Directory.CreateDirectory(CustomModels.Folder);
            model = Path.Combine(CustomModels.Folder, "Broken.vrm"); notes = null; File.WriteAllText(model, "not a model");
            AppRoot.EditorRepositoryFactory = () => new LocalRepository();
            yield return SceneManager.LoadSceneAsync("Bootstrap");
            float deadline = Time.realtimeSinceStartup + 30;
            while ((!AppRoot.Instance || !AppRoot.Instance.UI || AppRoot.Instance.IsChanging) && Time.realtimeSinceStartup < deadline) yield return null;
            var app = AppRoot.Instance; app.RefreshCustomModels();
            app.Settings.characterId = CustomModels.Prefix + "Broken"; app.StartSession(false);
            deadline = Time.realtimeSinceStartup + 20; yield return null;
            while (app.IsChanging && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.Null(app.Brain, "No session starts with a broken file");
            Assert.True(SceneManager.GetSceneByName("MainMenu").isLoaded, "The menu stays");
        }
        // A direct link downloads into the Models folder; a web page that is not a VRM is refused without saving anything.
        [UnityTest] public IEnumerator DownloadsVrmFromLink()
        {
            var bytes = File.ReadAllBytes(Fixture); int port = 18000 + UnityEngine.Random.Range(0, 1000);
            using var server = new HttpListener(); server.Prefixes.Add("http://localhost:" + port + "/"); server.Start();
            _ = Task.Run(async () =>
            {
                while (server.IsListening)
                {
                    HttpListenerContext context; try { context = await server.GetContextAsync(); } catch { return; }
                    var body = context.Request.Url.AbsolutePath.EndsWith(".vrm") ? bytes : System.Text.Encoding.UTF8.GetBytes("<html>share page</html>");
                    context.Response.ContentLength64 = body.Length; context.Response.OutputStream.Write(body, 0, body.Length); context.Response.Close();
                }
            });
            var invalid = CustomModels.Download("ftp://example.com/a.vrm"); Assert.True(invalid.IsFaulted); Assert.IsInstanceOf<InvalidOperationException>(invalid.Exception.InnerException);
            var page = CustomModels.Download("http://localhost:" + port + "/share"); while (!page.IsCompleted) yield return null;
            Assert.True(page.IsFaulted, "A web page is refused"); Assert.That(page.Exception.InnerException.Message, Does.Contain("直接リンク"));
            var download = CustomModels.Download("http://localhost:" + port + "/files/Neko%20Chan.vrm"); while (!download.IsCompleted) yield return null;
            Assert.False(download.IsFaulted, download.Exception?.ToString()); model = download.Result;
            Assert.That(Path.GetFileName(model), Is.EqualTo("Neko Chan.vrm")); Assert.That(File.ReadAllBytes(model).Length, Is.EqualTo(bytes.Length));
            Assert.True(CustomModels.Scan().Any(p => p.id == CustomModels.Prefix + "Neko Chan"));
            server.Stop();
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            foreach (var path in new[] { model, notes }) if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path);
            if (AppRoot.Instance) Object.Destroy(AppRoot.Instance.gameObject); AppRoot.EditorRepositoryFactory = null; yield return null;
        }
    }
}
