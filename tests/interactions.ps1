$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$runDirectory=Join-Path $env:TEMP ('DeskPill-InteractionTests-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runDirectory | Out-Null
try {
 Copy-Item -LiteralPath (Join-Path $root 'bin/DeskPill.exe') -Destination $runDirectory
 $output=Join-Path $runDirectory 'InteractionTests.exe'
 $arguments=@('/nologo','/target:exe','/langversion:5',('/out:'+$output),('/r:'+(Join-Path $runDirectory 'DeskPill.exe')),'/r:System.dll','/r:System.Core.dll','/r:System.Xaml.dll')
 foreach($assembly in @('WindowsBase','PresentationCore','PresentationFramework')) { $arguments+='/r:'+(Join-Path $framework ('WPF/'+$assembly+'.dll')) }
 $arguments+=Join-Path $PSScriptRoot 'InteractionTests.cs'
 & (Join-Path $framework 'csc.exe') @arguments
 if($LASTEXITCODE -ne 0){throw 'Interaction test compilation failed'}
 & $output
 $testExit=$LASTEXITCODE
} finally {
 # This exact, newly created temporary directory contains only the test binary and assembly copy.
 $resolvedRun = [IO.Path]::GetFullPath($runDirectory)
 $resolvedTemp = [IO.Path]::GetFullPath($env:TEMP).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
 if (!$resolvedRun.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase) -or !(Split-Path $resolvedRun -Leaf).StartsWith('DeskPill-InteractionTests-')) { throw 'Refusing unexpected test cleanup path' }
 Remove-Item -LiteralPath $resolvedRun -Recurse -Force
}
exit $testExit
