# DeskPill 1.2

Native Windows desktop notch with Music, Shelf, Focus, Water and Tasks. Hide unused pages without deleting their data. Eight built-in skins and a Custom palette support live preview.

Windows notification previews offer Open app, Copy and Recent actions. Esc dismisses a preview without activating DeskPill; unattended previews expire after a configurable two to fifteen seconds, with hover pausing the countdown. Recent retains up to 50 arrivals in memory. Per-app, focus and fullscreen preferences control peeks. Deferred previews coalesce to the newest eligible arrival. Opening a source launches its registered app, not a specific message.

Ctrl+Alt+V opens searchable text clipboard history. Select a result and press Enter to paste into the captured source window, or Ctrl+Enter to copy. Windows focus/input restrictions leave the text copied for manual paste. Clipboard persistence is off by default.

Also includes Windows media controls, OCR, focus timers, hydration tracking, tasks, calendar import and optional online tools. See the README for privacy behavior and limitations, and [performance measurements](PERFORMANCE.md) for measured coverage. Hardware-dependent checks require an interactive Windows desktop.

Optional English dictation is built into DeskPill: one-time verified speech-pack setup, Win+Space start/finish, Esc cancellation, input-reactive listening motion, a 90-second cap, review after foreground changes, and guarded paste. The independent worker releases its offline model after each transcription. Dictation is off by default; enabling it takes over the Windows language-switch shortcut. English recognition was checked with generated audio without microphone or clipboard access; live microphone hardware and physical shortcut behavior still need user verification.
