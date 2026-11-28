param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
if (!$SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') }
$releaseDirectory = Join-Path $PSScriptRoot 'dist/DeskPill-2.3.0'
New-Item -ItemType Directory -Force -Path $releaseDirectory | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin/DeskPill.exe') -Destination $releaseDirectory -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin/DeskPill.Speech.exe') -Destination $releaseDirectory -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD_PARTY_NOTICES.md') -Destination $releaseDirectory -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $releaseDirectory -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination $releaseDirectory -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs/AGENTS.md') -Destination $releaseDirectory -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs/DICTATION.md') -Destination $releaseDirectory -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs/CAPABILITIES.md') -Destination (Join-Path $releaseDirectory 'CAPABILITIES.md') -Force
@'
DeskPill 2.3.0

Run DeskPill.exe. Hover over the top-center notch to expand it.
Use dots/arrows or the mouse wheel to switch pages.
Right-click the island or use its tray icon for Settings and Exit.

For an alternate floating preview:
DeskPill.exe --preview
Preview does not register global shortcuts or monitor the clipboard.

Normal-mode shortcuts: Ctrl+Alt+Space (Ask), Ctrl+Alt+T (screen OCR), Ctrl+Alt+V (clipboard palette).
Agents: connect an installed native or official npm CLI or add an app shortcut. Settings > Pages hides/restores it.
Agent voice: hold the mic button or choose click toggle, review, then Send. No automatic dispatch.
Codex: camera button attaches an explicitly selected screen image for review before Send.
Conversation gear: Copy answer, Copy conversation and guarded Paste into the source app.
See AGENTS.md for supported tools and limits.
Music: click artwork/title to open its app or a uniquely matched accessible browser tab.
Volume: the speaker button opens a smooth timed flyout; Esc or an outside click dismisses it.
Motion: Settings > Motion has hover timing, animation pace, live preview and reset.
Glass: select the second theme in Appearance to control surface opacity and highlights.
Size & shape: preview overall size, idle width, Classic/Floating placement and corner shapes. Reset restores defaults; Cancel restores saved geometry.
Gaming: right-click the tray icon > Game mode for the translucent FPS bar.
With Game mode off, detected games tuck the notch away. Windows trace access is required for FPS.
Optional English dictation: Settings > Dictation. Choose Vosk or Parakeet, read the requirements, explicitly download the chosen pack, enable and Save. Parakeet offers Auto/CPU/GPU (Vulkan). See DICTATION.md.
Hold Ctrl+Space while speaking; release to insert. The voice waveform stays inside the notch.
Esc cancels. Disabling releases Ctrl+Space to other apps.
Keep DeskPill.Speech.exe beside DeskPill.exe.
Data: %LOCALAPPDATA%\DeskPill
Startup registration is opt-in through Settings.

Windows 10/11 and .NET Framework 4.8 are required. No installation is needed.
See README.md and CAPABILITIES.md for usage and current limitations.
'@ | Set-Content -LiteralPath (Join-Path $releaseDirectory 'START-HERE.txt') -Encoding UTF8
$archive = Join-Path $PSScriptRoot 'dist/DeskPill-2.3.0-portable.zip'
Compress-Archive -Path (Join-Path $releaseDirectory '*') -DestinationPath $archive -Force
Write-Host ('Packaged ' + $archive)
