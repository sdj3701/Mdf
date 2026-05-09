---
name: build-player
description: Build MDF Development player for multiplayer E2E harness.
---

# Build player

Use `mp_build_player` once implemented. Until then use Editor build automation through `unity-cli exec` or `-executeMethod`.

Rules:

- Development Build for automation server.
- Include `Title`, `MatchingLobby`, `JoinLobby`, `Game` scenes.
- Store evidence builds under `artifacts/builds/<timestamp>/`.
- For repeated local visual MP checks on Windows, a stable ignored output dir such as `artifacts/builds/mptest-current` may be used with `tools/harness/mp/configure_windows_firewall.py` so Defender Firewall does not prompt for every timestamped exe path.
- Capture build log and build path.
- Never include test automation in non-development builds.
