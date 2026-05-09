## battle-command-foundation-authority-guards: Require explicit validation before battle execution

Status: active
Pinned: false
Category: battle
Created: 2026-05-06
Last used: 2026-05-06
Last verified: 2026-05-06
Use count: 1
Review after: 2026-08-04
Triggers: battle command foundation, client-requested battle commands, opponent resolution, State Authority validation
Applies to: `ServerBattleCommandExecutor`, `BattleCommandValidator`, future `BattleSpawnMonsterCommand`, future `UseMagicScrollCommand`
Verified by: see Verification section below; migrated from old Status: verified-local
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
A generic battle command executor can accidentally become a gameplay bypass if it accepts a missing validation delegate, or if shared opponent-resolution helpers rebuild battle pairings from client/presentation code. That makes future spawn/scroll commands look command-based while skipping source ownership, player role, opponent, target, inventory, or cost checks.

Recipe:
- Reject executable battle commands when validation or execution delegates are missing; do not treat `null` validation as accepted.
- Reject `PresentationOnly` scope before any persistent execution path.
- Require `GameManagers` State Authority and Battle1/Battle2 phase before the server authority executor can run command-specific validation.
- Resolve opponents from the published battle snapshot for client/presentation paths.
- Permit mutating opponent fallback such as `GetBattleOpponent()` only under `ServerAuthorityOnly` plus `HasStateAuthority`.
- Keep `RpcInfo.Source` checks as transient live authorization only; durable gameplay identity remains MDF `playerId` and connection-token state.
- Add edit-mode source guards for the reject codes and the authority-only opponent fallback so later command phases cannot weaken the foundation silently.

Verification:
- `python tools/harness/precommit.py --all` reported `0 errors, 0 warnings`.
- `unity-cli --project Mdfproject editor refresh --compile` completed.
- `unity-cli --project Mdfproject console --type error --stacktrace user` returned `[]`.
- `unity-cli --project Mdfproject test --mode EditMode` passed 13/13 after adding the foundation guards.
