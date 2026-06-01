# Manual Host Wall Placement Investigation

Date: 2026-06-01

## Summary

The current multiplayer evidence proves that `PlaceWallCommand` and `FieldManager.CreateWallAt` work on the host authority path. The failing user repro is therefore most likely in the manual input path before the command is queued.

Most useful split:

- If the host `Player.log` contains `[PlaceWallCommand] Execute request` for the clicked cell, the failure is authority/spawn/visibility.
- If that log is missing, the failure is UI/input/mode/camera/grid gating before `CommandProcessor.RequestCommandExecution`.

The latest 2 HumanBot + 2 AI run passed command-path wall placement:

- Artifact: `artifacts/mp/20260601-021130-two-humanbot-two-ai-smoke`
- `finalStatus=PASS`
- `cleanupStatus=PASS`
- `orphanedPids=[]`
- `successfulPlaceWallCommands=6`
- `successfulRemoveWallCommands=6`
- Host player 0 and client player 1 both placed and removed walls across 3 prepare rounds.

That does not prove the human mouse-click path. It proves the server command and wall spawn path are not generally broken.

## Manual Wall Placement Path

### 1. HUD wall button

File: `Mdfproject/Assets/Scripts/UI/Game/GamePrepareUIToolkitController.cs`

Relevant flow:

- `hudWallButton` is bound from `game-wall-button`.
- `hudWallButton.RegisterCallback<PointerUpEvent>` calls `HandleHudWallClicked()`.
- `HandleHudWallClicked()` calls:

```csharp
localPlayer.fieldManager.TogglePlacementMode(PlacementMode.Wall);
UpdateHudState(true);
```

The wall button only toggles mode if:

- `CanUsePrepareHudActions()` passes.
- `localPlayer != null`.
- `localPlayer.fieldManager != null`.

Visible symptom if this fails:

- Wall button does not get `hud-button-active`.
- No placement preview appears.
- Field clicks never reach wall placement mode.

### 2. FieldManager mode toggle

File: `Mdfproject/Assets/Scripts/Managers/FieldManager.cs`

Relevant flow:

```csharp
public void TogglePlacementMode(PlacementMode mode, GameObject unitPrefab = null)
{
    if (placementManager.GetCurrentMode() == mode)
    {
        placementManager.StopPlacementMode();
    }
    else
    {
        placementManager.StartPlacementMode(mode, unitPrefab);
    }
}
```

This is a thin wrapper. Real gating is in `PlacementManager`.

### 3. PlacementManager starts wall mode

File: `Mdfproject/Assets/Scripts/Managers/PlacementManager.cs`

Relevant flow:

```csharp
public void StartPlacementMode(PlacementMode mode, GameObject unitPrefab = null)
{
    if (!IsPlacementReady()) return;
    if (GameManagers.Instance != null &&
        (GameManagers.Instance.GetGameState() != GameManagers.GameState.Prepare ||
         GameManagers.Instance.IsSequenceTransitioning)) return;
    currentMode = mode;
    unitPrefabToPlace = unitPrefab;
    SetupPreviewObject();
}
```

Host-specific risks here:

- `playerManager.Object.HasInputAuthority` must be true for the local host player field.
- `playerManager.IsReadyForPlayerActions` must be true.
- `GameManagers.Instance.CommandProcessor` must exist.
- State must be `Prepare`.
- `IsSequenceTransitioning` must be false.

The MP logs already show `HM-INPUT-GATE reason=sequenceTransitioning` during transition windows. If the user clicks during the short transition into Prepare, mode will not start or input will be ignored.

### 4. PlacementManager processes field click

File: `Mdfproject/Assets/Scripts/Managers/PlacementManager.cs`

Relevant flow:

```csharp
void Update()
{
    if (!IsPlacementReady())
    {
        if (previewObject != null && previewObject.activeSelf)
        {
            previewObject.SetActive(false);
        }
        return;
    }

    if (currentMode == PlacementMode.None || !showPreview) return;

    if (fieldManager == null || fieldManager.ground3D == null)
    {
        return;
    }

    UpdateMousePosition();
    HandleMouseInput();
    if (showPreview)
        UpdatePreviewDisplay();
}
```

Important issue: input handling is currently coupled to `showPreview`.

If `showPreview` is false at runtime, `HandleMouseInput()` never runs. That means wall mode can be active but field clicks do nothing. The prefab currently has `showPreview: 1`, but this remains a fragile code bug and should be decoupled.

Other click gate:

