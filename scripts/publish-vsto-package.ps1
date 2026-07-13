param([string]$Configuration = "Release")

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $repo "artifacts\OutlookRulesVSTO"
if (Test-Path -LiteralPath $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
New-Item -ItemType Directory -Path $publish | Out-Null

dotnet publish (Join-Path $repo "src\OutlookRulesVSTO.Host\OutlookRulesVSTO.Host.csproj") -c $Configuration -o $publish --no-self-contained
if ($LASTEXITCODE -ne 0) { throw "The private VSTO UI host failed to publish." }
dotnet publish (Join-Path $repo "src\OutlookRulesVSTO.Web\OutlookRulesVSTO.Web.csproj") -c $Configuration -o (Join-Path $publish "Web") --no-self-contained
if ($LASTEXITCODE -ne 0) { throw "The private VSTO web host failed to publish." }
foreach ($runtime in 'win-x64','win-x86') {
    dotnet publish (Join-Path $repo "src\OutlookRulesVSTO.Bridge\OutlookRulesVSTO.Bridge.csproj") -c $Configuration -r $runtime -o (Join-Path $publish "Bridge\$runtime") --self-contained false
    if ($LASTEXITCODE -ne 0) { throw "The internal $runtime bridge failed to publish." }
}

$msbuild = Get-ChildItem "${env:ProgramFiles}\Microsoft Visual Studio" -Recurse -Filter MSBuild.exe -ErrorAction SilentlyContinue |
    Where-Object FullName -Match '\\MSBuild\\Current\\Bin\\MSBuild.exe$' | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $msbuild) { throw "Visual Studio MSBuild with Office developer tools was not found." }
$vstoPublishUrl = Join-Path $publish "Vsto\"
$certificateThumbprint = $env:OUTLOOKRULESVSTO_SIGNING_CERT_THUMBPRINT
if ([string]::IsNullOrWhiteSpace($certificateThumbprint)) { throw "Set OUTLOOKRULESVSTO_SIGNING_CERT_THUMBPRINT to a valid code-signing certificate before publishing." }
& $msbuild.FullName (Join-Path $repo "src\OutlookRulesVSTO.Vsto\OutlookRulesVSTO.Vsto.csproj") /t:Publish "/p:Configuration=$Configuration" "/p:ManifestCertificateThumbprint=$certificateThumbprint" "/p:PublishUrl=$vstoPublishUrl" "/p:InstallUrl=$vstoPublishUrl"
if ($LASTEXITCODE -ne 0) { throw "The Outlook VSTO add-in failed to publish." }
$appPublish = Join-Path $repo "src\OutlookRulesVSTO.Vsto\bin\$Configuration\app.publish"
if (-not (Test-Path -LiteralPath $appPublish)) { throw "The VSTO publish output was not created: $appPublish" }
New-Item -ItemType Directory -Path $vstoPublishUrl -Force | Out-Null
Copy-Item -Path (Join-Path $appPublish '*') -Destination $vstoPublishUrl -Recurse -Force
$certificate = Get-ChildItem Cert:\CurrentUser\My | Where-Object {
    $_.Thumbprint -eq $certificateThumbprint.Replace(' ', '') -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date)
} | Select-Object -First 1
if (-not $certificate) { throw "The configured VSTO signing certificate was not found in Cert:\CurrentUser\My." }
Export-Certificate -Cert $certificate -FilePath (Join-Path $vstoPublishUrl 'Publisher.cer') -Force | Out-Null

Copy-Item (Join-Path $repo "sample-config") (Join-Path $publish "sample-config") -Recurse
Copy-Item (Join-Path $repo "docs\index.html") (Join-Path $publish "documentation.html")
Copy-Item (Join-Path $repo "THIRD_PARTY_NOTICES.md") $publish
