# Build app.ico (multi-size PNG-compressed ICO)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'csc.exe not found. .NET Framework 4.x is required.' }

$gen = Join-Path $root 'tools\IconGen.exe'
& $csc /nologo /target:exe /out:$gen /reference:System.Drawing.dll (Join-Path $root 'src\Brand.cs') (Join-Path $root 'tools\IconGen.cs')
if ($LASTEXITCODE -ne 0) { throw 'IconGen build failed' }
& $gen (Join-Path $root 'app.ico')
if ($LASTEXITCODE -ne 0) { throw 'icon generation failed' }
Write-Host 'app.ico updated'
