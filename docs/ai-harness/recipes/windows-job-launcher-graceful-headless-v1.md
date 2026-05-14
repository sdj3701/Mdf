## windows-job-launcher-graceful-headless-v1: Contain, quit, and optionally headless-run MDF players

Status: active
Pinned: true
Category: cleanup
Created: 2026-05-08
Last used: 2026-05-14
Last verified: 2026-05-14
Use count: 12
Review after: 2026-08-06
Triggers: Windows cleanup blocker, `/quit` timeout, D3D/GPU pressure, headless smoke/prepare E2E
Applies to: `tools/harness/mp/launch_player.py`, `build_player.py`, `run_matrix.py`, `MPTestGracefulQuit.cs`
Verified by: see Verification section below; migrated from old Status: verified; artifacts/mp/20260510-101534-matrix; artifacts/mp/20260510-103350-two-humanbot-two-ai-upgrade-check; artifacts/mp/20260510-113305-wave-common-2hbot-2ai-headless; artifacts/mp/20260510-170014-monster-healthbar-battle2-2hbot-2ai-headless; artifacts/mp/20260514-035507-two-humanbot-two-ai-smoke, artifacts/mp/20260514-035543-battle-spawn-monster-command; artifacts/mp/20260514-044306-two-humanbot-two-ai-smoke; artifacts/mp/20260514-044344-battle-spawn-monster-command; artifacts/mp/20260514-051726-two-humanbot-two-ai-smoke; artifacts/mp/20260514-061112-two-humanbot-two-ai-smoke; artifacts/mp/20260514-064919-two-humanbot-two-ai-smoke; artifacts/mp/20260514-072736-two-humanbot-two-ai-smoke
Replacement: none
Archive policy: never auto-archive pinned/protected recipe; review only by explicit human direction

Recipe:
- Launch Windows MDF players through the ctypes Job Object path. Cleanup reports should show `jobCreated=true`, `assignedToJob=true`, `killOnJobCloseSet=true`, and `headlessPlayer` when enabled.
- Keep cleanup order: automation `/quit`, wait, terminate Job Object, terminate/kill process, `taskkill` fallback.
- Before heavy E2E, block if live `MDF-MPTest.exe` count exceeds `--orphan-threshold` unless `--force-run-with-orphans` is explicitly passed.
- Runtime `/quit` and `--mpExitAfterSeconds` should use graceful MPTest quit logs: `quit_requested`, `human_bot_stop_requested`, `runner_shutdown_begin`/`runner_shutdown_complete` or timeout, `automation_server_stop`, `application_quit_called`.
- Use `--headless-player` for smoke and logic E2E that do not require screenshot assertions. Headless command JSON should include `-batchmode -nographics`, result JSON should include `headlessPlayer=true`, and screenshot artifacts should be marked skipped.
- Smoke matrix cases should call `write_case_cleanup_report` instead of hand-written quit/wait cleanup, so every child artifact records `cleanup-report.json`, `cleanupReportPath`, and `orphanedPids`.

Verification:
- `python -m py_compile tools/harness/mp/launch_player.py tools/harness/mp/build_player.py tools/harness/mp/run_matrix.py` PASS.
- `python tools/harness/precommit.py --all` PASS, `0 errors, 0 warnings`.
- `unity-cli --project Mdfproject editor refresh --compile` PASS and `unity-cli --project Mdfproject console --type error --stacktrace user` returned `[]`.
- `unity-cli --project Mdfproject test --mode EditMode` PASS, `43/43`.
- `python tools/harness/mp/build_player.py --launch-smoke --exit-after-seconds 5 --orphan-threshold 0` built `artifacts/builds/20260507-231956/MDF-MPTest.exe`; launch smoke cleanup PASS with leftover count 0 and graceful quit logs in `launch-smoke.Player.log`.
- `python tools/harness/mp/run_matrix.py --profile smoke --dry-run --headless-player` PASS with all three smoke child commands carrying `--headless-player`.
- `python tools/harness/mp/run_human_bot_prepare_progression.py --seed 1001 --player-path artifacts/builds/20260507-231956/MDF-MPTest.exe --headless-player --orphan-threshold 0` PASS at `artifacts/mp/20260507-232704-human-bot-prepare`; `result.json` recorded `headlessPlayer=true`, cleanup PASS, `orphanedPids=[]`, and live `MDF-MPTest.exe` returned to 0.
- `python tools/harness/mp/run_matrix.py --profile smoke --headless-player` PASS at `artifacts/mp/20260508-000921-matrix`; every child cleanup report recorded `cleanupStatus=PASS`, `orphanedPids=[]`, `headlessPlayer=true`, and `jobCreated=true`/`assignedToJob=true`/`killOnJobCloseSet=true`. Live `MDF-MPTest.exe` returned to 0.

