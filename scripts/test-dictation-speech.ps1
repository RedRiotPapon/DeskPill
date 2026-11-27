param([switch]$Install)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$directory=Join-Path $root 'artifacts/speech-fixture'
New-Item -ItemType Directory -Force -Path $directory | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'bin/DeskPill.exe'),(Join-Path $root 'bin/DeskPill.Speech.exe') -Destination $directory -Force
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$speechDll=Get-ChildItem -LiteralPath (Join-Path $env:WINDIR 'Microsoft.NET/assembly/GAC_MSIL/System.Speech') -Filter System.Speech.dll -Recurse | Select-Object -First 1 -ExpandProperty FullName
if(!$speechDll){throw 'This optional fixture needs the Windows System.Speech assembly and an English TTS voice.'}
$arguments=@('/nologo','/target:exe','/langversion:5',('/out:'+(Join-Path $directory 'SpeechFixtureProbe.exe')),('/r:'+(Join-Path $directory 'DeskPill.exe')),('/r:'+$speechDll),(Join-Path $root 'tools/SpeechFixtureProbe.cs'))
& (Join-Path $framework 'csc.exe') @arguments
if($LASTEXITCODE -ne 0){throw 'Speech fixture compilation failed'}
$probe=Join-Path $directory 'SpeechFixtureProbe.exe'
if($Install){& $probe --install}else{& $probe}
if($LASTEXITCODE -ne 0){throw 'Speech fixture failed'}
