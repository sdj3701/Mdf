## battle2-camera-transition-e2e-v1: Inspect automatic Battle2 camera state without navigating it

Status: active
Pinned: false
Category: multiplayer, camera, battle
Created: 2026-07-14
Last used: 2026-07-14
Last verified: 2026-07-14
Use count: 3
Review after: 2026-08-11
Triggers: Battle1 to Battle2 camera stays on own field, attack UI opens over defender field, overlapping camera transition drops a request
Applies to: `CameraManager`, `GameManagers.StateTransition`, `MPTestAutomationServer`, `run_human_bot_3round_progression.py`, `long_progression_common.py`
Verified by: `artifacts/mp/20260714-043422-human-bot-3round-progression/camera-checkpoints/003-r01-battle2.json`; E:/UnityProjects/mdf/artifacts/mp/20260714-151104-final-lobby-cadence/20260714-061104-human-bot-3round-progression
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active harness references it

Problem:
Battle1 cleanup can begin an asynchronous return-to-own-field transition immediately before Battle2 assigns the same peer as attacker. A guard that drops camera requests while a transition is active leaves the attack UI open over the peer's own field. A test that requests navigation before checking the camera hides this race.

Recipe:
- Run `run_human_bot_3round_progression.py` with `--completion-mode battle2-reached --verify-battle2-camera` and a fresh graphical Development player.
- At the stable Battle2 checkpoint, derive the expected view from the snapshot's single `isLocal=true` player. An attacker must view the other player's durable `playerId`; a defender must view its own field.
- Call `view_player_field` only with `requestNavigation=false`. Require matching `ownPlayerId`, `viewingPlayerId`, and `requestedPlayerId`, plus `currentViewingMatchesRegistry=true`, `targetOnCurrentRunner=true`, `transitioning=false`, and the expected `attackMode`.
- Keep peer JSON and aggregate camera checkpoint JSON under `camera-checkpoints/`. Camera errors must fail the enclosing progression checkpoint.
- For the original host-local race, confirm from snapshots that the build host was the Battle1 defender and became the Battle2 attacker. Seeds label the run but do not guarantee which player attacks first.
- Require `cleanupStatus=PASS` and `orphanedPids=[]` before reporting E2E PASS.

Verification:
- `artifacts/mp/20260714-043422-human-bot-3round-progression` reached Battle2 with `firstAttackerPlayerId=1`, so build host player 0 reproduced the defender-to-attacker boundary.
- The read-only host probe reported `ownPlayerId=0`, `viewingPlayerId=1`, `attackMode=true`, and `transitioning=false`; the client defender reported its own field and `attackMode=false`.
- The run completed with `success=true`, `cleanupStatus=PASS`, and `orphanedPids=[]`.

Pitfalls:
- Never set `requestNavigation=true` in the assertion; that actively repairs the state being tested.
- Do not assume a seed fixes the first attacker. Gate the exact regression claim on the captured Battle1/Battle2 roles.
- Poll briefly for the camera tween to settle, but keep the phase checkpoint stable so a later state cannot be mistaken for success.

Lifecycle notes:
- 2026-07-14: Added opt-in read-only Battle2 camera assertions and verified the exact host defender-to-attacker race.
- 2026-07-14: verified artifact `artifacts/mp/20260714-043422-human-bot-3round-progression/camera-checkpoints/003-r01-battle2.json`
- 2026-07-14: verified artifact `E:/UnityProjects/mdf/artifacts/mp/20260714-151104-final-lobby-cadence/20260714-061104-human-bot-3round-progression`
