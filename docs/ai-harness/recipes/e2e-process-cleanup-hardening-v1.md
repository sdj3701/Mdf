## e2e-process-cleanup-hardening-v1: Report cleanup separately and prove orphaned player PIDs

Status: active
Pinned: true
Category: cleanup, battle
Created: 2026-05-08
Last used: 2026-07-13
Last verified: 2026-07-13
Use count: 7
Review after: 2026-08-06
Triggers: `MDF-MPTest.exe` left running after E2E, Windows process cleanup, orphan detection
Applies to: `tools/harness/mp/launch_player.py`, HumanBot prepare/battle runners, matrix runner
Verified by: see Verification section below; migrated from old Status: verified with environment blocker; blocker note preserved in recipe body; artifacts/mp/20260712-085632-matrix/20260712-085638-human-bot-battle-progression/cleanup-report.json; artifacts/mp/20260712-214518-matrix/20260712-214655-build-host-build-client/cleanup-report.json; artifacts/mp/20260712-234448-matrix/matrix-summary.json; artifacts/mp/20260713-021757-window-close-graceful/cleanup-report.json
Replacement: none
Archive policy: never auto-archive pinned/protected recipe; review only by explicit human direction

Problem:
Functional E2E assertions can pass while Unity player processes fail to exit. In this Windows environment, `/quit`, `TerminateProcess` via Python, `Stop-Process`, and `taskkill /PID <pid> /T /F` can all fail to remove the original `MDF-MPTest.exe` PID even though child PIDs are terminated. Treat this as cleanup `NEEDS_ENVIRONMENT` unless strict cleanup is explicitly requested.

Recipe:
- Record the baseline `MDF-MPTest.exe` PIDs before launching an E2E case.
- For each launched player, record PID, parent PID, redacted command line, artifact dir, stdout/stderr paths, and `Player.log` path.
- Cleanup order is: automation `/quit`, wait for exit, Python `terminate`, wait, Python `kill`, wait, CIM live-process check, then Windows `taskkill /PID <pid> /T /F`, followed by a CIM absent wait.
- Always write `cleanup-report.json` with per-peer cleanup steps, `orphanedPids`, `cleanupSuccess`, and `cleanupStatus`.
- Keep gameplay result separate from cleanup: non-strict runs may have `functionalSuccess=true`, `success=true`, and `cleanupStatus=NEEDS_ENVIRONMENT`; strict runs append `cleanup_failed:<status>`.
- `--leave-processes-on-fail` leaves peers alive for debugging only when functional failures already exist.
- `run_matrix.py` forwards cleanup flags only to cases that support them and records per-case `cleanupStatus`; dry-run skips Unity editor cleanup side effects.
- Do not add a pywin32 dependency for Job Objects without approval. The current fallback is documented `taskkill`; a future ctypes Job Object wrapper should attach the player at process creation time if this environment blocker must become a hard PASS.

Verification:
- `python -m py_compile tools\harness\mp\launch_player.py tools\harness\mp\run_human_bot_prepare_progression.py tools\harness\mp\run_human_bot_seed_sweep.py tools\harness\mp\battle_progression_common.py tools\harness\mp\run_matrix.py` PASS.
- `python tools/harness/precommit.py --all` PASS, `0 errors, 0 warnings`.
- `unity-cli --project Mdfproject console --type error --stacktrace user` returned `[]`.
- `python tools/harness/mp/run_human_bot_prepare_progression.py --seed 1001 --bot-persona balanced --bot-max-commands 5 --min-commands 3` passed functional assertions with artifact `artifacts/mp/20260507-173915-human-bot-prepare`.
- `artifacts/mp/20260507-173915-human-bot-prepare/result.json` recorded `functionalSuccess=true`, `success=true`, `cleanupSuccess=false`, `cleanupStatus=NEEDS_ENVIRONMENT`, and `orphanedPids=[48936,53784]`.
- `artifacts/mp/20260507-173915-human-bot-prepare/cleanup-report.json` recorded both peers timing out after `/quit` and terminate, still alive by CIM after Python kill, and `taskkill_tree` failing the original PID with "There is no running instance of the task" after terminating child PIDs.
- Prepare metrics for the same artifact: `BuyUnit=4`, `RerollShop=0`, `firstRerollSoldSlotCount=null`, `rerollBeforeThreeSold=false`, final field unit total `3`.

Pitfalls:
- Do not use `Popen.poll()` alone as cleanup proof on Windows. This environment showed `Popen` exit code `1` while CIM/tasklist still listed the same `MDF-MPTest.exe` PID.
- Do not redact or omit cleanup failures. Report exact orphan PIDs and separate cleanup status from gameplay assertions.

Lifecycle notes:
- 2026-07-12: verified artifact `artifacts/mp/20260712-085632-matrix/20260712-085638-human-bot-battle-progression/cleanup-report.json`
- 2026-07-13: Strict smoke cleanup passed with orphanedPids=[].
- 2026-07-13: verified artifact `artifacts/mp/20260712-214518-matrix/20260712-214655-build-host-build-client/cleanup-report.json`
- 2026-07-13: Battle profile completed with cleanupStatus=PASS and orphanedPids=[] for all cases.
- 2026-07-13: verified artifact `artifacts/mp/20260712-234448-matrix/matrix-summary.json`
- 2026-07-13: verified artifact `artifacts/mp/20260713-021757-window-close-graceful/cleanup-report.json`
