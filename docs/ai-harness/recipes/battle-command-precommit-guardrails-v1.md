## battle-command-precommit-guardrails-v1: Block clear battle command bypasses

Status: active
Pinned: false
Category: AI/HumanBot, battle
Created: 2026-05-06
Last used: 2026-06-01
Last verified: 2026-05-06
Use count: 2
Review after: 2026-08-04
Triggers: future edits near monster spawn, magic scrolls, strategic skills, HumanBot, battle command validators
Applies to: `tools/harness/precommit.py`, battle command architecture, AI/HumanBot policies, scroll presentation, manual skill policy
Verified by: see Verification section below; migrated from old Status: verified
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
Battle command architecture can regress even when E2E scripts exist if a future edit reintroduces direct AI monster spawn, client-side scroll gameplay effects, manual skill bypasses, or HumanBot-as-AI registration. These are cheaper and safer to catch statically before Unity/E2E runs.

Recipe:
- BLOCK clear unsafe patterns:
  - HumanBot/test human files registering or attaching `AIPlayerController`.
  - AI policy files calling `SpawnMonsterAtPositionAsync`, `.ActivateSkill(`, or `ApplyEffect(` directly.
  - Testing HumanBot files calling `.ActivateSkill(` or `ApplyEffect(` directly.
  - `RPC_BroadcastMagicScrollUsed`, `CreateScrollPresentationLocal`, or `PlayPresentation` calling gameplay effect or inventory-consumption methods.
  - battle command classes missing required authority validation/execution tokens.
- WARN review-only patterns:
  - direct low-level `SpawnMonsterAtPositionAsync` outside the approved low-level mechanism/command files.
  - legacy `RPC_RequestSpawnMonster` or `RPC_RequestUseMagicScroll` calls.
  - direct scroll gameplay/inventory or attack-pool consumption outside command executor files.
- Keep precommit output ASCII-only; Windows PowerShell may run under CP949.
- Keep false positives low by excluding Editor test source from production bypass checks and stripping `#if false` disabled legacy blocks before scanning.

Verification:
- `python tools/harness/precommit.py --self-test` passed guardrail fixtures for HumanBot AI registration, scroll presentation gameplay, and AI direct monster spawn.
- `python tools/harness/precommit.py --all` reported `0 errors, 0 warnings`.
