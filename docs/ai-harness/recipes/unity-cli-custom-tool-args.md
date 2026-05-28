## unity-cli-custom-tool-args: Invoke mp_* tools with CLI flags

Status: active
Pinned: false
Category: unity-cli
Created: 2026-05-05
Last used: 2026-05-28
Last verified: 2026-05-18
Use count: 20
Review after: 2026-08-03
Triggers: mp_dump_state, mp_assert_state, mp_build_player, custom tool smoke
Applies to: unity-cli connector 0.3.15 custom tools
Verified by: see Verification section below; migrated from old Status: verified-local
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
