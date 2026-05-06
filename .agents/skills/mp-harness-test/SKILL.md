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

Battle command cases must additionally prove:

- `BattleSpawnMonsterCommand`, `UseMagicScrollCommand`, and optional `ActivateSkillCommand` acceptance/execution with `[MPTEST]` command logs.
- same-player replicated `attackMonsterPoolHash`, `ownedScrollsHash`, battle command sequence counters, monster semantic hashes, and active effect hashes match across peers.
- HumanBot, when used, remains `isAI=false` and `ai.controllerRegistered=false`.
- reconnect/disconnect/Host Migration cases compare a synchronized checkpoint; do not weaken post-checkpoint mismatches as random.

Do not claim PASS without artifacts.