```csharp
bool pointerOverUI = MdfInput.IsPointerOverFieldBlockingUI();
if (MdfInput.PrimaryPointerWasPressedThisFrame() && !pointerOverUI)
{
    if (currentMode == PlacementMode.Wall && TryRemoveWall())
    {
        return;
    }

    TryPlace();
}
```

If any UI overlay is seen as blocking, `TryPlace()` is never called.

### 5. TryPlace queues command

File: `Mdfproject/Assets/Scripts/Managers/PlacementManager.cs`

Relevant flow:

```csharp
if (!IsPositionValidForPlacement(currentMouseGridPosition, dataToPlace)) return;

case PlacementMode.Wall:
    var wallCommand = new PlaceWallCommand(playerManager.playerId, currentMouseGridPosition);
    GameManagers.Instance.CommandProcessor.RequestCommandExecution(wallCommand);
    break;
```

Wall placement is rejected before command queue if:

- Grid position is out of range.
- A wall already exists.
- The cell is the goal cell.
- The cell has a melee unit and no alternative slot exists.

There is no log at this pre-command rejection point today, so a manual failure can be silent.

### 6. CommandProcessor host path

File: `Mdfproject/Assets/Scripts/Commands/Core/CommandProcessor.cs`

On State Authority host:

```csharp
ReceiveAndEnqueueCommand(type, intParams, stringParams, vectorParams);
gm.RPC_BroadcastCommandToClients(type, intParams, stringParams, vectorParams);
```

Then `GameManagers.Update()` processes the command queue.

The 4-player MP run proves this path works for host player 0.

### 7. PlaceWallCommand authority execution

File: `Mdfproject/Assets/Scripts/Commands/PlayerActions/PlaceWallCommand.cs`

Server-only execution validates:

- `Runner.IsServer`
- player exists
- field manager exists
- player action readiness
- field ownership
- valid grid
- no wall already there
- not goal cell
- wall stock > 0

Then:

```csharp
fm.CreateWallAt(Position);
```

If this runs, logs should include:

- `[PlaceWallCommand] Execute request`
- `[WallFlow-Create] CreateWallAt ENTER`
- `[WallFlow-Create] CreateWallAt SUCCESS`
- `[PlaceWallCommand] SUCCESS`

### 8. FieldManager wall spawn

File: `Mdfproject/Assets/Scripts/Managers/FieldManager.cs`

Networked wall spawn uses:

```csharp
runner.Spawn(netPrefab, worldPos, Quaternion.identity, playerManager.Object.InputAuthority);
```

This requires state authority on the wall owner's `PlayerManager`. The MP artifact shows host has that and successfully spawns host-owned walls.

## Remaining Likely Failure Points

### A. Wall mode is not actually active

Likely if the wall button appears to click but no preview/active state persists.

Potential causes:

- `CanUsePrepareHudActions()` is false.
- `localPlayer` is stale or null.
- `localPlayer.fieldManager` is null.
- `PlacementManager.IsPlacementReady()` is false.
- The game is still `IsSequenceTransitioning`.

Evidence to collect:

- Log `HandleHudWallClicked` inputs and result.
- Log `StartPlacementMode` early returns.
- Check `fieldManager.GetPlacementMode()` after button click.

### B. Field click is still blocked by UI raycast

Likely if wall mode activates but no command log appears.

Known context:

- `MdfInput.IsPointerOverFieldBlockingUI()` uses `EventSystem.RaycastAll`.
- Prior fix made `RankingUIController` pass-through unless the pointer is on an actual ranking card.
- There may be another non-gameplay UI raycast target covering the field: legacy shop root, hidden canvas, option/root panel, world-space status bar, or a UI Toolkit panel GameObject not classified as pass-through.

Evidence to collect:

- On failed click, dump all `RaycastResult.gameObject` names, component types, and whether each was treated as blocking.
- Log `GamePrepareUIToolkitController.IsPointerOverBlockingElement(pointer)`.
- Log `RankingUIController.IsPointerOverBlockingElement(pointer)`.

### C. `showPreview` disables input

The code currently returns before `HandleMouseInput()` when `showPreview == false`.

Even though `Player_Root.prefab` currently serializes `showPreview: 1`, this should be fixed because preview visibility should not decide whether a command can be sent.

Proposed change:

- Always run `UpdateMousePosition()` and `HandleMouseInput()` while placement mode is active.
- Only guard `UpdatePreviewDisplay()` and preview object visibility behind `showPreview`.

### D. Camera is viewing another field

Ranking cards can move the camera to another player field:

