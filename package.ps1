param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
if (!$SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') }
$releaseDirectory = Join-Path $PSScriptRoot 'dist/DeskPill-1.1.0'
New-Item -ItemType Directory -Force -Path $releaseDirectory | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin/DeskPill.exe') -Destination $releaseDirectory -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $releaseDirectory -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination $releaseDirectory -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs/CAPABILITIES.md') -Destination (Join-Path $releaseDirectory 'CAPABILITIES.md') -Force
@'
DeskPill 1.1.0

Run DeskPill.exe. Hover over the top-center notch to expand it.
Use dots/arrows or the mouse wheel to switch the five pages.
Right-click the island or use its tray icon for Settings and Exit.

For an alternate floating preview:
DeskPill.exe --preview
Preview does not register global shortcuts or monitor the clipboard.

Normal-mode shortcuts: Ctrl+Alt+Space (Ask), Ctrl+Alt+T (screen OCR), Ctrl+Alt+V (clipboard palette).
Data: %LOCALAPPDATA%\DeskPill
Startup registration is opt-in through Settings.

Windows 10/11 and .NET Framework 4.8 are required. No installation is needed.
See README.md and CAPABILITIES.md for usage and current limitations.
'@ | Set-Content -LiteralPath (Join-Path $releaseDirectory 'START-HERE.txt') -Encoding UTF8
$archive = Join-Path $PSScriptRoot 'dist/DeskPill-1.1.0-portable.zip'
Compress-Archive -Path (Join-Path $releaseDirectory '*') -DestinationPath $archive -Force
Write-Host ('Packaged ' + $archive)
