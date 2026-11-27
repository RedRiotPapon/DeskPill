# Contributing

Build on Windows with .NET Framework 4.8 and Windows SDK metadata. Run `./build.ps1 -Test` before submitting a change. UI, media and OCR checks need an interactive desktop; CI runs the deterministic domain, parsing and notification inbox suites.

Keep changes focused. Explain the user-visible behavior, reproduction steps and validation in your pull request. For UI changes, test hover reversal, keyboard navigation, Fast animations, and Classic plus a colored skin. Verify that palette previews do not save until the user chooses Save.

Keep service logic outside page controls. `Domain.cs` owns state normalization and persistence; `NativeServices.cs` and `NotificationService.cs` handle Windows integrations; `IslandMotion.cs` owns spring motion; `Themes.cs` owns palette tokens.

Do not commit personal state, credentials, local experiments, research material, build output or desktop captures containing private content. Documentation images belong in `docs/images` and must show DeskPill with isolated demo data. Local `artifacts/` and `.impeccable/` folders are ignored.

Useful commands:

```powershell
./build.ps1
./build.ps1 -Test
./tests/domain.ps1
./tests/agents.ps1
./tests/integrations.ps1
./tests/notifications.ps1
./scripts/measure-motion.ps1
./scripts/test-paste-delivery.ps1
./package.ps1 -SkipBuild
```

Report bugs through GitHub Issues with Windows version, monitor scale, app version and steps to reproduce. Do not include notification text, clipboard history or API keys.

Run motion measurements alone on an idle interactive desktop. They record WPF callback pacing, not actual compositor presentation times. The paste delivery probe briefly activates its own test windows and intercepts Ctrl+V without reading or changing the clipboard.

Optional speech verification: after installing the English pack in Settings, run `./scripts/test-dictation-speech.ps1`. It synthesizes an English fixture with a Windows TTS voice and runs DeskPill's independent speech worker without microphone or clipboard access. `-Install` explicitly downloads the pack if missing. `./tests/dictation.ps1` covers bounded WAV encoding, shortcut state, cancellation and checksum guards without downloading assets.

Game-mode checks: `./tests/gaming.ps1` exercises detection and presentation-CSV filtering without tracing. `./scripts/test-game-fps.ps1` uses its own rendering fixture; `-Install` opts into downloading PresentMon. Positive FPS needs Windows ETW trace access. UI changes should verify Classic, Glass at multiple opacities, motion preview/cancel/reset, native input pass-through and theme restoration.

Agent integration changes must use isolated fixtures for quoting, output bounds, resume, cancellation and owned process cleanup. Do not run billable model calls as unattended tests. Keep CLI session control distinct from manual app shortcuts.