Pitfalls:
- Do not rely on `Popen.poll()` alone as Windows cleanup proof; verify CIM/tasklist absence and cleanup-report orphan lists.
- Do not overwrite a peer's explicit `-logFile` artifact with `collect_player_log(..., peer_name)` after cleanup; use a separate fallback label such as `build-host-or-last` so graceful quit lines remain in the peer `Player.log`.
- Do not default screenshot or visual-debugging cases to headless mode.

Lifecycle notes:
- 2026-05-10: Verified headless smoke matrix E2E cleanup on 2026-05-10; cleanupStatus=PASS orphanedPids=[].
- 2026-05-10: verified artifact `artifacts/mp/20260510-101534-matrix`
- 2026-05-10: Verified headless player cleanup for 2 HumanBot + 2 AI upgrade check on 2026-05-10; cleanupStatus=PASS orphanedPids=[].
- 2026-05-10: verified artifact `artifacts/mp/20260510-103350-two-humanbot-two-ai-upgrade-check`
- 2026-05-10: Headless two-process MP test cleaned up with cleanupStatus=PASS and orphanedPids=[].
- 2026-05-10: verified artifact `artifacts/mp/20260510-113305-wave-common-2hbot-2ai-headless`
- 2026-05-11: Headless 2 HumanBot + 2 AI MP cleanup completed with cleanupStatus=PASS and orphanedPids=[].
- 2026-05-11: verified artifact `artifacts/mp/20260510-170014-monster-healthbar-battle2-2hbot-2ai-headless`
- 2026-05-13: Headless 2 HumanBot + 2 AI MoveUnit smoke cleanup completed with cleanupStatus=PASS and orphanedPids=[].
- 2026-05-13: verified artifact `artifacts/mp/20260513-131556-two-humanbot-two-ai-smoke`
- 2026-05-14: Headless MP cleanup stayed clean for 2 HumanBot + 2 AI smoke and battle spawn command; cleanupStatus=PASS orphanedPids=[].
- 2026-05-14: verified artifact `artifacts/mp/20260514-035507-two-humanbot-two-ai-smoke`
- 2026-05-14: verified artifact `artifacts/mp/20260514-035543-battle-spawn-monster-command`
- 2026-05-14: Headless MP cleanup stayed clean after whole-ground spawn click fix; cleanupStatus=PASS orphanedPids=[].
- 2026-05-14: verified artifact `artifacts/mp/20260514-044306-two-humanbot-two-ai-smoke`
- 2026-05-14: verified artifact `artifacts/mp/20260514-044344-battle-spawn-monster-command`
- 2026-05-14: Headless cleanup stayed clean for 2 HumanBot + 2 AI after monster card tap suppression; cleanupStatus=PASS orphanedPids=[].
- 2026-05-14: verified artifact `artifacts/mp/20260514-051726-two-humanbot-two-ai-smoke`
- 2026-05-14: Verified headless 2 HumanBot + 2 AI cleanup PASS with orphanedPids empty.
- 2026-05-14: verified artifact `artifacts/mp/20260514-061112-two-humanbot-two-ai-smoke`
- 2026-05-14: Verified cleanup stayed PASS and orphanedPids=[] after long 2 HumanBot + 2 AI GameOver movement run and subsequent 33-round movement timeout run.
- 2026-05-14: verified artifact `artifacts/mp/20260514-064919-two-humanbot-two-ai-smoke`
- 2026-05-14: verified artifact `artifacts/mp/20260514-072736-two-humanbot-two-ai-smoke`
