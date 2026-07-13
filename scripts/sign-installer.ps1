param(
    [Parameter(Mandatory = $true)][string[]]$Paths,
    [string]$CertificateThumbprint = $env:OUTLOOKRULESVSTO_SIGNING_CERT_THUMBPRINT,
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    Write-Warning "Geen publiek code-signingcertificaat ingesteld. Zet OUTLOOKRULESVSTO_SIGNING_CERT_THUMBPRINT om MSI en EXE te ondertekenen."
    return
}
$certificate = Get-ChildItem Cert:\CurrentUser\My | Where-Object {
    $_.Thumbprint -eq $CertificateThumbprint.Replace(" ", "") -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date)
} | Select-Object -First 1
if (-not $certificate) { throw "Code-signingcertificaat met thumbprint $CertificateThumbprint en private key werd niet gevonden." }
$signTool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $signTool) { throw "signtool.exe werd niet gevonden in de Windows SDK." }

foreach ($path in $Paths) {
    $fullPath = [IO.Path]::GetFullPath($path)
    if (-not (Test-Path -LiteralPath $fullPath)) { throw "Te ondertekenen bestand ontbreekt: $fullPath" }
    & $signTool.FullName sign /sha1 $certificate.Thumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 $fullPath
    if ($LASTEXITCODE -ne 0) { throw "Ondertekenen mislukt: $fullPath" }
    & $signTool.FullName verify /pa $fullPath
    if ($LASTEXITCODE -ne 0) { throw "Authenticode-verificatie mislukt: $fullPath" }
}
