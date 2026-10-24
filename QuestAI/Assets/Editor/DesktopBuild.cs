using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.XR.Management;

namespace QuestAI.Editor
{
    public static class DesktopBuild
    {
        [MenuItem("Quest AI/Build Windows desktop")]
        public static void Build()
        {
            if(!EditorBuildSettings.TryGetConfigObject<UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget>(XRGeneralSettings.k_SettingsKey,out var targets))
                throw new InvalidOperationException("XR build settings are missing");
            const string desktopPath="Assets/Generated/DesktopXRSettings.asset";
            var xr=AssetDatabase.LoadAssetAtPath<XRGeneralSettings>(desktopPath);
            if(!xr){xr=ScriptableObject.CreateInstance<XRGeneralSettings>();AssetDatabase.CreateAsset(xr,desktopPath);}
            if(!xr.Manager){xr.Manager=ScriptableObject.CreateInstance<XRManagerSettings>();AssetDatabase.AddObjectToAsset(xr.Manager,xr);}
            xr.InitManagerOnStart=false;xr.Manager.automaticLoading=false;xr.Manager.automaticRunning=false;
            foreach(var loader in xr.Manager.activeLoaders.ToArray())xr.Manager.TryRemoveLoader(loader);
            targets.SetSettingsForBuildTarget(BuildTargetGroup.Standalone,xr);EditorUtility.SetDirty(xr);EditorUtility.SetDirty(xr.Manager);EditorUtility.SetDirty(targets);AssetDatabase.SaveAssets();
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone,ScriptingImplementation.Mono2x);
            PlayerSettings.fullScreenMode=FullScreenMode.Windowed;PlayerSettings.defaultScreenWidth=1600;PlayerSettings.defaultScreenHeight=1000;PlayerSettings.resizableWindow=true;
            Directory.CreateDirectory("Builds/Windows");
            var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=EditorBuildSettings.scenes.Where(x=>x.enabled).Select(x=>x.path).ToArray(),locationPathName="Builds/Windows/WhiteRoom.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
            File.WriteAllText("../artifacts/windows-build-report.txt",result.summary.result+"\n"+result.summary.totalSize+" bytes\n"+result.summary.totalTime);
            if(result.summary.result!=BuildResult.Succeeded)throw new Exception("Windows build failed");
        }
    }
}
