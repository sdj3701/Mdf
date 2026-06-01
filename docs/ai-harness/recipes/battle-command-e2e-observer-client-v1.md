## battle-command-e2e-observer-client-v1: Keep battle command E2E client stable while host HumanBot drives progression

Status: active
Pinned: false
Category: battle
Created: 2026-05-06
Last used: 2026-06-01
Last verified: 2026-05-20
Use count: 7
Review after: 2026-08-04
Triggers: battle E2E reaches command execution on the host, but the client returns to `MatchingLobby` or the final snapshot is back in `Prepare`
Applies to: `tools/harness/mp/run_battle_spawn_monster_command.py`, `tools/harness/mp/run_magic_scroll_command.py`, `tools/harness/mp/run_human_bot_battle_progression.py`, battle command snapshot checks
Verified by: see Verification section below; migrated from old Status: verified; artifacts/mp/20260514-035543-battle-spawn-monster-command; artifacts/mp/20260514-044344-battle-spawn-monster-command; artifacts/mp/20260514-051807-battle-spawn-monster-command; artifacts/mp/20260520-054126-battle-spawn-monster-command
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
Battle command tests need a connected client snapshot to prove replication. Running an unbounded HumanBot on both build peers can make the client repeatedly emit prepare decisions while scene/GameManagers are transitioning; a failed run showed host `BattleSpawnMonster` evidence advanced while the client returned to `MatchingLobby`, leaving `game_managers_or_command_processor_missing` bot logs and no comparable snapshot. Also, final snapshots can legitimately be in a later `Prepare` phase after battle commands already executed, so requiring the final state to still be `Battle1`/`Battle2` loses valid evidence.

Recipe:
- Default battle command E2E to a host HumanBot driver plus a connected build client observer. Enable `--client-human-bot` only for cases that specifically validate both peer emitters.
- Pause bots before the pre-battle checkpoint, then start the host bot after host/client Game snapshots compare cleanly.
- Use `--bot-prepare-mode augment-only` for battle command E2E. This allows the bot to take a first augment but avoids repeated maze `PlaceWall` planning, which can block the host simulation long enough for the observer client to hit Fusion `Timeout`.
- Treat battle phase as reached if both peers were observed in `Battle1`/`Battle2` during polling, or if replicated battle command counters advanced and snapshot comparison succeeded.
- Keep the success sample strict: host and client command counters must match, monster/scroll semantic hashes must be present when required, and `compare_state_snapshots.py` must pass.
- Poll battle command snapshots frequently enough to catch short-lived battle monsters. Save the first strict host/client live monster semantic match as `snapshots/build-host-spawn-semantic.json`, `snapshots/build-client-spawn-semantic.json`, and `spawn-semantic-comparison.json`; the final snapshot may be later in the battle after the spawned monster has died or reached the goal.
- Build augment snapshot comparisons from State Authority published presented/selected augment snapshots before local UI caches. Non-authority local presented augment lists can remain populated after a selected augment is already replicated, and should not become the durable comparison source.
- For a dedicated magic-scroll command E2E, use the test-only HumanBot option `--mpBotPreferScrollAugment` / `preferScrollAugment=true` so the prepare policy picks a `GrantMagicScroll` augment when one is offered. This creates deterministic scroll inventory evidence without adding production grant hooks.
- Save `battle-command-evidence-latest.json`, `battle-start-comparison.json` when observed, and final `battle-command-evidence.json`; use the final evidence file for PASS/FAIL.
- In `--mpTest` player builds, disable stack traces for `Log` and `Warning` via `Application.SetStackTraceLogType`. Long battle command runs emit many MPTEST and gameplay diagnostics; full stack traces on every log can make Player.log explode and contribute to Fusion `Timeout` disconnects.
- Launch build peers with explicit per-peer `-logFile <artifact>/<peer>.Player.log` so host/client disconnect reasons are not interleaved in the shared Unity Player.log.

Verification:
- Initial failure artifact `artifacts/mp/20260506-075006-battle-spawn-monster-command` showed host `spawnMonsterSeq=86`, client `scene=MatchingLobby`, and repeated client `game_managers_or_command_processor_missing`.
- Follow-up failure artifact `artifacts/mp/20260506-080146-battle-spawn-monster-command` showed client fallback from `OnDisconnectedFromServer:Timeout`; the shared Player.log contained stack traces for routine `[MPTEST]`/gameplay logs.
- Follow-up artifact `artifacts/mp/20260506-081229-battle-spawn-monster-command` used peer-specific Player logs and showed host tick stuck at `349` while wall planning ran between prepare decisions, followed by client `OnDisconnectedFromServer:Timeout`.
- Final Phase 11 build `artifacts/builds/20260506-084532/MDF-MPTest.exe` passed launch smoke.
- `artifacts/mp/20260506-084714-battle-spawn-monster-command` passed with `acceptedBattleCommandSeq=1`, `spawnMonsterSeq=1`, matching host/client command counters, `spawnSemanticsObserved=true`, and `spawn-semantic-comparison.json` success.
- `artifacts/mp/20260506-084621-magic-scroll-command` passed with `acceptedBattleCommandSeq=2`, `spawnMonsterSeq=1`, `useMagicScrollSeq=1`, matching host/client command counters, `scroll_accepted`, `scroll_effect_applied`, and client `scroll_presentation` timeline entries. The bot selected `Aug_Scroll_Heal` with `preferScrollAugment=true`.
- `artifacts/mp/20260506-084753-human-bot-battle-progression` passed with `acceptedBattleCommandSeq=2`, `spawnMonsterSeq=2`, matching host/client command counters, and no failures.

Lifecycle notes:
- 2026-05-14: Observer client battle command E2E stayed synchronized; spawn semantics observed on host and client.
- 2026-05-14: verified artifact `artifacts/mp/20260514-035543-battle-spawn-monster-command`
- 2026-05-14: Observer client stayed synchronized after whole-ground battle spawn-zone validation change; host/client command counters matched and spawn semantics were observed.
- 2026-05-14: verified artifact `artifacts/mp/20260514-044344-battle-spawn-monster-command`
- 2026-05-14: Observer client battle command E2E stayed synchronized after explicit card selection/map touch split.
- 2026-05-14: verified artifact `artifacts/mp/20260514-051807-battle-spawn-monster-command`
- 2026-05-20: verified artifact `artifacts/mp/20260520-054126-battle-spawn-monster-command`
