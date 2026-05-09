## field-roster-authority-reconcile-v1: Treat battle field roster as authority-owned state

Status: active
Pinned: true
Category: multiplayer
Created: 2026-05-08
Last used: 2026-05-08
Last verified: 2026-05-08
Use count: 1
Review after: 2026-08-06
Triggers: placedUnitsHash mismatch, unit combine/despawn, late MoveUnit, battle transition snapshot mismatch, host placed unit missing on client
Applies to: `FieldManager`, `PlayerManager`, `GameManagers.StartBattleForPlayers`, MP state snapshots
Verified by: `python tools/harness/precommit.py --all`; `unity-cli --project Mdfproject editor refresh --compile`; `unity-cli --project Mdfproject console --type error --stacktrace user`; `unity-cli --project Mdfproject test --mode EditMode`; `python tools/harness/mp/run_human_bot_3round_progression.py --seed 8101 --host-human-bot --bot-prepare-mode full --no-headless-player --player-path artifacts/builds/20260508-142926/MDF-MPTest.exe --cleanup-timeout-seconds 20 --orphan-threshold 0`
Replacement: none
Archive policy: keep active while Fusion unit registration, combine/despawn, Host Migration, or reconnect can rebuild field maps

Recipe:
- When a client can receive `MoveUnit` before the spawned unit registration resolves, queue the network move briefly and replay it after `RegisterUnitAt`.
- When State Authority combines or replaces units, send explicit unregister RPCs for consumed NetworkObjects and then broadcast an authoritative roster.
- Do not treat retired NetworkIds as permanently invalid. If State Authority spawns or rosters the same raw ID again, clear the retired marker so Fusion NetworkId reuse cannot hide a newly bought unit.
- Before battle starts, rebroadcast each authority-owned field roster so battle snapshots do not observe a partially registered client field.
- Keep snapshot `manualSkillReadyHash` to stable configured skill capability; current mana, target availability, casting state, and other frame-local readiness values are diagnostics, not durable peer-equality fields.

Pitfalls:
- Rebuilding `placedUnits` from live scene objects during normal phase transitions can surface objects that were not yet registered on every peer. Follow with authority roster reconciliation before claiming a battle checkpoint PASS.
- A passing final Prepare snapshot does not prove earlier Battle snapshots were synchronized; check every checkpoint comparison.
