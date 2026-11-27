param([string]$Output,[string]$AssemblyPath)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if (!$Output) { $Output=Join-Path $root 'artifacts/motion-measurement.json' }
$directory=Join-Path $root 'artifacts/motion-probe'
New-Item -ItemType Directory -Force -Path $directory | Out-Null
if (!$AssemblyPath) { $AssemblyPath=Join-Path $root 'bin/DeskPill.exe' }
Copy-Item -LiteralPath $AssemblyPath -Destination $directory -Force
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$arguments=@('/nologo','/target:exe','/langversion:5',('/out:'+(Join-Path $directory 'MotionMeasurements.exe')),('/r:'+(Join-Path $directory 'DeskPill.exe')),'/r:System.dll','/r:System.Core.dll','/r:System.Xaml.dll','/r:System.Windows.Forms.dll','/r:System.Web.Extensions.dll')
foreach($assembly in @('WindowsBase','PresentationCore','PresentationFramework')) { $arguments+='/r:'+(Join-Path $framework ('WPF/'+$assembly+'.dll')) }
$arguments+=Join-Path $root 'tools/MotionMeasurements.cs'
& (Join-Path $framework 'csc.exe') @arguments
if($LASTEXITCODE -ne 0){throw 'Motion measurement compilation failed'}
& (Join-Path $directory 'MotionMeasurements.exe') $Output
if($LASTEXITCODE -ne 0){throw 'Motion measurement failed'}
