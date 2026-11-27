
# Agent connections

Agents Home runs a new, owned CLI session per connection and resumes only the UUID returned by that same connection during the current DeskPill lifetime. Changing its provider, executable, workspace or edit setting starts a fresh session. It does not inspect other app, IDE, cloud or CLI conversations.

Install and sign into the provider CLI separately. DeskPill detects native `codex.exe`, `claude.exe` and `gemini.exe` on PATH, Claude's user-local bin, and versioned Codex app bin folders. Choose a native executable manually if detection fails. Shell scripts, npm `.cmd` shims and automatic installation are unsupported. Gemini installations exposing only a node shim require a native wrapper executable supplied by the user.

Codex uses `exec --json --sandbox read-only -c approval_policy="never" --skip-git-repo-check -`, with the reviewed prompt on stdin. The connection's explicit edit switch changes only its sandbox to `workspace-write`. Resume uses the exact returned UUID after these same explicit sandbox and approval options. Claude uses `-p --verbose --output-format stream-json --permission-mode plan`, with the prompt on stdin and an exact `--resume` UUID when available. Gemini uses `--output-format stream-json --approval-mode plan -p <quoted prompt>` and an exact `--resume` UUID. Gemini plan mode may require enabling experimental planning in the CLI; unsupported versions fail visibly and DeskPill does not fall back to broader permissions. Existing provider configuration, authentication, hooks and extensions are managed by the CLI itself.

App shortcut connections offer manual copy and open. They do not receive prompts or report session progress through the runtime.

Stop and exit terminate only the process job DeskPill created for that run, including descendants. The executable is created suspended and assigned to a Windows Job Object before execution; failure to assign prevents it from running. A run stops after 30 minutes. The home menu can start a new conversation while idle, clearing only that connection’s in-memory session context. One run per connection is allowed. Different connections may run independently.

DeskPill keeps drafts, session UUIDs and the last 16,384 output characters in memory; it writes no transcripts or history. A JSONL line exceeding 65,536 characters is discarded while its stream continues draining. Provider CLIs may maintain their own normal session files. CLI stderr is bounded and kept separately from the agent reply. The conversation’s info button reveals tool diagnostics and switches back to the response. Successful replies remain successful when optional connectors log authentication failures; failed runs still show Needs attention with diagnostics available. Connector authentication remains managed by the CLI; DeskPill does not disable servers or change credentials. No provider invocation, microphone access or network request occurs until explicitly requested through the respective UI.

Implementation references, checked 2026-11-27 against installed Codex and Claude `--help` (no model requests):

- [Codex noninteractive mode](https://developers.openai.com/codex/noninteractive/)
- [Codex SDK subprocess arguments](https://github.com/openai/codex/blob/main/sdk/typescript/src/exec.ts)
- [Claude CLI reference](https://code.claude.com/docs/en/cli-reference)
- [Gemini headless mode](https://geminicli.com/docs/cli/headless/)
- [Gemini configuration reference](https://geminicli.com/docs/reference/configuration/)

Run `powershell -NoProfile -ExecutionPolicy Bypass -File tests/agents.ps1` for isolated fixture checks. The suite compiles into its own temporary folder, never calls a real provider and does not write into a user's workspace.
