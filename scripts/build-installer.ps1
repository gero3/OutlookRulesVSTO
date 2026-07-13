param(
    [string]$Configuration = "Release",
    [string]$Version = "1.0.10"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
function Stable-Guid([string]$value) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $bytes = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($value))[0..15] }
    finally { $sha.Dispose() }
    $bytes[6] = ($bytes[6] -band 0x0f) -bor 0x50
    $bytes[8] = ($bytes[8] -band 0x3f) -bor 0x80
    return ([Guid]::new([byte[]]$bytes)).ToString().ToUpperInvariant()
}
$productCode = Stable-Guid "OutlookRulesVSTO|MSI|$Version"
& (Join-Path $PSScriptRoot "publish-vsto-package.ps1") -Configuration $Configuration
if ($LASTEXITCODE -ne 0) { throw "OutlookRulesVSTO publiceren is mislukt." }

$project = Join-Path $repo "installer\OutlookRulesVSTO.Installer.wixproj"
dotnet build $project -c $Configuration -p:OutlookRulesVSTOVersion=$Version -p:OutlookRulesVSTOProductCode=$productCode
if ($LASTEXITCODE -ne 0) { throw "Bouwen van de WiX-installer is mislukt." }
$msi = Join-Path $repo "installer\bin\$Configuration\OutlookRulesVSTOSetup.msi"
& (Join-Path $PSScriptRoot "sign-installer.ps1") -Paths @($msi)

& (Join-Path $PSScriptRoot "download-installer-prerequisites.ps1")
$bundle = Join-Path $repo "installer\OutlookRulesVSTO.Bundle.wixproj"
dotnet build $bundle -c $Configuration -p:OutlookRulesVSTOVersion=$Version
if ($LASTEXITCODE -ne 0) { throw "Bouwen van de WiX-bootstrapper is mislukt." }

$source = Join-Path $repo "installer\bin\$Configuration\OutlookRulesVSTOInstaller.exe"
$destination = Join-Path $repo "artifacts\OutlookRulesVSTOInstaller.exe"
& (Join-Path $PSScriptRoot "sign-bundle.ps1") -BundlePath $source
Copy-Item -LiteralPath $source -Destination $destination -Force
$legacyMsi = Join-Path $repo "artifacts\OutlookRulesVSTOSetup.msi"
if (Test-Path -LiteralPath $legacyMsi) { Remove-Item -LiteralPath $legacyMsi -Force }
Write-Host "OutlookRulesVSTO-installatiewizard gereed: $destination" -ForegroundColor Green
