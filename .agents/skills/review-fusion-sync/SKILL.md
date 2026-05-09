---
name: review-fusion-sync
description: Review MDF Photon Fusion authority, RPC, state sync, reconnect, and host migration risks.
---

# Fusion sync review

Check changed files against `docs/ai-harness/fusion-sync-rules.md`.

Focus:

- client-trusted playerId/gold/HP/walls/shop/augment/spawn
- `RpcSources.All` without `RpcInfo` or authority validation
- persistent state represented only by RPC side effects
- new command missing `CommandType` and serialization
- PlayerRef used as durable ownership
- host migration changes touching only one recovery file
- AI/HumanBot strategic monster spawn bypassing `BattleSpawnMonsterCommand`
- magic scroll gameplay effects or inventory consumption from presentation RPC/helpers instead of `UseMagicScrollCommand`
- manual/strategic skill decisions bypassing `ActivateSkillCommand`
- HumanBot/test human peers registering or attaching `AIPlayerController`
- battle command classes missing explicit State Authority validation for source, role, opponent, target, inventory/pool revision, and execution scope

Return PASS/FAIL/NEEDS_MANUAL_CHECK with file:line evidence.
