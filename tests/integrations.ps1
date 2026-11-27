$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$csc="$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$out=Join-Path $env:TEMP 'DeskPill-IntegrationTests.exe'
& $csc /nologo /langversion:5 /out:$out /r:System.Net.Http.dll /r:System.Web.Extensions.dll /r:System.Security.dll /r:System.Runtime.Serialization.dll (Join-Path $root 'src/DeskPill/Domain.cs') (Join-Path $root 'src/DeskPill/IntegrationServices.cs') (Join-Path $PSScriptRoot 'IntegrationTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Integration fixture compilation failed' }
& $out
if ($LASTEXITCODE -ne 0) { throw 'Integration fixtures failed' }
