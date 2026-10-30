param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'Verify-Repository.ps1') | Out-Null
$parent = Join-Path $root ('.local-tests\clean-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$clean = Join-Path $parent 'clean source with spaces'
New-Item -ItemType Directory -Force -Path $clean | Out-Null
foreach ($name in @('.gitignore','.gitattributes','README.md','THIRD_PARTY_NOTICES.md','source-provenance.json','build.ps1')) { Copy-Item -LiteralPath (Join-Path $root $name) -Destination (Join-Path $clean $name) }
foreach ($name in @('src','assets','scripts','tests','tools','examples','docs','.github')) {
 Copy-Item -LiteralPath (Join-Path $root $name) -Destination (Join-Path $clean $name) -Recurse
}
# No old game/research/EXE/input folder is available to the copied project.
& (Join-Path $clean 'scripts\Verify-Repository.ps1') | Out-Null
& (Join-Path $clean 'build.ps1') | Out-Null
& (Join-Path $clean 'scripts\Test-Offline.ps1') | Out-Null
$sessions = @(Get-ChildItem -LiteralPath (Join-Path $clean '.local-tests') -Directory | Where-Object { $_.Name -like 'offline-*' } | Sort-Object Name)
$summary = Get-Content -LiteralPath (Join-Path $sessions[-1].FullName 'summary.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $summary.passed) { throw 'Clean source verification failed.' }
[ordered]@{ passed=$true; isolatedSourceCopy=$true; pathWithSpaces=$true; fullDotNetSdkUsed=$false; originalGameUsed=$false; offline=$summary; cleanBuildExeSha256=(Get-FileHash -LiteralPath (Join-Path $clean 'bin\Release\프리파라 코디 리모컨.exe') -Algorithm SHA256).Hash.ToLowerInvariant() } | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $parent 'clean-build-summary.json') -Encoding UTF8
Get-Content -LiteralPath (Join-Path $parent 'clean-build-summary.json') -Raw -Encoding UTF8
