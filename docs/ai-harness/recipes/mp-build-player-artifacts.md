## mp-build-player-artifacts: Development build and launch smoke

Status: active
Pinned: false
Category: harness
Created: 2026-05-05
Last used: 2026-05-22
Last verified: 2026-05-22
Use count: 24
Review after: 2026-08-03
Triggers: Development Build, build artifact, player launch smoke
Applies to: `mp_build_player`, `tools/harness/mp/build_player.py`
Verified by: see Verification section below; migrated from old Status: verified-local; artifacts/builds/mptest-current; artifacts/mp/20260510-113305-wave-common-2hbot-2ai-headless; artifacts/builds/mptest-current; artifacts/builds/mptest-current; artifacts/builds/20260514-035351; artifacts/builds/20260514-044206; artifacts/builds/20260514-051634; artifacts/builds/20260514-054535; artifacts/builds/20260514-064144; artifacts/builds/20260519-040047/build-metadata.json; artifacts/builds/20260519-055613/build-metadata.json; artifacts/builds/20260519-070913; artifacts/builds/20260520-053607; artifacts/builds/20260520-060212; artifacts/builds/mptest-current/build-metadata.json; artifacts/builds/mptest-current/build-metadata.json; artifacts/builds/mptest-current/build-metadata.json; artifacts/builds/mptest-current/build-metadata.json; artifacts/builds/mptest-current/build-metadata.json; artifacts/builds/mptest-current/build-metadata.json; artifacts/builds/mptest-current/build-metadata.json
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

Lifecycle notes:
- 2026-05-10: Built current MDF-MPTest Development player for 2 HumanBot + 2 AI headless upgrade check on 2026-05-10.
- 2026-05-10: verified artifact `artifacts/builds/mptest-current`
- 2026-05-10: Built updated headless Development player and used it for 2 HumanBot + 2 AI wave common-count verification.
- 2026-05-10: verified artifact `artifacts/mp/20260510-113305-wave-common-2hbot-2ai-headless`
- 2026-05-11: Rebuilt headless MPTest player after monster HP bar pooling fix.
- 2026-05-11: verified artifact `artifacts/builds/mptest-current`
- 2026-05-13: verified artifact `artifacts/builds/mptest-current`
- 2026-05-13: Rebuilt `artifacts/builds/mptest-current` for 2 HumanBot + 2 AI MoveUnit smoke; launch smoke cleanup PASS.
- 2026-05-14: Rebuilt Development player for UIToolkit attack sequence/resource HUD change; launch smoke cleanup PASS orphanedPids=[].
- 2026-05-14: verified artifact `artifacts/builds/20260514-035351`
- 2026-05-14: Rebuilt Development player for whole-ground monster spawn click fix; launch smoke cleanup PASS orphanedPids=[].
- 2026-05-14: verified artifact `artifacts/builds/20260514-044206`
- 2026-05-14: Rebuilt Development player after explicit monster-card selection input fix; launch smoke cleanup PASS orphanedPids=[].
- 2026-05-14: verified artifact `artifacts/builds/20260514-051634`
- 2026-05-14: Verified Development player build and launch smoke before round 2 MP move test.
- 2026-05-14: verified artifact `artifacts/builds/20260514-054535`
- 2026-05-14: Rebuilt Development player after round-agnostic movement and drag NetworkTransform restore; launch smoke cleanup PASS orphanedPids=[].
- 2026-05-14: verified artifact `artifacts/builds/20260514-064144`
- 2026-05-19: Verified Windows player build after active build target switch; launch smoke cleanupStatus=PASS orphanedPids=[].
- 2026-05-19: verified artifact `artifacts/builds/20260519-040047/build-metadata.json`
- 2026-05-19: Rebuilt Windows player after lobby image restore and ranking input passthrough; launch smoke cleanupStatus=PASS orphanedPids=[].
- 2026-05-19: verified artifact `artifacts/builds/20260519-055613/build-metadata.json`
- 2026-05-19: Built Development player artifacts/builds/20260519-070913/MDF-MPTest.exe and launch smoke exited cleanly with cleanupStatus=PASS orphanedPids=[].
- 2026-05-19: verified artifact `artifacts/builds/20260519-070913`
- 2026-05-20: verified artifact `artifacts/builds/20260520-053607`
- 2026-05-20: verified artifact `artifacts/builds/20260520-060212`
- 2026-05-21: verified artifact `artifacts/builds/mptest-current/build-metadata.json`
- 2026-05-21: Rebuilt Development player after ranking UI HP max display fix; launch smoke cleanupStatus=PASS orphanedPids=[].
- 2026-05-21: verified artifact `artifacts/builds/mptest-current/build-metadata.json`
- 2026-05-21: Verified for UI Toolkit build used by two-humanbot-two-ai smoke.
- 2026-05-21: verified artifact `artifacts/builds/mptest-current/build-metadata.json`
- 2026-05-21: Verified development player rebuild for PlayerRanking UI layout change.
- 2026-05-21: verified artifact `artifacts/builds/mptest-current/build-metadata.json`
- 2026-05-21: Verified development player rebuild for PlayerRanking self/opponent position adjustment.
- 2026-05-21: verified artifact `artifacts/builds/mptest-current/build-metadata.json`
- 2026-05-22: verified artifact `artifacts/builds/mptest-current/build-metadata.json`
- 2026-05-22: verified artifact `artifacts/builds/mptest-current/build-metadata.json`
