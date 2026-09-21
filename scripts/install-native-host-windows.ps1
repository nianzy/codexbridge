[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$NativeHostPath,
    [string]$ManifestTemplatePath,
    [string]$InstalledManifestPath,
    [switch]$Uninstall
)

$hostName = 'app.codexbridge.nativehost'
$extensionOrigin = 'chrome-extension://pnpgopcjhgfmnkefmdnoebmcbnnhnhee/'
$registryPaths = @(
    "HKCU:\Software\Google\Chrome\NativeMessagingHosts\$hostName",
    "HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\$hostName"
)
$defaultTemplatePath = Join-Path $PSScriptRoot '..\apps\windows\native-host\app.codexbridge.nativehost.json'
$defaultInstalledManifestPath = Join-Path $env:LOCALAPPDATA 'Codex Bridge\NativeMessagingHosts\app.codexbridge.nativehost.json'

if ($Uninstall) {
    foreach ($registryPath in $registryPaths) {
        if ((Test-Path -LiteralPath $registryPath) -and $PSCmdlet.ShouldProcess($registryPath, 'Remove Native Messaging registration')) {
            Remove-Item -LiteralPath $registryPath -Recurse -Force
        }
    }

    $manifestToRemove = if ($InstalledManifestPath) {
        [IO.Path]::GetFullPath($InstalledManifestPath)
    } else {
        [IO.Path]::GetFullPath($defaultInstalledManifestPath)
    }
    $expectedManifestRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Codex Bridge\NativeMessagingHosts'))
    $manifestIsUnderExpectedRoot = $manifestToRemove.StartsWith(
        $expectedManifestRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)
    $manifestExists = Test-Path -LiteralPath $manifestToRemove
    if ($manifestIsUnderExpectedRoot -and $manifestExists -and $PSCmdlet.ShouldProcess(
            $manifestToRemove,
            'Remove generated Native Messaging manifest')) {
        Remove-Item -LiteralPath $manifestToRemove -Force
    }
    return
}

if ([String]::IsNullOrWhiteSpace($NativeHostPath)) {
    throw 'NativeHostPath is required unless -Uninstall is specified.'
}

$resolvedNativeHostPath = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $NativeHostPath -ErrorAction Stop).Path)
$templatePathToUse = $defaultTemplatePath
if (-not [String]::IsNullOrWhiteSpace($ManifestTemplatePath)) {
    $templatePathToUse = $ManifestTemplatePath
}
$resolvedTemplatePath = [IO.Path]::GetFullPath(
    (Resolve-Path -LiteralPath $templatePathToUse -ErrorAction Stop).Path)
$installedManifestPathToUse = $defaultInstalledManifestPath
if (-not [String]::IsNullOrWhiteSpace($InstalledManifestPath)) {
    $installedManifestPathToUse = $InstalledManifestPath
}
$resolvedInstalledManifestPath = [IO.Path]::GetFullPath($installedManifestPathToUse)

if (-not (Test-Path -LiteralPath $resolvedNativeHostPath -PathType Leaf)) {
    throw "Native host executable not found: $resolvedNativeHostPath"
}

$template = Get-Content -Raw -Encoding UTF8 -LiteralPath $resolvedTemplatePath | ConvertFrom-Json
if ($template.name -ne $hostName) {
    throw "Manifest name must be $hostName"
}
if ($template.type -ne 'stdio') {
    throw 'Manifest type must be stdio.'
}
if (@($template.allowed_origins) -notcontains $extensionOrigin) {
    throw "Manifest allowed_origins must contain $extensionOrigin"
}

$template.path = $resolvedNativeHostPath
$installedManifestDirectory = Split-Path -Parent $resolvedInstalledManifestPath
$materializedManifest = $template | ConvertTo-Json -Depth 10
$utf8NoBom = [Text.UTF8Encoding]::new($false)

if ($PSCmdlet.ShouldProcess($resolvedInstalledManifestPath, 'Write materialized Native Messaging manifest')) {
    New-Item -ItemType Directory -Force -Path $installedManifestDirectory | Out-Null
    [IO.File]::WriteAllText($resolvedInstalledManifestPath, $materializedManifest + [Environment]::NewLine, $utf8NoBom)
}

foreach ($registryPath in $registryPaths) {
    if ($PSCmdlet.ShouldProcess($registryPath, 'Register Native Messaging manifest for current user')) {
        New-Item -Path $registryPath -Force | Out-Null
        Set-Item -Path $registryPath -Value $resolvedInstalledManifestPath
    }
}

Write-Output "Registered $hostName for Chrome and Edge."
Write-Output "Manifest: $resolvedInstalledManifestPath"
