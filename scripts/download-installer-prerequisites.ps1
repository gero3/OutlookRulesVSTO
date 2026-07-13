param([string]$Output = "")

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Output)) { $Output = Join-Path $repo "installer\prerequisites" }
$Output = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Path $Output -Force | Out-Null

function Test-Payload([string]$path, [string]$algorithm, [string]$expectedHash) {
    if (-not (Test-Path -LiteralPath $path)) { return $false }
    $actual = (Get-FileHash -LiteralPath $path -Algorithm $algorithm).Hash
    if ($actual -ne $expectedHash.ToUpperInvariant()) { return $false }
    $signature = Get-AuthenticodeSignature -LiteralPath $path
    return $signature.Status -eq [System.Management.Automation.SignatureStatus]::Valid -and
        $signature.SignerCertificate.Subject -match 'O=Microsoft Corporation'
}

function Download([string]$url, [string]$path, [string]$algorithm, [string]$expectedHash) {
    if (Test-Payload $path $algorithm $expectedHash) {
        Write-Host "Geverifieerde prerequisite aanwezig: $([IO.Path]::GetFileName($path))"
        return
    }
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    $partial = "$path.download"
    try {
        Invoke-WebRequest -Uri $url -OutFile $partial -UseBasicParsing
        if (-not (Test-Payload $partial $algorithm $expectedHash)) {
            throw "Hash- of Microsoft Authenticode-controle mislukt voor $url"
        }
        Move-Item -LiteralPath $partial -Destination $path -Force
    }
    finally { if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Force } }
}

$dotNetVersion = "10.0.9"
$releases = Invoke-RestMethod "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json"
$release = $releases.releases | Where-Object { $_.'release-version' -eq $dotNetVersion } | Select-Object -First 1
if (-not $release) { throw "De vastgezette .NET-release $dotNetVersion werd niet gevonden." }
$desktop = $release.windowsdesktop.files | Where-Object { $_.rid -eq "win-x64" -and $_.name -eq "windowsdesktop-runtime-win-x64.exe" } | Select-Object -First 1
$aspnet = $release.'aspnetcore-runtime'.files | Where-Object { $_.rid -eq "win-x64" -and $_.name -eq "aspnetcore-runtime-win-x64.exe" } | Select-Object -First 1
if (-not $desktop -or -not $aspnet) { throw "De vastgezette .NET 10 prerequisite-downloads konden niet worden bepaald." }

Download $desktop.url (Join-Path $Output "windowsdesktop-runtime-10-win-x64.exe") "SHA512" $desktop.hash
Download $aspnet.url (Join-Path $Output "aspnetcore-runtime-10-win-x64.exe") "SHA512" $aspnet.hash
Download "https://go.microsoft.com/fwlink/p/?LinkId=2124703" (Join-Path $Output "MicrosoftEdgeWebview2Setup.exe") "SHA256" "F91077E2C116DCF6377E555D0D4A3A564D242351AD6718B6954658D4F74819C1"
Download "https://download.visualstudio.microsoft.com/download/pr/dc1b9b1b-c516-4573-8e5a-05130b120a0a/1b5cc0c52563be2500f6642e725d2b8f207314dafcf0dfd11d29aa48947da733/vstor40_x64.exe" (Join-Path $Output "vstor40_x64.exe") "SHA256" "1B5CC0C52563BE2500F6642E725D2B8F207314DAFCF0DFD11D29AA48947DA733"
Download "https://download.visualstudio.microsoft.com/download/pr/dc1b9b1b-c516-4573-8e5a-05130b120a0a/8d4f6ba370ef06ea7c897a38ae9260e1492422f507fa9344af1f7f2aeb68276d/vstor40_x86.exe" (Join-Path $Output "vstor40_x86.exe") "SHA256" "8D4F6BA370EF06EA7C897A38AE9260E1492422F507FA9344AF1F7F2AEB68276D"
