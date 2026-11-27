# Agents Context Implementation Plan

> **For agentic workers:** Use executing-plans for root integration and dispatching-parallel-agents for independent runtime and capture modules.

**Goal:** Deliver the approved agent UX and reviewed screen-context flow while keeping regular DeskPill users unaffected.

**Architecture:** Lazy agent coordinator; provider-aware native launch resolution; bounded in-memory turns; explicit capture module and immutable per-run image paths; root partial window owns UX, voice routing and completion. Existing ordinary dictation, motion and game/notification policies remain authoritative.

**Tech Stack:** C# 5 / WPF / .NET Framework 4.8 / native Win32; no new runtime package.

**Spec:** docs/superpowers/specs/2026-11-27-agents-context.md

## Global Constraints
- Agent features only activate after explicit connection/use; no startup discovery polling or capture.
- Preserve Classic and hover timing; no cloud speech or GUI takeover.
- Keep provider permissions unchanged; unknown integrations remain manual handoff.
- No paid provider calls or real desktop capture in automated tests.

## Review Focus
- Wrong capture/paste target after notch focus; cancel and stale voice completion.
- Screenshot file cleanup and ownership through start/failure/Stop/exit.
- Provider/path quoting, unknown npm shims, capabilities and resume images.
- Multiple agents finishing while using another page or editing.
- Scroll/selection and draft persistence across close and target switches.

## Tasks
- [x] Runtime: RED/GREEN fixture coverage for known npm launch resolution, bounded transcript, actual tool progress and explicit image arguments. Modify AgentsRuntime and dedicated resolver/tests only; keep compatibility with old public calls.
- [x] Vision: RED/GREEN pure geometry/capability/file-lifetime tests; new AgentVision.cs plus explicit preview/capture picker; expose capture result and no idle hooks. Root owns window integration.
- [x] Root UX: test activation gate, direct composer, copy/paste, collapse, notices, voice routing/toggle and deferred mic setup; implement partial files and window hooks with no regular-mode change.
- [x] Integration: connect preview image lifetime to owned runtime, preserve per-agent drafts, ensure image not silently reused, and test launch failures/cancellation.
- [x] Review the complete diff, inspect own demo renders at 96/120/144/192 DPI, run all suites, and update docs/version/package.

Release procedure: push after local checks, publish after green CI, then restart the user app and refresh its legacy shortcut copy.

## Verification record
All local suites passed against the 2.1.0 binary. Runtime fixture checks: 67; synthetic vision checks: 22; interaction checks: 67. Independent reviews identified and resolved run-copy shutdown cleanup, terminal attention detection, failed-turn transcript visibility, capture/paste identity matching and preview-window exclusion. Real provider requests, live microphone input and physical mixed-DPI capture remain unverified. See docs/PERFORMANCE.md for the fresh callback measurement.
