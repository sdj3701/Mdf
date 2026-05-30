## unity-cli-editmode-filter-and-force-import-v1: Verify newly added EditMode tests by class filter

Status: active
Pinned: false
Category: unity-cli, asset
Created: 2026-05-06
Last used: 2026-05-31
Last verified: 2026-05-31
Use count: 74
Review after: 2026-08-04
Triggers: EditMode test runner returns `total=0` for a newly added method filter, Unity does not appear to pick up new test methods after compile
Applies to: `unity-cli --project Mdfproject test --mode EditMode`, newly added tests, forced AssetDatabase import
Verified by: see Verification section below; migrated from old Status: verified; Mdfproject; artifacts/addressables-registry-editmode-latest.log; unity-cli --project Mdfproject test --mode EditMode --filter AttackSlashCalibrationEditModeTests => 6/6; unity-cli --project Mdfproject test --mode EditMode => 82/82; unity-cli --project Mdfproject test --mode EditMode --filter AttackSlashCalibrationEditModeTests => 8/8; unity-cli --project Mdfproject test --mode EditMode => 84/84; unity-cli --project Mdfproject test --mode EditMode --filter AttackSlashCalibrationEditModeTests => 9/9; unity-cli --project Mdfproject test --mode EditMode => 85/85; unity-cli --project Mdfproject test --mode EditMode --filter AttackSlashCalibrationEditModeTests => 9/9; unity-cli --project Mdfproject test --mode EditMode => 85/85; unity-cli --project Mdfproject test --mode EditMode --filter AttackSlashTuningEditModeTests => 4/4 passed; EditMode => 80/80 passed; unity-cli --project Mdfproject test --mode EditMode --filter AttackSlashTuningEditModeTests => 5/5 passed; EditMode => 81/81 passed; unity-cli --project Mdfproject test --mode EditMode --filter AttackSlashTuningEditModeTests => 6/6 passed; EditMode => 82/82 passed; unity-cli --project Mdfproject test --mode EditMode --filter AttackSlashTuningEditModeTests => 7/7 passed; EditMode => 83/83 passed; unity-cli --project Mdfproject test --mode EditMode --filter AttackSlashTuningEditModeTests => 6/6 passed; EditMode => 82/82 passed; unity-cli --project Mdfproject test --mode EditMode --filter AttackSlashTuningEditModeTests => 6/6 passed; EditMode => 82/82 passed; unity-cli --project Mdfproject test --mode EditMode --filter AttackSlashTuningEditModeTests => 6/6 passed; unity-cli --project Mdfproject test --mode EditMode --filter AttackSlashTuningEditModeTests => 6/6 passed; unity-cli --project Mdfproject test --mode EditMode --filter AttackSlashTuningEditModeTests => 6/6 passed; EditMode => 82/82 passed; unity-cli --project Mdfproject test --mode EditMode --filter AttackSlashTuningEditModeTests => 7/7 passed; unity-cli --project Mdfproject test --mode EditMode --filter AttackSlashTuningEditModeTests => 7/7 passed; EditMode => 83/83 passed; unity-cli --project E:/UnityProjects/mdf/Mdfproject test --mode EditMode --filter AttackSlashTuningEditModeTests => 7/7 passed; EditMode => 83/83 passed; unity-cli --project E:/UnityProjects/mdf/Mdfproject test --mode EditMode --filter AttackSlashTuningEditModeTests => 7/7 passed; EditMode => 83/83 passed; unity-cli --project E:/UnityProjects/mdf/Mdfproject test --mode EditMode --filter AttackSlashTuningEditModeTests => 7/7 passed; EditMode => 83/83 passed; unity-cli --project E:/UnityProjects/mdf/Mdfproject test --mode EditMode --filter AttackSlashTuningEditModeTests => 8/8 passed; EditMode => 84/84 passed; unity-cli --project E:/UnityProjects/mdf/Mdfproject test --mode EditMode --filter AttackSlashTuningEditModeTests => 8/8 passed; EditMode => 84/84 passed; AttackSlashTuningEditModeTests 8/8 passed; EditMode 84/84 passed; AttackSlashTuningEditModeTests => 8/8 passed; EditMode => 84/84 passed; AttackSlashTuningEditModeTests => 8/8 passed; EditMode => 84/84 passed; AttackSlashTuningEditModeTests => 9/9 passed; EditMode => 85/85 passed; AttackSlashTuningEditModeTests => 9/9 passed; EditMode => 85/85 passed; AttackSlashTuningEditModeTests => 10/10 passed; EditMode => 86/86 passed; AttackSlashTuningEditModeTests => 9/9 passed; EditMode => 85/85 passed; AttackSlashTuningEditModeTests => 8/8 passed; EditMode => 84/84 passed; AttackSlashTuningEditModeTests => 8/8 passed; EditMode => 84/84 passed; unity-cli --project Mdfproject test --mode EditMode --filter AttackSlashTuningEditModeTests => 9/9; unity-cli --project Mdfproject test --mode EditMode => 85/85; AttackSlashTuningEditModeTests => 10/10 passed; EditMode => 86/86 passed; ProjectileVfxConfigEditModeTests 12/12; AttackSlashTuningEditModeTests 10/10; EditMode 98/98; ProjectileVfxConfigEditModeTests 12/12; AttackSlashTuningEditModeTests 10/10; EditMode 98/98; ProjectileVfxConfigEditModeTests 12/12; AttackSlashTuningEditModeTests 10/10; EditMode 98/98 after projectile preview duplicate guard; ProjectileVfxConfigEditModeTests and full EditMode after projectile VFX renderer occlusion guard; ProjectileVfxConfigEditModeTests 12/12 and EditMode 98/98 after projectile VFX occlusion and sorting guard; AttackSlashTuningEditModeTests 10/10; EditMode 98/98 with --allow-dirty-scenes
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
After adding new methods to an existing EditMode test class, `unity-cli --project Mdfproject test --mode EditMode --filter <methodName>` can return `total=0` even though the methods are valid and the full/class test run will discover them. Treat a zero-count method-filter run as inconclusive, not PASS.

