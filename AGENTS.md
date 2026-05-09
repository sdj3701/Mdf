# AGENTS.md

## Project

This repo is MDF, a Unity 2021.3.45f1 multiplayer defense/auto-battle project using Photon Fusion 2.0.9 and unity-cli.
Unity root is `Mdfproject`; focus gameplay work under `Mdfproject/Assets/Scripts`.
It is not DOTS/ECS unless the repo later adds DOTS code.
For a fresh clone, use `docs/ai-harness/developer-onboarding.md`.

## Read first

For harness, multiplayer, or feature work, read:

- `docs/ai-harness/index.md`
- `docs/ai-harness/project-structure.md`
- `docs/ai-harness/feature-implementation-loop.md`
- `docs/ai-harness/fusion-sync-rules.md`
- `docs/ai-harness/mp-test-protocol.md`
- `docs/ai-harness/automation-server-contract.md`
- `docs/ai-harness/state-snapshot-schema.md`
- `docs/ai-harness/unity-cli-recipes.md`
- `docs/ai-harness/host-migration-test-plan.md`
- `docs/ai-harness/learned-recipes.md`
- `.agent/rules/projectrull.md`

## Feature requests

Short gameplay/content/AI/UI/network requests, including Korean equivalents of add unit, make scroll, improve AI, or change augment, automatically use `docs/ai-harness/content-development-routine.md` and the `mdf-content-feature` or `feature-loop` skill. Do not ask for the long template; choose the smallest relevant matrix profile and require command outputs, artifact paths, `cleanupStatus=PASS`, and `orphanedPids=[]` for E2E PASS.

## Unity workflow

Use `unity-cli --project Mdfproject` whenever possible.

After C# changes:

- `unity-cli --project Mdfproject status`
- `unity-cli --project Mdfproject editor refresh --compile`
- `unity-cli --project Mdfproject console --type error --stacktrace user`

After Unity YAML asset changes:

- `unity-cli --project Mdfproject reserialize <changed .prefab/.unity/.asset/.mat/.controller paths>`
- `unity-cli --project Mdfproject editor refresh --compile`
- `unity-cli --project Mdfproject console --type error --stacktrace user`

When relevant, run:

- `unity-cli --project Mdfproject test --mode EditMode`
- `unity-cli --project Mdfproject test --mode PlayMode`

## Multiplayer invariants

- State Authority owns match flow, HP, gold, walls, shop, augment, placement, monster spawn, battle pairing, rewards, AI fill, and game-over state.
- Clients may request actions, but authority must validate `playerId`, cost, phase, grid, ownership, cooldown, target, and command sequence.
- `PlayerRef` is connection identity, not durable gameplay identity. Durable identity is project `playerId` plus connection token/reconnect cache.
- RPCs are events/requests. Persistent state must be `[Networked]`, deterministic reconstructed state, or captured in snapshots.
- Host Migration changes must check `NetworkManager`, `HostMigrationHandler`, `GameManagers.MigrationRecovery`, `PlayerManager`, and `FieldManager` together.
- Do not edit `Mdfproject/Assets/Photon/Fusion/**`, Firebase, TMP, Toon Shader, Samples, or generated vendor files unless explicitly approved.

## Harness invariants

- Test automation must never run in normal production builds.
- Build automation requires `UNITY_EDITOR || DEVELOPMENT_BUILD`, `--mpTest`, loopback bind, and per-run token auth.
- Use `[MPTEST]` logs for timelines and state snapshots for assertions.
- Test both directions: Editor Host + Build Client and Build Host + Editor Client.
- Do not claim PASS unless command output and artifacts prove it.

## Knowledge capture

Before Unity CLI, screenshot, build, asset, Photon, or multiplayer test work, search learned recipes; touch lifecycle metadata when used/verified and report when no reusable recipe was found.
