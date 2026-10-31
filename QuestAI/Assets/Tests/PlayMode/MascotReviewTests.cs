using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace QuestAI.Tests
{
    // Films each mascot from the front while it walks, waves and changes expression, for visual review in
    // artifacts/previews/Review-*.png. Explicit: runs only when selected by name.
    [Explicit]
    public class MascotReviewTests
    {
        [UnityTest] public IEnumerator FilmMascots()
        {
            AppRoot.EditorRepositoryFactory = () => new LocalRepository();
            yield return SceneManager.LoadSceneAsync("Bootstrap");
            float deadline = Time.realtimeSinceStartup + 30;
            while ((!AppRoot.Instance || !AppRoot.Instance.UI || AppRoot.Instance.IsChanging) && Time.realtimeSinceStartup < deadline) yield return null;
            var app = AppRoot.Instance;
            var camera = new GameObject("Review camera").AddComponent<Camera>(); camera.fieldOfView = 30; camera.nearClipPlane = .05f;
            Object.DontDestroyOnLoad(camera.gameObject);
            foreach (var id in new[] { "calm", "bright", "bunny", "cat" })
            {
                app.Settings.characterId = id; app.Settings.environmentId = "LivingRoom"; app.Settings.autonomousMovement = false;
                app.StartSession(false); deadline = Time.realtimeSinceStartup + 30;
                while ((app.IsChanging || !app.Brain || app.Brain.profile.id != id) && Time.realtimeSinceStartup < deadline) yield return null;
                var brain = app.Brain; var head = brain.body.rig.head;
                void Frame(float distance, float height) { var t = brain.transform; camera.transform.position = t.position + t.forward * distance + Vector3.up * height + t.right * .15f; camera.transform.LookAt(t.position + Vector3.up * height * .85f); }
                yield return new WaitForSeconds(1);
                Frame(1.6f, .55f); yield return ApplicationTests.SaveImage(camera, "Review-" + id + "-idle");
                Assert.True(brain.PerformLocalMotion("ゆっくり3歩前に歩いて"));
                for (int i = 0; i < 3; i++) { yield return new WaitForSeconds(.35f); Frame(1.4f, .55f); yield return ApplicationTests.SaveImage(camera, "Review-" + id + "-walk" + i); }
                yield return new WaitForSeconds(2); brain.performer.Stop();
                brain.body.Apply(new BehaviorDecision { emotion = Emotion.happy, gesture = Gesture.wave });
                yield return new WaitForSeconds(.9f); Frame(1.6f, .55f); yield return ApplicationTests.SaveImage(camera, "Review-" + id + "-wave");
                yield return new WaitForSeconds(2);
                foreach (var emotion in new[] { Emotion.neutral, Emotion.happy, Emotion.sad, Emotion.curious })
                {
                    brain.body.SetEmotion(emotion); yield return new WaitForSeconds(.12f);
                    camera.transform.position = head.position + brain.transform.forward * .75f + Vector3.up * .05f; camera.transform.LookAt(head.position + Vector3.up * .15f);
                    yield return ApplicationTests.SaveImage(camera, "Review-" + id + "-face-" + emotion + "-mid");
                    yield return new WaitForSeconds(1); yield return ApplicationTests.SaveImage(camera, "Review-" + id + "-face-" + emotion);
                }
                camera.transform.position = head.position + brain.transform.right * .7f + Vector3.up * .1f; camera.transform.LookAt(head.position + Vector3.up * .12f);
                yield return ApplicationTests.SaveImage(camera, "Review-" + id + "-side");
                camera.transform.position = head.position - brain.transform.forward * .8f + Vector3.up * .3f; camera.transform.LookAt(head.position);
                yield return ApplicationTests.SaveImage(camera, "Review-" + id + "-back");
            }
            Object.Destroy(camera.gameObject);
        }
        [UnityTearDown] public IEnumerator Cleanup() { if (AppRoot.Instance) Object.Destroy(AppRoot.Instance.gameObject); AppRoot.EditorRepositoryFactory = null; yield return null; }
    }
}
