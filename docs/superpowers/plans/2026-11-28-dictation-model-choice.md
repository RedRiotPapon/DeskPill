# Dictation model choice

User approved an optional Parakeet Unified English choice beside Vosk, with requirements beneath each choice and explicit user-controlled downloads.

- Preserve Vosk as default; store a normalized SpeechModel ID in Settings.
- Add SpeechModels catalog/readiness and separate pinned Parakeet assets. Show exact download sizes, recommended RAM/disk and CPU/GPU behavior without promising measured accuracy or GPU streaming.
- New original worker adapter calls the pinned transcribe.cpp 0.5.1 public C ABI in the existing separate speech worker. Bounded WAV on stdin, UTF-8 text on stdout, no disk audio. User additionally requested GPU: expose Auto/CPU/GPU (Vulkan), with honest driver/memory recommendations. Auto may retry CPU on GPU failure; forced GPU fails visibly. worker exits on completion/cancel. No Hex code/runtime dependency.
- Verify runtime archive and Q8 model SHA-256 before installation; extract bounded regular files only, retain existing packs through failure/cancel. No automatic downloads or model switch.
- Route ordinary and agent voice through a session snapshot of selected model; no change to hold/release or draft review.
- Test selection persistence, unknown-value repair, archive checks, UI staging and independent readiness. Test actual adapter with existing verified model and generated audio, without opening microphone.
- Update requirements/licensing/docs; build and run all suites, push after checks, CI and release.

Verification: all local suites passed; generated English fixture passed Vosk, Parakeet CPU, Vulkan on RTX 3060, CPU-only Auto and forced-GPU rejection. Publisher archive passed the same bounded extractor used by installation. Independent code review found no actionable blockers. Auto retries nonzero worker exits, not timeouts. Live microphone and cross-hardware performance remain interactive validation limits.
