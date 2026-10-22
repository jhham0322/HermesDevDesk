param([Parameter(Mandatory=$true)][string]$ReleaseDirectory, [Parameter(Mandatory=$true)][string]$ExpectedVersion)
$ErrorActionPreference='Stop'
$release=[IO.Path]::GetFullPath($ReleaseDirectory)
$fixture=Join-Path $release ('force-reinstall-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$target=Join-Path $fixture 'install'
$setup=Join-Path $release "HermesDevDesk-$ExpectedVersion-win-x64-Setup.exe"
function Install-Fixture {
 $p=Start-Process -FilePath $setup -ArgumentList @('/extract',('"'+$target+'"')) -WindowStyle Hidden -PassThru
 if(-not $p.WaitForExit(60000)){throw 'Fixture install timed out'}
 if($p.ExitCode -ne 0){throw "Fixture install failed: $(Get-Content ($target+'.install-error.txt') -Raw)"}
}
Install-Fixture
$source=Join-Path $fixture 'LockHolder.cs'
@"
using System;
using System.IO;
using System.Threading;
class LockHolder{
 static void Main(string[] a){using(var file=new FileStream(a[0],FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){File.WriteAllText(a[1],"ready");while(true)Thread.Sleep(100);}}
}
"@ | Set-Content -LiteralPath $source
$helper=Join-Path $fixture 'LockHolder.exe'
& (Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe') /nologo /target:winexe /platform:x64 "/out:$helper" $source
if($LASTEXITCODE -ne 0){throw 'Lock fixture compilation failed'}
$inside=Join-Path $target 'engine/LockHolder.exe'
Copy-Item -LiteralPath $helper -Destination $inside
$processes=@()
function Start-Holder([string]$exe,[string]$file,[string]$name){
 $ready=Join-Path $fixture ($name+'.ready')
 $p=Start-Process -FilePath $exe -ArgumentList @(('"'+$file+'"'),('"'+$ready+'"')) -WindowStyle Hidden -PassThru
 $watch=[Diagnostics.Stopwatch]::StartNew()
 while(-not (Test-Path -LiteralPath $ready)){
  if($p.HasExited -or $watch.Elapsed.TotalSeconds -gt 10){throw 'Lock fixture failed to start'}
  Start-Sleep -Milliseconds 50
 }
 return $p
}
try {
 $app=Start-Holder $inside (Join-Path $target 'engine/in-use.bin') 'inside'
 $processes+= $app
 $holder=Start-Holder $helper (Join-Path $target 'external-lock.bin') 'outside'
 $processes+= $holder
 $unrelated=Start-Holder $helper (Join-Path $fixture 'unrelated.bin') 'unrelated'
 $processes+= $unrelated
 $outsideHistory=Join-Path $fixture 'state.json';Set-Content $outsideHistory 'KEEP_HISTORY'
 $stale=Join-Path $target 'read-only-stale.txt';Set-Content $stale 'REMOVE_ME';[IO.File]::SetAttributes($stale,[IO.FileAttributes]::ReadOnly)
 Set-Content (Join-Path $target 'README.md') 'CORRUPT_OLD_FILE'
 # Simulate an interrupted/damaged old install, recognized by the actual app executable.
 Remove-Item -LiteralPath (Join-Path $target '.hermes-owned')
 Install-Fixture
 $app.Refresh();$holder.Refresh();$unrelated.Refresh()
 if(-not $app.HasExited -or -not $holder.HasExited){throw 'Installation lock holders were not force closed'}
 if($unrelated.HasExited){throw 'Unrelated process was terminated'}
 foreach($relative in @('read-only-stale.txt','external-lock.bin','engine/in-use.bin','engine/LockHolder.exe')){
  if(Test-Path -LiteralPath (Join-Path $target $relative)){throw "Stale file remains: $relative"}
 }
 if((Get-Content $outsideHistory -Raw).Trim() -ne 'KEEP_HISTORY'){throw 'Outside history was changed'}
 if([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $target 'HermesDevDesk.exe')).FileVersion -ne "$ExpectedVersion.0"){throw 'Installed version mismatch'}
 [ordered]@{passed=$true;version=$ExpectedVersion;installPath=$target;appProcessForcedClosed=$true;externalFileHolderForcedClosed=$true;unrelatedProcessPreserved=$true;readOnlyAndStaleFilesRemoved=$true;damagedOwnershipRecovered=$true;outsideHistoryPreserved=$true}|ConvertTo-Json|Set-Content (Join-Path $release 'force-reinstall-report.json')
 Get-Content (Join-Path $release 'force-reinstall-report.json')
} finally {
 foreach($p in $processes){if(-not $p.HasExited){$p.Kill();$p.WaitForExit(10000)|Out-Null};$p.Dispose()}
}
