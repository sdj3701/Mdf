## fusion-duplicate-startgame-guard-v1: Guard duplicate NetworkRunner StartGame requests

Status: active
Pinned: false
Category: multiplayer, networking
Created: 2026-06-01
Last used: 2026-06-01
Last verified: 2026-06-01
Use count: 1
Review after: 2026-08-30
Triggers: ServerAlreadyInRoom, duplicate StartGame, CreateRoom double click, JoinRoom double click, NetworkManager StartGame, Fusion Code 104
Applies to: `NetworkManager.StartGame`, MatchingLobby room create/join UI, MP automation start peer flow
Verified by: `python tools/harness/precommit.py --all`; `unity-cli --project Mdfproject status`; `unity-cli --project Mdfproject editor refresh --compile`; `unity-cli --project Mdfproject console --type error --stacktrace user`; `unity-cli --project Mdfproject test --mode EditMode --filter MPTestHarnessEditModeTests`; `python tools/harness/mp/build_player.py --output-dir artifacts/builds/mptest-current --cleanup-timeout-seconds 20 --orphan-threshold 0`; artifacts/mp/20260601-040245-duplicate-start-host-guard/result.json
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
Photon/Fusion can disconnect with Code 104 `ServerAlreadyInRoom` when the same `NetworkRunner` receives a second `StartGame` request while the first room create/join is still in flight. MDF can hit this from rapid UI clicks or automation retries because `NetworkManager.State` remains `InLobby` until the runner finishes entering the session.

Recipe:
- Add an explicit `_startGameInProgress` guard around `NetworkManager.StartGame`.
- Arm the guard before awaiting `_runner.StartGame`.
- Clear the guard on failed `StartGame`, exception, `OnPlayerJoined`, normal `OnShutdown`, connection-loss fallback, and manual leave.
- Keep the network UI block active while the start request is in flight so MatchingLobby controls do not keep accepting room actions.
- Prove the fix with a targeted headless build run that sends repeated `/startHost` requests and scans the Player log for `ServerAlreadyInRoom`.

Verification:
- On 2026-06-01, `artifacts/mp/20260601-040245-duplicate-start-host-guard/result.json` reported `success=true`, `serverAlreadyInRoomLogged=false`, `duplicateGuardLogged=true`, runner `isRunning=true`, mode `Host`, scene `02_JoinLobby`, `cleanupStatus=PASS`, and `orphanedPids=[]`.

Pitfalls:
- Checking only `ConnectionState.InLobby` is not enough because that state remains true while `StartGame` is still awaiting Fusion.
- Do not reset the guard immediately after a successful `StartGame` result if UI can still send actions before `OnPlayerJoined` or the scene transition completes.

Lifecycle notes:
- 2026-06-01: Captured user-reported Fusion Code 104 `ServerAlreadyInRoom` log and verified duplicate-start guard with repeated `/startHost` calls.
