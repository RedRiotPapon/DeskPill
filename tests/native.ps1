$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$framework = 'C:/Windows/Microsoft.NET/Framework64/v4.0.30319'
$refs = @('System.dll','System.Core.dll','System.Xaml.dll','System.Drawing.dll','System.Management.dll','System.Runtime.WindowsRuntime.dll','System.Runtime.dll','System.Runtime.InteropServices.WindowsRuntime.dll','System.Threading.Tasks.dll') | ForEach-Object { '/r:' + $framework + '/' + $_ }
$refs += @('WindowsBase.dll','PresentationCore.dll','UIAutomationClient.dll','UIAutomationTypes.dll') | ForEach-Object { '/r:' + $framework + '/WPF/' + $_ }
$metadata = Get-ChildItem -LiteralPath 'C:/Program Files (x86)/Windows Kits/10/UnionMetadata' -Filter Windows.winmd -Recurse | Where-Object { $_.Directory.Name -ne 'Facade' } | Sort-Object FullName -Descending | Select-Object -First 1
if (!$metadata) { throw 'Windows SDK metadata is required.' }
$refs += '/r:' + $metadata.FullName
New-Item -ItemType Directory -Force ($root + '/artifacts') | Out-Null
& ($framework + '/csc.exe') /nologo /langversion:5 /target:exe ('/out:' + (Join-Path $root 'artifacts\NativeProbe.exe')) $refs (Join-Path $root 'tests\NativeProbe.cs') (Join-Path $root 'src\DeskPill\NativeServices.cs')
if ($LASTEXITCODE -ne 0) { throw 'Native compilation failed' }
& ($root + '/artifacts/NativeProbe.exe')
if ($LASTEXITCODE -ne 0) { throw 'Native probe failed' }
