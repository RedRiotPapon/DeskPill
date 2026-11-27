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
./tests/integrations.ps1
./tests/notifications.ps1
./package.ps1 -SkipBuild
```

Report bugs through GitHub Issues with Windows version, monitor scale, app version and steps to reproduce. Do not include notification text, clipboard history or API keys.
