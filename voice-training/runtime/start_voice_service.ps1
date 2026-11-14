$ErrorActionPreference = 'Stop'
$voiceRuntime = $PSScriptRoot
$voiceRepo = Join-Path $voiceRuntime 'GPT-SoVITS'
$voicePython = Join-Path $voiceRuntime 'venv\Scripts\python.exe'
$voiceConfig = Join-Path (Split-Path $voiceRuntime -Parent) 'trained-20261014-v2\yier\inference.yaml'
if (!(Test-Path -LiteralPath $voiceConfig)) { throw 'Trained inference configuration does not exist yet.' }
$env:PYTHONUTF8 = '1'
$env:PYTHONIOENCODING = 'utf-8'
$env:version = 'v2'
$env:OMP_NUM_THREADS = '4'
$env:MKL_NUM_THREADS = '4'
$env:CUDAGraph = '0'
$env:PATH = (Join-Path $voiceRepo 'runtime') + [IO.Path]::PathSeparator + $env:PATH
$env:PYTHONPATH = $voiceRepo + [IO.Path]::PathSeparator + (Join-Path $voiceRepo 'GPT_SoVITS')
Push-Location -LiteralPath $voiceRepo
try { & $voicePython (Join-Path $voiceRuntime 'serve_voice.py') }
finally { Pop-Location }
