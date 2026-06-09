## unity-ui-toolkit-overlay-screencapture-v1: Capture runtime UI Toolkit overlays

Status: active
Pinned: false
Category: unity-cli
Created: 2026-05-15
Last used: 2026-06-05
Last verified: 2026-06-05
Use count: 21
Review after: 2026-08-13
Triggers: UI Toolkit overlay, UIDocument, Game View screenshot, ScreenCapture, prepare UI visual QA
Applies to: Mdfproject runtime UI Toolkit overlays and Game View visual proof
Verified by: Mdfproject/artifacts/screenshots/game-augment-offset-screen-capture.png; Mdfproject/artifacts/screenshots/game-character-selection-offset-clean.png; Mdfproject/artifacts/screenshots/shop-card-cost-style-playmode.png; Mdfproject/artifacts/screenshots/gameprepare-uxml-preview.png; Mdfproject/artifacts/screenshots/game-ui-wireframe-paneltarget-delayed.png; Mdfproject/artifacts/screenshots/game-ui-enlarged-game-scene-runtime.png; artifacts/mp/20260519-040220-matrix/matrix-summary.json; artifacts/mp/20260519-055718-matrix/matrix-summary.json; artifacts/mp/20260520-060539-two-humanbot-two-ai-smoke/screenshots/host-20260520-060610.png; artifacts/mp/20260521-031726-matrix/20260521-031730-editor-host-build-client/screenshots/client-20260521-031752.png; artifacts/mp/20260521-045119-matrix/20260521-045122-four-player-smoke/screenshots/host-20260521-045153.png; artifacts/mp/20260521-081533-two-humanbot-two-ai-smoke/screenshots/host-20260521-081602.png; artifacts/mp/20260521-082714-two-humanbot-two-ai-smoke/screenshots/host-20260521-082744.png; artifacts/mp/20260521-083622-two-humanbot-two-ai-smoke/screenshots/host-20260521-083657.png; artifacts/mp/20260522-021002-matrix/20260522-021005-human-bot-prepare/screenshots/client-20260522-021030.png; artifacts/mp/20260522-021953-matrix/20260522-021956-human-bot-prepare/screenshots/client-20260522-022022.png; artifacts/mp/20260522-041334-matrix/20260522-041337-editor-host-build-client/screenshots/client-20260522-041402.png; artifacts/mp/20260603-042245-two-humanbot-two-ai-smoke/visual-compare-target.json; artifacts/mp/20260605-042337-two-humanbot-two-ai-smoke/screenshots/host-20260605-042406.png
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
`unity-cli --project Mdfproject screenshot --view game` can capture the Game camera render without runtime UI Toolkit overlay content from a `UIDocument`. This is not enough for visual QA of overlay panels such as the Game prepare shop or augment cards.

Recipe:
- Put the target overlay into a visible runtime state in Play Mode.
- Use `UnityEngine.ScreenCapture.CaptureScreenshot(<absolute path>)` through `unity-cli exec` after the UI is visible.
- Wait for the file to be written before inspecting or reporting it.
- If Game View screenshot routes omit UI Toolkit or `CaptureScreenshotAsTexture` returns an Editor/UI Builder view, render the `UIDocument` through a temporary `PanelSettings.targetTexture`, wait several editor updates, then read the `RenderTexture` into a PNG.
- Keep the output under an artifact directory, for example `Mdfproject/artifacts/screenshots/<case>.png`.

Verification:
- On 2026-05-15, `unity-cli screenshot --view game` captured only the 03_Game camera and omitted the augment `UIDocument`.
- The same visible overlay captured with `ScreenCapture.CaptureScreenshot` included the UI Toolkit cards and HUD in `Mdfproject/artifacts/screenshots/game-augment-offset-screen-capture.png`.
- On 2026-05-15, `PanelSettings.targetTexture` plus delayed `RenderTexture.ReadPixels` captured the Game prepare wireframe overlay in `Mdfproject/artifacts/screenshots/game-ui-wireframe-paneltarget-delayed.png` when Game View screenshot omitted the overlay.

Pitfalls:
- Direct Play Mode on `03_Game` can emit unrelated runtime Addressables errors if normal room/bootstrap flow is bypassed. For layout proof, keep compile/console verification separate from this visual-only preview.

