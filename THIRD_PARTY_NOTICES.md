# Optional speech components

DeskPill's dictation capture, interface, shortcut handling and isolated worker are part of this repository. Offline recognition optionally downloads Vosk 0.3.45 for Windows x64 and vosk-model-small-en-us-0.15 from their publishers. These are not included in the main portable ZIP. The publisher archives are installed under the current user's DeskPill data folder.

Vosk API: Copyright 2020 Alpha Cephei Inc., licensed under Apache License 2.0. See https://github.com/alphacep/vosk-api and https://github.com/alphacep/vosk-api/blob/master/COPYING. The runtime archive includes GCC runtime libraries, which are covered by the GNU GPL with the GCC Runtime Library Exception: https://www.gnu.org/licenses/gcc-exception-3.1.html. Upstream source and build information are available in the Vosk repository.

English model: vosk-model-small-en-us-0.15, listed under Apache License 2.0 by the publisher: https://alphacephei.com/vosk/models.

Apache License 2.0: https://www.apache.org/licenses/LICENSE-2.0

Downloads are version-pinned and checked against SHA-256 digests before extraction. Recognition runs locally; audio is sent only to DeskPill's own worker over an anonymous process pipe, not to a network service.