- `RankingUIController.OnToolkitCardClicked()`
- `CameraManager.MoveToPlayerField(...)`

Wall placement mode still belongs to the local player's `FieldManager`. If the user is viewing an opponent field, the click may map to the local field plane or clamp to an unexpected local grid cell. This can look like "nothing generated" because the wall is created somewhere else or rejected as invalid/occupied/goal.

Evidence to collect:

- Log `CameraManager.IsViewingOwnField` and `CurrentViewingField.playerId` when wall mode starts and when field click occurs.

Possible UX fix:

- When wall mode is toggled, force `CameraManager.ReturnToOwnField()` before accepting field clicks.
- Or block wall mode outside own field and show/emit a clear diagnostic.

### E. Silent pre-command placement rejection

`TryPlace()` silently returns if `IsPositionValidForPlacement()` is false.

Common silent reasons:

- Clicked goal cell.
- Clicked existing wall.
- Clicked melee unit cell with no relocation slot.
- Click maps outside the local field and is clamped to a blocked edge cell.

Evidence to collect:

- Log current grid, `hasWall`, `hasUnit`, `unitType`, `goalCell`, wall stock, and validity result on every wall-mode click.

### F. Authority/spawn failure after command

Less likely because MP command-path passed, but still possible in the user's live run if prefab or scene state differs.

Evidence:

- Look for `[PlaceWallCommand]` and `[WallFlow-Create]` logs.
- If command runs but `CreateWallAt` fails, the existing logs identify null prefab, existing wall, invalid grid, missing relocation slot, no state authority, `Runner.Spawn` failure, or missing `DestructibleWall`.

## Proposed Investigation Plan

1. Add temporary development-only diagnostics around the manual path.

   Files:

   - `Mdfproject/Assets/Scripts/UI/Game/GamePrepareUIToolkitController.cs`
   - `Mdfproject/Assets/Scripts/Managers/PlacementManager.cs`
   - `Mdfproject/Assets/Scripts/Managers/MdfInput.cs`

   Log tags:

   - `[ManualWall] button`
   - `[ManualWall] mode`
   - `[ManualWall] click`
   - `[ManualWall] ui-block`
   - `[ManualWall] validity`
   - `[ManualWall] command`

2. Decouple click handling from `showPreview`.

   `showPreview` should only affect visual preview. It should not prevent `HandleMouseInput()`.

3. Add full UI-raycast diagnostics for one failed wall-mode click.

   The diagnostic must print each raycast target and the exact reason it is treated as blocking or pass-through.

4. Add camera/field ownership diagnostics.

   On wall button and field click, record:

   - local player id
   - field owner id
   - `CameraManager.IsViewingOwnField`
   - current viewing field id
   - current wall mode

5. Reproduce manually as host.

   Required result interpretation:

   - No `[ManualWall] command`: input/mode/UI/camera/grid gate.
   - `[ManualWall] command` exists but no `[PlaceWallCommand]`: `CommandProcessor`/host queue issue.
   - `[PlaceWallCommand]` exists but no `[WallFlow-Create] SUCCESS`: authority/spawn/field issue.
   - Success logs exist but user cannot see wall: camera/view/sync/visibility issue.

6. Patch the confirmed failing branch only.

   Expected likely patches, depending on logs:

   - UI blocker: broaden pass-through classification for display-only UI Toolkit or hidden legacy roots.
   - Preview gate: move `HandleMouseInput()` outside `showPreview`.
   - Camera mismatch: return to own field when entering wall placement.
   - Silent invalid grid: prevent wall mode clicks while not viewing own field and log invalid placement reasons.

7. Verify.

   Minimum verification after the confirmed fix:

   - `python tools/harness/precommit.py --all`
   - `unity-cli --project Mdfproject status`
   - `unity-cli --project Mdfproject editor refresh --compile`
   - `unity-cli --project Mdfproject console --type error --stacktrace user`
   - `unity-cli --project Mdfproject test --mode EditMode --filter MPTestHarnessEditModeTests`
   - 2 HumanBot + 2 AI 4-player MP smoke with wall commands
   - Manual host repro log showing `[ManualWall] command` and `[PlaceWallCommand] SUCCESS`

## Recommended Next Fix Candidate

Make this small mechanical fix first because it is clearly wrong independent of the final repro:

- In `PlacementManager.Update()`, do not return from input handling when `showPreview == false`.

Then add diagnostics. If the user's repro still fails, the diagnostic output will isolate the remaining gate in a single run.

