param([string]$DotnetPath, [string]$SevenZipPath, [switch]$SkipPublish, [string]$EnginePackagePath, [string]$OutputDirectory = 'artifacts/final', [switch]$CompressInstaller, [string]$PackageSource = 'https://api.nuget.org/v3/index.json', [string]$PackagesDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $projectRoot
if (-not $DotnetPath) { $DotnetPath = (Get-Command dotnet -ErrorAction Stop).Source }
if (-not $SevenZipPath) { $SevenZipPath = [IO.Path]::GetFullPath((Join-Path $projectRoot '../../.tools/7zip2604/portable/x64/7za.exe')) }
if (-not (Test-Path -LiteralPath $DotnetPath)) { throw 'Provide the project-local .NET 10 SDK path with -DotnetPath.' }
$env:DOTNET_ROOT = Split-Path $DotnetPath -Parent
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.build-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_NOLOGO = '1'
$env:MSBuildEnableWorkloadResolver = 'false'
$packages = if ($PackagesDirectory) { [IO.Path]::GetFullPath($PackagesDirectory) } else { Join-Path $projectRoot 'artifacts/nuget' }
$outputPath = [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
if (-not $outputPath.StartsWith($projectRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Build output must remain inside this project.' }
$appPath = Join-Path $outputPath 'app'
if (-not $SkipPublish -and (Test-Path -LiteralPath $outputPath)) { throw 'Choose a new empty output directory for this build.' }
if (-not $SkipPublish) {
    & $DotnetPath publish src/Hermes.Desktop/Hermes.Desktop.csproj -c Release -r win-x64 --self-contained true -o $appPath --nologo -p:NuGetAudit=false "-p:RestorePackagesPath=$packages" --source $PackageSource -p:DebugType=none -p:DebugSymbols=false "-p:PathMap=$projectRoot=/_/HermesDevDesk"
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
}
if (-not $EnginePackagePath) { $EnginePackagePath = Join-Path $projectRoot 'vendor/codex-0.162.1' }
if (-not (Test-Path -LiteralPath $EnginePackagePath)) { throw 'Official engine package required' }
if (-not (Test-Path -LiteralPath (Join-Path $appPath 'engine'))) { Copy-Item -LiteralPath $EnginePackagePath -Destination (Join-Path $appPath 'engine') -Recurse }
foreach ($name in @('README.md','LICENSE')) { Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $appPath }
Copy-Item -LiteralPath (Join-Path $packages 'microsoft.netcore.app.runtime.win-x64/10.0.12/LICENSE.TXT') -Destination (Join-Path $appPath 'DOTNET-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $packages 'microsoft.netcore.app.runtime.win-x64/10.0.12/THIRD-PARTY-NOTICES.TXT') -Destination (Join-Path $appPath 'DOTNET-THIRD-PARTY-NOTICES.txt')
Copy-Item -LiteralPath (Join-Path $packages 'microsoft.windowsdesktop.app.runtime.win-x64/10.0.12/LICENSE') -Destination (Join-Path $appPath 'WPF-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $packages 'microsoft.web.webview2/1.0.4258.31/LICENSE.txt') -Destination (Join-Path $appPath 'WEBVIEW2-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $packages 'microsoft.web.webview2/1.0.4258.31/NOTICE.txt') -Destination (Join-Path $appPath 'WEBVIEW2-NOTICE.txt')
Copy-Item -LiteralPath (Join-Path $projectRoot 'design/icons/LICENSE.txt') -Destination (Join-Path $appPath 'FLUENT-ICONS-LICENSE.txt')
$manifestPath = Join-Path $appPath '.hermes-manifest.tsv'
$files = Get-ChildItem -LiteralPath $appPath -File -Recurse | Where-Object { $_.Name -ne '.hermes-manifest.tsv' }
$manifestLines = $files | Sort-Object FullName | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + [char]9 + [IO.Path]::GetRelativePath($appPath, $_.FullName).Replace('\','/') }
[IO.File]::WriteAllLines($manifestPath, [string[]]$manifestLines, [Text.UTF8Encoding]::new($false))
Add-Type -AssemblyName System.IO.Compression.FileSystem
$payload = Join-Path $outputPath ('HermesPayload-' + [guid]::NewGuid().ToString('N') + '.zip')
[IO.Compression.ZipFile]::CreateFromDirectory($appPath, $payload, [IO.Compression.CompressionLevel]::NoCompression, $false)
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$setup = Join-Path $outputPath 'HermesDevDesk-0.8.6-win-x64-Setup.exe'
if (Test-Path -LiteralPath $setup) { throw 'Version artifact already exists; use a fresh version/build directory instead of overwriting.' }
$manifestFile = Join-Path $projectRoot 'src/Hermes.Desktop/app.manifest'
$iconFile = Join-Path $projectRoot 'src/Hermes.Desktop/Assets/Hermes.ico'
$setupSource = Join-Path $projectRoot 'installer/Setup3.cs'
& $compiler /nologo /codepage:65001 /target:winexe /platform:x64 /optimize+ "/win32manifest:$manifestFile" "/win32icon:$iconFile" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /reference:Microsoft.CSharp.dll "/resource:$payload,HermesPayload.zip" "/out:$setup" "$setupSource"
if ($LASTEXITCODE -ne 0) { throw 'Installer compile failed' }
if ($CompressInstaller -and (Test-Path -LiteralPath $SevenZipPath)) {
    $installerArchive = Join-Path $outputPath 'HermesDevDesk-0.8.6-installer.7z'
    & $SevenZipPath a -t7z -m0=LZMA2 -mx=9 -md=32m -mmt=2 $installerArchive $setup
    if ($LASTEXITCODE -ne 0) { throw 'Compression failed' }
    & $SevenZipPath t $installerArchive
    if ($LASTEXITCODE -ne 0) { throw 'Archive validation failed' }
}
$portable = Join-Path $outputPath 'HermesDevDesk-0.8.6-win-x64-Portable.zip'
if (Test-Path -LiteralPath $portable) { throw 'Portable artifact already exists; use a fresh build directory.' }
[IO.Compression.ZipFile]::CreateFromDirectory($appPath, $portable, [IO.Compression.CompressionLevel]::Fastest, $false)
Get-Item -LiteralPath $setup,$portable | Select-Object FullName,Length
Get-FileHash -LiteralPath $setup -Algorithm SHA256 | Format-List
