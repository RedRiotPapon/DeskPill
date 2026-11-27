$ErrorActionPreference = 'Stop'
foreach ($suite in @('domain.ps1','integrations.ps1','native.ps1','ui.ps1','interactions.ps1','notifications.ps1','dictation.ps1','motion.ps1','ring.ps1')) {
    & (Join-Path $PSScriptRoot $suite)
    if ($LASTEXITCODE -ne 0) { throw ('Failed: ' + $suite) }
}
Write-Host 'All DeskPill suites passed.'
