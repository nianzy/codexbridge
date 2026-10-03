[CmdletBinding()]
param(
    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\..\artifacts\release')
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$propsPath = Join-Path $repositoryRoot 'Directory.Build.props'
$version = (Select-Xml -LiteralPath $propsPath -XPath '/Project/PropertyGroup/Version').Node.InnerText.Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected a stable Windows release version in Directory.Build.props.' }
$notesPath = Join-Path $repositoryRoot "docs\releases\v$version.md"
if (-not (Test-Path -LiteralPath $notesPath -PathType Leaf)) { throw "Release notes missing: $notesPath" }

$outputRoot = [IO.Path]::GetFullPath($OutputRoot)
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
if (-not $outputRoot.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputRoot must be a child of the ignored repository artifacts directory.'
}
$packageName = "CodexBridge-win-x64-v$version"
$zipPath = Join-Path $outputRoot "$packageName.zip"
$checksumPath = "$zipPath.sha256"
if ((Test-Path -LiteralPath $zipPath) -or (Test-Path -LiteralPath $checksumPath)) {
    throw 'Release output already exists. Choose a new OutputRoot; existing packages are never overwritten.'
}
# Fresh staging prevents stale or private files entering a new package.
$stagingRoot = Join-Path $outputRoot ('.staging-' + [Guid]::NewGuid().ToString('N'))
$releaseRoot = Join-Path $stagingRoot $packageName
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null

function Publish-Project([string]$ProjectPath, [string]$OutputPath) {
    & dotnet publish $ProjectPath -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o $OutputPath
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $ProjectPath (exit code $LASTEXITCODE)." }
}

Publish-Project (Join-Path $repositoryRoot 'apps\windows\CodexBridge.App\CodexBridge.App.csproj') (Join-Path $releaseRoot 'App')
Publish-Project (Join-Path $repositoryRoot 'apps\windows\CodexBridge.NativeHost\CodexBridge.NativeHost.csproj') (Join-Path $releaseRoot 'NativeHost')
foreach ($directory in @('scripts', 'Extension\icons', 'Contracts', 'Docs\releases', 'assets\readme')) {
    New-Item -ItemType Directory -Path (Join-Path $releaseRoot $directory) -Force | Out-Null
}
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'apps\windows\native-host\app.codexbridge.nativehost.json') -Destination (Join-Path $releaseRoot 'NativeHost')
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'scripts\install-native-host-windows.ps1') -Destination (Join-Path $releaseRoot 'scripts')
foreach ($file in @('manifest.json', 'content-capture.js', 'service-worker.js', 'side-panel.css', 'side-panel.html', 'side-panel.js', 'icons\icon16.png', 'icons\icon32.png', 'icons\icon64.png', 'icons\icon128.png')) {
    Copy-Item -LiteralPath (Join-Path $repositoryRoot "extension\$file") -Destination (Join-Path $releaseRoot "Extension\$file")
}
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'contracts\capture-v1.schema.json') -Destination (Join-Path $releaseRoot 'Contracts')
Copy-Item -LiteralPath $notesPath -Destination (Join-Path $releaseRoot 'Docs\releases')
foreach ($file in @('README.md', 'CHANGELOG.md', 'LICENSE')) {
    Copy-Item -LiteralPath (Join-Path $repositoryRoot $file) -Destination $releaseRoot
}
foreach ($file in @('01-codex-bridge-main.png', '02-chatgpt-to-codex.png', '03-codex-to-chatgpt.png')) {
    Copy-Item -LiteralPath (Join-Path $repositoryRoot "assets\readme\$file") -Destination (Join-Path $releaseRoot 'assets\readme')
}

foreach ($relativeExe in @('App\CodexBridge.App.exe', 'NativeHost\CodexBridge.NativeHost.exe')) {
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $releaseRoot $relativeExe))
    if ($info.ProductVersion -ne $version) { throw "Unexpected product version: $relativeExe" }
}
if (Get-ChildItem -LiteralPath $releaseRoot -Recurse -File -Filter '*.pdb') { throw 'Debug symbols must not enter the release package.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($releaseRoot, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $true)
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($checksumPath, "$hash  $packageName.zip$([Environment]::NewLine)", [Text.UTF8Encoding]::new($false))
Write-Output "Release directory: $releaseRoot"
Write-Output "Release ZIP: $zipPath"
Write-Output "SHA256: $hash"
