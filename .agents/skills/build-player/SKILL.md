---
name: build-player
description: Build MDF Development player for multiplayer E2E harness.
---

# Build player

Use `mp_build_player` once implemented. Until then use Editor build automation through `unity-cli exec` or `-executeMethod`.

Rules:

- Development Build for automation server.
- Include `Title`, `MatchingLobby`, `JoinLobby`, `Game` scenes.
- Store build under `artifacts/builds/<timestamp>/`.
- Capture build log and build path.
- Never include test automation in non-development builds.
