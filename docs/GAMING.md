# Optional game glass and FPS

Game mode is off by default. Enable it in Settings → Gaming. While a matching game is in front, DeskPill temporarily uses a compact Floating Glass surface with the saved Glass opacity/highlight values. The window passes mouse input through. Hover expansion and ordinary HUD peeks pause so the notch cannot interrupt game input. The saved theme and style return when the game loses focus; normal fullscreen hiding still applies to other apps.

## Detection

Explicit executable names match windowed or fullscreen games. Optional automatic detection requires a fullscreen foreground graphics process and excludes browsers, Chromium window classes and common desktop apps. This is a heuristic, not a game database. Turn detection off and use explicit names if it misidentifies an app. Protected processes may prevent module inspection. The notch remains on the user's configured monitor.

## FPS setup

Install the optional, version-pinned PresentMon 2.6.0 component in Gaming settings. The 1 MB download comes from the official Intel GameTechDev GitHub release and is SHA-256 checked. It is launched only while a matching game is foreground, with one PID filter and a unique DeskPill trace-session name. GPU work, display tracking and input tracking are disabled. No game injection or output CSV files are used. Detection inspects executable and module names; no game code is modified.

Windows allows the ETW trace for administrators or members of the local Performance Log Users group. An administrator can grant that group membership; sign out and back in for a changed token. DeskPill does not modify account permissions or elevate automatically. The settings help explains the requirement. The glass bar works without trace access and displays FPS —.

## Measurement and limits

The number is presented FPS: 1,000 divided by the recent mean `MsBetweenPresents` interval. Only the target PID is accepted. Separate swap chains are not added together; the most active recent chain supplies the result. At least three valid samples are required, and samples older than 1.5 seconds expire. Invalid durations and stale data produce no number. This is not monitor refresh rate or a guarantee of displayed frames.

The helper uses bounded 30-second capture sessions and stops only its own named session on game exit. Failed permissions are reported as unavailable. Protected games and exclusive fullscreen can prevent measurement or overlay visibility. Gameplay overhead depends on hardware and cannot be guaranteed to be zero.

Deterministic tests cover matching, exclusions, CSV quoting, PID filtering, swap chains, invalid timing, staleness, live preference rollback and native mouse pass-through restoration. `scripts/test-game-fps.ps1` renders an isolated WPF fixture, without attaching to a user's game. It verified the access-denied/unavailable path on this machine; positive live FPS remains unverified here because the current Windows token lacks trace access.
