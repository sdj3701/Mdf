# MDF Codex Harness Overlay v2

Copy this overlay into the root of the MDF repository (`Mdf-feature-ai3`) and overwrite existing files when prompted.

This overlay contains:

- `AGENTS.md` under 70 lines
- cleaned `.agent/rules/projectrull.md`
- MDF-specific `docs/ai-harness/*`
- full copy-paste phase prompts in `docs/ai-harness/codex-full-phase-prompts.md`
- Codex custom agents under `.codex/agents`
- Codex hooks under `.codex/hooks`
- Codex skills under `.agents/skills`
- precommit and overlay validation tools under `tools/harness`
- placeholder folders for runtime MP harness C# implementation

## Apply

```bash
unzip MDF-codex-harness-overlay-v2-root.zip -d <repo-root>
cd <repo-root>
python tools/harness/validate_overlay.py
python tools/harness/precommit.py --self-test
```

Then paste the prompt in:

```text
docs/ai-harness/codex-first-prompt.md
```

After Phase 0/1, continue with one phase at a time from:

```text
docs/ai-harness/codex-full-phase-prompts.md
```

Do not ask Codex to run all phases at once unless you intentionally accept higher risk.
