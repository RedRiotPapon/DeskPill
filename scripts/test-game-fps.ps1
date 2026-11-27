param([switch]$Install)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$directory=Join-Path $root 'artifacts/fps-fixture'
New-Item -ItemType Directory -Force -Path $directory | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'bin/DeskPill.exe') -Destination $directory -Force
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$arguments=@('/nologo','/target:exe','/langversion:5',('/out:'+(Join-Path $directory 'FpsFixture.exe')),('/r:'+(Join-Path $directory 'DeskPill.exe')),'/r:System.dll','/r:System.Core.dll','/r:System.Xaml.dll','/r:System.Windows.Forms.dll')
foreach($assembly in @('WindowsBase','PresentationCore','PresentationFramework')){$arguments+='/r:'+(Join-Path $framework ('WPF/'+$assembly+'.dll'))}
$arguments+=Join-Path $root 'tools/FpsFixture.cs'
& (Join-Path $framework 'csc.exe') @arguments
if($LASTEXITCODE -ne 0){throw 'FPS fixture compilation failed'}
if($Install){& (Join-Path $directory 'FpsFixture.exe') --install}else{& (Join-Path $directory 'FpsFixture.exe')}
if($LASTEXITCODE -eq 2){Write-Warning 'Windows did not supply FPS data. The unavailable state was verified.'}
elseif($LASTEXITCODE -ne 0){throw 'FPS fixture failed'}
