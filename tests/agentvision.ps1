$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$runDirectory=Join-Path $env:TEMP ('DeskPill-AgentVisionTests-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runDirectory | Out-Null
try {
 $output=Join-Path $runDirectory 'AgentVisionTests.exe'
 $arguments=@('/nologo','/target:exe','/langversion:5',('/out:'+$output),'/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Security.dll','/r:System.Xaml.dll')
 foreach($assembly in @('WindowsBase','PresentationCore','PresentationFramework')) { $arguments+='/r:'+(Join-Path $framework ('WPF/'+$assembly+'.dll')) }
 $arguments+=Join-Path $root 'src/DeskPill/AgentVision.cs'
 $arguments+=Join-Path $root 'src/DeskPill/AgentCapturePicker.cs'
 $arguments+=Join-Path $PSScriptRoot 'AgentVisionTests.cs'
 & (Join-Path $framework 'csc.exe') @arguments
 if($LASTEXITCODE -ne 0){throw 'Agent vision test compilation failed'}
 & $output
 $testExit=$LASTEXITCODE
} finally {
 $resolvedRun=[IO.Path]::GetFullPath($runDirectory)
 $resolvedTemp=[IO.Path]::GetFullPath($env:TEMP).TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar
 if(!$resolvedRun.StartsWith($resolvedTemp,[StringComparison]::OrdinalIgnoreCase) -or !(Split-Path $resolvedRun -Leaf).StartsWith('DeskPill-AgentVisionTests-')) {throw 'Refusing unexpected test cleanup path'}
 Remove-Item -LiteralPath $resolvedRun -Recurse -Force
}
exit $testExit
