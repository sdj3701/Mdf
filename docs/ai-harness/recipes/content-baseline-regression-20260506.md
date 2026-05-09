## content-baseline-regression-20260506: Freeze pre-content harness baseline

Status: active
Pinned: false
Category: feature-workflow
Created: 2026-05-06
Last used: 2026-05-06
Last verified: 2026-05-06
Use count: 1
Review after: 2026-08-04
Triggers: content development start, baseline freeze, full harness regression, latest Development player
Applies to: pre-content development baseline, Unity CLI, latest Development player, HumanBot/random-aware regression
Verified by: see Verification section below; migrated from old Status: verified-local
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
Before content work, freeze a known-good harness baseline with static checks, Unity compile/tests, the default matrix, HumanBot progression, progressed reconnect/disconnect, progressed Host Migration, and seed sweep. Record the exact build and artifact paths so later content regressions have a concrete comparison point.

Recipe:
- Use the latest Development player that matches the current C# code. For this baseline: `artifacts/builds/20260505-230726/MDF-MPTest.exe`.
- Run the baseline commands:
  - `python tools/harness/validate_overlay.py`
  - `python tools/harness/precommit.py --self-test`
  - `python tools/harness/precommit.py --all`
  - `unity-cli --project Mdfproject status`
  - `unity-cli --project Mdfproject editor refresh --compile`
  - `unity-cli --project Mdfproject console --type error --stacktrace user`
  - `unity-cli --project Mdfproject test --mode EditMode`
- Run the minimal multiplayer regression set with the same player path:
  - `python tools/harness/mp/run_matrix.py --case all --player-path artifacts/builds/20260505-230726/MDF-MPTest.exe`
  - `python tools/harness/mp/run_human_bot_prepare_progression.py --seed 1001 --player-path artifacts/builds/20260505-230726/MDF-MPTest.exe`
  - `python tools/harness/mp/run_human_bot_4p_progression.py --seed 2001 --player-path artifacts/builds/20260505-230726/MDF-MPTest.exe`
  - `python tools/harness/mp/run_progressed_same_token_reconnect.py --seed 3002 --player-path artifacts/builds/20260505-230726/MDF-MPTest.exe`
  - `python tools/harness/mp/run_progressed_disconnect_ai_takeover.py --seed 3001 --player-path artifacts/builds/20260505-230726/MDF-MPTest.exe`
  - `python tools/harness/mp/run_progressed_host_migration_e2e.py --seed 4001 --player-path artifacts/builds/20260505-230726/MDF-MPTest.exe`
  - `python tools/harness/mp/run_human_bot_seed_sweep.py --seeds 5101,5102,5103 --player-path artifacts/builds/20260505-230726/MDF-MPTest.exe`

Verification:
- `validate_overlay.py` passed, `precommit.py --self-test` passed, and `precommit.py --all` reported `0 errors, 0 warnings`.
- Unity status was ready, compile completed, console errors were `[]`, and EditMode passed `8/8`.
- Build metadata for `artifacts/builds/20260505-230726/MDF-MPTest.exe` reported `result=Succeeded`, `developmentBuild=true`, and `allowDebugging=true`.
- Matrix all passed at `artifacts/mp/20260505-231757-matrix` with `success=true` and all child cases exiting `0`.
- HumanBot prepare passed at `artifacts/mp/20260505-232230-human-bot-prepare` with `SelectAugment`, host `accepted_command`, durable selected augment hash delta, and random outcome matches.
- HumanBot 4p progression passed at `artifacts/mp/20260505-232307-human-bot-4p-progression` with three client bots, three `accepted_command` entries, and host-vs-client random-aware matches.
- Progressed same-token reconnect passed at `artifacts/mp/20260505-232355-progressed-same-token-reconnect` with `success=true` and `failures=[]`.
- Progressed disconnect AI takeover passed at `artifacts/mp/20260505-232449-progressed-disconnect-ai-takeover` with `success=true` and `failures=[]`.
- Progressed Host Migration passed at `artifacts/mp/20260505-232529-progressed-host-migration-e2e` with `accepted_command` entries for `SelectAugment` and `PlaceWall`, callback/token/resume proof, and durable pre/post `mismatches=[]`.
- Seed sweep passed at `artifacts/mp/20260505-232610-human-bot-seed-sweep` for seeds `5101,5102,5103` with `success=true` and `failures=[]`.
- `rg "result=fail|phase=error|parseError" <baseline artifact dirs>` found no matches.
- Scanning `*.Player.log` in the baseline artifact set for `InvalidOperationException`, `NullReferenceException`, `Error when accessing`, and `Failed to free` found no matches.

Pitfalls:
- The matrix command transcript can include cleanup-time `mp_stop` snapshot errors after the scenario already passed, for example `game.battleSnapshot:InvalidOperationException` and `players.allPlayers:InvalidOperationException` in the final Editor `mp_stop` response. Treat this as a cleanup snapshot caveat only when comparison JSONs, result JSONs, saved snapshots, and Player logs are clean.
- Do not update `unity-cli` mid-baseline just because it reports a newer connector version.

### unity-2021-test-framework-fallback

Status: active
Pinned: false
Category: unity-cli
Created: 2026-05-05
Last used: 2026-05-05
Last verified: 2026-05-05
Use count: 1
Review after: 2026-08-03
Triggers: test, EditMode, PlayMode, command line
Applies to: Unity 2021.3.45f1, Test Framework 1.1.33
Verified by: see Verification section below; migrated from old Status: verified-local
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
unity-cli test should be preferred, but older Unity/Test Framework setups may need Unity command-line fallback.

Recipe:
1. Try `unity-cli --project Mdfproject test --mode EditMode`.
2. Try `unity-cli --project Mdfproject test --mode PlayMode`.
3. If unavailable, use Unity `-runTests -batchmode -projectPath Mdfproject -testResults <xml> -testPlatform EditMode|PlayMode`.

Verification:
- `unity-cli --project Mdfproject test --mode EditMode --filter MPTestHarnessEditModeTests` passed 8/8 tests.
- `unity-cli --project Mdfproject test --mode PlayMode` executed successfully but found 0 tests in the current non-asmdef project layout.

Pitfalls:
- Do not assume Unity 6 test flags.
- Runtime scripts live in predefined `Assembly-CSharp`; PlayMode tests that reference them are not practical without introducing an asmdef boundary. Prefer EditMode tests for harness internals and E2E scripts for runtime multiplayer behavior unless the project adopts asmdefs.
- Treat `PlayMode total=0` as a documented limitation, not as runtime PASS by itself. Runtime multiplayer behavior is proven by Editor/build E2E artifacts until the project adopts test asmdefs.
