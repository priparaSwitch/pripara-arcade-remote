param(
 [string]$OutputDirectory = 'bin\Release',
 [string]$DataDirectory = 'assets',
 [switch]$CodeOnly,
 [switch]$WithoutImages
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
function From-Root([string]$Path) {
 if ([IO.Path]::IsPathRooted($Path)) { return [IO.Path]::GetFullPath($Path) }
 return [IO.Path]::GetFullPath((Join-Path $root $Path))
}
if ($env:OS -ne 'Windows_NT' -or -not [Environment]::Is64BitOperatingSystem) { throw 'Windows x64 is required.' }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) { throw 'The Windows .NET Framework x64 C# compiler was not found. A full .NET SDK is not required.' }
$output = From-Root $OutputDirectory
$data = From-Root $DataDirectory
foreach ($protected in @($root,(Join-Path $root 'src'),(Join-Path $root 'assets'))) {
 if ($output.TrimEnd('\') -eq $protected.TrimEnd('\')) { throw 'Use a separate output directory, such as bin\Release.' }
}
if (-not $CodeOnly -and -not (Test-Path -LiteralPath (Join-Path $data 'data.db') -PathType Leaf)) { throw 'data.db is required. Supply -DataDirectory or use -CodeOnly.' }
if ($WithoutImages -and (Test-Path -LiteralPath (Join-Path $output 'images.bin'))) { throw 'Use an empty output directory with -WithoutImages; an existing image pack will not be deleted.' }
New-Item -ItemType Directory -Force -Path $output | Out-Null
$exe = Join-Path $output '프리파라 코디 리모컨.exe'
$icon = Join-Path $root 'src\app.ico'
$argsList = @('/nologo','/target:winexe','/platform:x64','/optimize+','/codepage:65001',
 ('/out:'+$exe),('/win32manifest:'+(Join-Path $root 'src\app.manifest')),('/win32icon:'+$icon),('/resource:'+$icon+',app.ico'),
 '/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll',
 '/r:System.Web.Extensions.dll','/r:System.IO.Compression.dll')
foreach ($name in @('Program.cs','Catalog.cs','Favorites.cs','GameBridge.cs','SelfTests.cs')) { $argsList += Join-Path $root ('src\'+$name) }
& $compiler @argsList
if ($LASTEXITCODE -ne 0) { throw 'C# compilation failed.' }
if (-not $CodeOnly) {
 Copy-Item -LiteralPath (Join-Path $data 'data.db') -Destination (Join-Path $output 'data.db') -Force
 $image = Join-Path $data 'images.bin'
 if (-not $WithoutImages -and (Test-Path -LiteralPath $image -PathType Leaf)) { Copy-Item -LiteralPath $image -Destination (Join-Path $output 'images.bin') -Force }
}
$summary = [ordered]@{ executable=$exe; bytes=(Get-Item -LiteralPath $exe).Length; sha256=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant(); target='x64 .NET Framework'; sdkRequired=$false; sourceVersion='1.1.3.0'; dataEmbedded=$false }
$summary | ConvertTo-Json
