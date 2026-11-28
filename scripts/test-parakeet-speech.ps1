param(
    [string]$Runtime = (Join-Path $env:LOCALAPPDATA 'DeskPill/speech/parakeet-unified-1/runtime/transcribe-native-windows-x86_64-cpu-vulkan'),
    [string]$Model = (Join-Path $env:LOCALAPPDATA 'DeskPill/speech/parakeet-unified-1/model.gguf'),
    [ValidateSet('Auto','CPU','GPU')][string]$Backend = 'CPU',
    [switch]$CpuOnly
)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$directory=Join-Path $root 'artifacts/parakeet-fixture'
New-Item -ItemType Directory -Force -Path $directory | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'bin/DeskPill.exe'),(Join-Path $root 'bin/DeskPill.Speech.exe') -Destination $directory -Force
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$speechDll=Get-ChildItem -LiteralPath (Join-Path $env:WINDIR 'Microsoft.NET/assembly/GAC_MSIL/System.Speech') -Filter System.Speech.dll -Recurse | Select-Object -First 1 -ExpandProperty FullName
if(!$speechDll){throw 'This optional fixture needs Windows System.Speech and an English TTS voice.'}
$arguments=@('/nologo','/target:exe','/langversion:5',('/out:'+(Join-Path $directory 'ParakeetFixtureProbe.exe')),('/r:'+(Join-Path $directory 'DeskPill.exe')),('/r:'+$speechDll),(Join-Path $root 'tools/ParakeetFixtureProbe.cs'))
& (Join-Path $framework 'csc.exe') @arguments
if($LASTEXITCODE -ne 0){throw 'Parakeet fixture compilation failed'}
$probe=Join-Path $directory 'ParakeetFixtureProbe.exe'
if($CpuOnly){& $probe $Runtime $Model $Backend cpu-only}else{& $probe $Runtime $Model $Backend}
if($LASTEXITCODE -ne 0){throw 'Parakeet fixture failed'}
