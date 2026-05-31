## mp-progressed-reconnect-disconnect: Freeze progressed checkpoints without weakening comparisons

Status: active
Pinned: true
Category: reconnect, security, AI/HumanBot
Created: 2026-05-06
Last used: 2026-05-31
Last verified: 2026-05-31
Use count: 9
Review after: 2026-08-04
Triggers: HumanBot progressed checkpoint, same-token reconnect, disconnect AI takeover, randomized augment/shop/field state
Applies to: `tools/harness/mp/run_progressed_disconnect_ai_takeover.py`, `tools/harness/mp/run_progressed_same_token_reconnect.py`, Phase 22
Verified by: see Verification section below; migrated from old Status: verified-local; artifacts/mp/20260515-202032-matrix/20260515-202037-progressed-reconnect-after-battle/result.json; artifacts/mp/20260531-022409-matrix/20260531-022414-progressed-reconnect-after-battle/result.json; artifacts/mp/20260531-030739-matrix/20260531-030744-progressed-reconnect-after-battle/result.json; artifacts/mp/20260531-030739-matrix/20260531-030744-progressed-reconnect-after-battle/result.json; artifacts/mp/20260531-032610-matrix/20260531-032615-progressed-reconnect-after-battle/result.json; artifacts/mp/20260531-040328-matrix/20260531-040334-progressed-reconnect-after-battle/result.json
Replacement: none
Archive policy: never auto-archive pinned/protected recipe; review only by explicit human direction

Problem:
Progressed reconnect/disconnect tests need a synchronized HumanBot-created checkpoint before killing a client. Normal round timers and takeover AI can legitimately continue changing shop, augment, gold, wall, and unit state while the harness waits for reconnect, which makes checkpoint preservation impossible to assert. Same-token reconnect also needs a spare Photon transport slot, but raising `--mpMaxPlayers` normally creates an AI fill gameplay slot that can add random drift before the checkpoint.

Recipe:
- Use `--mpFreezeGameFlow` only in `--mpTest` Development/Editor runs when a test must preserve a checkpoint across disconnect/reconnect. It stops authority game-flow timer transitions and AI controller decisions; HumanBot still runs because it is a separate test-only driver.
- For same-token reconnect, use transport `--mpMaxPlayers 3` but pair it with `--mpDisableAiFill` and an expected gameplay player count of 2. This leaves a Photon slot for client B without creating an extra AI `PlayerManager`.
- Fail fast if the pre-bot checkpoint or HumanBot progressed checkpoint is not synchronized. Do not continue to kill/reconnect from a bad checkpoint.
- Preserve and compare target fingerprints across events: HP, gold, wall count, shop revision/hash, presented and selected augment hashes, field grid/wall/unit hashes.
- Selected and presented augment names must be published by State Authority into Networked snapshot arrays on `PlayerManager`; late-joining/reconnected clients use these arrays when local `AugmentManager` lists are empty.
- Require `targetComparison.success`, `fullComparison.success`, `roleStateComparison.success`, and `progressedPreservation.success` for same-token reconnect.

Verification:
- `artifacts/mp/20260505-183104-progressed-disconnect-ai-takeover` passed with `failures=[]`. Its progressed checkpoint proved `SelectAugment`, and `progressed-preservation-assertions.json` preserved the target shop, augment, and field hashes after AI takeover.
- `artifacts/mp/20260505-183145-progressed-same-token-reconnect` passed with `failures=[]`. `same-token-reconnect-assertions.json` showed target `playerId=1`, matching token hash, `hostTargetIsAI=false`, `hostTargetConnected=true`, full-world comparison success, role-state comparison success, and progressed preservation success.
- Build used `artifacts/builds/20260505-183018/MDF-MPTest.exe`.
- Persist the final runner verdict as `result.json` inside each artifact directory. Stdout `failures=[]` is useful but not enough for strict artifact review.

Pitfalls:
- A support human client consumes the spare Photon slot; do not use that as the same-token workaround.
- `--mpFreezeGameFlow` is a test-only preservation tool, not a gameplay fix. Do not use it in normal progression tests that are meant to prove battle/round advancement.
- A live `NotifyAugmentSelectedCommand` update is not enough for reconnect; reconnect clients need Networked selected/presented augment snapshot names.

Lifecycle notes:
- 2026-05-16: verified artifact `artifacts/mp/20260515-202032-matrix/20260515-202037-progressed-reconnect-after-battle/result.json`
- 2026-05-31: verified artifact `artifacts/mp/20260531-022409-matrix/20260531-022414-progressed-reconnect-after-battle/result.json`
- 2026-05-31: verified artifact `artifacts/mp/20260531-030739-matrix/20260531-030744-progressed-reconnect-after-battle/result.json`
- 2026-05-31: verified artifact `artifacts/mp/20260531-030739-matrix/20260531-030744-progressed-reconnect-after-battle/result.json`
- 2026-05-31: verified artifact `artifacts/mp/20260531-032610-matrix/20260531-032615-progressed-reconnect-after-battle/result.json`
- 2026-05-31: verified artifact `artifacts/mp/20260531-040328-matrix/20260531-040334-progressed-reconnect-after-battle/result.json`
