$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$project = Join-Path $PSScriptRoot 'DKImageSimpleUpscaler.csproj'

dotnet restore $project
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$publish = Join-Path $PSScriptRoot 'bin\Release\net8.0-windows\win-x64\publish'
Write-Host "완료: $publish\DKImageSimpleUpscaler.exe"
