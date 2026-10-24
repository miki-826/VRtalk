param(
    [string]$Unity = 'C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe',
    [string]$ProjectPath
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $taskRoot 'QuestAI'
if ($ProjectPath) { $project = (Resolve-Path -LiteralPath $ProjectPath).Path }
$log = Join-Path $taskRoot 'artifacts\android-build.log'
New-Item -ItemType Directory -Force (Split-Path -Parent $log) | Out-Null
if (-not (Test-Path -LiteralPath $Unity)) { throw "Unity 6000.6.0f1 was not found: $Unity" }
$arguments = @('-batchmode', '-quit', '-buildTarget', 'Android', '-projectPath', ('"' + $project + '"'), '-executeMethod', 'QuestAI.Editor.ProjectBuilder.BuildApk', '-logFile', ('"' + $log + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WindowStyle Hidden -PassThru
$process.WaitForExit()
if ($process.ExitCode -ne 0) { throw "APK build failed. See $log" }
$apk = Join-Path $project 'Builds\WhiteRoom-Quest3S.apk'
if (-not (Test-Path -LiteralPath $apk)) { throw "Unity exited without creating the APK. See $log" }
Get-Item -LiteralPath $apk
Get-FileHash -LiteralPath $apk -Algorithm SHA256

