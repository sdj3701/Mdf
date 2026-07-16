## human-bot-ui-close-before-board-actions-v1: Mirror accepted HumanBot commands in prepare UI

Status: active
Pinned: false
Category: AI/HumanBot
Created: 2026-05-08
Last used: 2026-07-15
Last verified: 2026-07-15
Use count: 9
Review after: 2026-08-06
Triggers: HumanBot commands are accepted while shop or augment UI remains visually stale or blocks field monitoring
Applies to: `MPTestHumanBotDriver`, prepare UI Toolkit/legacy adapters, shop/augment synchronization, HumanBot prepare progression
Verified by: see Verification section below; migrated from old Status: verified; artifacts/mp/20260715-034414-human-bot-prepare; artifacts/mp/20260715-035255-human-bot-prepare/human-bot-ui-evidence.json; artifacts/mp/20260715-035915-two-humanbot-two-ai-smoke/two-humanbot-two-ai-assertions.json; artifacts/mp/20260715-040340-two-humanbot-two-ai-smoke/two-humanbot-two-ai-assertions.json
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Recipe:
- Keep the HumanBot adapter and diagnostics under `UNITY_EDITOR || DEVELOPMENT_BUILD`; production players must not gain an automation entry point.
- Apply UI presentation only after `HumanClientCommandEmitter.TryEmit` accepts the command. A rejected command must leave the UI untouched.
- Reuse shared presentation methods rather than synthesizing pointer clicks: `BuyUnit` immediately marks the selected card pending/disabled, `SelectAugment` closes the choice panel, and board actions or bot stop dismiss transient prepare panels.
- Let purchase confirmation project the authority result into the peer-local shop cache and refresh from the durable network shop snapshot. Pending presentation is immediate feedback, while sold state remains authority-owned and idempotent.
- Clear locally presented augment choices after authority confirmation and suppress delayed/retried synchronization for an already submitted choice so the panel cannot reopen stale content.
- Support both the active UI Toolkit surface and the legacy Canvas fallback through the same semantic actions.
- Emit `[MPTEST] phase=human_bot_ui` records with surface, action, panel visibility, slot index, pending/sold/enabled state; expose aggregate counters through bot status and fail the progression harness when presentation failures occur.

Verification:
- `unity-cli --project Mdfproject editor refresh --compile` PASS.
- `unity-cli --project Mdfproject console --type error --stacktrace user` returned `[]`.
- Targeted `MPTestHarnessEditModeTests` PASS, `105/105`; full EditMode PASS, `458/458`; PlayMode PASS, `13/13`.
- `python tools/harness/precommit.py --all` returned `0 errors`; its 20 warnings are existing vendor/demo or static-rule noise.
- StandaloneWindows64 Development build succeeded at `artifacts/builds/20260715-humanbot-ui-sync-win64-v2/MDF-MPTest.exe`; launch smoke cleanup PASS with no orphaned processes.
- Graphical two-peer HumanBot progression PASS at `artifacts/mp/20260715-034414-human-bot-prepare` with 8 UI presentation events: 6 shop purchases, 1 augment selection, 1 panel dismissal, and 0 failures.
- Client logs show every accepted purchase as `shop_purchase_pending` with `shopSlotPending=True` and `shopSlotEnabled=False`; subsequent decisions observe sold slots increasing from 0 through 5. The accepted augment selection records `augmentVisible=False`.
- `human-bot-ui-evidence.json` records final `shopVisible=false` and `augmentVisible=false`; `cleanup-report.json` records `cleanupStatus=PASS` and `orphanedPids=[]`.

Lifecycle notes:
- 2026-07-15: Accepted HumanBot commands now mirror shop/augment UI state; graphical two-peer progression passed with 8 UI events and zero failures.
- 2026-07-15: verified artifact `artifacts/mp/20260715-034414-human-bot-prepare`
- 2026-07-15: Launching a visible graphical HumanBot prepare run for live user observation.
- 2026-07-15: Visible run: 7 shop presentations, 1 augment close, 4 panel dismissals, 0 UI failures.
- 2026-07-15: verified artifact `artifacts/mp/20260715-035255-human-bot-prepare/human-bot-ui-evidence.json`
- 2026-07-15: Validating accepted-command UI presentation simultaneously on host and client HumanBots.
- 2026-07-15: Host and client HumanBots each closed augment UI, presented three disabled purchase slots, and reported zero UI failures.
- 2026-07-15: verified artifact `artifacts/mp/20260715-035915-two-humanbot-two-ai-smoke/two-humanbot-two-ai-assertions.json`
- 2026-07-15: Repeating simultaneous host/client HumanBot UI presentation validation.
- 2026-07-15: Both Host and Client logged 1 augment close, 5 purchase presentations, and zero UI failures.
- 2026-07-15: verified artifact `artifacts/mp/20260715-040340-two-humanbot-two-ai-smoke/two-humanbot-two-ai-assertions.json`
