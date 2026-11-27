param([switch]$Test)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$compiler = Join-Path $frameworkRoot 'csc.exe'
$sdkMetadata = Get-ChildItem -LiteralPath 'C:/Program Files (x86)/Windows Kits/10/UnionMetadata' -Filter Windows.winmd -Recurse | Where-Object { $_.Directory.Name -ne 'Facade' } | Sort-Object FullName -Descending | Select-Object -First 1
if (!(Test-Path -LiteralPath $compiler) -or !$sdkMetadata) { throw 'DeskPill needs the installed .NET Framework compiler and Windows SDK metadata. See README.md.' }
$outputDirectory = Join-Path $projectRoot 'bin'
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$references = @('System.dll','System.Core.dll','System.Xaml.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Runtime.Serialization.dll','System.Security.dll','System.Management.dll','System.Net.Http.dll','System.Web.Extensions.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll')
$arguments = @('/nologo','/target:winexe','/optimize+','/langversion:5',('/out:' + (Join-Path $outputDirectory 'DeskPill.exe')),('/win32manifest:' + (Join-Path $projectRoot 'app.manifest')))
foreach ($reference in $references) { $arguments += '/r:' + $reference }
foreach ($reference in @('WindowsBase','PresentationCore','PresentationFramework','UIAutomationClient','UIAutomationTypes')) { $arguments += '/r:' + (Join-Path $frameworkRoot ('WPF/' + $reference + '.dll')) }
foreach ($reference in @('System.Runtime.WindowsRuntime','System.Runtime','System.Runtime.InteropServices.WindowsRuntime','System.Threading.Tasks')) { $arguments += '/r:' + (Join-Path $frameworkRoot ($reference + '.dll')) }
$arguments += '/r:' + $sdkMetadata.FullName
$arguments += '/win32icon:' + (Join-Path $projectRoot 'assets/DeskPill.ico')
$arguments += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src/DeskPill') -Filter '*.cs' | ForEach-Object FullName)
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'DeskPill compilation failed.' }
& $compiler /nologo /target:exe /platform:x64 /optimize+ /langversion:5 /r:System.Web.Extensions.dll ('/out:' + (Join-Path $outputDirectory 'DeskPill.Speech.exe')) (Join-Path $projectRoot 'tools/SpeechWorker.cs')
if ($LASTEXITCODE -ne 0) { throw 'Speech worker compilation failed.' }
Write-Host ('Built ' + (Join-Path $outputDirectory 'DeskPill.exe'))
if ($Test) { & (Join-Path $projectRoot 'tests/run.ps1') }
