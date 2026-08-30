$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$artifactRoot = Join-Path $projectRoot 'artifacts'
$publishDir = Join-Path $artifactRoot 'InternalAlarmApp_v1.1'
$zipPath = Join-Path $artifactRoot 'InternalAlarmApp_v1.1.zip'
$localDotnet = Join-Path $projectRoot '.dotnet-sdk\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }

& $dotnet restore (Join-Path $projectRoot 'InternalAlarmApp.sln') --ignore-failed-sources
& $dotnet build (Join-Path $projectRoot 'InternalAlarmApp.sln') -c Release --no-restore

if (Test-Path -LiteralPath $publishDir) { Remove-Item -LiteralPath $publishDir -Recurse -Force }
New-Item -ItemType Directory -Path $publishDir | Out-Null

& $dotnet restore (Join-Path $projectRoot 'src\InternalAlarmApp\InternalAlarmApp.csproj') -r win-x64 --ignore-failed-sources
& $dotnet publish (Join-Path $projectRoot 'src\InternalAlarmApp\InternalAlarmApp.csproj') -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $publishDir
$publishedPdb = Join-Path $publishDir 'InternalAlarmApp.pdb'
if (Test-Path -LiteralPath $publishedPdb) { Remove-Item -LiteralPath $publishedPdb -Force }

$standaloneExe = Join-Path $artifactRoot 'InternalAlarmApp_v1.1.exe'
Copy-Item -LiteralPath (Join-Path $publishDir 'InternalAlarmApp.exe') -Destination $standaloneExe -Force

Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $publishDir 'README.txt')
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD_PARTY_NOTICES.md') -Destination $publishDir
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $publishDir -Recurse
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Compress-Archive -LiteralPath $publishDir -DestinationPath $zipPath -CompressionLevel Optimal
Write-Host "Created Standalone Exe: $standaloneExe"
Write-Host "Created Package Dir: $publishDir"
Write-Host "Created Package Zip: $zipPath"
