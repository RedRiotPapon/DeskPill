param()
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent

$directory=Join-Path $root 'artifacts/paste-probe'
New-Item -ItemType Directory -Force -Path $directory | Out-Null
$AssemblyPath=Join-Path $root 'bin/DeskPill.exe'
Copy-Item -LiteralPath $AssemblyPath -Destination $directory -Force
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$arguments=@('/nologo','/target:exe','/langversion:5',('/out:'+(Join-Path $directory 'PasteDeliveryProbe.exe')),('/r:'+(Join-Path $directory 'DeskPill.exe')),'/r:System.dll','/r:System.Core.dll','/r:System.Xaml.dll','/r:System.Windows.Forms.dll','/r:System.Web.Extensions.dll')
foreach($assembly in @('WindowsBase','PresentationCore','PresentationFramework')) { $arguments+='/r:'+(Join-Path $framework ('WPF/'+$assembly+'.dll')) }
$arguments+=Join-Path $root 'tools/PasteDeliveryProbe.cs'
& (Join-Path $framework 'csc.exe') @arguments
if($LASTEXITCODE -ne 0){throw 'Paste delivery probe compilation failed'}
& (Join-Path $directory 'PasteDeliveryProbe.exe')
if($LASTEXITCODE -ne 0){throw 'Paste delivery probe failed'}
