$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

dotnet restore
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

$publish = Join-Path $PSScriptRoot 'bin\Release\net8.0-windows\win-x64\publish'
Write-Host "완료: $publish\DKImageSimpleUpscaler.exe"
