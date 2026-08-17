[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsDirectory = Join-Path $repositoryRoot 'artifacts'
$publishDirectory = Join-Path $artifactsDirectory 'BacpacExplorer-win-x64'
$archivePath = Join-Path $artifactsDirectory 'BacpacExplorer-win-x64.zip'
$projectPath = Join-Path $repositoryRoot 'src\BacpacExplorer.App\BacpacExplorer.App.csproj'
$licensePath = Join-Path $repositoryRoot 'LICENSE'

if (Test-Path -LiteralPath $publishDirectory)
{
    $resolvedPublishDirectory = [System.IO.Path]::GetFullPath($publishDirectory)
    if (-not $resolvedPublishDirectory.StartsWith($artifactsDirectory + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase))
    {
        throw "Refusing to clean a publish directory outside the artifacts directory: $resolvedPublishDirectory"
    }

    Remove-Item -LiteralPath $resolvedPublishDirectory -Recurse -Force
}

if (Test-Path -LiteralPath $archivePath)
{
    Remove-Item -LiteralPath $archivePath -Force
}

New-Item -ItemType Directory -Path $artifactsDirectory -Force | Out-Null
$publishArguments = @(
    $projectPath
    '-p:PublishProfile=Portable-win-x64'
    '-o'
    $publishDirectory
)

if ($Version)
{
    $publishArguments += "-p:Version=$Version"
}

dotnet publish @publishArguments
if ($LASTEXITCODE -ne 0)
{
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Copy-Item -LiteralPath $licensePath -Destination (Join-Path $publishDirectory 'LICENSE')
Compress-Archive -LiteralPath $publishDirectory -DestinationPath $archivePath -CompressionLevel Optimal
Write-Output "Portable package created: $archivePath"
