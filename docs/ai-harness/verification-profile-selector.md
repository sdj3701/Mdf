# MDF Verification Profile Selector

This page maps MDF feature categories to verification commands. It must stay aligned with `tools/harness/mp/run_matrix.py`.

Scene names in these profiles are canonical numbered scenes at runtime (`00_Title`, `01_MatchingLobby`, `02_JoinLobby`, `03_Game`) while CLI inputs may still use legacy aliases (`Title`, `MatchingLobby`, `TestMatching`, `JoinLobby`, `Game`). Harness assertions and runner code must use alias-aware scene comparison.

Use the helper when the file/category mapping is not obvious:

```powershell
python tools/harness/mp/select_verification_profile.py --category battle --category ai
python tools/harness/mp/select_verification_profile.py --changed-files Mdfproject/Assets/Scripts/Commands/Battle/UseMagicScrollCommand.cs
```

The helper outputs JSON with `requiredUnityCommands`, `recommendedProfiles`, `targetedCases`, `headlessDefault`, reserialize/reviewer flags, and reasons.

## Current Matrix Profiles

| Profile | Cases | Default headless in `run_matrix.py` | Purpose |
| --- | --- | --- | --- |
| `smoke` | `editor-host-build-client`, `build-host-editor-client`, `build-host-build-client` | Yes | Cheap Editor/Build launch, session, Game scene, and basic snapshot readiness. |
| `regression` | `smoke`, `ai-fill-smoke`, `disconnect-ai-takeover`, `same-token-reconnect`, `four-player-smoke`, `human-bot-prepare` | No | Broader pre-long regression. |
| `battle` | `battle-spawn-monster-command`, `magic-scroll-command`, `human-bot-battle-progression` | No | Battle command and battle-visible gameplay proof. |
| `lifecycle` | `progressed-reconnect-after-battle`, `progressed-disconnect-after-battle`, `progressed-host-migration-after-battle` | No | Reconnect, disconnect/AI takeover, and Host Migration after an early battle checkpoint. |
| `long` | `human-bot-3round-progression` | Yes | Bounded 3-round progression. |
| `endurance` | `human-bot-game-to-end` | Yes | Explicit opt-in GameOver or bounded timeout/stall classification. |
| `long-lifecycle` | `3round-reconnect`, `3round-disconnect-ai-takeover`, `3round-host-migration` | Yes | Lifecycle insertion after 3-round progression. |
| `random-aware` | `human-bot-prepare`, `human-bot-4p-progression`, `progressed-reconnect-after-battle`, `progressed-disconnect-after-battle`, `progressed-host-migration-after-battle`, `human-bot-seed-sweep` | No | Randomized Prepare/progression and same-player random outcome proof. |
| `full-regression` | `regression`, `battle`, `lifecycle`, `long` | Yes | Merge or large-change coverage that includes long progression. |
| `nightly` | `regression`, `battle`, `lifecycle`, `battle-seed-sweep`, `human-bot-seed-sweep` | Yes | Current pre-long nightly set; excludes `long` and `endurance`. |

## Baseline Commands

For C# changes:

```powershell
python tools/harness/precommit.py --all
unity-cli --project Mdfproject status
unity-cli --project Mdfproject editor refresh --compile
unity-cli --project Mdfproject console --type error --stacktrace user
unity-cli --project Mdfproject test --mode EditMode
```

For Unity YAML asset changes:

```powershell
unity-cli --project Mdfproject reserialize <changed .prefab/.unity/.asset/.mat/.controller paths>
unity-cli --project Mdfproject editor refresh --compile
unity-cli --project Mdfproject console --type error --stacktrace user
```

## Selection Rules

| Change category | Minimum selector result |
| --- | --- |
| `code-only/internal` | Precommit plus Unity status/compile/console and relevant Unity tests. |
| `Unity asset/content` | Reserialize changed assets plus compile/console; use asset guard when prefab/scene/material/ScriptableObject risk exists. |
| `multiplayer-visible` | `smoke` or a targeted case proving the changed behavior. |
| `command/authority` | `smoke` plus targeted command case; use `mdf_fusion_reviewer`. |
| `battle-visible` | `battle`. |
| `AI/HumanBot` | Targeted HumanBot prepare or battle case; add `random-aware` if significant. |
| `prepare-phase` | Targeted HumanBot prepare; add `random-aware` for randomized outcomes. |
| `random outcome` | `random-aware`. |
| `persistent state` | `lifecycle`; use snapshot comparison proof. |
| `reconnect/Host Migration sensitive` | `lifecycle`; use `long-lifecycle` if the request specifically needs a 3-round checkpoint. |
| `long-progression sensitive` | `long`. |
| `endurance/game-to-end sensitive` | `endurance` only when explicitly requested. |
| Merge or large change | `full-regression`; do not use for small local changes by default. |

Do not automatically choose `endurance`. Do not automatically choose `full-regression` for small changes. `nightly` must match `run_matrix.py` and must not be treated as `full-regression`.

Use `full-regression` only when the request is a merge or large-change verification request, or when local analysis shows several independent gameplay surfaces changed together.

## E2E PASS Requirements

An E2E PASS requires:

- Artifact path.
- `cleanupStatus=PASS`.
- `orphanedPids=[]`.
- No `[MPTEST] phase=error` unless explicitly classified as environment-only and not claimed PASS.
- Snapshot comparison success for the scenario scope.
- Screenshots captured or explicitly skipped because `headlessPlayer=true` and screenshots were not assertions.

If the orphan pressure gate blocks, stop and report `NEEDS_ENVIRONMENT` instead of running heavier profiles.
