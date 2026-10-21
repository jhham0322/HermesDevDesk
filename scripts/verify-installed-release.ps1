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
$infoPath = Join-Path $fixture 'installer-info.json'
Run-Checked $setup @('/info', ('"'+$infoPath+'"'))
$info = Get-Content -LiteralPath $infoPath -Raw | ConvertFrom-Json
if ($info.version -ne $ExpectedVersion -or -not $info.defaultTarget.EndsWith('Programs\HermesDevDesk\App')) { throw 'Installer does not use fixed installation folder' }
$verify = Join-Path $fixture 'installer-verify.txt'
Run-Checked $setup @('/verify', ('"'+$verify+'"'))
$cleanupReport = Join-Path $fixture 'cleanup-report.txt'
Run-Checked $setup @('/cleanup-test', ('"'+(Join-Path $fixture 'cleanup-fixture')+'"'), ('"'+$cleanupReport+'"'))
Get-Content -LiteralPath $cleanupReport
$installed = Join-Path $fixture 'install'
Run-Checked $setup @('/extract', ('"'+$installed+'"'))
$outsideHistory = Join-Path $fixture 'preserved-history.txt'
Set-Content -LiteralPath $outsideHistory -Value 'PRESERVE_HISTORY'
Set-Content -LiteralPath (Join-Path $installed 'stale-file.txt') -Value 'REMOVE_ON_REINSTALL'
Set-Content -LiteralPath (Join-Path $installed 'README.md') -Value 'REPLACE_ON_REINSTALL'
Run-Checked $setup @('/extract', ('"'+$installed+'"'))
if ((Test-Path -LiteralPath (Join-Path $installed 'stale-file.txt')) -or (Get-Content -LiteralPath $outsideHistory -Raw).Trim() -ne 'PRESERVE_HISTORY') { throw 'Same-folder replacement did not clean installation or preserve external history' }
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
$browserData = Join-Path $fixture 'browser-data'
Run-Checked $app @('--browser-experience-smoke', '--data-dir', ('"'+$browserData+'"'))
$browser = Get-Content -LiteralPath (Join-Path $browserData 'browser-experience-report.json') -Raw | ConvertFrom-Json
if (-not $browser.passed -or $browser.version -ne $ExpectedVersion -or $browser.checks.Count -lt 9) { throw 'Installed browser behavior failed' }
[ordered]@{ passed = $true; version = $ExpectedVersion; installerSha256 = (Get-FileHash $setup -Algorithm SHA256).Hash; installedExecutable = $app; modelUiChecks = $settings.checks; browserChecks = $browser.checks.Count; modelScreenshot = (Join-Path $data 'simple-settings.png'); conversationReport = (Join-Path $performanceData 'conversation-performance.json') } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $releaseRoot 'installed-validation.json') -Encoding utf8
Get-Content -LiteralPath (Join-Path $releaseRoot 'installed-validation.json')
