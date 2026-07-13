param([string]$Output = "")

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$fileName = "Qwen_Qwen3.5-0.8B-Q4_K_M.gguf"
$expectedSha256 = "fb044e93939a70469c905781334f5de1e6c8b608ced6cbc8c9249bd4127d9526"
$url = "https://huggingface.co/bartowski/Qwen_Qwen3.5-0.8B-GGUF/resolve/main/$fileName"
if ([string]::IsNullOrWhiteSpace($Output)) { $Output = Join-Path $repo "models\$fileName" }
$Output = [System.IO.Path]::GetFullPath($Output)

if ((Test-Path -LiteralPath $Output) -and (Get-FileHash -LiteralPath $Output -Algorithm SHA256).Hash.ToLowerInvariant() -eq $expectedSha256) {
    Write-Host "Lokaal AI-model is al aanwezig: $Output"
    return
}

New-Item -ItemType Directory -Path (Split-Path -Parent $Output) -Force | Out-Null
$partial = "$Output.download"
Write-Host "Qwen3.5-0.8B downloaden (ongeveer 580 MB)..."
try {
    Start-BitsTransfer -Source $url -Destination $partial -DisplayName "OutlookRulesVSTO lokaal AI-model"
    if ((Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedSha256) { throw "De SHA-256-controle van het modelbestand is mislukt." }
    Move-Item -LiteralPath $partial -Destination $Output -Force
}
finally {
    if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Force }
}
Write-Host "Model gereed: $Output"
