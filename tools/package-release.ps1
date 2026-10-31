param([Parameter(Mandatory=$true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskWindows = Join-Path $taskRoot 'QuestAI/Builds/Windows'
$taskApk = Join-Path $taskRoot 'QuestAI/Builds/WhiteRoom-Quest3S.apk'
if (-not (Test-Path -LiteralPath (Join-Path $taskWindows 'WhiteRoom.exe')) -or -not (Test-Path -LiteralPath $taskApk)) { throw 'Build Windows and Quest before packaging.' }
$taskOutput = Join-Path $taskRoot "artifacts/releases/v$Version"
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskZip = Join-Path $taskOutput 'VRtalk-Windows.zip'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$taskStream = [System.IO.File]::Open($taskZip, [System.IO.FileMode]::Create)
$taskArchive = [System.IO.Compression.ZipArchive]::new($taskStream, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($taskFile in Get-ChildItem -LiteralPath $taskWindows -Recurse -File) {
        $taskRelative = $taskFile.FullName.Substring($taskWindows.TrimEnd([char[]]'\/').Length + 1)
        if ($taskRelative -match '(^|[\\/])[^\\/]*DontShip[^\\/]*([\\/]|$)' -or $taskRelative -eq 'desktop-smoke.png') { continue }
        $taskEntry = 'VRtalk-Windows/' + $taskRelative.Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskArchive, $taskFile.FullName, $taskEntry, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $taskArchive.Dispose(); $taskStream.Dispose() }
Copy-Item -LiteralPath $taskApk -Destination (Join-Path $taskOutput 'VRtalk-Quest3S.apk') -Force
$taskChecksums = foreach ($taskName in @('VRtalk-Windows.zip', 'VRtalk-Quest3S.apk')) {
    $taskFile = Get-Item -LiteralPath (Join-Path $taskOutput $taskName)
    [pscustomobject]@{ name=$taskName; bytes=$taskFile.Length; sha256=(Get-FileHash -LiteralPath $taskFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
}
[System.IO.File]::WriteAllText((Join-Path $taskOutput 'checksums.json'), ($taskChecksums | ConvertTo-Json), [System.Text.UTF8Encoding]::new($false))
($taskChecksums | ForEach-Object { $_.sha256 + '  ' + $_.name }) | Set-Content -LiteralPath (Join-Path $taskOutput 'SHA256SUMS.txt') -Encoding ascii
$taskChecksums | ConvertTo-Json
Write-Output "Release package: $taskOutput"