Recipe:
- Prefer a class filter when verifying new tests in `MPTestHarnessEditModeTests`:
  - `unity-cli --project Mdfproject test --mode EditMode --filter MPTestHarnessEditModeTests`
- If Unity appears stale, force import the changed test file by piping code through stdin to avoid PowerShell quoting problems:
  - `@' ... '@ | unity-cli --project Mdfproject exec`
  - Example body:
    - `UnityEditor.AssetDatabase.ImportAsset("Assets/Scripts/Testing/MP/Editor/MPTestHarnessEditModeTests.cs", UnityEditor.ImportAssetOptions.ForceUpdate);`
    - `UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();`
    - `return "forced";`
- Then run:
  - `unity-cli --project Mdfproject editor refresh --compile --force`
  - `unity-cli --project Mdfproject test --mode EditMode --filter MPTestHarnessEditModeTests`
- Do not rely on `total=0` method-filter output as evidence that a new test passed.

Verification:
- `unity-cli --project Mdfproject test --mode EditMode --filter ActivateSkillCommandSourceContainsStrategicManualSkillGuards` returned `total=0`.
- `unity-cli --project Mdfproject test --mode EditMode --filter MPTestHarnessEditModeTests` then discovered the new Phase 8 tests and passed `19/19`.

Lifecycle notes:
- 2026-05-10: Verified by unity-cli --project Mdfproject test --mode EditMode --filter MPTestHarnessEditModeTests discovering and passing 60/60 tests including RankingUiSplitsFourPlayersEvenlyAcrossSides on 2026-05-10.
- 2026-05-10: Verified EditMode suite passed 63/63 including scene alias tests on 2026-05-10.
- 2026-05-10: verified artifact `Mdfproject`
- 2026-05-16: verified artifact `artifacts/addressables-registry-editmode-latest.log`
- 2026-05-16: unity-cli --project Mdfproject test --mode EditMode --filter AttackSlashCalibrationEditModeTests discovered and passed 5/5 tests on 2026-05-16
- 2026-05-16: unity-cli --project Mdfproject test --mode EditMode --filter AttackSlashCalibrationEditModeTests discovered and passed 6/6 tests after configurable slash scale boost on 2026-05-16
- 2026-05-16: verified artifact `unity-cli --project Mdfproject test --mode EditMode --filter AttackSlashCalibrationEditModeTests => 6/6; unity-cli --project Mdfproject test --mode EditMode => 82/82`
- 2026-05-16: verified artifact `unity-cli --project Mdfproject test --mode EditMode --filter AttackSlashCalibrationEditModeTests => 8/8; unity-cli --project Mdfproject test --mode EditMode => 84/84`
- 2026-05-16: verified artifact `unity-cli --project Mdfproject test --mode EditMode --filter AttackSlashCalibrationEditModeTests => 9/9; unity-cli --project Mdfproject test --mode EditMode => 85/85`
- 2026-05-16: verified artifact `unity-cli --project Mdfproject test --mode EditMode --filter AttackSlashCalibrationEditModeTests => 9/9; unity-cli --project Mdfproject test --mode EditMode => 85/85`
- 2026-05-18: verified artifact `unity-cli --project Mdfproject test --mode EditMode --filter AttackSlashTuningEditModeTests => 4/4 passed; EditMode => 80/80 passed`
- 2026-05-18: verified artifact `unity-cli --project Mdfproject test --mode EditMode --filter AttackSlashTuningEditModeTests => 5/5 passed; EditMode => 81/81 passed`
- 2026-05-18: verified artifact `unity-cli --project Mdfproject test --mode EditMode --filter AttackSlashTuningEditModeTests => 6/6 passed; EditMode => 82/82 passed`
- 2026-05-18: verified artifact `unity-cli --project Mdfproject test --mode EditMode --filter AttackSlashTuningEditModeTests => 7/7 passed; EditMode => 83/83 passed`
- 2026-05-18: verified artifact `unity-cli --project Mdfproject test --mode EditMode --filter AttackSlashTuningEditModeTests => 6/6 passed; EditMode => 82/82 passed`
- 2026-05-18: verified artifact `unity-cli --project Mdfproject test --mode EditMode --filter AttackSlashTuningEditModeTests => 6/6 passed; EditMode => 82/82 passed`
- 2026-05-18: verified artifact `unity-cli --project Mdfproject test --mode EditMode --filter AttackSlashTuningEditModeTests => 6/6 passed`
- 2026-05-18: verified artifact `unity-cli --project Mdfproject test --mode EditMode --filter AttackSlashTuningEditModeTests => 6/6 passed`
- 2026-05-18: verified artifact `unity-cli --project Mdfproject test --mode EditMode --filter AttackSlashTuningEditModeTests => 6/6 passed; EditMode => 82/82 passed`
- 2026-05-18: verified artifact `unity-cli --project Mdfproject test --mode EditMode --filter AttackSlashTuningEditModeTests => 7/7 passed`
- 2026-05-18: verified artifact `unity-cli --project Mdfproject test --mode EditMode --filter AttackSlashTuningEditModeTests => 7/7 passed; EditMode => 83/83 passed`
- 2026-05-18: verified artifact `unity-cli --project E:/UnityProjects/mdf/Mdfproject test --mode EditMode --filter AttackSlashTuningEditModeTests => 7/7 passed; EditMode => 83/83 passed`
- 2026-05-18: verified artifact `unity-cli --project E:/UnityProjects/mdf/Mdfproject test --mode EditMode --filter AttackSlashTuningEditModeTests => 7/7 passed; EditMode => 83/83 passed`
- 2026-05-18: verified artifact `unity-cli --project E:/UnityProjects/mdf/Mdfproject test --mode EditMode --filter AttackSlashTuningEditModeTests => 7/7 passed; EditMode => 83/83 passed`
- 2026-05-18: verified artifact `unity-cli --project E:/UnityProjects/mdf/Mdfproject test --mode EditMode --filter AttackSlashTuningEditModeTests => 8/8 passed; EditMode => 84/84 passed`
- 2026-05-18: verified artifact `unity-cli --project E:/UnityProjects/mdf/Mdfproject test --mode EditMode --filter AttackSlashTuningEditModeTests => 8/8 passed; EditMode => 84/84 passed`
- 2026-05-18: verified artifact `AttackSlashTuningEditModeTests 8/8 passed; EditMode 84/84 passed`
- 2026-05-18: verified artifact `AttackSlashTuningEditModeTests => 8/8 passed; EditMode => 84/84 passed`
- 2026-05-19: verified artifact `AttackSlashTuningEditModeTests => 8/8 passed; EditMode => 84/84 passed`
- 2026-05-19: verified artifact `AttackSlashTuningEditModeTests => 9/9 passed; EditMode => 85/85 passed`
- 2026-05-19: verified artifact `AttackSlashTuningEditModeTests => 9/9 passed; EditMode => 85/85 passed`
- 2026-05-19: verified artifact `AttackSlashTuningEditModeTests => 10/10 passed; EditMode => 86/86 passed`
- 2026-05-19: verified artifact `AttackSlashTuningEditModeTests => 9/9 passed; EditMode => 85/85 passed`
- 2026-05-19: verified artifact `AttackSlashTuningEditModeTests => 8/8 passed; EditMode => 84/84 passed`
- 2026-05-19: verified artifact `AttackSlashTuningEditModeTests => 8/8 passed; EditMode => 84/84 passed`
- 2026-05-24: 2026-05-24: AttackSlashTuningEditModeTests => 9/9 passed; EditMode => 85/85 passed
- 2026-05-24: 2026-05-24: slash VFX particle replay fix; AttackSlashTuningEditModeTests => 9/9 passed; EditMode => 85/85 passed
- 2026-05-24: Verified root-timed slash VFX runtime timing change with targeted and full EditMode.
- 2026-05-24: verified artifact `unity-cli --project Mdfproject test --mode EditMode --filter AttackSlashTuningEditModeTests => 9/9; unity-cli --project Mdfproject test --mode EditMode => 85/85`
- 2026-05-24: verified artifact `AttackSlashTuningEditModeTests => 10/10 passed; EditMode => 86/86 passed`
- 2026-05-30: 2026-05-30 projectile VFX timing fix: unity-cli --project Mdfproject test --mode EditMode --filter ProjectileVfxConfigEditModeTests passed 12/12; full EditMode passed 98/98
- 2026-05-30: 2026-05-30 combat scheduler projectile fire delay: filtered ProjectileVfxConfigEditModeTests passed 12/12; full EditMode passed 98/98
- 2026-05-30: verified artifact `ProjectileVfxConfigEditModeTests 12/12; AttackSlashTuningEditModeTests 10/10; EditMode 98/98`
- 2026-05-30: verified artifact `ProjectileVfxConfigEditModeTests 12/12; AttackSlashTuningEditModeTests 10/10; EditMode 98/98`
- 2026-05-30: verified artifact `ProjectileVfxConfigEditModeTests 12/12; AttackSlashTuningEditModeTests 10/10; EditMode 98/98 after projectile preview duplicate guard`
- 2026-05-30: verified artifact `ProjectileVfxConfigEditModeTests and full EditMode after projectile VFX renderer occlusion guard`
- 2026-05-30: verified artifact `ProjectileVfxConfigEditModeTests 12/12 and EditMode 98/98 after projectile VFX occlusion and sorting guard`
- 2026-05-31: Verified melee slash VFX event-stream integration tests while preserving dirty test scene.
- 2026-05-31: verified artifact `AttackSlashTuningEditModeTests 10/10; EditMode 98/98 with --allow-dirty-scenes`
