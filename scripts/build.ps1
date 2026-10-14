param([string]$DotnetPath, [string]$SevenZipPath, [switch]$SkipPublish, [string]$EnginePackagePath)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $projectRoot
if (-not $DotnetPath) { $DotnetPath = [IO.Path]::GetFullPath((Join-Path $projectRoot '../../.tools/dotnet10/sdk/dotnet.exe')) }
if (-not $SevenZipPath) { $SevenZipPath = [IO.Path]::GetFullPath((Join-Path $projectRoot '../../.tools/7zip2604/portable/x64/7za.exe')) }
if (-not (Test-Path -LiteralPath $DotnetPath)) { throw 'Provide the project-local .NET 10 SDK path with -DotnetPath.' }
$env:DOTNET_ROOT = Split-Path $DotnetPath -Parent
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.build-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_NOLOGO = '1'
$packages = Join-Path $projectRoot 'artifacts/nuget'
if (-not $SkipPublish) {
    & $DotnetPath publish src/Hermes.Desktop/Hermes.Desktop.csproj -c Release -r win-x64 --self-contained true -o artifacts/final/app --nologo -p:NuGetAudit=false "-p:RestorePackagesPath=$packages" --source https://api.nuget.org/v3/index.json
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
}
$appPath = Join-Path $projectRoot 'artifacts/final/app'
if (-not $EnginePackagePath) { $EnginePackagePath = Join-Path $projectRoot 'vendor/codex-0.162.1' }
if (-not (Test-Path -LiteralPath $EnginePackagePath)) { throw 'Official engine package required' }
if (-not (Test-Path -LiteralPath (Join-Path $appPath 'engine'))) { Copy-Item -LiteralPath $EnginePackagePath -Destination (Join-Path $appPath 'engine') -Recurse }
foreach ($name in @('README.md','LICENSE')) { Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $appPath }
Copy-Item -LiteralPath (Join-Path $packages 'microsoft.netcore.app.runtime.win-x64/10.0.12/LICENSE.TXT') -Destination (Join-Path $appPath 'DOTNET-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $packages 'microsoft.netcore.app.runtime.win-x64/10.0.12/THIRD-PARTY-NOTICES.TXT') -Destination (Join-Path $appPath 'DOTNET-THIRD-PARTY-NOTICES.txt')
Copy-Item -LiteralPath (Join-Path $packages 'microsoft.windowsdesktop.app.runtime.win-x64/10.0.12/LICENSE') -Destination (Join-Path $appPath 'WPF-LICENSE.txt')
$manifestPath = Join-Path $appPath '.hermes-manifest.tsv'
$files = Get-ChildItem -LiteralPath $appPath -File -Recurse | Where-Object { $_.Name -ne '.hermes-manifest.tsv' }
$manifestLines = $files | Sort-Object FullName | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + [char]9 + [IO.Path]::GetRelativePath($appPath, $_.FullName).Replace('\','/') }
[IO.File]::WriteAllLines($manifestPath, [string[]]$manifestLines, [Text.UTF8Encoding]::new($false))
Add-Type -AssemblyName System.IO.Compression.FileSystem
$payload = Join-Path $projectRoot ('artifacts/final/HermesPayload-' + [guid]::NewGuid().ToString('N') + '.zip')
[IO.Compression.ZipFile]::CreateFromDirectory($appPath, $payload, [IO.Compression.CompressionLevel]::NoCompression, $false)
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$setup = Join-Path $projectRoot 'artifacts/final/HermesDevDesk-0.5.1-win-x64-Setup.exe'
if (Test-Path -LiteralPath $setup) { throw 'Version artifact already exists; use a fresh version/build directory instead of overwriting.' }
$manifestFile = Join-Path $projectRoot 'src/Hermes.Desktop/app.manifest'
$setupSource = Join-Path $projectRoot 'installer/Setup3.cs'
& $compiler /nologo /codepage:65001 /target:winexe /platform:x64 /optimize+ "/win32manifest:$manifestFile" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /reference:Microsoft.CSharp.dll "/resource:$payload,HermesPayload.zip" "/out:$setup" "$setupSource"
if ($LASTEXITCODE -ne 0) { throw 'Installer compile failed' }
if (Test-Path -LiteralPath $SevenZipPath) {
    & $SevenZipPath a -t7z -m0=LZMA2 -mx=9 -md=32m -mmt=2 'artifacts/final/HermesDevDesk-0.5.1-installer.7z' $setup
    if ($LASTEXITCODE -ne 0) { throw 'Compression failed' }
    & $SevenZipPath t 'artifacts/final/HermesDevDesk-0.5.1-installer.7z'
    if ($LASTEXITCODE -ne 0) { throw 'Archive validation failed' }
}
Get-Item -LiteralPath $setup | Select-Object FullName,Length
Get-FileHash -LiteralPath $setup -Algorithm SHA256 | Format-List
