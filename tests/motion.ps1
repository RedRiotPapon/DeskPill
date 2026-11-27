param([string]$Frames)
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary=Join-Path $env:TEMP ('DeskPill-MotionProbe-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
 Copy-Item -LiteralPath (Join-Path $project 'bin/DeskPill.exe') -Destination $temporary
 $executable=Join-Path $temporary 'MotionProbe.exe'
 $arguments=@('/nologo','/target:exe','/langversion:5',('/out:'+$executable),('/r:'+(Join-Path $temporary 'DeskPill.exe')),'/r:System.dll','/r:System.Core.dll','/r:System.Xaml.dll')
 foreach($assembly in @('WindowsBase','PresentationCore','PresentationFramework')) { $arguments+='/r:'+(Join-Path $framework ('WPF/'+$assembly+'.dll')) }
 $arguments+=Join-Path $PSScriptRoot 'MotionProbe.cs'
 & (Join-Path $framework 'csc.exe') @arguments
 if($LASTEXITCODE -ne 0) { throw 'Motion probe compilation failed.' }
 if($Frames) { & $executable $Frames } else { & $executable }
 $result=$LASTEXITCODE
} finally {
 # Exact fresh test directory, containing only copied assembly and probe binary.
 $resolvedTemporary=[IO.Path]::GetFullPath($temporary)
 $temporaryRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
 if((Split-Path -Parent $resolvedTemporary).TrimEnd('\') -ne $temporaryRoot -or !(Split-Path -Leaf $resolvedTemporary).StartsWith('DeskPill-MotionProbe-')) { throw 'Refusing cleanup outside the generated temporary probe directory.' }
 Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force
}
exit $result
