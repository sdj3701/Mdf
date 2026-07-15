# MDF Multiplayer Test Harness

This folder contains the runtime half of MDF's multiplayer automation harness. The
Python matrix under `tools/harness/mp` is the supported orchestration entry point.

## Build isolation

The automation server, bootstrap, HumanBot, stress drivers, snapshots, assertions,
and scene aliases compile only for `UNITY_EDITOR || DEVELOPMENT_BUILD`. Runtime
automation also requires the explicit `--mpTest` command-line flag.

`MPTestCommandLine`, `MPTestLogger`, and `MPTestHostMigrationEvents` remain as thin
facades because production gameplay code calls their diagnostics hooks. In a normal
release build they return disabled/default state, and diagnostic calls are removed
at the call site with `Conditional` attributes so log arguments are not evaluated.

## Supported responsibilities

- `MPTestBootstrap` starts explicitly requested test peers.
- `MPTestAutomationServer` exposes authenticated loopback-only automation routes.
- `MPTestStateSnapshot` and `MPTestAssertions` capture and compare durable state.
- `MPTestHumanBotDriver` uses the shared production decision policies and the
  validated human-client command path.
- `MPTestGracefulQuit` and the stress drivers provide deterministic cleanup and
  bounded performance scenarios.
- Editor-only Unity CLI tools live in `Editor/`; multiplayer scenarios themselves
  run through the Python matrix and runtime automation routes.

## Rules

- Never activate automation without both a development/editor build and `--mpTest`.
- Never log raw connection tokens, automation tokens, or Photon AppId.
- Do not add gameplay-only shortcuts that bypass State Authority validation.
- Do not register test peers as server AI players.
- A multiplayer result is PASS only with artifacts, `cleanupStatus=PASS`, and
  `orphanedPids=[]`.
