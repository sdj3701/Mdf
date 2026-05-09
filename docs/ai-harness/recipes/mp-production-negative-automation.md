## mp-production-negative-automation: Prove normal builds do not expose /ping

Status: active
Pinned: true
Category: security
Created: 2026-05-05
Last used: 2026-05-05
Last verified: 2026-05-05
Use count: 1
Review after: 2026-08-03
Triggers: production automation safety, Phase 7 hardening, normal build proof
Applies to: `tools/harness/mp/run_production_negative_automation.py`, `mp_build_player --development_build false`
Verified by: see Verification section below; migrated from old Status: verified-local
Replacement: none
Archive policy: never auto-archive pinned/protected recipe; review only by explicit human direction

Problem:
Static gates prove intent, but the harness should also be able to produce an artifact showing a normal non-development player does not expose the automation server even when launched with `--mpTest`, `--mpAutomationPort`, and `--mpAutomationToken`.

Recipe:
- Build a normal player with `unity-cli --project Mdfproject mp_build_player --development_build false --allow_debugging false`.
- Launch that player with `--mpTest`, an automation port, a per-run token, and `--mpExitAfterSeconds`.
- Poll `http://127.0.0.1:<port>/ping` with the token.
- PASS only when `build-metadata.json` reports `developmentBuild=false`, `/ping` never responds, and the player exits without timeout.
- Store `production-negative-automation.json`, redacted launch command, stdout/stderr, copied `Player.log` when available, and build wrapper metadata under `artifacts/mp/<timestamp>-production-negative-automation/`.

Verification:
- `artifacts/mp/20260505-110846-production-negative-automation/production-negative-automation.json` reported `success=true`, `productionAutomationDisabled=true`, `developmentBuild=false`, `/ping` did not respond, and the player exited with code `0`.
- The paired build metadata under `artifacts/builds/20260505-110846-production-negative/build-metadata.json` reported `options=None`, `developmentBuild=false`, and `allowDebugging=false`.

Pitfalls:
- Do not reuse Development player builds for this proof.
- Do not treat a missing token failure from a Development build as production-negative proof; the build metadata must show `developmentBuild=false`.
- E2E helpers that auto-select the latest build must skip `developmentBuild=false` metadata, otherwise a production-negative proof build can accidentally become the next multiplayer test player.
