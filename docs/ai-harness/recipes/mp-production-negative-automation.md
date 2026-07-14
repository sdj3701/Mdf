## mp-production-negative-automation: Prove normal builds contain no MPTest runtime behavior

Status: active
Pinned: true
Category: security
Created: 2026-05-05
Last used: 2026-07-14
Last verified: 2026-07-14
Use count: 5
Review after: 2026-08-03
Triggers: production automation safety, Phase 7 hardening, normal build proof
Applies to: `tools/harness/mp/run_production_negative_automation.py`, `mp_build_player --development_build false`
Verified by: see Verification section below; migrated from old Status: verified-local; artifacts/mp/20260714-124857-production-negative-automation/production-negative-automation.json
Replacement: none
Archive policy: never auto-archive pinned/protected recipe; review only by explicit human direction

Problem:
Static gates prove intent, but a normal non-development player must ignore all MPTest launch flags, not merely keep the automation HTTP port closed.

Recipe:
- Build a normal player with `unity-cli --project Mdfproject mp_build_player --development_build false --allow_debugging false`.
- Launch that player with `--mpTest`, `--mpAutoStart`, `--mpLoadGame`, a unique connection token, an automation port/token, and a short `--mpExitAfterSeconds`.
- Poll `http://127.0.0.1:<port>/ping` with the token.
- Do not expect the release player to honor `--mpExitAfterSeconds`. Observe it beyond that deadline, then terminate it externally through the process cleanup helper.
- PASS only when `build-metadata.json` reports `developmentBuild=false`, `/ping` never responds, `MPTestBootstrap` is absent from executable managed assemblies or IL2CPP metadata and from the Player log, no autostart marker appears, the unique connection token is absent from PlayerPrefs, the player does not auto-exit, and cleanup proves `orphanedPids=[]`. Unity 2021 may retain inert Editor type-layout strings in `globalgamemanagers.assets`; report those as `metadataCacheMatches`, but do not confuse them with executable release code.
- Store `production-negative-automation.json`, redacted launch command, stdout/stderr, copied `Player.log` when available, and build wrapper metadata under `artifacts/mp/<timestamp>-production-negative-automation/`.

Verification:
- `artifacts/mp/20260505-110846-production-negative-automation/production-negative-automation.json` reported `success=true`, `productionAutomationDisabled=true`, `developmentBuild=false`, `/ping` did not respond, and the player exited with code `0`.
- The paired build metadata under `artifacts/builds/20260505-110846-production-negative/build-metadata.json` reported `options=None`, `developmentBuild=false`, and `allowDebugging=false`.
- The historical artifact predates the full bootstrap/PlayerPrefs/autostart/auto-exit assertions. Run the updated script before marking this expanded recipe verified.

Pitfalls:
- Do not reuse Development player builds for this proof.
- Do not treat a missing token failure from a Development build as production-negative proof; the build metadata must show `developmentBuild=false`.
- An early process exit is a failure: a correctly isolated release ignores the MPTest auto-exit flag and remains alive until external cleanup.
- E2E helpers that auto-select the latest build must skip `developmentBuild=false` metadata, otherwise a production-negative proof build can accidentally become the next multiplayer test player.

Lifecycle notes:
- 2026-07-14: verified artifact `artifacts/mp/20260714-124857-production-negative-automation/production-negative-automation.json`
