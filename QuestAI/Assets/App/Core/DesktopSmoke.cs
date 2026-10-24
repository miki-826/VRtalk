#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using UnityEngine;
namespace QuestAI
{
    // Opt-in command-line check: no network, microphone or persistent user data.
    public sealed class DesktopSmoke : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install(){if(Array.IndexOf(Environment.GetCommandLineArgs(),"--desktop-smoke")>=0){var go=new GameObject("Desktop smoke check");DontDestroyOnLoad(go);go.AddComponent<DesktopSmoke>();}}
        IEnumerator Start()
        {
            float end=Time.realtimeSinceStartup+60;
            while((!AppRoot.Instance||!AppRoot.Instance.UI||!UnityEngine.SceneManagement.SceneManager.GetSceneByName("MainMenu").isLoaded)&&Time.realtimeSinceStartup<end)yield return null;
            if(!AppRoot.Instance||!AppRoot.Instance.UI){Debug.LogError("DESKTOP_SMOKE_FAILED startup");Application.Quit(1);yield break;}
            yield return new WaitForSeconds(1);
            var app=AppRoot.Instance;app.StartSession(false);end=Time.realtimeSinceStartup+60;
            while((app.IsChanging||!app.Brain)&&Time.realtimeSinceStartup<end)yield return null;
            if(!app.Brain||app.IsChanging){Debug.LogError("DESKTOP_SMOKE_FAILED room");Application.Quit(1);yield break;}
            yield return new WaitForSeconds(1);
            var camera=app.Head.GetComponent<Camera>();var target=new RenderTexture(1600,1000,24);target.Create();camera.targetTexture=target;camera.Render();
            var previous=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(1600,1000,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1600,1000),0,0);image.Apply();
            File.WriteAllBytes(Path.Combine(Application.dataPath,"../desktop-smoke.png"),image.EncodeToPNG());RenderTexture.active=previous;camera.targetTexture=null;Destroy(image);target.Release();Destroy(target);
            Debug.Log("DESKTOP_SMOKE_OK room, camera, UI, character initialized");Application.Quit(0);
        }
    }
}
#endif
