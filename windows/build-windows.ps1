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
    & (Join-Path $PSScriptRoot 'publish-release.ps1') -Version '0.0.1'
}

if ($Run) {
    Start-Process -FilePath $releaseExecutable -WindowStyle Hidden
    Write-Host "已启动：$releaseExecutable"
}
