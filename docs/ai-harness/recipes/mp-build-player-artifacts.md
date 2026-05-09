## mp-build-player-artifacts: Development build and launch smoke

Status: active
Pinned: false
Category: harness
Created: 2026-05-05
Last used: 2026-05-05
Last verified: 2026-05-05
Use count: 1
Review after: 2026-08-03
Triggers: Development Build, build artifact, player launch smoke
Applies to: `mp_build_player`, `tools/harness/mp/build_player.py`
Verified by: see Verification section below; migrated from old Status: verified-local
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
E2E needs a deterministic Development player plus evidence that `--mpTest` starts, logs, and exits cleanly.

Recipe:
- Use `python tools\harness\mp\build_player.py --launch-smoke --exit-after-seconds 5`.
- Build output is under `artifacts/builds/<timestamp>/`.
- `build-metadata.json` records Unity version, build target, scenes, output path, Development/AllowDebugging flags, and Player.log source path.
- Launch smoke artifacts include stdout/stderr, copied `Player.log`, redacted command JSON, and `launch-smoke.json`.

Verification:
- Build output `artifacts/builds/20260504-203544/MDF-MPTest.exe` exists.
- `build-metadata.json` reported `result=Succeeded`, target `StandaloneWindows64`, Development Build enabled, and scenes `Title`, `MatchingLobby`, `JoinLobby`, `Game`.
- Launch smoke exited `0` and copied `Player.log` with `[MPTEST] bootstrap`, `automation_server`, and `exit_after_seconds` lines.

Pitfalls:
- On Windows, Python subprocess text capture can hit CP949 decode failures on unity-cli output. Use `encoding="utf-8", errors="replace"` in harness scripts that capture command output.
