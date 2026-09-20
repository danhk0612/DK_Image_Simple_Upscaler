$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$project = Join-Path $PSScriptRoot 'DKImageSimpleUpscaler.csproj'
$dist = Join-Path $PSScriptRoot 'dist'
$appPublish = Join-Path $PSScriptRoot 'app-publish'

Remove-Item $dist -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $appPublish -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $dist | Out-Null
New-Item -ItemType Directory -Path $appPublish | Out-Null

dotnet restore $project
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet publish $project `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -p:PublishSingleFile=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -p:AssemblyName=DKImageSimpleUpscaler.App `
  -o $appPublish
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$appExe = Join-Path $appPublish 'DKImageSimpleUpscaler.App.exe'
if (-not (Test-Path $appExe)) {
    throw "Framework-dependent single-file app was not created: $appExe"
}
Copy-Item $appExe (Join-Path $dist 'DKImageSimpleUpscaler.App.exe') -Force

$programFilesX86 = [Environment]::GetEnvironmentVariable('ProgramFiles(x86)')
$vswhere = Join-Path $programFilesX86 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) {
    throw 'Visual Studio C++ Build Tools are required to build the native launcher.'
}

$vsInstall = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath |
    Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($vsInstall)) {
    throw 'Visual Studio C++ Build Tools were not found.'
}

$vsDevCmd = Join-Path $vsInstall 'Common7\Tools\VsDevCmd.bat'
$batchPath = Join-Path $env:TEMP 'DKImageSimpleUpscaler-build-launcher.cmd'
$batch = @"
@echo off
call "$vsDevCmd" -arch=x64 -host_arch=x64
if errorlevel 1 exit /b %errorlevel%
rc.exe /nologo /fo "launcher\launcher.res" "launcher\launcher.rc"
if errorlevel 1 exit /b %errorlevel%
cl.exe /nologo /std:c++17 /EHsc /O2 /utf-8 "launcher\launcher.cpp" "launcher\launcher.res" /link /SUBSYSTEM:WINDOWS /OUT:"dist\DKImageSimpleUpscaler.exe" Advapi32.lib Shell32.lib
exit /b %errorlevel%
"@

Set-Content -Path $batchPath -Value $batch -Encoding Ascii
try {
    & $env:ComSpec /d /c "`"$batchPath`""
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    Remove-Item $batchPath -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $PSScriptRoot 'launcher\launcher.res') -Force -ErrorAction SilentlyContinue
}

$launcherExe = Join-Path $dist 'DKImageSimpleUpscaler.exe'
if (-not (Test-Path $launcherExe)) {
    throw "Native launcher was not created: $launcherExe"
}

Write-Host '완료:'
Get-ChildItem $dist -File | ForEach-Object {
    Write-Host ("  {0} ({1:N2} MB)" -f $_.Name, ($_.Length / 1MB))
}
