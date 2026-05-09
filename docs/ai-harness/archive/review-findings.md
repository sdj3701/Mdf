# MDF Harness Overlay v2 Review Findings

Historical archive note, 2026-05-08: this file records an earlier overlay review. It is not a current source-of-truth workflow document; use `../content-development-routine.md`, `../verification-profile-selector.md`, `../feature-implementation-loop.md`, `../mp-test-protocol.md`, `../state-snapshot-schema.md`, and `../learned-recipes.md` for current work.

## Verdict

The first MDF overlay was directionally correct, but it was not yet ideal for a user who wants to overwrite files and then drive Codex only through phase prompts.

## Fixed in v2

1. **Custom agents were missing.**
   - v1 prompts asked Codex to use reviewers, but `.codex/agents/*.toml` did not exist.
   - v2 adds `mdf_code_mapper`, `mdf_fusion_reviewer`, `mdf_unity_verifier`, `mdf_mp_test_runner`, and `mdf_asset_guard`.

2. **Hook commands were relative.**
   - v1 hooks used `python .codex/hooks/...`, which can break when Codex starts in `Mdfproject/` or another subdirectory.
   - v2 resolves hooks from the Git root with `$(git rev-parse --show-toplevel)`.

3. **Phase prompts were too short.**
   - v1 phase prompts described goals but did not include enough stop conditions, verification commands, artifacts, and PASS/FAIL rules.
   - v2 adds `docs/ai-harness/codex-full-phase-prompts.md` with copy-paste prompts through final audit.

4. **The feature implementation loop was implicit.**
   - The user’s primary goal is not merely installing a harness; it is making Codex implement a feature, run multiplayer Editor/Build E2E, inspect logs/screenshots, fix, and repeat.
   - v2 adds `feature-implementation-loop.md` and a ready-to-use feature prompt template.

5. **Host Migration proof was under-specified.**
   - v1 mentioned Host Migration but did not define what evidence is required before claiming PASS.
   - v2 adds `host-migration-test-plan.md` with feasibility-first and E2E proof gates.

6. **Precommit needed broader MDF-specific checks.**
   - v2 keeps harsh checks to safe BLOCK rules and moves semantic concerns to WARN, matching the harness principle of avoiding false-positive BLOCKs.

7. **Overlay validation was missing.**
   - v2 adds `tools/harness/validate_overlay.py` to check referenced docs, agents, skills, and line-count constraints.

## Still intentionally left for Codex to implement

The overlay is a planning/instruction/guardrail layer. It does not implement runtime C# harness code yet. Codex should implement these phase-by-phase:

- `Mdfproject/Assets/Scripts/Testing/MP/MPTestCommandLine.cs`
- `MPTestBootstrap.cs`
- `MPTestLogger.cs`
- `MPTestStateSnapshot.cs`
- `MPTestAssertions.cs`
- `MPTestAutomationServer.cs`
- `MPTestMainThreadDispatcher.cs`
- `Mdfproject/Assets/Scripts/Testing/MP/Editor/MPTestUnityCliTools.cs`
- `BuildAutomation.cs`
- `tools/harness/mp/*.py`

Do not skip compile/test/E2E artifact proof when these are added.
