param(
    [string]$Unity = 'C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe',
    [string]$ProjectPath
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $taskRoot 'QuestAI'
if ($ProjectPath) { $project = (Resolve-Path -LiteralPath $ProjectPath).Path }
$log = Join-Path $taskRoot 'artifacts/windows-build.log'
$arguments = @('-batchmode','-quit','-buildTarget','Win64','-projectPath',('"' + $project + '"'),'-executeMethod','QuestAI.Editor.DesktopBuild.Build','-logFile',('"' + $log + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WindowStyle Hidden -PassThru
$process.WaitForExit()
if ($process.ExitCode -ne 0) { throw "Windows build failed. See $log" }
Get-Item -LiteralPath (Join-Path $project 'Builds/Windows/WhiteRoom.exe')
