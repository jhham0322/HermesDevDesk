param([string]$Destination)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
if (-not $Destination) { $Destination=Join-Path $projectRoot 'vendor/codex-0.162.1' }
$Destination=[IO.Path]::GetFullPath($Destination)
if(-not $Destination.StartsWith([IO.Path]::GetFullPath($projectRoot).TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Engine destination must remain inside this project'}
for($checkPath=$Destination;$checkPath;$checkPath=Split-Path $checkPath -Parent){if((Test-Path -LiteralPath $checkPath) -and ((Get-Item -LiteralPath $checkPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Reparse destination blocked'}}
if (Test-Path -LiteralPath $Destination) { throw 'Destination already exists; preserve it and choose a new path.' }
$private=Join-Path $projectRoot ('artifacts/engine-fetch-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $private -Force | Out-Null
$archive=Join-Path $private 'codex-0.162.1-win32-x64.tgz'
Invoke-WebRequest -Uri 'https://registry.npmjs.org/@openai/codex/-/codex-0.162.1-win32-x64.tgz' -OutFile $archive
$sha=[Security.Cryptography.SHA512]::Create()
$hash='sha512-'+[Convert]::ToBase64String($sha.ComputeHash([IO.File]::ReadAllBytes($archive)))
if($hash -ne 'sha512-dfl4MhHuEi0ARKyNmrTxk+RQmsWNCQAoVLUtrYq+yrW1C8P2sg5ziwgHUUACHjYLKuACV7ARH5Nq5pnCz3GlaQ=='){throw 'Official package integrity mismatch; extraction blocked'}
$names=& tar -tf $archive
if($LASTEXITCODE -ne 0){throw 'Archive listing failed'}
foreach($name in $names){if($name.StartsWith('/') -or $name.Contains(':') -or $name.Contains('\') -or $name.Split('/') -contains '..' -or -not $name.StartsWith('package/')){throw 'Unsafe archive name'}}
& tar -xf $archive -C $private
if($LASTEXITCODE -ne 0){throw 'Extraction failed'}
Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/openai/codex/rust-v0.162.1/LICENSE' -OutFile (Join-Path $private 'package/CODEX-LICENSE.txt')
Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/openai/codex/rust-v0.162.1/NOTICE' -OutFile (Join-Path $private 'package/CODEX-NOTICE.txt')
New-Item -ItemType Directory -Path (Split-Path $Destination -Parent) -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $private 'package') -Destination $Destination -Recurse
Write-Output $Destination
