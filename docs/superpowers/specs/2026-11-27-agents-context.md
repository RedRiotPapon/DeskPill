# Agents context upgrade — approved design

Implement the eight priorities approved in chat: easier native/npm setup; direct prompt/mic entry; bounded in-memory conversation history and Copy Answer; truthful activity and quiet completion; idle collapse without stopping work; optional toggle/hold voice and route enabled Win+Space to the open agent draft; explicit reviewed screen attachments (Codex first); explicit verified paste with copy fallback.

Agents stays default-visible and removable. No agent runtime, background discovery polling, microphone or screen capture before connection/use. Discovery is an explicit setup operation. Preserve Classic, motion defaults, existing music/notifications/gaming and normal dictation outside Agents. No cloud speech, built-in pseudo-agent, arbitrary batch execution, generalized GUI control, or broader provider permissions.

Vision chooses area, previously used external window, or explicit monitor. Retain per-connection reviewed attachment drafts across close/target switch; show source/time/provider and removable preview before Send. Capture occurs only on explicit action. Unsupported providers say unavailable. Temp images live only as needed by the owned run and are cleaned on every terminal path. Do not claim deletion from provider history.

Keep the existing bounded diagnostics separate. Show real progress events, not invented reasoning. In-memory transcript is bounded; new conversation clears it. Completion is silent by default, respects focus/fullscreen/game/tiny overlap and never interrupts an editor. Paste verifies captured window/process/foreground, offers Copy on ambiguity, and never presses Enter.
