# Knowledge Capture

## Goal

When Codex discovers a reusable method, command, workaround, or failure signature, it must write it down so later sessions do not rediscover it.

## Capture when

- A unity-cli command requires a specific flag or fallback.
- A screenshot/build/test command works only in a certain Unity 2021 form.
- A Fusion/Firebase/Addressables issue has a repeatable diagnosis.
- A multiplayer E2E timing wait becomes stable.
- A Host Migration/reconnect artifact pattern proves useful.
- A common false positive appears in precommit/hook checks.

## Do not capture

- Secrets, tokens, Photon AppId, raw connection tokens.
- One-off exploratory failures with no reusable conclusion.
- Huge raw logs.
- Unverified guesses.

## Destinations

- Short reusable recipes: `docs/ai-harness/learned-recipes.md`
- Unity command recipes: `docs/ai-harness/unity-cli-recipes.md`
- Fusion/network invariants: `docs/ai-harness/fusion-sync-rules.md`
- E2E test steps: `docs/ai-harness/mp-test-protocol.md`
- Automation API changes: `docs/ai-harness/automation-server-contract.md`
- Repeated workflow: `.agents/skills/<skill>/SKILL.md`

## Recipe template

```md
## recipe-id: Short title

Status: verified|candidate|deprecated
Last verified: YYYY-MM-DD
Applies to: Unity 2021.3.45f1, MDF, tool/version
Triggers: keywords

Problem:
...

Recipe:
1. ...

Verification:
- command/result/artifact

Pitfalls:
- ...
```
