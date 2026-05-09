## unity-cli-editmode-filter-and-force-import-v1: Verify newly added EditMode tests by class filter

Status: active
Pinned: false
Category: unity-cli, asset
Created: 2026-05-06
Last used: 2026-05-10
Last verified: 2026-05-10
Use count: 2
Review after: 2026-08-04
Triggers: EditMode test runner returns `total=0` for a newly added method filter, Unity does not appear to pick up new test methods after compile
Applies to: `unity-cli --project Mdfproject test --mode EditMode`, newly added tests, forced AssetDatabase import
Verified by: see Verification section below; migrated from old Status: verified
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
