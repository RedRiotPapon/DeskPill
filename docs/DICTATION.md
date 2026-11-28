# Optional English dictation

DeskPill owns the capture, shortcut handling, listening animation, review and paste behavior. Recognition uses your choice of Vosk Small English or Parakeet Unified English through DeskPill's own x64 worker. It requires no separate dictation application or speech account.

On a new profile’s first launch, DeskPill offers **Set up dictation** or **Continue without dictation**. Setup opens Settings → Dictation for model selection and an explicit download. Neither choice starts a download, enables the shortcut or opens the microphone. Skipping, Esc or closing the welcome screen remembers the choice; Settings → Dictation remains available later. Existing profiles are not interrupted when updating. A failed save keeps the welcome screen open with a retry message.

## Resource and privacy behavior

- Off by default. The low-level Ctrl+Space listener is installed only after the user enables dictation and saves; disabling unhooks it and cancels an active session.
- While enabled, Ctrl+Space is reserved for dictation and can override the foreground app’s shortcut. Win+Space remains available for Windows language switching. Modified chords pass through. Hold both keys to record; releasing either finishes exactly once. Repeated keydown events never restart capture, and the trailing Space release is swallowed even if Ctrl is released first.
- Microphone capture starts on demand, uses mono PCM at 16 kHz, and is bounded to 90 seconds. Esc discards the session.
- A microphone icon and scrolling level history live inside the notch. The waveform eases toward actual microphone input using elapsed time, not frame counts. Transcription has a distinct quiet processing motion. Reduced motion stops the continuous animation while still displaying current input level. Rendering detaches when the view is hidden or removed. No separate listening or review window opens.
- Speech models load only after recording, in `DeskPill.Speech.exe`. Cancellation kills that worker; ordinary completion waits for it to exit. There is no model preload at startup or while idle.
- Audio is kept in bounded memory and passed to the worker over stdin. DeskPill creates no recording file or transcript history. Automatic insertion uses the captured window/process guards. If foreground changes, the transcript opens for review. Clipboard persistence follows the user's existing clipboard preference.
- Setup downloads a pinned runtime and English model from their publishers, checks SHA-256, bounds extracted sizes and paths, and installs into the current user's DeskPill data folder. A failed download does not replace the working pack. The network is not used for recognition.

## Coverage and limits

Deterministic tests cover PCM/WAV bounds, shortcut press/release/repeat behavior, opt-in defaults, early cancellation and checksum mismatch. UI checks cover staged settings, inline listening/processing/review, a release during microphone preparation, reduced-motion input levels and dismissal. The optional generated-English fixture exercises the actual shipped worker and model without opening a microphone or changing clipboard contents.

The English fixture passed locally. Live microphone capture, actual physical Ctrl+Space interception, accent coverage and long sessions still need interactive user testing. The lightweight Vosk model trades accuracy for size and can miss names, accents and punctuation; both models need correction in some conditions. Elevated apps, secure desktops and Windows input restrictions can prevent insertion; the text remains available for manual use. Speech requires 64-bit Windows; the core app remains usable without the speech pack.

## Choose a model

Settings → Dictation has separate model cards. Choosing a radio button never downloads a pack. Each card has its own explicit Download/Repair button, status and requirements; Cancel stops an installation without switching the saved selection. Save applies the model and processing-device preference. The selected model/device is snapshotted at recording start for both ordinary dictation and agent voice drafts. Existing Vosk installations remain selected by default.

| | Vosk Small English | Parakeet Unified English Q8_0 |
| --- | --- | --- |
| Download | About 54 MB, including runtime | 731,357,568-byte model + 17,824,610-byte runtime, about 749 MB total |
| Recommended system RAM | 4 GB; publisher describes about 300 MB model memory | 8 GB; longer clips can require more memory |
| Recommended free disk | 300 MB | 2 GB for installation/repair |
| Windows | x64 | x64 Intel/AMD |
| Processing | CPU | Auto / CPU / GPU (Vulkan) |
| GPU requirement | None | Optional Vulkan-capable graphics and current driver; 2 GB free graphics memory recommended, not a hard minimum |
| Model behavior | Lightweight; punctuation/names can need correction | Cased and punctuated English; actual accuracy depends on speech |

RAM/disk values are user guidance rather than measured guarantees or model-enforced limits. A Microsoft Visual C++ 2015–2022 x64 runtime may be needed for Parakeet. Automatic uses Vulkan when available, CPU otherwise, and retries a worker that exits with an error once on CPU (timeouts still stop with an error). Forced GPU reports incompatibility instead of quietly switching. No device probing, model preload or GPU work runs while idle. This integration transcribes after release; it does not implement streaming during recording.

Parakeet uses the version-pinned transcribe.cpp 0.5.1 C ABI inside the separate speech worker. Its Q8 model is pinned to publisher revision `7e948f21b7bdbac698d3318db9d350f1096f3b6c`. A verified stage replaces the app-owned pack with rollback; regular-file archive extraction rejects path escapes, links, oversized expansion and truncation. Installed native and model attribution is retained. It has no Hex runtime dependency.

On the available PC, a generated 7.33-second English sample transcribed correctly on CPU and the RTX 3060 Vulkan backend without microphone or clipboard access. One CPU run took 1.35 seconds with about 1,090 MiB peak sampled working set; a later Automatic/Vulkan run took 0.87 seconds with 129 MiB sampled host working set (GPU memory not measured). First Vulkan use took 13.02 seconds. These short synthetic runs do not establish real-microphone accuracy, steady latency, or results on other PCs. Tests also exercised CPU-only Automatic and forced-GPU rejection with Vulkan unavailable. Existing Vosk synthetic-audio coverage remains separate.

Publisher references: [Vosk model catalog](https://alphacephei.com/vosk/models), [Parakeet pinned model card](https://huggingface.co/handy-computer/parakeet-unified-en-0.6b-gguf/blob/7e948f21b7bdbac698d3318db9d350f1096f3b6c/README.md), [transcribe.cpp release](https://github.com/handy-computer/transcribe.cpp/releases/tag/v0.5.1).

To reproduce the optional generated-audio check after downloading a pack: `.\scripts\test-parakeet-speech.ps1 -Backend GPU` (or CPU/Auto). The script uses installed assets and never downloads, records a microphone, changes settings or pastes text. `-Runtime` and `-Model` accept explicit asset paths; `-CpuOnly` restricts only the child worker for fallback checks.
