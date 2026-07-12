## magic-scroll-command-authority-split: Keep scroll gameplay out of presentation RPCs

Status: active
Pinned: false
Category: battle
Created: 2026-05-06
Last used: 2026-07-12
Last verified: 2026-05-31
Use count: 6
Review after: 2026-08-04
Triggers: magic scroll use, client-side VFX broadcast, scroll inventory hash drift, buff/status/zone side effects
Applies to: `UseMagicScrollCommand`, `GameManagers.RPC_RequestUseMagicScrollCommand`, `PlayerManager.OwnedScrolls`, `ScrollCaster`, `MPTestStateSnapshot`
Verified by: see Verification section below; migrated from old Status: compile-verified; battle scroll E2E still needs a dedicated runner; artifacts/mp/20260530-185136-matrix/20260530-185141-magic-scroll-command; artifacts/mp/20260530-190027-matrix/20260530-190032-magic-scroll-command
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
Magic scroll presentation used to create a `ScrollCaster` on every peer and call `SkillEffect.ApplyEffect`. Damage/heal effects often self-guard on target authority, but buff, status, and zone effects can still create client-side durable behavior or snapshot drift if the broadcast path applies gameplay.

Recipe:
- Route scroll use through a State Authority battle command with `casterPlayerId`, stable `scrollSlotIndex`, target position, source reason, and the client's applied owned-scroll revision.
- Validate battle phase, caster source authority, attacker/defender battle roles, finite target, authoritative scroll slot, scroll skill data, and target domain before consumption.
- Consume the scroll only after validation on State Authority. If gameplay application fails, refund the same slot.
- Split `ScrollCaster` into `CastGameplay` and `PlayPresentation`; `CastGameplay` rejects non-authority network peers and `PlayPresentation` only instantiates VFX.
- Leave legacy name-based scroll RPCs as explicit deprecated rejects with `scroll_rejected` telemetry instead of silently returning.
- Track `ownedScrollsHash`, `ownedScrollRevision`, and `useMagicScrollSeq` in snapshots/comparisons so inventory and effect application drift are visible.

Verification:
- `python tools/harness/precommit.py --all` reported `0 errors, 0 warnings`.
- `unity-cli --project Mdfproject status` reported the Editor ready for `E:/UnityProjects/mdf/Mdfproject`.
- `unity-cli --project Mdfproject editor refresh --compile` completed compilation.
- `unity-cli --project Mdfproject console --type error --stacktrace user` returned `[]`.
- `unity-cli --project Mdfproject test --mode EditMode` passed `13/13`.
- `tools/harness/mp/run_magic_scroll_command.py` and `tools/harness/mp/run_human_bot_battle_progression.py` were not present yet, so scroll command sync still needs dedicated E2E harness implementation before claiming multiplayer artifact PASS.
- Owned scroll RPC sync loads assets asynchronously. Track the latest received revision and recheck it before and after every await so a stale payload cannot overwrite a newer inventory.
- Host Migration durable player snapshots must capture owned scroll asset refs/names plus `OwnedMagicScrollRevision`, restore them on the new State Authority, and resend them to clients before scroll-slot commands can be trusted.
- Duration buff/status/zone effects from scrolls are authority-only after the command split, but they are not yet durable Host Migration state. Treat post-scroll Host Migration/effect preservation as a Phase 6/10/12 blocker until active effect hashes and restore semantics exist.

Lifecycle notes:
- 2026-05-31: verified artifact `artifacts/mp/20260530-185136-matrix/20260530-185141-magic-scroll-command`
- 2026-05-31: verified artifact `artifacts/mp/20260530-190027-matrix/20260530-190032-magic-scroll-command`
