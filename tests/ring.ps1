$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$ringProbeDirectory=Join-Path $env:TEMP ('DeskPill-RingProbe-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $ringProbeDirectory | Out-Null
try {
 Copy-Item -LiteralPath (Join-Path $project 'bin/DeskPill.exe') -Destination $ringProbeDirectory
 $executable=Join-Path $ringProbeDirectory 'RingProbe.exe'
 $arguments=@('/nologo','/target:exe','/langversion:5',('/out:'+$executable),('/r:'+(Join-Path $ringProbeDirectory 'DeskPill.exe')),'/r:System.dll','/r:System.Core.dll','/r:System.Xaml.dll')
 foreach($assembly in @('WindowsBase','PresentationCore','PresentationFramework')) { $arguments+='/r:'+(Join-Path $framework ('WPF/'+$assembly+'.dll')) }
 $arguments+=Join-Path $PSScriptRoot 'RingProbe.cs'
 & (Join-Path $framework 'csc.exe') @arguments
 if($LASTEXITCODE -ne 0) { throw 'Ring probe compilation failed.' }
 & $executable
 $result=$LASTEXITCODE
} finally {
 $resolvedRingProbe=[IO.Path]::GetFullPath($ringProbeDirectory)
 $ringProbeTemporaryRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
 if((Split-Path -Parent $resolvedRingProbe).TrimEnd('\') -ne $ringProbeTemporaryRoot -or !(Split-Path -Leaf $resolvedRingProbe).StartsWith('DeskPill-RingProbe-')) { throw 'Refusing cleanup outside the generated temporary ring probe directory.' }
 Remove-Item -LiteralPath $resolvedRingProbe -Recurse -Force
}
exit $result
