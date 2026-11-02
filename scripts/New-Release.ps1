param([string]$BuildDirectory='bin\Release',[string]$Tag='v1.1.3',[switch]$Force)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ($Tag -ne 'v1.1.3') { throw 'The tag must match the current source version: v1.1.3.' }
& (Join-Path $PSScriptRoot 'Verify-Repository.ps1') | Out-Null
$build = if ([IO.Path]::IsPathRooted($BuildDirectory)) { [IO.Path]::GetFullPath($BuildDirectory) } else { Join-Path $root $BuildDirectory }
$destination = Join-Path $root ('dist\release\'+$Tag)
New-Item -ItemType Directory -Force -Path $destination | Out-Null
$exeName = '프리파라 코디 리모컨.exe'
$files = @($exeName,'data.db','images.bin')
$identity = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $build $exeName))
if ($identity.Version.ToString() -ne '1.1.3.0' -or $identity.ProcessorArchitecture.ToString() -ne 'Amd64') { throw 'The release requires the x64 1.1.3 managed application, not a game executable.' }
foreach ($name in $files) {
 $source = Join-Path $build $name
 if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw ('Missing build input: '+$name) }
 $target = Join-Path $destination $name
 if ((Test-Path -LiteralPath $target) -and -not $Force) { throw 'Release output exists; use -Force to replace only known generated release files.' }
 Copy-Item -LiteralPath $source -Destination $target -Force
}
$assets = Get-Content -LiteralPath (Join-Path $root 'assets\manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($entry in $assets.files) {
 $name = Split-Path -Leaf $entry.path
 if ((Get-FileHash -LiteralPath (Join-Path $destination $name) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) { throw ('Release data is not the reviewed bundled dataset: '+$name) }
}
Copy-Item -LiteralPath (Join-Path $root 'docs\사용방법.txt') -Destination (Join-Path $destination '사용방법.txt') -Force
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Write-ReleaseZip([string]$Path,[string[]]$Names) {
 if ((Test-Path -LiteralPath $Path) -and -not $Force) { throw ('Output exists: '+$Path) }
 $stream = [IO.File]::Open($Path,[IO.FileMode]::Create,[IO.FileAccess]::Write,[IO.FileShare]::None)
 try {
  $archive = New-Object IO.Compression.ZipArchive($stream,[IO.Compression.ZipArchiveMode]::Create,$true)
  try { foreach ($name in $Names) { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,(Join-Path $destination $name),$name,[IO.Compression.CompressionLevel]::Optimal) | Out-Null } }
  finally { $archive.Dispose() }
 } finally { $stream.Dispose() }
}
Write-ReleaseZip (Join-Path $destination ('pripara-arcade-remote-'+$Tag+'-full.zip')) @($exeName,'data.db','images.bin','사용방법.txt')
Write-ReleaseZip (Join-Path $destination ('pripara-arcade-remote-'+$Tag+'-light.zip')) @($exeName,'data.db','사용방법.txt')
$notes = @"
PriPara Arcade Remote $Tag

Full: EXE + data.db + optional images.bin. Light: the same EXE + data.db.
The three companion files must be beside one another. Images may be omitted.
Version 1.1.3 shows its version in the window title and upper-right corner.
Tabs: Favorites, Sets, All, Hair accessories, Tops/dresses, Bottoms, Shoes.
The star on each card toggles a persistent favorite without selecting or sending the outfit.
Sort by ID, name or grade, ascending or descending. Grade is available when supplied in data.db.
All searches individual items across the four slots, excluding 174 audited set representatives.
Game-transfer behavior is unchanged.
Validation is offline fake-memory/Catalog/UI only, not a new in-game test.
No game, camera driver/helper, save, log, dump or SQLite provider DLL is bundled.
Individual files and full/light ZIPs are listed in SHA256SUMS.txt.
"@
[IO.File]::WriteAllText((Join-Path $destination 'RELEASE_NOTES.txt'),$notes,(New-Object Text.UTF8Encoding($false)))
$generated = @($exeName,'data.db','images.bin','사용방법.txt','RELEASE_NOTES.txt',('pripara-arcade-remote-'+$Tag+'-full.zip'),('pripara-arcade-remote-'+$Tag+'-light.zip'))
$sums = @();$metadata = @()
foreach ($name in $generated) {
 $file = Join-Path $destination $name
 $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
 $sums += $hash+'  '+$name
 $metadata += [ordered]@{ name=$name; bytes=(Get-Item -LiteralPath $file).Length; sha256=$hash }
}
[IO.File]::WriteAllText((Join-Path $destination 'SHA256SUMS.txt'),($sums -join "`n")+"`n",(New-Object Text.UTF8Encoding($false)))
$report = [ordered]@{ project='pripara-arcade-remote'; version='1.1.3.0'; tag=$Tag; files=$metadata; containsOriginalGame=$false; containsPrivateLogs=$false; newGameTest=$false; githubPublished=$false }
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $root 'dist\release-summary.json') -Encoding UTF8
$report | ConvertTo-Json -Depth 6
