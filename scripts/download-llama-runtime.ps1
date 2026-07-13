param([string]$Output = "")

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$version = "b9963"
$archiveName = "llama-$version-bin-win-cpu-x64.zip"
$expectedSha256 = "267e1fc1a043cb9cfbd5dbc452b6bb1f00331108f848b0a3c6fbe0dade52d928"
$url = "https://github.com/ggml-org/llama.cpp/releases/download/$version/$archiveName"
if ([string]::IsNullOrWhiteSpace($Output)) { $Output = Join-Path $repo "runtime\llama" }
$Output = [System.IO.Path]::GetFullPath($Output)
$server = Join-Path $Output "llama-server.exe"
if (Test-Path -LiteralPath $server) { Write-Host "llama.cpp-runtime is al aanwezig: $Output"; return }
$archive = Join-Path $env:TEMP $archiveName
try {
    Write-Host "llama.cpp $version downloaden..."
    Start-BitsTransfer -Source $url -Destination $archive -DisplayName "OutlookRulesVSTO llama.cpp-runtime"
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedSha256) { throw "De SHA-256-controle van llama.cpp is mislukt." }
    New-Item -ItemType Directory -Path $Output -Force | Out-Null
    Expand-Archive -LiteralPath $archive -DestinationPath $Output -Force
    if (-not (Test-Path -LiteralPath $server)) { throw "llama-server.exe ontbreekt in het runtime-archief." }
}
finally { if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force } }
Write-Host "llama.cpp gereed: $Output"
