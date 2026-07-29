[CmdletBinding()]
param(
    [switch]$Run,
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'YierPet.Windows\YierPet.Windows.csproj'
$releaseExecutable = Join-Path $PSScriptRoot 'YierPet.Windows\bin\Release\net8.0-windows\YierPet.Windows.exe'

if ($Run) {
    Get-Process -Name 'YierPet.Windows' -ErrorAction SilentlyContinue | Stop-Process -Force
}

dotnet build $project -c Release

if ($Publish) {
    $output = Join-Path $PSScriptRoot 'artifacts\win-x64'
    $publishArgs = @(
        'publish', $project, '-c', 'Release', '-r', 'win-x64',
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-o', $output
    )
    & dotnet @publishArgs
    Write-Host "发布完成：$output"
}

if ($Run) {
    Start-Process -FilePath $releaseExecutable
    Write-Host "已启动：$releaseExecutable"
}
