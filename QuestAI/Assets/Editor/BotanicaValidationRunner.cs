using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
namespace QuestAI.Editor
{
    [InitializeOnLoad]
    public static class BotanicaValidationRunner
    {
        static TestRunnerApi api;
        static BotanicaValidationRunner()
        {
            api=ScriptableObject.CreateInstance<TestRunnerApi>();api.RegisterCallbacks(new Results());
            EditorApplication.delayCall+=Check;
        }
        static void Check()
        {
            const string request="../artifacts/botanica-test.request";
            if(!File.Exists(request))return;
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode){EditorApplication.delayCall+=Check;return;}
            File.Delete(request);
            SessionState.SetBool("BotanicaValidationRunning",true);
            api.Execute(new ExecutionSettings(new Filter {testMode=TestMode.PlayMode, testNames=new[]{"QuestAI.Tests"}}));
        }
        class Results:ICallbacks
        {
            public void RunStarted(ITestAdaptor t){}
            public void TestStarted(ITestAdaptor t){}
            public void TestFinished(ITestResultAdaptor r){}
            public void RunFinished(ITestResultAdaptor r)
            {
                if(!SessionState.GetBool("BotanicaValidationRunning",false))return;
                SessionState.SetBool("BotanicaValidationRunning",false);
                TestRunnerApi.SaveResultToFile(r,"../artifacts/botanica-playmode.xml");
                File.WriteAllText("../artifacts/botanica-tests-result.txt",r.TestStatus+"\nPassed: "+r.PassCount+"\nFailed: "+r.FailCount);
            }
        }
    }
}
