# 编译 DeepSeek Chat 为单个 exe（使用系统自带 .NET Framework 编译器，无需联网）
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw '找不到 csc.exe，请确认已安装 .NET Framework 4.x' }

$fw = Split-Path $csc
$refNames = @(
    'System.dll',
    'System.Core.dll',
    'System.Drawing.dll',
    'System.Windows.Forms.dll',
    'System.Web.Extensions.dll',
    'System.Security.dll'
)

$out = Join-Path $root 'DeepSeekChat.exe'
$ico = Join-Path $root 'app.ico'

$argList = New-Object System.Collections.Generic.List[string]
$argList.Add('/nologo')
$argList.Add('/target:winexe')
$argList.Add('/optimize+')
$argList.Add('/platform:anycpu')
$argList.Add('/langversion:5')
$argList.Add('/out:' + $out)
if ((Test-Path $ico) -and ((Get-Item $ico).Length -gt 1000)) {
    $argList.Add('/win32icon:' + $ico)
}
foreach ($n in $refNames) {
    $argList.Add('/reference:' + (Join-Path $fw $n))
}
Get-ChildItem (Join-Path $root 'src') -Filter '*.cs' | Sort-Object Name | ForEach-Object {
    $argList.Add($_.FullName)
}

Write-Host 'Compiling...'
$proc = Start-Process -FilePath $csc -ArgumentList $argList.ToArray() -NoNewWindow -Wait -PassThru
if ($proc.ExitCode -ne 0) {
    throw ('Build failed, exit code ' + $proc.ExitCode)
}
if (-not (Test-Path $out)) { throw 'No exe produced' }

$f = Get-Item $out
$kb = [math]::Round($f.Length / 1024.0, 1)
Write-Host ('OK -> ' + $f.FullName + '  ' + $kb + ' KB')
