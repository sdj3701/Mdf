## unity-cli-project-selector: Always target Mdfproject

Status: active
Pinned: false
Category: unity-cli
Created: 2026-05-05
Last used: 2026-06-11
Last verified: 2026-05-31
Use count: 102
Review after: 2026-08-03
Triggers: unity-cli, status, console, test, build
Applies to: MDF repo layout, unity-cli
Verified by: see Verification section below; migrated from old Status: verified-from-repo; Mdfproject; artifacts/singleplayer-breakwall-addressables-latest.log; artifacts/singleplayer-breakwall-addressables-latest.log; unity-cli --project Mdfproject status => ready; editor refresh --compile => Compilation complete; console errors => []; unity-cli --project Mdfproject status => ready; editor refresh --compile => Compilation complete; console --type error => []; unity-cli --project Mdfproject status => ready; editor refresh --compile => Compilation complete; console --type error => []; unity-cli --project Mdfproject status => ready; editor refresh --compile => Compilation complete; console --type error => []; unity-cli --project Mdfproject status => ready; editor refresh --compile => Compilation complete; console --type error => []; unity-cli --project Mdfproject status => ready; editor refresh --compile => Compilation complete; console --type error => []; unity-cli --project Mdfproject editor refresh --compile => Compilation complete; console --type error => []; unity-cli --project Mdfproject status => ready; editor refresh --compile => Compilation complete; console --type error => []; unity-cli --project Mdfproject editor refresh --compile => Compilation complete; console --type error => []; unity-cli --project Mdfproject status => ready; editor refresh --compile => Compilation complete; console --type error => []; unity-cli --project E:/UnityProjects/mdf/Mdfproject editor refresh --compile --force => Compilation complete; console --type error => []; unity-cli --project E:/UnityProjects/mdf/Mdfproject status => ready; editor refresh --compile --force => Compilation complete; console --type error => []; unity-cli --project E:/UnityProjects/mdf/Mdfproject status => ready; editor refresh --compile --force => Compilation complete; console --type error => []; unity-cli --project E:/UnityProjects/mdf/Mdfproject status => ready; editor refresh --compile --force => Compilation complete; console --type error => []; unity-cli --project E:/UnityProjects/mdf/Mdfproject status => ready; reserialize character prefabs/test scene; editor refresh --compile --force => Compilation complete; console --type error => []; unity-cli status ready; reserialize character prefabs/test scene; refresh --compile --force; console errors []; unity-cli --project E:/UnityProjects/mdf/Mdfproject status => ready; editor refresh --compile --force => Compilation complete; console --type error => []; unity-cli --project E:/UnityProjects/mdf/Mdfproject status => ready; reserialize test scene and UnitData assets; editor refresh --compile --force => Compilation complete; console errors []; unity-cli --project E:/UnityProjects/mdf/Mdfproject status => ready; editor refresh --compile --force => Compilation complete; console errors []; unity-cli --project E:/UnityProjects/mdf/Mdfproject editor refresh --compile --force => Compilation complete; console errors []; unity-cli --project E:/UnityProjects/mdf/Mdfproject status => ready; editor refresh --compile --force => Compilation complete; console errors []; unity-cli --project E:/UnityProjects/mdf/Mdfproject status => ready; reserialize Assets/Scenes/test.unity; editor refresh --compile --force => Compilation complete; console errors []; unity-cli --project E:/UnityProjects/mdf/Mdfproject status => ready; editor refresh --compile --force => Compilation complete; final console errors []; unity-cli --project E:/UnityProjects/mdf/Mdfproject status => ready; editor refresh --compile --force => Compilation complete; console errors []; unity-cli --project Mdfproject editor refresh --compile => Compilation complete; console --type error => []; unity-cli --project Mdfproject status => ready; reserialize Assassin UnitData/test scene; editor refresh --compile => Compilation complete; console --type error => []; unity-cli --project Mdfproject status; editor refresh --compile; console --type error --stacktrace user; EditMode 98/98; unity-cli --project Mdfproject status; editor refresh --compile; console --clear; console errors []; EditMode 98/98; unity-cli --project Mdfproject compile/console/EditMode 98/98 after projectile preview duplicate guard; unity-cli --project Mdfproject refresh/console/EditMode after projectile VFX renderer occlusion guard; unity-cli --project Mdfproject refresh/console/EditMode 98/98 after projectile VFX occlusion and sorting guard; unity-cli --project Mdfproject status ready; editor refresh --compile PASS; console errors []; unity-cli --project Mdfproject status ready; compile PASS; console []; unity-cli --project Mdfproject status/compile/console/EditMode 107 passed on 2026-05-31; unity-cli --project Mdfproject status/compile/console/EditMode 109 passed on 2026-05-31; unity-cli --project Mdfproject status/compile/console/EditMode 109 passed on 2026-05-31 pending-active-slot; artifacts/mp/20260531-044835-matrix/20260531-044847-network-budget-pending-stress/post-battle-host-migration-result.json; artifacts/mp/20260531-052919-matrix/20260531-052924-status-effect-host-migration/post-battle-host-migration-result.json; artifacts/mp/20260531-061353-matrix/20260531-061359-network-budget-pending-stress/post-battle-host-migration-result.json; artifacts/mp/20260531-063526-matrix/20260531-063541-network-budget-pending-stress/post-battle-host-migration-result.json; unity-cli --project Mdfproject status/compile/console/EditMode 110 passed on 2026-05-31
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
The repository root is not the Unity project root. The Unity project is under `Mdfproject`.

