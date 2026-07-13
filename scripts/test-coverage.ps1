param(
    [double]$MinimumLineCoverage = 90,
    [double]$MinimumBranchCoverage = 70,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$results = Join-Path $repo "TestResults\coverage"
$report = Join-Path $results "report"
if (Test-Path -LiteralPath $results) { Remove-Item -LiteralPath $results -Recurse -Force }
New-Item -ItemType Directory -Path $results | Out-Null

dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw "Restoring the coverage report tool failed." }

$projects = @(
    "tests\OutlookRulesVSTO.Tests\OutlookRulesVSTO.Tests.csproj",
    "tests\OutlookRulesVSTO.WebTests\OutlookRulesVSTO.WebTests.csproj"
)
foreach ($project in $projects) {
    dotnet test (Join-Path $repo $project) -c $Configuration --collect:"XPlat Code Coverage" --results-directory $results --nologo
    if ($LASTEXITCODE -ne 0) { throw "Tests or coverage collection failed for $project." }
}

dotnet tool run reportgenerator `
    "-reports:$results\**\coverage.cobertura.xml" `
    "-targetdir:$report" `
    "-reporttypes:Cobertura;Html;JsonSummary" `
    "-assemblyfilters:+OutlookRulesVSTO.Core;+OutlookRulesVSTO.Web;+OutlookRulesVSTO.Bridge;+OutlookRulesVSTO.Application"
if ($LASTEXITCODE -ne 0) { throw "Merging coverage reports failed." }

[xml]$coverage = Get-Content (Join-Path $report "Cobertura.xml")
$lineCoverage = [Math]::Round([double]$coverage.coverage.'line-rate' * 100, 2)
$branchCoverage = [Math]::Round([double]$coverage.coverage.'branch-rate' * 100, 2)
Write-Host "Line coverage:   $lineCoverage% (required: $MinimumLineCoverage%)"
Write-Host "Branch coverage: $branchCoverage% (required: $MinimumBranchCoverage%)"
Write-Host "HTML report:     $(Join-Path $report 'index.html')"

if ($lineCoverage -lt $MinimumLineCoverage) { throw "Line coverage $lineCoverage% is below $MinimumLineCoverage%." }
if ($branchCoverage -lt $MinimumBranchCoverage) { throw "Branch coverage $branchCoverage% is below $MinimumBranchCoverage%." }
