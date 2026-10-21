param([Parameter(Mandatory=$true)][string]$ReleaseDirectory, [Parameter(Mandatory=$true)][string]$ExpectedVersion)
$ErrorActionPreference = 'Stop'
$releaseRoot = [IO.Path]::GetFullPath($ReleaseDirectory)
$setup = Join-Path $releaseRoot "HermesDevDesk-$ExpectedVersion-win-x64-Setup.exe"
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($setup).FileVersion -ne "$ExpectedVersion.0") { throw 'Installer file version mismatch' }
$fixture = Join-Path $releaseRoot ('installed-check-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
function Run-Checked([string]$Executable, [string[]]$Arguments) {
    $process = Start-Process -FilePath $Executable -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(60000)) { throw "Test process timed out: $Executable" }
    if ($process.ExitCode -ne 0) { throw "Test process failed: $Executable, exit=$($process.ExitCode); reports in $fixture" }
}
$verify = Join-Path $fixture 'installer-verify.txt'
Run-Checked $setup @('/verify', ('"'+$verify+'"'))
$cleanupReport = Join-Path $fixture 'cleanup-report.txt'
Run-Checked $setup @('/cleanup-test', ('"'+(Join-Path $fixture 'cleanup-fixture')+'"'), ('"'+$cleanupReport+'"'))
Get-Content -LiteralPath $cleanupReport
$installed = Join-Path $fixture 'install' 
Run-Checked $setup @('/extract', ('"'+$installed+'"'))
foreach ($binary in @('HermesDevDesk.exe','HermesDevDesk.dll')) {
    if ([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $installed $binary)).FileVersion -ne "$ExpectedVersion.0") { throw "Installed $binary version mismatch" }
}
$app = Join-Path $installed 'HermesDevDesk.exe'
$data = Join-Path $fixture 'model-settings-data'
Run-Checked $app @('--simple-settings-smoke', '--data-dir', ('"'+$data+'"'))
$settings = Get-Content -LiteralPath (Join-Path $data 'simple-settings-report.json') -Raw | ConvertFrom-Json
if (-not $settings.passed -or $settings.version -ne $ExpectedVersion -or $settings.executable -ne $app -or $settings.checks -lt 16) { throw 'Installed runtime/UI version or model actions mismatch' }
$performanceData = Join-Path $fixture 'conversation-data'
Run-Checked $app @('--conversation-performance-smoke', '--data-dir', ('"'+$performanceData+'"'))
[ordered]@{ passed = $true; version = $ExpectedVersion; installerSha256 = (Get-FileHash $setup -Algorithm SHA256).Hash; installedExecutable = $app; modelUiChecks = $settings.checks; modelScreenshot = (Join-Path $data 'simple-settings.png'); conversationReport = (Join-Path $performanceData 'conversation-performance.json') } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $releaseRoot 'installed-validation.json') -Encoding utf8
Get-Content -LiteralPath (Join-Path $releaseRoot 'installed-validation.json')
