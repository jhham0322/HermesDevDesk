param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$DataDirectory
)
$ErrorActionPreference = 'Stop'
$clock = [Diagnostics.Stopwatch]::StartNew()
$process = Start-Process -FilePath $AppPath -ArgumentList @('--browser-cancellation-smoke', '--data-dir', ('"' + $DataDirectory + '"')) -WindowStyle Hidden -PassThru
Write-Host "Browser fixture started (PID $($process.Id))"
$exited = $process.WaitForExit(120000)
foreach ($name in @('browser-cancellation-stage.txt', 'browser-cancellation-error.txt')) {
    $path = Join-Path $DataDirectory $name
    if (Test-Path -LiteralPath $path) { Get-Content -LiteralPath $path }
}
if (-not $exited) {
    if (-not $process.HasExited) { $process.Kill() }
    throw 'Browser cancellation fixture watchdog exceeded (120 seconds)'
}
Write-Host "Browser fixture process exited after $($clock.ElapsedMilliseconds)ms with code $($process.ExitCode)"
if ($process.ExitCode -ne 0) { throw 'Browser cancellation checks failed' }
Get-Content -LiteralPath (Join-Path $DataDirectory 'browser-cancellation-report.json')
