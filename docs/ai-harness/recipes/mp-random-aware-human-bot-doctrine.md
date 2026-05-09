## mp-random-aware-human-bot-doctrine: Compare replicated outcomes, not fixed random values

Status: active
Pinned: true
Category: host-migration, reconnect, AI/HumanBot, battle
Created: 2026-05-06
Last used: 2026-05-06
Last verified: 2026-05-06
Use count: 1
Review after: 2026-08-04
Triggers: HumanBot, random-aware, shop hash, augment hash, wallHash, Host Migration, reconnect
Applies to: Phase 18+ HumanBot progression, randomized shop/wall/augment/battle state, snapshot comparison
Verified by: see Verification section below; migrated from old Status: verified-from-code
Replacement: none
Archive policy: never auto-archive pinned/protected recipe; review only by explicit human direction

Problem:
MDF uses gameplay-critical randomness in shop rerolls, augment presentation, permanent wall selection, AI maze planning, and battle pairing. A command-only replay tape can issue the same commands against a different randomized state and therefore is not a reliable progression engine.

Recipe:
- Use a real connected human peer with test-only HumanBot policy to request commands through `CommandProcessor.RequestCommandExecution`.
- Keep the player human: do not attach `AIPlayerController` and do not register it in `ComponentRegistry`.
- Let State Authority decide persistent random outcomes.
- Compare the same player's replicated `shop.itemsHash`, `augment.presentedHash`, `field.wallHash`, `field.placedUnitsHash`, battle mapping hash, HP, gold, and command/revision fields across peers.
- Treat bot decisions, accepted commands, random outcomes, and checkpoint snapshots as diagnostic journals.

Verification:
- Code mapping confirmed `AIPlayerController` registers in `ComponentRegistry` and `MPTestStateSnapshot` reports `isAI` from `ComponentRegistry.Has<AIPlayerController>(playerId)`.
- Code mapping confirmed client command requests route through `CommandProcessor.RequestCommandExecution` to `PlayerManager.RPC_RequestCommandToServer`, where phase/cost/grid/shop/augment/source validation is enforced.
- Existing compare scripts already compare same-player shop and field hashes; Phase 18 docs extend that doctrine to HumanBot and augment/random outcome journals.
- Phase 19 core verification confirmed `MPTestHumanBotDriver` compiles, stays test-only, does not register `AIPlayerController`, and emits bot status/journal data under the snapshot `test` section.

Pitfalls:
- Do not assert fixed shop items, fixed wall coordinates, fixed augment names, or fixed battle pairings across different runs.
- Do not hide post-checkpoint mismatches as expected randomness.
- `--mpBotSeed` is a diagnostic handle until each gameplay RNG source is explicitly controlled.
- `test.bot.commandsIssued` counts HumanBot request submissions, not confirmed server acceptance. E2E tests must prove accepted commands through durable snapshot deltas, server accepted-command logs, or command/revision evidence.
- A HumanBot running on the host uses the State Authority broadcast path. To prove real client request validation, run `--mpHumanBot` on a connected client peer.
