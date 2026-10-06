$ErrorActionPreference = 'Stop'

$projectRoot = $PSScriptRoot
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$outputDirectory = Join-Path $projectRoot 'dist'
$manifest = Join-Path $projectRoot 'app.manifest'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw '未找到 Windows 自带的 C# 编译器。'
}

New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$sources = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' -Recurse | Select-Object -ExpandProperty FullName
$references = @(
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll',
    '/reference:System.Web.Extensions.dll'
)
$common = @(
    '/nologo',
    '/optimize+',
    '/platform:anycpu',
    '/codepage:65001',
    ('/win32manifest:' + $manifest),
    '/main:PeripheralPeek.Program'
) + $references

& $compiler '/target:winexe' ('/out:' + (Join-Path $outputDirectory 'PeripheralPeek.exe')) @common @sources
if ($LASTEXITCODE -ne 0) { throw '托盘程序编译失败。' }

& $compiler '/target:exe' ('/out:' + (Join-Path $outputDirectory 'PeripheralPeek.Probe.exe')) @common @sources
if ($LASTEXITCODE -ne 0) { throw '探测程序编译失败。' }

Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $outputDirectory 'README.md') -Force

Write-Host 'Build complete:' -ForegroundColor Green
Write-Host ('  ' + (Join-Path $outputDirectory 'PeripheralPeek.exe'))
Write-Host ('  ' + (Join-Path $outputDirectory 'PeripheralPeek.Probe.exe'))
