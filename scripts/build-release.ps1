param(
    [string]$Version = "0.1.0"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$artifactsDirectory = Join-Path $repositoryRoot "artifacts"
$packageName = "ZanarkandWorkshop-v$Version-win-x64"
$publishDirectory = Join-Path $artifactsDirectory $packageName
$archivePath = Join-Path $artifactsDirectory "$packageName.zip"

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}

if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}

New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

dotnet publish (Join-Path $repositoryRoot "ZanarkandWorkshop\ZanarkandWorkshop.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:Version=$Version `
    -p:PublishSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishDirectory

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Get-ChildItem -LiteralPath $publishDirectory -Filter "*.pdb" |
    Remove-Item -Force

Copy-Item -LiteralPath (Join-Path $repositoryRoot "Readme.md") -Destination $publishDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot "CHANGELOG.md") -Destination $publishDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot "NOTICE.md") -Destination $publishDirectory

Compress-Archive -Path (Join-Path $publishDirectory "*") -DestinationPath $archivePath

Write-Host "Release package created: $archivePath"