Lifecycle notes:
- 2026-05-15: Used ScreenCapture.CaptureScreenshot for visible Game prepare shop/character selection UI Toolkit overlay proof.
- 2026-05-15: verified artifact `Mdfproject/artifacts/screenshots/game-character-selection-offset-clean.png`
- 2026-05-15: Verified Play Mode ScreenCapture preview for shop cost card styles without triangular UIToolkit background artifacts.
- 2026-05-15: verified artifact `Mdfproject/artifacts/screenshots/shop-card-cost-style-playmode.png`
- 2026-05-15: verified artifact `Mdfproject/artifacts/screenshots/gameprepare-uxml-preview.png`
- 2026-05-15: verified artifact `Mdfproject/artifacts/screenshots/game-ui-wireframe-paneltarget-delayed.png`
- 2026-05-15: verified artifact `Mdfproject/artifacts/screenshots/game-ui-enlarged-game-scene-runtime.png`
- 2026-05-19: Used UI Toolkit overlay resource pattern for PlayerRanking panel; layout/resource path verified by MPTestHarnessEditModeTests and smoke matrix.
- 2026-05-19: verified artifact `artifacts/mp/20260519-040220-matrix/matrix-summary.json`
- 2026-05-19: Ranking UI Toolkit overlay changed to display-only picking so GamePrepare augment cards keep input; verified by EditMode source guard and smoke matrix.
- 2026-05-19: verified artifact `artifacts/mp/20260519-055718-matrix/matrix-summary.json`
- 2026-05-20: verified artifact `artifacts/mp/20260520-060539-two-humanbot-two-ai-smoke/screenshots/host-20260520-060610.png`
- 2026-05-21: verified artifact `artifacts/mp/20260521-031726-matrix/20260521-031730-editor-host-build-client/screenshots/client-20260521-031752.png`
- 2026-05-21: Verified ranking UI Toolkit overlay capture shows four players at HP 80/80 with full red bars.
- 2026-05-21: verified artifact `artifacts/mp/20260521-045119-matrix/20260521-045122-four-player-smoke/screenshots/host-20260521-045153.png`
- 2026-05-21: Verified runtime UI Toolkit overlay screenshot after ranking HUD and prepare controls update.
- 2026-05-21: verified artifact `artifacts/mp/20260521-081533-two-humanbot-two-ai-smoke/screenshots/host-20260521-081602.png`
- 2026-05-21: Verified round-relative PlayerRanking layout: self/opponent left of round timer, reserves right of round timer.
- 2026-05-21: verified artifact `artifacts/mp/20260521-082714-two-humanbot-two-ai-smoke/screenshots/host-20260521-082744.png`
- 2026-05-21: Verified PlayerRanking self/opponent cards moved right into the requested box area before the round timer.
- 2026-05-21: verified artifact `artifacts/mp/20260521-083622-two-humanbot-two-ai-smoke/screenshots/host-20260521-083657.png`
- 2026-05-22: verified artifact `artifacts/mp/20260522-021002-matrix/20260522-021005-human-bot-prepare/screenshots/client-20260522-021030.png`
- 2026-05-22: verified artifact `artifacts/mp/20260522-021953-matrix/20260522-021956-human-bot-prepare/screenshots/client-20260522-022022.png`
- 2026-05-22: verified artifact `artifacts/mp/20260522-041334-matrix/20260522-041337-editor-host-build-client/screenshots/client-20260522-041402.png`
- 2026-05-31: Verified Battle1 PlayerRanking HUD screenshot with per-match sword/shield role icons.
- 2026-05-31: verified artifact `artifacts/mp/20260531-041001-two-humanbot-two-ai-smoke/screenshots/host-battle-hud-crop.png`
- 2026-06-03: Verified clean prepare host/client screenshot after hiding transient UI for arena background visual check.
- 2026-06-03: verified artifact `artifacts/mp/20260603-042245-two-humanbot-two-ai-smoke/visual-compare-target.json`
- 2026-06-05: Verified rotated/enlarged arena background clean prepare capture with no top-corner empty clear-color gaps.
- 2026-06-05: verified artifact `artifacts/mp/20260605-042337-two-humanbot-two-ai-smoke/screenshots/host-20260605-042406.png`
- 2026-06-05: Verified attack-mode Battle1 host/client screenshots after converting arena background from cube side faces to a wider plane and fixing the Skeleton monster icon key.
- 2026-06-05: verified artifact `artifacts/mp/20260605-050259-two-humanbot-two-ai-smoke/screenshots/host-20260605-050350.png`
