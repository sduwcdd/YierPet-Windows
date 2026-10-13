[CmdletBinding()]
param([string]$Version = '0.0.1')

$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must use major.minor.patch.' }
$project = Join-Path $PSScriptRoot 'YierPet.Windows\YierPet.Windows.csproj'
$output = Join-Path $PSScriptRoot "artifacts\release-$Version"
dotnet publish $project -c Release -r win-x64 --self-contained true -o $output `
    -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false `
    "-p:Version=$Version"
if ($LASTEXITCODE -ne 0) { throw "Publish failed: $LASTEXITCODE" }

$builtExe = Join-Path $output 'YierPet.Windows.exe'
$assetName = "YierPet-Windows-v$Version-win-x64.exe"
$asset = Join-Path $output $assetName
Copy-Item -LiteralPath $builtExe -Destination $asset -Force

# Run only a standalone copy: the check must not find a neighbouring Content folder.
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('YierPet-release-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$testExe = Join-Path $testRoot $assetName
$testReport = Join-Path $testRoot 'verification.json'
Copy-Item -LiteralPath $asset -Destination $testExe
try {
    $process = Start-Process -FilePath $testExe -ArgumentList @('--verify-release', ('"' + $testReport + '"')) -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(180000)) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit()
        throw 'Release verification timed out.'
    }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $testReport)) { throw 'Release executable failed verification.' }
    $result = Get-Content -LiteralPath $testReport -Raw | ConvertFrom-Json
    if (-not $result.success -or -not $result.bundledFilesExtracted) { throw 'Bundled content did not pass verification.' }
    Copy-Item -LiteralPath $testReport -Destination (Join-Path $output 'verification.json') -Force
} finally {
    # Only files created above are removed; no recursive directory deletion.
    foreach ($file in @($testExe, $testReport)) {
        if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file -Force }
    }
    Remove-Item -LiteralPath $testRoot
}
$hash = (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Value "$hash  $assetName" -Encoding ascii
Write-Host "Verified release asset: $asset"
