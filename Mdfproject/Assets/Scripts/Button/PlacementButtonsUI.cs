// Assets/Scripts/UI/PlacementButtonsUI.cs
using UnityEngine;

public class PlacementButtonsUI : MonoBehaviour
{
    private static bool TryGetReadyLocalPlayer(out PlayerManager localPlayer)
    {
        localPlayer = null;
        var gm = GameManagers.Instance;
        if (gm == null || gm.localPlayer == null)
        {
            return false;
        }

        if (!gm.localPlayer.IsReadyForPlayerActions || gm.localPlayer.fieldManager == null)
        {
            return false;
        }

        localPlayer = gm.localPlayer;
        return true;
    }

    public void OnPlaceWallButtonClicked()
    {
        if (TryGetReadyLocalPlayer(out var localPlayer))
        {
            bool enablingWallMode = localPlayer.fieldManager.GetPlacementMode() != PlacementMode.Wall;
            if (enablingWallMode && CameraManager.Instance != null && !CameraManager.Instance.IsViewingOwnField)
            {
                CameraManager.Instance.ReturnToOwnField();
            }

            if (enablingWallMode)
            {
                GamePrepareUIToolkitController.TrySetShopVisibilityFromLegacy(false, out _);
            }

            localPlayer.fieldManager.TogglePlacementMode(PlacementMode.Wall);
        }
    }

    public void OnCancelPlacementButtonClicked()
    {
        if (TryGetReadyLocalPlayer(out var localPlayer))
        {
            localPlayer.fieldManager.StopPlacementMode();
        }
    }
}