Recipe:
Use `unity-cli --project Mdfproject ...` or an absolute path to `Mdfproject` for all Editor automation.

Verification:
- `ProjectSettings/ProjectVersion.txt` exists under `Mdfproject`.
- `Packages/manifest.json` exists under `Mdfproject`.
- `unity-cli --project Mdfproject status` found the active Editor at `E:/UnityProjects/mdf/Mdfproject`.

Pitfalls:
- Running unity-cli from the repo root without `--project` may target the wrong Editor if multiple projects are open.

Lifecycle notes:
- 2026-05-10: Used for targeted UI compile/test verification with unity-cli --project Mdfproject.
- 2026-05-10: Verified by unity-cli --project Mdfproject status selecting E:/UnityProjects/mdf/Mdfproject on 2026-05-10.
- 2026-05-10: Verified unity-cli --project Mdfproject status selected E:/UnityProjects/mdf/Mdfproject on 2026-05-10.
- 2026-05-10: verified artifact `Mdfproject`
- 2026-05-16: verified artifact `artifacts/singleplayer-breakwall-addressables-latest.log`
- 2026-05-16: verified artifact `artifacts/singleplayer-breakwall-addressables-latest.log`
- 2026-05-18: verified artifact `unity-cli --project Mdfproject status => ready; editor refresh --compile => Compilation complete; console errors => []`
- 2026-05-18: verified artifact `unity-cli --project Mdfproject status => ready; editor refresh --compile => Compilation complete; console --type error => []`
- 2026-05-18: verified artifact `unity-cli --project Mdfproject status => ready; editor refresh --compile => Compilation complete; console --type error => []`
- 2026-05-18: verified artifact `unity-cli --project Mdfproject status => ready; editor refresh --compile => Compilation complete; console --type error => []`
- 2026-05-18: verified artifact `unity-cli --project Mdfproject status => ready; editor refresh --compile => Compilation complete; console --type error => []`
- 2026-05-18: verified artifact `unity-cli --project Mdfproject status => ready; editor refresh --compile => Compilation complete; console --type error => []`
- 2026-05-18: verified artifact `unity-cli --project Mdfproject editor refresh --compile => Compilation complete; console --type error => []`
- 2026-05-18: verified artifact `unity-cli --project Mdfproject status => ready; editor refresh --compile => Compilation complete; console --type error => []`
- 2026-05-18: verified artifact `unity-cli --project Mdfproject editor refresh --compile => Compilation complete; console --type error => []`
- 2026-05-18: verified artifact `unity-cli --project Mdfproject status => ready; editor refresh --compile => Compilation complete; console --type error => []`
- 2026-05-18: verified artifact `unity-cli --project E:/UnityProjects/mdf/Mdfproject editor refresh --compile --force => Compilation complete; console --type error => []`
- 2026-05-18: verified artifact `unity-cli --project E:/UnityProjects/mdf/Mdfproject status => ready; editor refresh --compile --force => Compilation complete; console --type error => []`
- 2026-05-18: verified artifact `unity-cli --project E:/UnityProjects/mdf/Mdfproject status => ready; editor refresh --compile --force => Compilation complete; console --type error => []`
- 2026-05-18: verified artifact `unity-cli --project E:/UnityProjects/mdf/Mdfproject status => ready; editor refresh --compile --force => Compilation complete; console --type error => []`
- 2026-05-18: verified artifact `unity-cli --project E:/UnityProjects/mdf/Mdfproject status => ready; reserialize character prefabs/test scene; editor refresh --compile --force => Compilation complete; console --type error => []`
- 2026-05-18: verified artifact `unity-cli status ready; reserialize character prefabs/test scene; refresh --compile --force; console errors []`
- 2026-05-18: verified artifact `unity-cli --project E:/UnityProjects/mdf/Mdfproject status => ready; editor refresh --compile --force => Compilation complete; console --type error => []`
- 2026-05-19: verified artifact `unity-cli --project E:/UnityProjects/mdf/Mdfproject status => ready; reserialize test scene and UnitData assets; editor refresh --compile --force => Compilation complete; console errors []`
- 2026-05-19: verified artifact `unity-cli --project E:/UnityProjects/mdf/Mdfproject status => ready; editor refresh --compile --force => Compilation complete; console errors []`
- 2026-05-19: verified artifact `unity-cli --project E:/UnityProjects/mdf/Mdfproject editor refresh --compile --force => Compilation complete; console errors []`
- 2026-05-19: verified artifact `unity-cli --project E:/UnityProjects/mdf/Mdfproject status => ready; editor refresh --compile --force => Compilation complete; console errors []`
- 2026-05-19: verified artifact `unity-cli --project E:/UnityProjects/mdf/Mdfproject status => ready; reserialize Assets/Scenes/test.unity; editor refresh --compile --force => Compilation complete; console errors []`
- 2026-05-19: verified artifact `unity-cli --project E:/UnityProjects/mdf/Mdfproject status => ready; editor refresh --compile --force => Compilation complete; final console errors []`
- 2026-05-19: verified artifact `unity-cli --project E:/UnityProjects/mdf/Mdfproject status => ready; editor refresh --compile --force => Compilation complete; console errors []`
- 2026-05-24: 2026-05-24: unity-cli --project Mdfproject status, reserialize UnitData assets, editor refresh --compile, console errors []
- 2026-05-24: 2026-05-24: slash VFX particle replay fix; unity-cli --project Mdfproject status, editor refresh --compile, console errors []
- 2026-05-24: Used unity-cli --project Mdfproject for status/reserialize during root-fixed slash VFX work; refresh/console later blocked by unresponsive connector.
- 2026-05-24: Verified root-timed slash VFX runtime timing change with unity-cli compile and console.
- 2026-05-24: verified artifact `unity-cli --project Mdfproject editor refresh --compile => Compilation complete; console --type error => []`
- 2026-05-24: verified artifact `unity-cli --project Mdfproject status => ready; reserialize Assassin UnitData/test scene; editor refresh --compile => Compilation complete; console --type error => []`
- 2026-05-30: 2026-05-30 projectile VFX timing fix: unity-cli --project Mdfproject status ready; editor refresh --compile completed; console errors []; EditMode 98/98
- 2026-05-30: 2026-05-30 combat scheduler projectile fire delay: unity-cli --project Mdfproject status/refresh/console/test succeeded
- 2026-05-30: verified artifact `unity-cli --project Mdfproject status; editor refresh --compile; console --type error --stacktrace user; EditMode 98/98`
- 2026-05-30: verified artifact `unity-cli --project Mdfproject status; editor refresh --compile; console --clear; console errors []; EditMode 98/98`
- 2026-05-30: verified artifact `unity-cli --project Mdfproject compile/console/EditMode 98/98 after projectile preview duplicate guard`
- 2026-05-30: verified artifact `unity-cli --project Mdfproject refresh/console/EditMode after projectile VFX renderer occlusion guard`
- 2026-05-30: verified artifact `unity-cli --project Mdfproject refresh/console/EditMode 98/98 after projectile VFX occlusion and sorting guard`
- 2026-05-31: Used project selector for melee slash CombatScheduler VFX event integration.
- 2026-05-31: verified artifact `unity-cli --project Mdfproject status ready; editor refresh --compile PASS; console errors []`
- 2026-05-31: Used project selector for CombatScheduler pending damage Host Migration hardening.
- 2026-05-31: verified artifact `unity-cli --project Mdfproject status ready; compile PASS; console []`
- 2026-05-31: verified artifact `unity-cli --project Mdfproject status/compile/console/EditMode 107 passed on 2026-05-31`
- 2026-05-31: verified artifact `unity-cli --project Mdfproject status/compile/console/EditMode 109 passed on 2026-05-31`
- 2026-05-31: verified artifact `unity-cli --project Mdfproject status/compile/console/EditMode 109 passed on 2026-05-31 pending-active-slot`
- 2026-05-31: verified artifact `artifacts/mp/20260531-044835-matrix/20260531-044847-network-budget-pending-stress/post-battle-host-migration-result.json`
- 2026-05-31: verified artifact `artifacts/mp/20260531-052919-matrix/20260531-052924-status-effect-host-migration/post-battle-host-migration-result.json`
- 2026-05-31: verified artifact `artifacts/mp/20260531-061353-matrix/20260531-061359-network-budget-pending-stress/post-battle-host-migration-result.json`
- 2026-05-31: verified artifact `artifacts/mp/20260531-063526-matrix/20260531-063541-network-budget-pending-stress/post-battle-host-migration-result.json`
- 2026-05-31: verified artifact `unity-cli --project Mdfproject status/compile/console/EditMode 110 passed on 2026-05-31`
- 2026-06-11: Consulted while checking current Unity project status before UI baseline run.
