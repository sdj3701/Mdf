## magic-scroll-asset-effect-audit-v1: Classify scroll assets and compare active effect hashes

Status: active
Pinned: false
Category: AI/HumanBot, battle, asset
Created: 2026-05-06
Last used: 2026-05-06
Last verified: 2026-05-06
Use count: 1
Review after: 2026-08-04
Triggers: adding scroll AI metadata, editing scroll assets, auditing scroll buff/status/zone authority, Phase 6 scroll asset/effect audit
Applies to: `MagicScrollData`, scroll `.asset` files, `BuffManager`, `ZoneController`, `MPTestStateSnapshot`, `compare_state_snapshots.py`
Verified by: see Verification section below; migrated from old Status: compile-verified; dedicated battle scroll E2E still needs implementation
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
Scroll asset metadata and runtime duration effects span Unity YAML, authority-only gameplay code, and snapshot comparison. It is easy to classify assets correctly but miss reserialization evidence, or to guard effect application while leaving clear/remove/recalculate paths able to mutate client-local durable effect state.

Recipe:
- Add AI metadata to `MagicScrollData` as serialized enum/bool/float fields, then classify every scroll asset explicitly.
- After editing scroll `.asset` YAML, run `unity-cli --project Mdfproject reserialize <changed scroll asset paths>` and keep the command output in the phase evidence.
- Do not only guard `ApplyBuff` or `ApplyStatusEffect`. Guard `Update`, clear/remove, and stat recalculation entry points too, because `GameEvents.OnGameStateChanged` can fire on non-authority peers from render/UI flow.
- Snapshot active duration effects with semantic keys: target owner/data/star or boss id, effect asset/type, buckets for effect value/tick/damage/slow/range, and coarse zone position. Do not include raw Unity instance IDs.
- Compare `effects.activeBuffCount`, `effects.activeStatusCount`, `effects.zoneCount`, `effects.activeBuffHash`, `effects.activeStatusHash`, and `effects.zoneHash` in both C# assertions and `tools/harness/mp/compare_state_snapshots.py`.
- Treat active buff/status/zone Host Migration timer restoration as unproven until a post-scroll migration artifact exists. Hash comparison proves peer sync at capture time, not durable timer restore.

Verification:
- `unity-cli --project Mdfproject reserialize Mdfproject/Assets/GameData/Scrolls/Scroll_Berserk.asset Mdfproject/Assets/GameData/Scrolls/Scroll_BloodCurse.asset Mdfproject/Assets/GameData/Scrolls/Scroll_Heal.asset Mdfproject/Assets/GameData/Scrolls/Scroll_Stun.asset` returned all four paths.
- `python tools/harness/precommit.py --all` reported `0 errors, 0 warnings`.
- `unity-cli --project Mdfproject editor refresh --compile` completed compilation.
- `unity-cli --project Mdfproject console --type error --stacktrace user` returned `[]`.
- `unity-cli --project Mdfproject test --mode EditMode` passed `13/13`.
- `tools/harness/mp/run_magic_scroll_command.py` is still missing, so battle scroll E2E remains `NEEDS_IMPLEMENTATION`.
