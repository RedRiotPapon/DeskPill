# Agents Home v2 implementation plan

**Goal:** Add an optional, default-visible Agents page with clear connections, useful session controls and reviewed voice prompts without changing existing notch interactions.

**Architecture:** Store bounded connection profiles alongside local app state. Keep runtime sessions in memory in a dedicated coordinator. Use documented CLI adapters for DeskPill-owned sessions and a separate app-handoff connection for other tools. Never claim access to arbitrary IDE/cloud conversations. An inline Agents detail view uses the existing spring motion and speech capture, with explicit Send after dictation.

**Tech stack:** C# 5, WPF, .NET Framework 4.8, Windows Job Objects, JSONL CLI output; no new package dependency.

## Requirements

- Agents is a separate sixth page, visible by default for new and upgraded installs; hide/reorder it through Settings → Pages without deleting connections.
- Preserve Classic, all existing pages, hover delays, reduced motion, dictation's ordinary shortcut, notification quiet rules and game input pass-through.
- Connection setup names the tool and workspace, detects known native executables, supports manually browsing an executable, and distinguishes CLI sessions from app shortcuts.
- Prompt target and permission mode are visible. Review-only is default. Codex workspace writes require a saved explicit opt-in. No bypass/YOLO flags or automatic approval.
- Existing CLI authentication belongs to its tool. Do not read credentials or scrape private session stores.
- Voice is optional and fills a draft; a separate Send dispatches. No microphone/model work while idle.
- Stop terminates only DeskPill-owned job processes. Results are bounded and stay in memory; hiding the page does not stop work. App exit ends owned runs.

## Work

- [x] Domain tests first: sixth page migration, hidden-page persistence, bounded connection profiles and selected connection repair.
- [x] Runtime tests first: quoted paths, fixed provider arguments, bounded JSONL parsing, session IDs, status/error transitions, one owned run per connection and process-tree cancellation via a fake CLI.
- [x] Implement runtime adapters and manual app handoff; verify local CLI help against official documentation.
- [x] Build compact empty/connected/working/attention states, an inline prompt/result surface and a connection editor. Preserve drafts across status updates and never resize on streamed events.
- [x] Route existing optional speech capture to a reviewed agent draft with its destination pinned; Esc cancels without dispatching or pasting.
- [x] Integrate page visibility, dock, navigation, settings and state migration. Add UI regressions for hide/re-enable, mode changes, target selection, draft retention and keyboard navigation.
- [x] Inspect real renders at 100–200% DPI in Classic and colored themes; verify clean idle animation behavior and existing motion suite.
- [x] Review, run all suites, document capability limits, package v2.0.0, push after checks and update local executable.

## Integration evidence

Codex and Claude Code are installed locally; their help documents noninteractive structured output and session resume. Gemini CLI documents JSONL headless mode and plan mode. Antigravity desktop/cloud conversations are not assumed controllable through those CLIs: users can add the installed app as an explicit handoff shortcut. CLI availability and sign-in vary by machine and must be shown honestly.

## Review focus

Cancellation during process startup; stale voice callbacks after changing target; output floods or malformed JSON; accidentally granting broader permissions on resume; external tools exposing no session API; page hiding while work is active; keyboard/mouse focus and drafts across re-rendering.

## Verification record

Implemented and reviewed the owned-process runtime and inline UI. Fixed Gemini chunk whitespace, pre-dispatch event coalescing, detached-draft completion, home/detail destination synchronization and provider/executable changes. UI fixture checks cover hide/restore, target selection, draft retention, actual fake-CLI Send/Stop and stable shell bounds. Connection dropdowns use scoped theme-aware controls. Software renders at 96, 120, 144 and 192 DPI were inspected; no physical mixed-DPI transitions or real provider requests were tested. Final local suites, CI and release are recorded in the release commit/checks.
