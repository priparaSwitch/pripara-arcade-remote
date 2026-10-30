param([string]$BuildDirectory='bin\Release',[string]$OutputDirectory='.local-tests')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
function From-Root([string]$Path) { if ([IO.Path]::IsPathRooted($Path)) { return [IO.Path]::GetFullPath($Path) }; return [IO.Path]::GetFullPath((Join-Path $root $Path)) }
$build = From-Root $BuildDirectory
$output = Join-Path (From-Root $OutputDirectory) ('offline-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Force -Path $output | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$runner = Join-Path $output 'OfflineRunner.exe'
& $compiler /nologo /target:exe /platform:x64 /codepage:65001 ('/out:'+$runner) /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll (Join-Path $root 'tests\OfflineRunner.cs')
if ($LASTEXITCODE -ne 0) { throw 'Offline host compilation failed.' }
$assetManifest = Get-Content -LiteralPath (Join-Path $root 'assets\manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$runs = @()
foreach ($kind in @('full','light')) {
 $folder = Join-Path $output $kind
 New-Item -ItemType Directory -Force -Path $folder | Out-Null
 foreach ($name in @('프리파라 코디 리모컨.exe','data.db')) { Copy-Item -LiteralPath (Join-Path $build $name) -Destination (Join-Path $folder $name) }
 if ($kind -eq 'full') { Copy-Item -LiteralPath (Join-Path $build 'images.bin') -Destination (Join-Path $folder 'images.bin') }
 $exe = Join-Path $folder '프리파라 코디 리모컨.exe'
 $selfReport = Join-Path $folder 'self-test.json'
 & $runner self-test $exe $folder $selfReport
 if ($LASTEXITCODE -ne 0) { throw ('Offline self-test failed: '+$kind) }
 $self = Get-Content -LiteralPath $selfReport -Raw -Encoding UTF8 | ConvertFrom-Json
 if (-not $self.passed -or $self.count -ne 15) { throw ('Unexpected self-test result: '+$kind) }
 $catalogReport = Join-Path $folder 'catalog.json'
 & $runner catalog $exe $folder $catalogReport
 if ($LASTEXITCODE -ne 0) { throw ('Catalog audit failed: '+$kind) }
 $catalog = Get-Content -LiteralPath $catalogReport -Raw -Encoding UTF8 | ConvertFrom-Json
 if (-not $catalog.passed -or $catalog.items -ne $assetManifest.items -or $catalog.sets -ne $assetManifest.sets) { throw 'Bundled data count mismatch.' }
 if (($kind -eq 'full' -and $catalog.decodedImages -ne $assetManifest.images) -or ($kind -eq 'light' -and ($catalog.imagesAvailable -or $catalog.decodedImages -ne 0))) { throw 'Optional image behavior mismatch.' }
 $runs += [ordered]@{ scenario=$kind; executableSha256=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant(); selfTests=$self.count; decodedImages=$catalog.decodedImages; passed=$true }
}
$corrupt = Join-Path $output 'corrupt-images'
New-Item -ItemType Directory -Force -Path $corrupt | Out-Null
foreach ($name in @('프리파라 코디 리모컨.exe','data.db')) { Copy-Item -LiteralPath (Join-Path $build $name) -Destination (Join-Path $corrupt $name) }
[IO.File]::WriteAllBytes((Join-Path $corrupt 'images.bin'),[byte[]]@(1,2,3,4))
$corruptReport = Join-Path $corrupt 'catalog.json'
& $runner catalog (Join-Path $corrupt '프리파라 코디 리모컨.exe') $corrupt $corruptReport
if ($LASTEXITCODE -ne 0) { throw 'Corrupt optional artwork blocked the catalog.' }
$c = Get-Content -LiteralPath $corruptReport -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $c.passed -or $c.imagesAvailable -or $c.decodedImages -ne 0 -or $c.items -ne $assetManifest.items) { throw 'Corrupt optional artwork behavior mismatch.' }
$summary = [ordered]@{ passed=$true; runs=$runs; fakeMemoryTests=30; fullImagesDecoded=$assetManifest.images; corruptOptionalImagesPassed=$true; liveGameReads=0; liveGameWrites=0; noNewGameTest=$true }
$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'summary.json') -Encoding UTF8
$summary | ConvertTo-Json -Depth 6
