## ui-toolkit-panel-raycaster-field-input-v1: Do not treat blank UI Toolkit panels as board blockers

Status: active
Pinned: false
Category: UI, input, multiplayer
Created: 2026-06-01
Last used: 2026-06-01
Last verified: 2026-06-01
Use count: 2
Review after: 2026-08-30
Triggers: manual Host wall placement, UI Toolkit PanelRaycaster, GamePrepareRuntimePanelSettings, PlayerRankingRuntimePanelSettings, field input blocked, MdfInput
Applies to: `MdfInput`, runtime UI Toolkit overlays, manual board placement
Verified by: `python tools/harness/precommit.py --all`; `unity-cli --project Mdfproject editor refresh --compile`; `unity-cli --project Mdfproject console --type error --stacktrace user`; `unity-cli --project Mdfproject test --mode EditMode --filter MPTestHarnessEditModeTests`; `python tools/harness/mp/build_player.py --output-dir artifacts/builds/mptest-current --cleanup-timeout-seconds 20 --orphan-threshold 0`; artifacts/mp/20260601-034810-manual-host-wall-click/result.json
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
Runtime UI Toolkit panels can show up in `EventSystem.RaycastAll` as a `PanelRaycaster` GameObject named after the panel settings, even when the pointer is over empty board space. If `MdfInput` treats that raycast hit as normal blocking UI, manual placement modes can show previews but never receive the click. The failure signature is `[ManualWall] ui-block ... GamePrepareRuntimePanelSettings:blocks=True ... module=PanelRaycaster` with unchanged `wallCount` and `wallHash`.

Recipe:
- Keep actual UI control blocking in each Toolkit controller using panel-space hit tests such as `IsPointerOverBlockingElement`.
- Treat the Toolkit panel raycaster object itself as a Toolkit raycast object or field passthrough, not as blocking gameplay UI.
- Recognize the generated raycaster by the runtime `PanelSettings.name`, for example `GamePrepareRuntimePanelSettings` or `PlayerRankingRuntimePanelSettings`.
- For manual wall checks, verify both the visual screenshot and durable state: `wallCount` decreases, `destructibleWallCount` increases, and `wallHash` changes.

Verification:
- Before the fix, `artifacts/mp/20260601-034316-manual-host-wall-click/result.json` failed with `manual_wall_click_did_not_change_wall_state`, `wallCount=15`, and unchanged `wallHash`; logs showed every field click blocked by `GamePrepareRuntimePanelSettings`.
- After the fix, `artifacts/mp/20260601-034810-manual-host-wall-click/result.json` reported `success=true`; the first field click changed `wallCount` from 15 to 14, `destructibleWallCount` from 0 to 1, and `wallHash` from `sha256:36bef41e79ea5daffef99cf75252c388d8bd33782b39d7fb761b2f4be05b2845` to `sha256:2382b613ad78422b39162f1debb8d51c6cdccc35c98572976d978a92609e0c90`.

Pitfalls:
- Do not disable UI Toolkit picking globally for actual buttons or cards. The panel raycaster must pass through empty space, while cards/buttons still block and handle pointer events.
- Screenshots alone are not enough because a purple preview can appear even when placement clicks are blocked. Use state snapshots and `[PlaceWallCommand] SUCCESS` logs.

Lifecycle notes:
- 2026-06-01: Captured Host graphical repro and fix proof in `artifacts/mp/20260601-034316-manual-host-wall-click` and `artifacts/mp/20260601-034810-manual-host-wall-click`.
