param([switch]$SkipAssetHashes)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$provenance = Get-Content -LiteralPath (Join-Path $root 'source-provenance.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$assets = Get-Content -LiteralPath (Join-Path $root 'assets\manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$checked = @()
foreach ($entry in @($provenance.sourceFiles)+$(if ($SkipAssetHashes) { @() } else { @($assets.files) })) {
 $path = Join-Path $root $entry.path
 if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw ('Missing bundled input: '+$entry.path) }
 $item = Get-Item -LiteralPath $path
 if ($item.Length -ne $entry.size -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) { throw ('Bundled input changed: '+$entry.path) }
 $checked += $entry.path
}
if ($provenance.applicationVersion -ne '1.1.2.0' -or $assets.dataSchemaVersion -ne 1) { throw 'Version metadata mismatch.' }
$bridge = $provenance.sourceFiles | Where-Object { $_.path -eq 'src/GameBridge.cs' }
if ($bridge.sha256 -ne 'c561d12b8a46ad93390e65c71f23bc2fc523c08e2075b4c99bd2c454ea229c3a') { throw 'Original game-transfer source changed.' }
$program = Get-Content -LiteralPath (Join-Path $root 'src\Program.cs') -Raw -Encoding UTF8
if (-not $program.Contains('if(testMode||refreshing||IsDisposed)return;') -or -not $program.Contains('if(testMode){SetStatus(')) { throw 'Offline UI guard missing.' }
$catalog = Get-Content -LiteralPath (Join-Path $root 'src\Catalog.cs') -Raw -Encoding UTF8
if (-not $catalog.Contains('DatabaseFileName="data.db"') -or -not $catalog.Contains('ArtworkFileName="images.bin"')) { throw 'Data companion filenames changed.' }
[ordered]@{ passed=$true; checkedFiles=$checked; version=$provenance.applicationVersion; originalTransferSourceUnchanged=$true; sourceGameIncluded=$false; scope='Bundled source/data integrity only; no game process access.' } | ConvertTo-Json -Depth 5
