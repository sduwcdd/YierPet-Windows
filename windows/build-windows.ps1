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
if ($LASTEXITCODE -ne 0) { throw "构建失败，dotnet 退出码：$LASTEXITCODE" }

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
    if ($LASTEXITCODE -ne 0) { throw "发布失败，dotnet 退出码：$LASTEXITCODE" }
    Write-Host "发布完成：$output"
}

if ($Run) {
    Start-Process -FilePath $releaseExecutable -WindowStyle Hidden
    Write-Host "已启动：$releaseExecutable"
}
