param(
    [string]$ProjectDir = (Split-Path -Parent $MyInvocation.MyCommand.Path)
)

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$runtimeVersion = "2.0.29"
$runtimeFileName = "AutoHotkey_$runtimeVersion.zip"
$runtimeSha256 = "B2D0200724A6B6AD22C965C939C5E5A2C64A35D1CCB455A3CA3F8CE415C5A296"
$runtimeUrls = @(
    "https://github.com/AutoHotkey/AutoHotkey/releases/download/v$runtimeVersion/$runtimeFileName",
    "https://www.autohotkey.com/download/2.0/$runtimeFileName"
)
$runtimeUrl = $runtimeUrls[0]

$outputDir = Join-Path $ProjectDir "RuntimePayload"
$runtimeZip = Join-Path $outputDir "AutoHotkey.runtime.zip"
$uxZip = Join-Path $outputDir "AutoHotkeyUX.scripts.zip"
$manifestPath = Join-Path $outputDir "runtime-manifest.json"

New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

function Get-Sha256([string]$Path) {
    $stream = [System.IO.File]::OpenRead($Path)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()

    try {
        $hash = $sha256.ComputeHash($stream)
        return ([System.BitConverter]::ToString($hash)).Replace("-", "")
    }
    finally {
        $sha256.Dispose()
        $stream.Dispose()
    }
}

$runtimeReady = $false
if (Test-Path $runtimeZip) {
    $runtimeReady = (Get-Sha256 $runtimeZip) -eq $runtimeSha256
}

if (-not $runtimeReady) {
    $partial = "$runtimeZip.partial"
    Remove-Item $partial -Force -ErrorAction SilentlyContinue

    Write-Host "Downloading AutoHotkey v$runtimeVersion portable runtime..."

    $downloaded = $false
    foreach ($candidateUrl in $runtimeUrls) {
        try {
            Invoke-WebRequest -UseBasicParsing -Uri $candidateUrl -OutFile $partial -Headers @{
                "User-Agent" = "AutoHotkeyUX.Modern build"
            }
            $runtimeUrl = $candidateUrl
            $downloaded = $true
            break
        }
        catch {
            Remove-Item $partial -Force -ErrorAction SilentlyContinue
            Write-Warning "Runtime download failed from $candidateUrl"
        }
    }

    if (-not $downloaded) {
        throw "Unable to download the AutoHotkey portable runtime from official sources."
    }

    $actual = Get-Sha256 $partial
    if ($actual -ne $runtimeSha256) {
        Remove-Item $partial -Force -ErrorAction SilentlyContinue
        throw "AutoHotkey runtime SHA-256 mismatch. Expected $runtimeSha256, got $actual."
    }

    Move-Item $partial $runtimeZip -Force
}

$repoRoot = Split-Path $ProjectDir -Parent
$stagingRoot = Join-Path $outputDir ".ux-staging"

Remove-Item $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null

$rootFiles = @(
    "WindowSpy.ahk",
    "install-ahk2exe.ahk",
    "install.ahk",
    "launcher.ahk"
)

foreach ($file in $rootFiles) {
    Copy-Item (Join-Path $repoRoot $file) (Join-Path $stagingRoot $file) -Force
}

Copy-Item (Join-Path $repoRoot "inc") (Join-Path $stagingRoot "inc") -Recurse -Force
Copy-Item (Join-Path $repoRoot "Templates") (Join-Path $stagingRoot "Templates") -Recurse -Force

Remove-Item $uxZip -Force -ErrorAction SilentlyContinue
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $stagingRoot,
    $uxZip,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false
)
Remove-Item $stagingRoot -Recurse -Force

$uxSha256 = Get-Sha256 $uxZip

$manifest = [ordered]@{
    version = $runtimeVersion
    runtimeSha256 = $runtimeSha256
    uxSha256 = $uxSha256
    sourceUrl = $runtimeUrl
    sourceTag = "v$runtimeVersion"
}

$manifestJson = @"
{
  "version": "$runtimeVersion",
  "runtimeSha256": "$runtimeSha256",
  "uxSha256": "$uxSha256",
  "sourceUrl": "$runtimeUrl",
  "sourceTag": "v$runtimeVersion"
}
"@
[System.IO.File]::WriteAllText(
    $manifestPath,
    $manifestJson,
    (New-Object System.Text.UTF8Encoding($false))
)

Write-Host "Embedded AutoHotkey runtime payload ready:"
Write-Host "  version: $runtimeVersion"
Write-Host "  runtime: $runtimeZip"
Write-Host "  UX:      $uxZip"
