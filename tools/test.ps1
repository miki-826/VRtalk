param(
    [string]$Unity = 'C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe',
    [string]$ProjectPath
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $taskRoot 'QuestAI'
if ($ProjectPath) { $project = (Resolve-Path -LiteralPath $ProjectPath).Path }
$output = Join-Path $taskRoot 'artifacts'
New-Item -ItemType Directory -Force $output | Out-Null
if (-not (Test-Path -LiteralPath $Unity)) { throw "Unity not found: $Unity" }
foreach ($suite in @('EditMode', 'PlayMode')) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $result = Join-Path $output "$suite-$stamp.xml"
    $log = Join-Path $output "$suite-$stamp.log"
    $arguments = @('-batchmode', '-buildTarget', 'Android', '-projectPath', ('"' + $project + '"'), '-runTests', '-testPlatform', $suite, '-testFilter', 'QuestAI.Tests', '-testResults', ('"' + $result + '"'), '-logFile', ('"' + $log + '"'))
    $process = Start-Process -FilePath $Unity -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $process.WaitForExit()
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $result)) { throw "$suite failed. See $log" }
    [xml]$report = Get-Content -LiteralPath $result -Raw
    if ($report.'test-run'.result -ne 'Passed' -or [int]$report.'test-run'.total -eq 0) { throw "$suite tests did not pass. See $result" }
    Write-Output "$suite : $($report.'test-run'.passed)/$($report.'test-run'.total) passed ($result)"
}

