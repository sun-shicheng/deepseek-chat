# 生成应用图标 app.ico（多尺寸 PNG 压缩 ICO）
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw '找不到 csc.exe，请确认已安装 .NET Framework 4.x' }

$gen = Join-Path $root 'tools\IconGen.exe'
& $csc /nologo /target:exe /out:$gen /reference:System.Drawing.dll (Join-Path $root 'tools\IconGen.cs')
if ($LASTEXITCODE -ne 0) { throw 'IconGen 编译失败' }
& $gen (Join-Path $root 'app.ico')
if ($LASTEXITCODE -ne 0) { throw '图标生成失败' }
Write-Host 'app.ico 已更新'
