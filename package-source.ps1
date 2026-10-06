param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "backups"),
    [switch]$Incremental,
    [string]$BaseRef
)
$ErrorActionPreference = "Stop"
if ($Incremental -and -not $PSBoundParameters.ContainsKey('OutputDirectory')) {
    $OutputDirectory = 'C:\DESKTOP\srcpack_Area\AutoHotkeyUX'
}

# Identifies source/document paths while excluding build output, tests, dependency payloads and private key files.
function Test-PackSourcePath([string]$RelativePath) {
    $relative = $RelativePath.Replace('/', '\')
    $name = [IO.Path]::GetFileName($relative)
    return $relative -notmatch '(^|\\)(\.git|\.agents|\.codex|\.aws|node_modules|bin|obj|artifacts|RuntimePayload|backups|tests?|testfixtures|\.verification)(\\|$)' -and
        $relative -notmatch '^tools\\ModernShell\.SmokeTests\\' -and
        $relative -notmatch '(^|\\)[^\\]*tests?[^\\]*\.(ahk|cs|ps1)$' -and
        $name -notmatch '^\.env(\.|$)' -and
        [IO.Path]::GetExtension($relative) -notin @('.exe', '.dll', '.pdb', '.zip', '.nupkg', '.log', '.db', '.pem', '.pfx', '.key')
}

# Packs all source, or tracked source changes against an explicit Git baseline, retaining repository-relative paths.
function New-SourceArchive {
    $taskRoot = [System.IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\') + '\'
    $taskOutput = [System.IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\') + '\'
    $baseCommit = $null
    $removed = @()
    if ($Incremental) {
        if ([string]::IsNullOrWhiteSpace($BaseRef)) { throw 'Incremental packages require -BaseRef with an explicit Git baseline.' }
        $baseCommit = git -C $PSScriptRoot rev-parse --verify --end-of-options "$BaseRef^{commit}"
        if ($LASTEXITCODE -ne 0) { throw 'The incremental baseline is not a valid Git commit.' }
        $paths = @(git -c core.quotePath=false -C $PSScriptRoot diff --name-only --diff-filter=ACMRT $baseCommit --)
        if ($LASTEXITCODE -ne 0) { throw 'Unable to determine incremental source files.' }
        $removed = @(git -c core.quotePath=false -C $PSScriptRoot diff --name-only --diff-filter=D $baseCommit -- | Where-Object { Test-PackSourcePath $_ })
        $files = @($paths | Where-Object { Test-PackSourcePath $_ } | ForEach-Object { Get-Item -LiteralPath (Join-Path $PSScriptRoot $_) })
    }
    else {
        $files = @(Get-ChildItem -LiteralPath $taskRoot -Recurse -File | Where-Object {
            -not $_.FullName.StartsWith($taskOutput, [StringComparison]::OrdinalIgnoreCase) -and
            (Test-PackSourcePath $_.FullName.Substring($taskRoot.Length))
        })
    }
    New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
    $kind = if ($Incremental) { 'delta' } else { 'source' }
    $archivePath = Join-Path $taskOutput ("AutoHotkeyUX-$kind-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.zip')
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $stream = [System.IO.File]::Open($archivePath, [System.IO.FileMode]::CreateNew)
    $archive = [System.IO.Compression.ZipArchive]::new($stream, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in $files) {
            $name = $file.FullName.Substring($taskRoot.Length).Replace('\', '/')
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $name, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
        $manifest = $archive.CreateEntry('SOURCE_PACKAGE_MANIFEST.json')
        $writer = [IO.StreamWriter]::new($manifest.Open(), [Text.UTF8Encoding]::new($false))
        try {
            $writer.Write(([ordered]@{
                project = 'AutoHotkeyUX'; kind = $kind; baseCommit = $baseCommit
                headCommit = (git -C $PSScriptRoot rev-parse HEAD); createdLocal = (Get-Date -Format 'o')
                fileCount = $files.Count; removedFiles = $removed
                apply = 'Overlay files at repository root; inspect removedFiles before deleting any old source.'
            } | ConvertTo-Json -Depth 4))
        }
        finally { $writer.Dispose() }
    }
    finally { $archive.Dispose(); $stream.Dispose() }
    Write-Host $archivePath
}

New-SourceArchive
