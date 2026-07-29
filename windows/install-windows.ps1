[CmdletBinding()]
param(
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'YierPet.Windows\YierPet.Windows.csproj'
$publishOutput = Join-Path $PSScriptRoot 'artifacts\framework-dependent'
$installRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\YierPet-Windows'
$installedExecutable = Join-Path $installRoot 'YierPet.Windows.exe'
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) '一二桌宠.lnk'
$startupShortcut = Join-Path ([Environment]::GetFolderPath('Startup')) '一二桌宠.lnk'

Get-Process -Name 'YierPet.Windows' -ErrorAction SilentlyContinue | Stop-Process -Force

dotnet publish $project -c Release --no-self-contained -o $publishOutput
if ($LASTEXITCODE -ne 0) {
    throw "发布失败，dotnet 退出码：$LASTEXITCODE"
}

New-Item -ItemType Directory -Force $installRoot | Out-Null
Copy-Item (Join-Path $publishOutput '*') -Destination $installRoot -Recurse -Force

function New-YierPetShortcut {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($Path)
    $shortcut.TargetPath = $installedExecutable
    $shortcut.WorkingDirectory = $installRoot
    $shortcut.IconLocation = "$installedExecutable,0"
    $shortcut.Description = '启动一二 Windows 桌宠'
    $shortcut.Save()
}

New-YierPetShortcut -Path $desktopShortcut
New-YierPetShortcut -Path $startupShortcut

Write-Host "安装完成：$installedExecutable"
Write-Host "桌面快捷方式：$desktopShortcut"
Write-Host "开机自启动：$startupShortcut"

if (-not $NoLaunch) {
    Start-Process -FilePath $installedExecutable
}
