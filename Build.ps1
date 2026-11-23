[CmdletBinding()]
param([switch]$Release)

$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw 'Compilador indisponivel. Use o Windows de 64 bits com .NET Framework 4.8.'
}

$outputDirectory = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$outputName = if ($Release) { 'Usage-LLMs-Windows.exe' } else { 'Usage LLMs.exe' }
$output = Join-Path $outputDirectory $outputName
$sources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -File
$logo = Join-Path $PSScriptRoot 'src\chatgpt-logo.png'
$providerMarks = Join-Path $PSScriptRoot 'src\provider-marks.png'
$providerLicense = Join-Path $PSScriptRoot 'src\provider-marks.LICENSE.txt'
$icon = Join-Path $PSScriptRoot 'src\UsageLLMs.ico'
$references = @(
    'System.dll'
    'System.Core.dll'
    'System.Drawing.dll'
    'System.Windows.Forms.dll'
    'System.Web.Extensions.dll'
)
$arguments = @(
    '/nologo'
    '/target:winexe'
    '/langversion:5'
    '/codepage:65001'
    '/optimize+'
    ("/win32manifest:" + (Join-Path $PSScriptRoot 'src\app.manifest'))
    "/win32icon:$icon"
    "/out:$output"
    "/resource:$logo,chatgpt-logo.png"
    "/resource:$providerMarks,provider-marks.png"
    "/resource:$providerLicense,provider-marks.LICENSE.txt"
)
$arguments += $references | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$arguments += $sources.FullName
& $compiler @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Falha ao compilar o widget (codigo $LASTEXITCODE)."
}
Write-Output $output
