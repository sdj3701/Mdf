## direct-singleplayer-battle-precheck-v1: Direct 03_Game singleplayer battle precheck

Status: active
Pinned: false
Category: unity-cli, battle, host-migration
Created: 2026-05-16
Last used: 2026-05-16
Last verified: 2026-05-16
Use count: 1
Review after: 2026-08-14
Triggers: 03_Game direct play, singleplayer timer loops, Prepare -> Battle1 blocked, hostMigrationHandler=null, aiTakeoverReady=false
Applies to: `Mdfproject/Assets/Scripts/Managers/GameManagers.MigrationRecovery.cs`, `Mdfproject/Assets/Scripts/Game/GameSceneInitializer.cs`
Verified by: artifacts/direct-single-battle-transition-summary-latest.log
Replacement: none
Archive policy: archive when unused for 180 days and not pinned/protected

Problem:
Opening `03_Game` directly in the Unity Editor starts a local `GameMode.Single` runner from `GameSceneInitializer`. In that path there is no `NetworkManager` and no `HostMigrationHandler`. Battle start prechecks must not wait forever on Host Migration AI takeover readiness for this direct singleplayer runner, but multiplayer/Host Migration paths must still require the handler.

Recipe:
- Reproduce with the active scene set to `Assets/Scenes/03_Game.unity`, clear console, enter Play Mode, and poll `mp_dump_state`.
- Failure signature: repeated `Sequence transition started: Prepare -> Battle1`, followed by `BattleStartPrecheck failed ... aiTakeoverReady=False ... aiReason=hostMigrationHandler=null`, then transition ends back in `Prepare`.
- Correct guard: allow missing `HostMigrationHandler` only when `NetworkManager.Instance == null && Runner.GameMode == GameMode.Single`. Do not make host/client or Host Migration paths pass with a missing handler.
- Evidence should include a natural `Prepare -> Battle1` snapshot or summary and a console check without the repeated BattleStartPrecheck failure.

Lifecycle notes:
- 2026-05-16: Direct 03_Game GameMode.Single has no NetworkManager/HostMigrationHandler, so battle precheck must allow the no-handler path only for direct singleplayer; verified natural Prepare->Battle1 transition.
- 2026-05-16: verified artifact `artifacts/direct-single-battle-transition-summary-latest.log`
