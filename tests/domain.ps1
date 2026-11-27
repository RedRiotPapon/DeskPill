$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$out=Join-Path $env:TEMP 'DeskPill-DomainTests.exe'
& C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe /nologo /target:exe /r:System.Runtime.Serialization.dll /out:$out (Join-Path $root 'src/DeskPill/Domain.cs') (Join-Path $PSScriptRoot 'DomainTests.cs')
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
& $out
exit $LASTEXITCODE
