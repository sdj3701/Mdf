## mp-build-player-artifacts: Development build and launch smoke

Status: active
Pinned: false
Category: harness
Created: 2026-05-05
Last used: 2026-05-31
Last verified: 2026-05-31
Use count: 34
Review after: 2026-08-03
Triggers: Development Build, build artifact, player launch smoke
Applies to: `mp_build_player`, `tools/harness/mp/build_player.py`
Verified by: see Verification section below; migrated from old Status: verified-local; artifacts/builds/mptest-current; artifacts/mp/20260510-113305-wave-common-2hbot-2ai-headless; artifacts/builds/mptest-current; artifacts/builds/unit-roster-battle-fix-win-aa; artifacts/builds/20260516-043105; artifacts/builds/20260516-050308; artifacts/builds/20260516-071517; artifacts/builds/20260516-083348; artifacts/builds/20260516-180032-slash-vfx-win; artifacts/builds/20260530-212020/MDF-MPTest.exe; artifacts/builds/20260531-065812/build-metadata.json; artifacts/builds/20260531-014628/build-metadata.json; artifacts/builds/20260531-022040/build-metadata.json; artifacts/builds/20260531-030203/build-metadata.json; artifacts/builds/20260531-030203/build-metadata.json; artifacts/builds/20260531-032232/build-metadata.json; artifacts/builds/20260531-040001/build-metadata.json; artifacts/builds/20260531-044700-win/build-metadata.json; artifacts/builds/20260531-052700-win/build-metadata.json; artifacts/builds/20260531-150548-player-snapshot-pack-win/build-metadata.json; artifacts/builds/20260531-153300-pending-fire-pack-win/build-metadata.json; artifacts/builds/20260531-identity-key-pack-win/build-metadata.json; artifacts\builds\20260531-084700-monster-prewarm-win64\build-metadata.json
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
- If a StandaloneWindows64 MP player logs Addressables paths under `StreamingAssets/aa/Android`, the player was built from stale active-target Addressables content. `mp_build_player` must switch to the requested build target and build Addressables player content before `BuildPipeline.BuildPlayer`.
- Restore the Editor active build target after targeted player builds. Leaving an Editor session switched from Android/mobile to Standalone can make URP grow `_AdditionalShadowParams` from 32 to 256 in the same session and spam "Property (_AdditionalShadowParams) exceeds previous array size".

Lifecycle notes:
- 2026-05-10: Built current MDF-MPTest Development player for 2 HumanBot + 2 AI headless upgrade check on 2026-05-10.
- 2026-05-10: verified artifact `artifacts/builds/mptest-current`
- 2026-05-10: Built updated headless Development player and used it for 2 HumanBot + 2 AI wave common-count verification.
- 2026-05-10: verified artifact `artifacts/mp/20260510-113305-wave-common-2hbot-2ai-headless`
- 2026-05-11: Rebuilt headless MPTest player after monster HP bar pooling fix.
- 2026-05-11: verified artifact `artifacts/builds/mptest-current`
- 2026-05-16: verified artifact `artifacts/builds/unit-roster-battle-fix-win-aa`
- 2026-05-16: Patched `mp_build_player` to switch active build target and build Addressables content first; verified StandaloneWindows64 output contains `aa/StandaloneWindows64` and passes `human-bot-battle-progression`.
- 2026-05-16: Patched `mp_build_player` to restore the Editor build target after targeted builds; source guard test and console check verified no immediate warning spam after restoring Android target.
- 2026-05-16: verified artifact `artifacts/builds/20260516-043105`
- 2026-05-16: verified artifact `artifacts/builds/20260516-050308`
- 2026-05-16: verified artifact `artifacts/builds/20260516-071517`
- 2026-05-16: verified artifact `artifacts/builds/20260516-083348`
- 2026-05-16: verified artifact `artifacts/builds/20260516-180032-slash-vfx-win`
- 2026-05-31: verified artifact `artifacts/builds/20260530-212020/MDF-MPTest.exe`
- 2026-05-31: verified artifact `artifacts/builds/20260531-065812/build-metadata.json`
- 2026-05-31: verified artifact `artifacts/builds/20260531-014628/build-metadata.json`
- 2026-05-31: verified artifact `artifacts/builds/20260531-022040/build-metadata.json`
- 2026-05-31: verified artifact `artifacts/builds/20260531-030203/build-metadata.json`
- 2026-05-31: verified artifact `artifacts/builds/20260531-030203/build-metadata.json`
- 2026-05-31: verified artifact `artifacts/builds/20260531-032232/build-metadata.json`
- 2026-05-31: verified artifact `artifacts/builds/20260531-040001/build-metadata.json`
- 2026-05-31: verified artifact `artifacts/builds/20260531-044700-win/build-metadata.json`
- 2026-05-31: verified artifact `artifacts/builds/20260531-052700-win/build-metadata.json`
- 2026-05-31: verified artifact `artifacts/builds/20260531-150548-player-snapshot-pack-win/build-metadata.json`
- 2026-05-31: verified artifact `artifacts/builds/20260531-153300-pending-fire-pack-win/build-metadata.json`
- 2026-05-31: verified artifact `artifacts/builds/20260531-identity-key-pack-win/build-metadata.json`
- 2026-05-31: verified artifact `artifacts\builds\20260531-084700-monster-prewarm-win64\build-metadata.json`
