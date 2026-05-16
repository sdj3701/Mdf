## field-roster-authority-reconcile-v1: Treat battle field roster as authority-owned state

Status: active
Pinned: true
Category: multiplayer
Created: 2026-05-08
Last used: 2026-05-16
Last verified: 2026-05-16
Use count: 18
Review after: 2026-08-06
Triggers: placedUnitsHash mismatch, unit combine/despawn, late MoveUnit, battle transition snapshot mismatch, host placed unit missing on client, `Networked properties can only be accessed when Spawned() has been called`
Applies to: `FieldManager`, `PlayerManager`, `GameManagers.StartBattleForPlayers`, `Unit`, `ManaController`, MP state snapshots
Verified by: `python tools/harness/precommit.py --all`; `unity-cli --project Mdfproject editor refresh --compile`; `unity-cli --project Mdfproject console --type error --stacktrace user`; `unity-cli --project Mdfproject test --mode EditMode`; `python tools/harness/mp/run_human_bot_3round_progression.py --seed 8101 --host-human-bot --bot-prepare-mode full --no-headless-player --player-path artifacts/builds/20260508-142926/MDF-MPTest.exe --cleanup-timeout-seconds 20 --orphan-threshold 0`; artifacts/mp/20260509-023108-2human-aifill-visible-3round; artifacts/mp/20260509-040924-2human-aifill-visible-3round-respawn-v6; artifacts/mp/20260509-045354-human-bot-3round-progression; artifacts/mp/20260509-051915-human-bot-3round-progression; artifacts/mp/20260509-100252-human-bot-3round-progression; artifacts\mp\20260509-223416-2human-aifill-visible-3round; artifacts/mp/20260510-103350-two-humanbot-two-ai-upgrade-check; artifacts/singleplayer-visual-melee-regression-latest.log; artifacts/mp/20260516-032200-matrix/20260516-032206-human-bot-battle-progression; artifacts/singleplayer-ai-field-battle-audit-latest.log
Replacement: none
Archive policy: keep active while Fusion unit registration, combine/despawn, Host Migration, or reconnect can rebuild field maps

Recipe:
- When any peer can receive `MoveUnit` before the spawned unit registration resolves, queue the network move briefly and replay it after `CreateUnitAt` or `RegisterUnitAt`. State Authority also needs this path because `BuyUnitCommand` can reserve a cell, return to the command loop, and receive a policy `MoveUnit` before async unit initialization has added `placedUnits`.
- When State Authority combines or replaces units, send explicit unregister RPCs for consumed NetworkObjects and then broadcast an authoritative roster.
- Do not treat retired NetworkIds as permanently invalid. If State Authority spawns or rosters the same raw ID again, clear the retired marker so Fusion NetworkId reuse cannot hide a newly bought unit.
- Before battle starts, rebroadcast each authority-owned field roster so battle snapshots do not observe a partially registered client field.
- Keep large roster broadcasts compact. Fusion RPC payload capacity can fail when a full roster ships IDs, grid positions, stars, and string data together; send compact authoritative identity/position/star data and rely on per-unit registration metadata for names or data keys.
- Dead units should remain network-active. Hide death presentation through renderers, colliders, and canvases, then respawn during the next Prepare transition; disabling the NetworkObject root can break later networked property reads and roster rebuilds.
- Keep snapshot `manualSkillReadyHash` to stable configured skill capability; current mana, target availability, casting state, and other frame-local readiness values are diagnostics, not durable peer-equality fields.
- Components that may initialize before Fusion calls `Spawned()` must not expose or mutate `[Networked]` fields directly through public properties or initialization methods. Keep local mirrors for pre-spawn/offline values, guard network reads with `Object.IsValid && Runner.IsRunning`, and let State Authority copy the pending local value into Networked state inside `Spawned()`.

Pitfalls:
- Rebuilding `placedUnits` from live scene objects during normal phase transitions can surface objects that were not yet registered on every peer. Follow with authority roster reconciliation before claiming a battle checkpoint PASS.
- A passing final Prepare snapshot does not prove earlier Battle snapshots were synchronized; check every checkpoint comparison.

Lifecycle notes:
- 2026-05-09: 2-human + 2 AI fill visual MP run reached round 3 with host/client snapshot match; same-cell roster no-op used to reduce placement flicker.
- 2026-05-09: verified artifact `artifacts/mp/20260509-023108-2human-aifill-visible-3round`
- 2026-05-09: verified round-3 Prepare respawn counts in `artifacts/mp/20260509-040924-2human-aifill-visible-3round-respawn-v6`; all four players had `deadUnitCount=0` and host/client snapshots matched.
- 2026-05-09: verified `Unit` berserk stat writes and `ManaController` pre-spawn local mirrors after graphical 2p 3-round progression passed with 10/10 checkpoint comparisons in `artifacts/mp/20260509-045354-human-bot-3round-progression`.
- 2026-05-09: verified pending purchased-unit move queue on State Authority and clients after graphical 2p 3-round progression passed with 10/10 checkpoint comparisons in `artifacts/mp/20260509-051915-human-bot-3round-progression`.
- 2026-05-09: Consulted for MoveUnit late registration and field roster ownership guard during HumanBot placement fix.
- 2026-05-09: Verified non-authority MoveUnit replay queues late spawned source units while authority keeps ownership/wall guards; visible 2P 3-round progression passed all 10 checkpoint comparisons.
- 2026-05-09: verified artifact `artifacts/mp/20260509-100252-human-bot-3round-progression`
- 2026-05-09: Consulted while extending purchased-unit pending cells into prepare placement policy candidates.
- 2026-05-10: verified artifact `artifacts\mp\20260509-223416-2human-aifill-visible-3round`
- 2026-05-10: Verified unit promotion/roster snapshot check in 2 HumanBot + 2 AI headless run on 2026-05-10; promotions observed, no same data/star count >=3, final host/client comparison passed.
- 2026-05-10: verified artifact `artifacts/mp/20260510-103350-two-humanbot-two-ai-upgrade-check`
- 2026-05-16: verified artifact `artifacts/singleplayer-visual-melee-regression-latest.log`
- 2026-05-16: verified artifact `artifacts/mp/20260516-032200-matrix/20260516-032206-human-bot-battle-progression`
- 2026-05-16: verified artifact `artifacts/singleplayer-ai-field-battle-audit-latest.log`
