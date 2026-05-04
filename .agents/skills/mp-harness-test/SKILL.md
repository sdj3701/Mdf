---
name: mp-harness-test
description: Run or design MDF multiplayer harness matrix tests and artifact collection.
---

# MP harness test

Read `docs/ai-harness/mp-test-protocol.md` and `state-snapshot-schema.md`.

Required loop:

1. Build Development player.
2. Launch host/client matrix.
3. Wait for automation ping/session/scene/player count.
4. Run scenario command if safe.
5. Dump snapshots.
6. Compare comparable fields.
7. Capture logs/screenshots/artifacts.
8. Cleanup.

Do not claim PASS without artifacts.
