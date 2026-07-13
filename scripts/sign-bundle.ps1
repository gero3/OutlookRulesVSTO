param(
    [Parameter(Mandatory = $true)][string]$BundlePath,
    [string]$CertificateThumbprint = $env:OUTLOOKRULESVSTO_SIGNING_CERT_THUMBPRINT
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    Write-Warning "Geen publiek code-signingcertificaat ingesteld; de Burn-bundle blijft ongetekend."
    return
}
$bundle = [IO.Path]::GetFullPath($BundlePath)
$wix = Get-ChildItem "$env:USERPROFILE\.nuget\packages\wixtoolset.sdk\6.0.2\tools\net472\x64" -Filter wix.exe | Select-Object -First 1
if (-not $wix) { throw "WiX x64 tool werd niet gevonden voor Burn-signing." }
$work = Join-Path $env:TEMP ("OutlookRulesVSTOSign-" + [Guid]::NewGuid().ToString("N"))
$engine = Join-Path $work "engine.exe"
$reattached = Join-Path $work "OutlookRulesVSTOInstaller.exe"
New-Item -ItemType Directory -Path $work -Force | Out-Null
try {
    & $wix.FullName burn detach $bundle -engine $engine
    if ($LASTEXITCODE -ne 0) { throw "Losmaken van de Burn-engine is mislukt." }
    & (Join-Path $PSScriptRoot "sign-installer.ps1") -Paths @($engine) -CertificateThumbprint $CertificateThumbprint
    & $wix.FullName burn reattach $bundle -engine $engine -o $reattached
    if ($LASTEXITCODE -ne 0) { throw "Opnieuw koppelen van de ondertekende Burn-engine is mislukt." }
    & (Join-Path $PSScriptRoot "sign-installer.ps1") -Paths @($reattached) -CertificateThumbprint $CertificateThumbprint
    Copy-Item -LiteralPath $reattached -Destination $bundle -Force
}
finally {
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}
