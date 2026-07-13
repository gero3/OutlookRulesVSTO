param(
    [Parameter(Mandatory = $true)][string]$Source,
    [Parameter(Mandatory = $true)][string]$Output
)

$ErrorActionPreference = "Stop"
$sourceRoot = [IO.Path]::GetFullPath($Source).TrimEnd('\')
if (-not (Test-Path -LiteralPath $sourceRoot)) { throw "Published OutlookRulesVSTO folder not found: $sourceRoot" }

function Xml([string]$value) { [Security.SecurityElement]::Escape($value) }
function StableId([string]$prefix, [string]$value) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($value.ToLowerInvariant())) }
    finally { $sha.Dispose() }
    return $prefix + ([BitConverter]::ToString($hash, 0, 10) -replace '-', '')
}
function StableGuid([string]$value) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $bytes = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes("OutlookRulesVSTO|" + $value.ToLowerInvariant()))[0..15] }
    finally { $sha.Dispose() }
    $bytes[6] = ($bytes[6] -band 0x0f) -bor 0x50
    $bytes[8] = ($bytes[8] -band 0x3f) -bor 0x80
    return ([Guid]::new([byte[]]$bytes)).ToString().ToUpperInvariant()
}

$directories = Get-ChildItem -LiteralPath $sourceRoot -Directory -Recurse | Sort-Object FullName
$files = Get-ChildItem -LiteralPath $sourceRoot -File -Recurse | Sort-Object FullName
$dirIds = @{ $sourceRoot = 'INSTALLFOLDER' }
foreach ($directory in $directories) {
    $relative = $directory.FullName.Substring($sourceRoot.Length + 1)
    $dirIds[$directory.FullName] = StableId 'dir_' $relative
}

$lines = [Collections.Generic.List[string]]::new()
$lines.Add('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
$lines.Add('  <Fragment>')
$lines.Add('    <DirectoryRef Id="INSTALLFOLDER">')
foreach ($directory in $directories) {
    $parent = Split-Path -Parent $directory.FullName
    if ($parent -eq $sourceRoot) {
        $lines.Add(('      <Directory Id="{0}" Name="{1}" />' -f $dirIds[$directory.FullName], (Xml $directory.Name)))
    }
}
$lines.Add('    </DirectoryRef>')
foreach ($directory in $directories) {
    $parent = Split-Path -Parent $directory.FullName
    if ($parent -ne $sourceRoot) {
        $lines.Add(('    <DirectoryRef Id="{0}"><Directory Id="{1}" Name="{2}" /></DirectoryRef>' -f $dirIds[$parent], $dirIds[$directory.FullName], (Xml $directory.Name)))
    }
}
$lines.Add('  </Fragment>')
$lines.Add('  <Fragment>')
$lines.Add('    <ComponentGroup Id="PublishedFiles">')
foreach ($file in $files) {
    $relative = $file.FullName.Substring($sourceRoot.Length + 1)
    $componentId = StableId 'cmp_' $relative
    $fileId = if ($relative -ieq 'OutlookRulesVSTO.Host.exe') { 'OutlookRulesVSTOHostExe' } else { StableId 'fil_' $relative }
    $directoryId = $dirIds[$file.DirectoryName]
    $lines.Add(('      <Component Id="{0}" Directory="{1}" Guid="{2}">' -f $componentId, $directoryId, (StableGuid $relative)))
    $lines.Add(('        <File Id="{0}" Source="{1}" KeyPath="yes" />' -f $fileId, (Xml $file.FullName)))
    $lines.Add('      </Component>')
}
$lines.Add('    </ComponentGroup>')
$lines.Add('  </Fragment>')
$lines.Add('</Wix>')

$outputPath = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Path (Split-Path -Parent $outputPath) -Force | Out-Null
[IO.File]::WriteAllLines($outputPath, $lines, [Text.UTF8Encoding]::new($false))
