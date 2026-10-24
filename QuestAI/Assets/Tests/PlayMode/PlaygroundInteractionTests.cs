using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
namespace QuestAI.Tests
{
    public class PlaygroundInteractionTests
    {
        [UnityTest] public IEnumerator WalkPlayCancelAndRemoveRestoreRoom()
        {
            AppRoot.EditorRepositoryFactory=()=>new LocalRepository();
            yield return SceneManager.LoadSceneAsync("Bootstrap");
            float deadline=Time.realtimeSinceStartup+30;
            while((!AppRoot.Instance||!AppRoot.Instance.UI||AppRoot.Instance.IsChanging)&&Time.realtimeSinceStartup<deadline)yield return null;
            var app=AppRoot.Instance;Assert.NotNull(app);yield return new WaitForSeconds(1);
            app.Settings.autonomousMovement=false;app.StartSession(false);
            deadline=Time.realtimeSinceStartup+30;
            while((app.IsChanging||!app.Brain)&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.NotNull(app.Brain);app.UI.Playground();
            var brain=app.Brain;var play=brain.GetComponent<CompanionPlayground>();int count=brain.world.objects.Count;
            Time.timeScale=4;
            try
            {
                bool placed=false;
                foreach(var p in brain.world.grid.FreeCells().OrderBy(p=>Vector3.Distance(p,brain.transform.position)).ToArray())
                    if(CompanionPlayground.CanPlace(brain.world,p,brain.transform.position,app.Head.position,new Vector3[0]))
                    {placed=play.TryPlace(CompanionPlayground.ToyKind.Ball,p);if(placed)break;}
                Assert.True(placed);Assert.AreEqual(count+1,brain.world.objects.Count);
                Assert.True(play.PlaySelected());deadline=Time.realtimeSinceStartup+25;
                while(play.Busy&&Time.realtimeSinceStartup<deadline)yield return null;
                Assert.False(play.Busy);Assert.False(brain.locomotion.Moving);
                yield return ApplicationTests.SaveImage(app.Head.GetComponent<Camera>(),"Playground-ball");
                Assert.True(play.PlaySelected());play.Cancel();yield return null;
                Assert.False(play.Busy);Assert.False(brain.locomotion.Moving);Assert.AreEqual(BodySystem.Pose.stand,brain.body.CurrentPose);
                play.ClearToys();Assert.AreEqual(0,play.Count);Assert.AreEqual(count,brain.world.objects.Count);
            }
            finally{Time.timeScale=1;Object.Destroy(app.gameObject);AppRoot.EditorRepositoryFactory=null;}
            yield return null;
        }
    }
}
