# Optional English dictation

DeskPill owns the capture, shortcut handling, listening animation, review and paste behavior. Recognition uses the Vosk C API through DeskPill's own small x64 worker. It requires no separate dictation application or speech account.

## Resource and privacy behavior

- Off by default. The low-level Win+Space listener is installed only after the user enables dictation and saves; disabling unhooks it and cancels an active session.
- While enabled, Win+Space replaces Windows' language-switch chord. Modified chords pass through. Hold both keys to record; releasing either finishes exactly once. Repeated keydown events never restart capture, and the trailing Space release is swallowed even if Win is released first.
- Microphone capture starts on demand, uses mono PCM at 16 kHz, and is bounded to 90 seconds. Esc discards the session.
- A microphone icon and scrolling level history live inside the notch. The waveform eases toward actual microphone input using elapsed time, not frame counts. Transcription has a distinct quiet processing motion. Reduced motion stops the continuous animation while still displaying current input level. Rendering detaches when the view is hidden or removed. No separate listening or review window opens.
- Speech models load only after recording, in `DeskPill.Speech.exe`. Cancellation kills that worker; ordinary completion waits for it to exit. There is no model preload at startup or while idle.
- Audio is kept in bounded memory and passed to the worker over stdin. DeskPill creates no recording file or transcript history. Automatic insertion uses the captured window/process guards. If foreground changes, the transcript opens for review. Clipboard persistence follows the user's existing clipboard preference.
- Setup downloads a pinned runtime and English model from their publishers, checks SHA-256, bounds extracted sizes and paths, and installs into the current user's DeskPill data folder. A failed download does not replace the working pack. The network is not used for recognition.

## Coverage and limits

Deterministic tests cover PCM/WAV bounds, shortcut press/release/repeat behavior, opt-in defaults, early cancellation and checksum mismatch. UI checks cover staged settings, inline listening/processing/review, a release during microphone preparation, reduced-motion input levels and dismissal. The optional generated-English fixture exercises the actual shipped worker and model without opening a microphone or changing clipboard contents.

The English fixture passed locally. Live microphone capture, actual physical Win+Space interception, accent coverage and long sessions still need interactive user testing. A small English model trades accuracy for size and can miss names, accents and punctuation. Elevated apps, secure desktops and Windows input restrictions can prevent insertion; the text remains available for manual use. Speech requires 64-bit Windows; the core app remains usable without the speech pack.
