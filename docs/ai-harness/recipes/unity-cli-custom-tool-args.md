## unity-cli-custom-tool-args: Invoke mp_* tools with CLI flags

Status: active
Pinned: false
Category: unity-cli
Created: 2026-05-05
Last used: 2026-07-15
Last verified: 2026-07-15
Use count: 33
Review after: 2026-08-03
Triggers: mp_dump_state, mp_assert_state, mp_build_player, custom tool smoke
Applies to: unity-cli connector 0.3.15 custom tools
Verified by: see Verification section below; migrated from old Status: verified-local; artifacts/builds/20260713-performance-memory-final-v2-win64/build-metadata.json; artifacts/builds/20260713-king-wall-timer-final-win64/build-metadata.json; artifacts/builds/20260714-king-ui-attack-final2-win64/build-metadata.json; artifacts/builds/20260714-195200-content-migration-capacity-win64/build-metadata.json; artifacts/builds/20260715-maintenance-architecture-final-win64-v3/build-metadata.json; artifacts/builds/20260715-maintenance-architecture-release-win64/build-metadata.json
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
Custom `[UnityCliTool]` commands need a reliable invocation form for harness scripts and manual smoke checks.

Recipe:
Invoke registered custom tools as top-level unity-cli subcommands. Pass parameters as snake_case flags that match the tool `JObject` keys.

Example:

```powershell
unity-cli --project Mdfproject mp_dump_state --role editor --case_name phase6_snapshot_smoke
```

Verification:
- `unity-cli --project Mdfproject mp_dump_state --role editor --case_name phase6_snapshot_smoke` returned a JSON snapshot from the open Editor in the `Title` scene.

Pitfalls:
- A snapshot from `Title` is only a tool smoke. It is not a gameplay PASS because `GameManagers` and players are absent.

Lifecycle notes:
- 2026-07-13: mp_build_player flags produced the final StandaloneWindows64 build.
- 2026-07-13: verified artifact `artifacts/builds/20260713-performance-memory-final-v2-win64/build-metadata.json`
- 2026-07-13: verified artifact `artifacts/builds/20260713-king-wall-timer-final-win64/build-metadata.json`
- 2026-07-14: Extended unity-cli timeout and explicit build target completed the player build and restored Android target.
- 2026-07-14: verified artifact `artifacts/builds/20260714-king-ui-attack-final2-win64/build-metadata.json`
- 2026-07-14: verified artifact `artifacts/builds/20260714-195200-content-migration-capacity-win64/build-metadata.json`
- 2026-07-15: verified artifact `artifacts/builds/20260715-maintenance-architecture-final-win64-v3/build-metadata.json`
- 2026-07-15: verified artifact `artifacts/builds/20260715-maintenance-architecture-release-win64/build-metadata.json`
