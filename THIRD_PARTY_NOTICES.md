# Optional speech components

DeskPill's dictation capture, interface, shortcut handling and isolated worker are part of this repository. Offline recognition optionally downloads Vosk 0.3.45 for Windows x64 and vosk-model-small-en-us-0.15 from their publishers. These are not included in the main portable ZIP. The publisher archives are installed under the current user's DeskPill data folder.

Vosk API: Copyright 2020 Alpha Cephei Inc., licensed under Apache License 2.0. See https://github.com/alphacep/vosk-api and https://github.com/alphacep/vosk-api/blob/master/COPYING. The runtime archive includes GCC runtime libraries, which are covered by the GNU GPL with the GCC Runtime Library Exception: https://www.gnu.org/licenses/gcc-exception-3.1.html. Upstream source and build information are available in the Vosk repository.

English model: vosk-model-small-en-us-0.15, listed under Apache License 2.0 by the publisher: https://alphacephei.com/vosk/models.

Apache License 2.0: https://www.apache.org/licenses/LICENSE-2.0

Downloads are version-pinned and checked against SHA-256 digests before extraction. Recognition runs locally; audio is sent only to DeskPill's own worker over an anonymous process pipe, not to a network service.

## Optional FPS component

PresentMon 2.6.0 is downloaded from Intel GameTechDev’s official GitHub release only when the user installs the FPS component. It is checked against a pinned SHA-256 digest. PresentMon is MIT licensed, Copyright (C) 2017–2024 Intel Corporation. Its full license is installed beside the downloaded executable. Source and license: https://github.com/GameTechDev/PresentMon and https://github.com/GameTechDev/PresentMon/blob/main/LICENSE.txt.

## Optional Parakeet speech pack

Parakeet Unified English 0.6B by NVIDIA, converted/quantized to GGUF Q8_0 by handy-computer. The pinned model revision `7e948f21b7bdbac698d3318db9d350f1096f3b6c` identifies its license as Creative Commons Attribution 4.0. Source and model card: https://huggingface.co/handy-computer/parakeet-unified-en-0.6b-gguf/tree/7e948f21b7bdbac698d3318db9d350f1096f3b6c . Upstream model: https://huggingface.co/nvidia/parakeet-unified-en-0.6b . License: https://creativecommons.org/licenses/by/4.0/ . Conversion repackages and quantizes upstream weights; DeskPill has not retrained them.

The optional native runtime is transcribe.cpp 0.5.1, MIT licensed, Copyright (c) 2026 The transcribe.cpp authors. Source: https://github.com/handy-computer/transcribe.cpp/tree/v0.5.1 . Its publisher archive includes the runtime and dependency licenses, which are retained during extraction. The model and runtime are downloaded only on request, with pinned sizes and SHA-256 checks; neither is included in the portable ZIP. DeskPill's C# adapter is original code and has no Hex dependency.
