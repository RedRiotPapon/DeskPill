$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$dir=Join-Path $env:TEMP ('DeskPill-Agents-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dir | Out-Null
$csc='C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $csc /nologo /target:exe /out:"$dir/Fixture.exe" (Join-Path $root 'tools/AgentFixture.cs')
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
& $csc /nologo /target:exe /r:System.Runtime.Serialization.dll /r:System.Web.Extensions.dll /out:"$dir/Tests.exe" (Join-Path $root 'src/DeskPill/Domain.cs') (Join-Path $root 'src/DeskPill/AgentsRuntime.cs') (Join-Path $PSScriptRoot 'AgentTests.cs')
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
& "$dir/Tests.exe" "$dir/Fixture.exe"
exit $LASTEXITCODE
