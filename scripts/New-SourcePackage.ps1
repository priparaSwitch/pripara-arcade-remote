param([switch]$Force)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'Verify-Repository.ps1') | Out-Null
$paths = @('.gitignore','.gitattributes','README.md','THIRD_PARTY_NOTICES.md','source-provenance.json','build.ps1')
foreach ($directory in @('src','assets','scripts','tests','tools','examples','docs','.github')) {
 $base = Join-Path $root $directory
 foreach ($item in Get-ChildItem -LiteralPath $base -Recurse -File) {
  if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Source packages must not follow symlinks.' }
  $paths += $item.FullName.Substring($root.Length+1).Replace('\','/')
 }
}
$paths = @($paths | Sort-Object -Unique)
foreach ($path in $paths) { if ($path -match '(^|/)(private|\.local-tests|bin|obj|dist|__pycache__)(/|$)' -or $path -match '\.(exe|dll|zip|rar|dmp|pem|key|pyc)$') { throw ('Unsafe source package path: '+$path) } }
$destination = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
$zipPath = Join-Path $destination 'pripara-arcade-remote-github-ready.zip'
if ((Test-Path -LiteralPath $zipPath) -and -not $Force) { throw 'Source ZIP already exists; use -Force to replace this generated ZIP.' }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$stream = [IO.File]::Open($zipPath,[IO.FileMode]::Create,[IO.FileAccess]::Write,[IO.FileShare]::None)
try {
 $archive = New-Object IO.Compression.ZipArchive($stream,[IO.Compression.ZipArchiveMode]::Create,$true)
 try { foreach ($path in $paths) { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,(Join-Path $root $path),('pripara-arcade-remote/'+$path),[IO.Compression.CompressionLevel]::Optimal) | Out-Null } }
 finally { $archive.Dispose() }
} finally { $stream.Dispose() }
[ordered]@{ sourcePackage=$true; releasePackage=$false; file=Split-Path -Leaf $zipPath; bytes=(Get-Item -LiteralPath $zipPath).Length; sha256=(Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant(); sourceFiles=$paths; githubPublished=$false } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $destination 'source-package-summary.json') -Encoding UTF8
Get-Item -LiteralPath $zipPath | Select-Object Name,Length
