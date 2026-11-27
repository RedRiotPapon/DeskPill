<p align="center"><img src="assets/DeskPill.svg" width="72" alt="DeskPill icon"></p>
<h1 align="center">DeskPill</h1>
<p align="center">A quiet home for music, focus and everyday tools at the top of your Windows desktop.</p>
<p align="center">Windows 10/11 · Native WPF · .NET Framework 4.8 · MIT license</p>

![DeskPill showing media, focus and custom themes](docs/images/desk-pill.png)

DeskPill is a small desktop notch that expands when you need it. Control your music, start a focus session, hold files between apps, and glance at notifications without opening another dashboard.

## What it does

- **Music:** real Windows media sessions, artwork, playback controls and seeking.
- **Focus:** work/break timer with progress that survives restarts.
- **Shelf:** drop files to keep shortcuts handy, then open, copy or drag them into another app.
- **Tasks & water:** lightweight tasks and optional hydration tracking.
- **Windows notifications:** previews for new notifications, with hover-to-hold and dismiss.
- **Quick tools:** clipboard history, screen OCR, calendar import, and an optional Gemini assistant.
- **Personalization:** Classic plus seven skins, live theme preview, and your own six-color Custom palette with matte/satin finish and outline strength.

The notch yields to occupied title-bar space, supports fullscreen hiding, and respects Windows reduced-motion settings. Startup is opt-in.

## Run it

Download the portable ZIP from [Releases](https://github.com/RedRiotPapon/DeskPill/releases/latest), extract it, and run `DeskPill.exe`. You can also build locally using the instructions below. No installer or additional app runtime is required on a system with .NET Framework 4.8.

1. Hover over the top-center notch to expand it.
2. Use the dots, arrows or mouse wheel to change pages.
3. Hover near the lower page dots for the page menu and Settings.
4. Right-click the notch or its tray icon for more tools and Exit.

| Shortcut | Action |
| --- | --- |
| `Ctrl+Alt+Space` | Open Ask |
| `Ctrl+Alt+T` | Select a screen region for OCR |

Shortcuts can conflict with other applications; the tray menu remains available. Settings also offers click-to-open mode, display selection, scale, position and page order.

## Make it yours

![DeskPill skins with demo data](docs/images/skins.png)

Open **Settings → Appearance** and select a skin to preview it immediately. **Save changes** keeps it; **Cancel** or closing Settings restores your saved look.

Choose **Custom** to edit shell, surface, accent, secondary, text and muted colors with hex inputs or color pickers. Change the finish and outline strength while watching the result. One Custom palette is saved locally.

## Build and test

You need Windows, .NET Framework 4.8 and Windows SDK metadata (`Windows.winmd`). The .NET desktop development workload and Windows SDK in Visual Studio Build Tools provide the development prerequisites. No NuGet packages are required.

```powershell
.\build.ps1            # Compile to bin/DeskPill.exe
.\build.ps1 -Test      # Build and run all test suites
.\package.ps1          # Create dist/DeskPill-1.0.0-portable.zip
```

The solution is also available as `DeskPill.sln`. Full UI, media and OCR probes require an interactive Windows desktop; CI runs the deterministic suites. See [CONTRIBUTING.md](CONTRIBUTING.md).

## Privacy and optional services

Settings and local state live in `%LOCALAPPDATA%\DeskPill`. File shelf entries store paths; removing one does not delete its file. Clipboard persistence is **off by default**. Notification text stays in memory.

Ask sends submitted prompts to Google when you configure your own Gemini key; the saved key uses Windows DPAPI for the current user. Weather, calendar URLs, lyrics and other online tools contact their respective services when used. Offline arithmetic does not require a key. No accounts, keys, personal state or telemetry configuration are included in this repository.

## Current limits

- Notification previews do not open or reply through the originating app. Queued previews expire after 30 seconds.
- Clipboard history is text-only, with no search, pinning or quick-paste palette yet.
- Media controls depend on the source app exposing a Windows media session; brightness support depends on the display.
- OCR depends on Windows OCR language availability. Protected applications and unsupported accessibility trees may limit window overlap detection.
- ICS import does not expand recurring events. Optional services can require network access and provider credentials.
- The portable executable is unsigned. Mixed-DPI hardware and frame pacing need broader testing.

## Project layout

```text
src/DeskPill/       Application, Windows services and interface
assets/            Original app icon
scripts/           Optional developer tools
tests/             Domain, integration, UI and motion checks
docs/images/       Curated DeskPill images using isolated demo data
```

Bugs and focused pull requests are welcome. Include the Windows version, display scaling and reproduction steps. Keep reports free of private notification contents or API keys.

[MIT License](LICENSE). Images use demo data generated from DeskPill itself.
