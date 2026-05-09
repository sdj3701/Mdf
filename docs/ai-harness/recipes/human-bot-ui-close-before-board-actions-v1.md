## human-bot-ui-close-before-board-actions-v1: Match player shop-close routine before placement

Status: active
Pinned: false
Category: AI/HumanBot
Created: 2026-05-08
Last used: 2026-05-08
Last verified: 2026-05-08
Use count: 1
Review after: 2026-08-06
Triggers: HumanBot unit purchase followed by maze wall placement or unit movement while the shop UI remains open
Applies to: `MPTestHumanBotDriver`, `ShopUIController`, HumanBot prepare progression
Verified by: see Verification section below; migrated from old Status: verified
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Recipe:
- Keep this in the test-only HumanBot path under `UNITY_EDITOR || DEVELOPMENT_BUILD`; do not change normal production quit or gameplay behavior.
- After `PrepareDecisionPolicy` chooses a command but before `HumanClientCommandEmitter.TryEmit`, close the visible local `ShopUIController` for board actions.
- Gate the close routine to `CommandType.PlaceWall` and `CommandType.MoveUnit`; buy, reroll, and augment commands should keep their normal UI behavior.
- Use `ShopUIController.SetContentVisibility(false)` so the HUD toggle state and CanvasGroup/raycast blocking are updated through the same UI API as user-driven shop closing.
- Log `[MPTEST] phase=human_bot_ui code=shop_close_before_board_action` with `commandType`, `playerId`, `shopUiPresent`, `wasVisible`, and `closed` for artifact review.

Verification:
- `unity-cli --project Mdfproject editor refresh --compile` PASS.
- `unity-cli --project Mdfproject console --type error --stacktrace user` returned `[]`.
- `unity-cli --project Mdfproject test --mode EditMode --filter MPTestHarnessEditModeTests` PASS, `44/44`.
- `unity-cli --project Mdfproject test --mode EditMode` PASS, `44/44`.
- `python tools/harness/precommit.py --all` PASS, `0 errors, 0 warnings`.
- Built Development player `artifacts/builds/20260508-003828/MDF-MPTest.exe`.
- `python tools/harness/mp/run_human_bot_prepare_progression.py --seed 1001 --bot-persona maze --bot-max-commands 8 --min-commands 6 --player-path artifacts/builds/20260508-003828/MDF-MPTest.exe --headless-player --orphan-threshold 0 --cleanup-timeout-seconds 20` PASS at `artifacts/mp/20260508-004010-human-bot-prepare`.
- The client Player log recorded four `BuyUnit` commands followed by `phase=human_bot_ui code=shop_close_before_board_action result=pass commandType=MoveUnit wasVisible=True closed=True`, then `MoveUnit`.
- `result.json` recorded `cleanupStatus=PASS`, `orphanedPids=[]`, `headlessPlayer=true`, and final live `MDF-MPTest.exe` count returned to 0.
