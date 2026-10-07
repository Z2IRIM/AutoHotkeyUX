param([string]$ProjectDir = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$taskExpectedHash = 'c29b8c3a5124850d79fc9e66e2ca79677c377d7f31631ad3022ba159c5d9e3be'
$taskPayloadRoot = Join-Path $ProjectDir 'ToolPayload'
$taskPayload = Join-Path $taskPayloadRoot 'Ahk2Exe.zip'

# Uses framework hashing so both Windows PowerShell and PowerShell 7 can prepare build resources.
function Get-CompilerPayloadHash([string]$Path) {
    $taskInput = [IO.File]::OpenRead($Path)
    $taskHasher = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($taskHasher.ComputeHash($taskInput)).Replace('-', '').ToLowerInvariant() }
    finally { $taskHasher.Dispose(); $taskInput.Dispose() }
}

# Prepares one pinned official compiler ZIP for embedding, independently of AHK runtime preparation.
function Initialize-EmbeddedCompilerPayload {
    if ((Test-Path -LiteralPath $taskPayload) -and
        (Get-CompilerPayloadHash $taskPayload) -eq $taskExpectedHash) { return }
    New-Item -ItemType Directory -Path $taskPayloadRoot -Force | Out-Null
    $taskTemporary = Join-Path $taskPayloadRoot ('download-' + [Guid]::NewGuid().ToString('N') + '.zip')
    try {
        Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/AutoHotkey/Ahk2Exe/releases/download/Ahk2Exe1.1.37.02a2/Ahk2Exe1.1.37.02a2.zip' -OutFile $taskTemporary -TimeoutSec 60
        if ((Get-CompilerPayloadHash $taskTemporary) -ne $taskExpectedHash) {
            throw 'The official Ahk2Exe ZIP does not match the pinned SHA-256.'
        }
        Move-Item -LiteralPath $taskTemporary -Destination $taskPayload -Force
    }
    finally { if (Test-Path -LiteralPath $taskTemporary) { Remove-Item -LiteralPath $taskTemporary -Force } }
}

Initialize-EmbeddedCompilerPayload
Write-Host 'Pinned Ahk2Exe payload ready: 1.1.37.02a2'
