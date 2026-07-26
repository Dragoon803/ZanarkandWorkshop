param(
    [string]$Version = "0.2.0"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$artifactsDirectory = Join-Path $repositoryRoot "artifacts"
$packageName = "ZanarkandWorkshop-v$Version-win-x64"
$publishDirectory = Join-Path $artifactsDirectory $packageName
$archivePath = Join-Path $artifactsDirectory "$packageName.zip"
$checksumPath = "$archivePath.sha256"

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}

if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}

if (Test-Path -LiteralPath $checksumPath) {
    Remove-Item -LiteralPath $checksumPath -Force
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
Copy-Item -LiteralPath (Join-Path $repositoryRoot "ReadmeAssets") `
    -Destination (Join-Path $publishDirectory "ReadmeAssets") -Recurse
$packagedBrandDirectory = Join-Path $publishDirectory "ZanarkandWorkshop\Assets"
New-Item -ItemType Directory -Path $packagedBrandDirectory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repositoryRoot "ZanarkandWorkshop\Assets\ZanarkandWorkshop.png") `
    -Destination $packagedBrandDirectory

Compress-Archive -Path (Join-Path $publishDirectory "*") -DestinationPath $archivePath

$checksum = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
"$checksum  $([System.IO.Path]::GetFileName($archivePath))" |
    Set-Content -LiteralPath $checksumPath -Encoding ascii

Write-Host "Release package created: $archivePath"
Write-Host "SHA-256 checksum created: $checksumPath"
