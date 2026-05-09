## human-bot-path-aware-placement-wall-sync-v1: Keep HumanBot placement and wall snapshots path-aware

Status: active
Pinned: false
Category: AI/HumanBot, multiplayer snapshot
Created: 2026-05-08
Last used: 2026-05-09
Last verified: 2026-05-09
Use count: 7
Review after: 2026-08-06
Triggers: host HumanBot moves only near buy slots, repeated same-round `MoveUnit` oscillation, no host `PlaceWall` before R3, `pathAwarePlacement=false`, `monsterPathCount=0`, or `player.*.field.wallHash` snapshot mismatch after `PlaceWall`
Applies to: `PrepareDecisionPolicy`, `AttackRangeCoverageConsideration`, `FieldManager` wall hashes, `run_human_bot_3round_progression.py`
Verified by: `python tools/harness/precommit.py --all`; `unity-cli --project Mdfproject editor refresh --compile`; `unity-cli --project Mdfproject console --type error --stacktrace user`; `unity-cli --project Mdfproject test --mode EditMode`; `python tools/harness/mp/run_human_bot_3round_progression.py --seed 8101 --host-human-bot --bot-prepare-mode full --headless-player --player-path artifacts/builds/20260508-145605/MDF-MPTest.exe --cleanup-timeout-seconds 20 --orphan-threshold 0`; `python tools/harness/mp/run_human_bot_3round_progression.py --seed 8101 --host-human-bot --bot-prepare-mode full --no-headless-player --player-path artifacts/builds/20260508-151911/MDF-MPTest.exe --cleanup-timeout-seconds 20 --orphan-threshold 0`; artifacts/mp/20260508-154650-human-bot-3round-progression; `python tools/harness/mp/run_human_bot_3round_progression.py --seed 8101 --host-human-bot --bot-prepare-mode full --no-headless-player --player-path artifacts/builds/20260508-161632/MDF-MPTest.exe --cleanup-timeout-seconds 20 --orphan-threshold 0`; artifacts/mp/20260508-161704-human-bot-3round-progression; artifacts/mp/20260508-215126-human-bot-3round-progression; artifacts/mp/20260509-002238-human-bot-3round-progression
Replacement: none
Archive policy: archive only after HumanBot prepare placement no longer uses `FieldManager.FindBestSpotForAI` or dynamic wall cell state is authoritative network state instead of scene-discovered wall maps.

Recipe:
- HumanBot prepare moves must pass monster path context into `FindBestSpotForAI`; before the maze has a single open entry, build path context from all `GetOpenBorderGaps()` starts, not only `TryGetSingleOpenEntryNavigationCell()`.
- Do not let HumanBot repositioning starve wall control. Once the bot has a minimum combat core, prioritize gap-closing `PlaceWall` decisions while `GetOpenBorderGaps().Count > 1` and wall stock is above reserve; after the border is controlled, require the stronger `TargetTotalUnits` / `CompositionDistanceToTarget <= 2` gate for extra maze walls.
- Do not stop wall work immediately after the three border gaps are sealed. If the bot has the minimum combat core and wall stock above the build reserve, keep placing the persistent interior maze blueprint in the same prepare phase.
- Keep at least one wall in reserve for repairs, but allow a previously built blueprint cell to be repaired with that reserve. Pending wall suppression should block only same-round duplicate `PlaceWall`, not the next round after a destroyer breaks the wall.
- If a bought unit occupies a planned border-gap wall cell, move that unit off the blueprint before building interior maze walls. Do not skip to interior walls while a border-gap closure from the persistent plan is blocked by a unit.
- Remember per-unit move decisions, pending move target cells, pending wall candidates, and pending shop slots for the current round so one unit does not bounce between equivalent good cells, multiple units do not reserve the same target cell, and a client does not flood `BuyUnit` or `PlaceWall` while authority replication is still catching up.
- Last-open-entry checks must account for pending border wall candidates as well as already replicated walls. Otherwise a client can queue the final border gap before its local wall map sees the earlier three gap closures.
- Ranged/high-ground scoring should cover monster path tiles when path context exists, keep only the strong path-coverage tier, and use field-center preference as a tie-break so ranged units do not drift to corners while equivalent central cells can hit the route.
- Maze wall plans should prune blueprint walls whose removal does not shorten the final monster path. Zero-gain setup walls are only useful if the final combined blueprint proves they contribute to the longer path.
- A dynamic network wall snapshot can mismatch when the client scene has the wall object but `FieldManager.placedWalls` is stale. Rebuild wall maps before `BuildWallCellHash()` and ranged placement tile lookup.
- During wall map rebuild, skip inactive `DestructibleWall` objects so destroyed walls do not remain in snapshot hashes. Do not require `CurrentHealth > 0` before `RebindAfterMigration()`, because newly replicated client walls can still have default health before rebinding.
- Bot journal fields `pathAwarePlacement=true` and `monsterPathCount>0` are quick proof that the placement path is active; snapshot proof still requires passing checkpoint comparison with `cleanupStatus=PASS` and `orphanedPids=[]`.
- If E2E shows repeated client `PlaceWall` attempts to the same cell followed by authority `wall_position_occupied`, inspect client wall map refresh before weakening snapshot comparison.
- Long progression PASS must require both host and client completion snapshots. A run where host reaches `target_round_complete` but the client is still behind, disconnected, or back in lobby is a FAIL even if host-side completion is true.
- Zone effects are battle-only transient state. If prepare checkpoints fail on `effects.zoneCount` after battle, first verify `ZoneController` destroys itself on non-battle `GameState` transitions before weakening snapshot comparison.

Lifecycle notes:
- 2026-05-09: Added pending buy-slot and move-target suppression, pending-aware last-gap checks, and peer completion gating after graphical 2p E2E exposed client BuyUnit flood and host-only false completion.
- 2026-05-09: verified artifact `artifacts/mp/20260508-161704-human-bot-3round-progression`
- 2026-05-09: Verified host R2 wall control, per-round move suppression, path-aware placement, and snapshot-clean 2p graphical 3-round progression.
- 2026-05-09: verified artifact `artifacts/mp/20260508-154650-human-bot-3round-progression`
- 2026-05-09: Verified persistent maze blueprint, first-round three-gap closure on host/client, and unit unblocking of planned border-gap cells in 2p 3-round HumanBot progression.
- 2026-05-09: verified artifact `artifacts/mp/20260508-215126-human-bot-3round-progression`
- 2026-05-09: Used while fixing first-round host maze priority and next-round wall repair; E2E reruns were cleanup-clean but blocked by client timeout/lobby fallback, so Last verified was not advanced.
- 2026-05-09: Verified redundant maze-wall pruning plus ranged path-cover/center placement priority in 2p 3-round HumanBot progression.
- 2026-05-09: verified artifact `artifacts/mp/20260509-002238-human-bot-3round-progression`
