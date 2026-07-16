#if UNITY_EDITOR
using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public sealed class SelectionActionPanelInputEditModeTests
{
    private const string UnitSellPanelPath = "Assets/Prefabs/UI/Unit/UI_Can_UnitSell.prefab";
    private const string WallActionPanelPath = "Assets/Prefabs/UI/Unit/UI_Can_WallRemove.prefab";

    [Test]
    public void UnitSellPanelProvidesWorldSpaceFallbackAndBlocksFieldInput()
    {
        string controllerSource = MdfSourcePolicy.ReadStaticContract(
            "Assets/Scripts/UI/UnitSellPanelController.cs");
        string inputSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/MdfInput.cs");

        Assert.That(controllerSource, Does.Contain("ActiveControllers"));
        Assert.That(controllerSource, Does.Contain("IsPointerOverActiveSellButton"));
        Assert.That(controllerSource, Does.Contain("MdfInput.TryGetPrimaryPointerPressThisFrame"));
        Assert.That(controllerSource, Does.Contain("MdfInput.TryGetPrimaryPointerReleaseThisFrame"));
        Assert.That(controllerSource, Does.Contain("capturedPointerId == releasedPointerId"));
        Assert.That(controllerSource, Does.Contain("MdfInput.IsTopmostVisibleUiTarget"));
        Assert.That(controllerSource, Does.Contain("RectTransformUtility.RectangleContainsScreenPoint"));
        Assert.That(controllerSource, Does.Contain("lastSellDispatchFrame == dispatchFrame"),
            "manual fallback and Button.onClick must not sell twice in the same frame");

        int blockerStart = inputSource.IndexOf(
            "public static bool IsPointerOverFieldBlockingUI()",
            StringComparison.Ordinal);
        Assert.That(blockerStart, Is.GreaterThanOrEqualTo(0));
        string blockerSource = inputSource.Substring(blockerStart);
        int sellCheck = blockerSource.IndexOf(
            "UnitSellPanelController.IsPointerOverActiveSellButton(pointerPosition)",
            StringComparison.Ordinal);
        int toolkitCheck = blockerSource.IndexOf(
            "GamePrepareUIToolkitController.IsPointerOverBlockingElement(pointerPosition)",
            StringComparison.Ordinal);
        Assert.That(sellCheck, Is.GreaterThanOrEqualTo(0));
        Assert.That(toolkitCheck, Is.GreaterThan(sellCheck),
            "the field must reserve the sell button before UI Toolkit passthrough is evaluated");
        Assert.That(inputSource, Does.Contain("touch.primaryTouch.position.ReadValue()"));
        Assert.That(inputSource, Does.Contain("mouse.position.ReadValue()"));

        string fieldSource = MdfSourcePolicy.ReadStaticContract(
            "Assets/Scripts/Managers/FieldManager.InputPresentation.cs");
        int unitSelectionStart = fieldSource.IndexOf("ShowUnitDetailPanel(selectedUnit);", StringComparison.Ordinal);
        int unitSelectionEnd = fieldSource.IndexOf("// 상태 초기화", unitSelectionStart, StringComparison.Ordinal);
        Assert.That(unitSelectionStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(unitSelectionEnd, Is.GreaterThan(unitSelectionStart));
        Assert.That(fieldSource.Substring(unitSelectionStart, unitSelectionEnd - unitSelectionStart),
            Does.Not.Contain("ShowWallRemovePanel"),
            "a unit click must not open a higher-sorted wall panel over its sell action");
        Assert.That(fieldSource, Does.Contain("requestRevision != sellPanelRequestRevision"),
            "a delayed pool/addressable completion must not reopen a stale sell action");
    }

    [Test]
    public void UnitSellFallbackUsesWorldSpaceCanvasCameraCoordinates()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(UnitSellPanelPath);
        Assert.That(prefab, Is.Not.Null, UnitSellPanelPath);

        GameObject cameraObject = new GameObject("UnitSellInputTestCamera");
        GameObject panelInstance = null;
        try
        {
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.pixelRect = new Rect(0f, 0f, 800f, 600f);
            cameraObject.transform.SetPositionAndRotation(new Vector3(0f, 0f, -10f), Quaternion.identity);

            panelInstance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            Assert.That(panelInstance, Is.Not.Null);
            panelInstance.transform.SetPositionAndRotation(Vector3.zero, camera.transform.rotation);

            UnitSellPanelController controller = panelInstance.GetComponent<UnitSellPanelController>();
            var applyCamera = typeof(UnitSellPanelController).GetMethod(
                "ApplyCanvasCamera",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(applyCamera, Is.Not.Null);
            applyCamera.Invoke(controller, new object[] { camera });
            Assert.That(panelInstance.GetComponentsInChildren<Canvas>(true)
                .Where(canvas => canvas.renderMode == RenderMode.WorldSpace)
                .All(canvas => canvas.worldCamera == camera), Is.True,
                "every nested world-space canvas must use the field camera at runtime");

            SerializedObject serializedController = new SerializedObject(controller);
            Button sellButton = serializedController.FindProperty("sellButton").objectReferenceValue as Button;
            RectTransform sellRect = sellButton != null ? sellButton.transform as RectTransform : null;
            Assert.That(sellRect, Is.Not.Null);

            Canvas.ForceUpdateCanvases();
            Vector3 buttonWorldCenter = sellRect.TransformPoint(sellRect.rect.center);
            Vector2 buttonScreenCenter = RectTransformUtility.WorldToScreenPoint(camera, buttonWorldCenter);
            var hitTest = typeof(UnitSellPanelController).GetMethod(
                "IsPointerOverButton",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(hitTest, Is.Not.Null);
            Assert.That((bool)hitTest.Invoke(controller, new object[] { sellButton, buttonScreenCenter }), Is.True);
            Assert.That((bool)hitTest.Invoke(controller, new object[] { sellButton, new Vector2(-1000f, -1000f) }), Is.False);
        }
        finally
        {
            if (panelInstance != null)
            {
                UnityEngine.Object.DestroyImmediate(panelInstance);
            }
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }

    [Test]
    public void WallRemoveDecorationsStayCenteredInsideRemoveButtonAndDoNotStealRaycasts()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WallActionPanelPath);
        Assert.That(prefab, Is.Not.Null, WallActionPanelPath);
        WallRemovePanelController controller = prefab.GetComponent<WallRemovePanelController>();
        Assert.That(controller, Is.Not.Null);

        SerializedObject serializedController = new SerializedObject(controller);
        Button removeButton = serializedController.FindProperty("removeButton").objectReferenceValue as Button;
        Button upgradeButton = serializedController.FindProperty("upgradeButton").objectReferenceValue as Button;
        Assert.That(removeButton, Is.Not.Null);
        Assert.That(upgradeButton, Is.Not.Null);

        RectTransform removeRect = removeButton.transform as RectTransform;
        RectTransform panelRect = removeRect != null ? removeRect.parent as RectTransform : null;
        Assert.That(removeRect, Is.Not.Null);
        Assert.That(panelRect, Is.Not.Null);

        Image[] decorativeImages = removeRect.GetComponentsInChildren<Image>(true)
            .Where(image => image != null &&
                            image.transform.parent == removeRect &&
                            image.gameObject != removeButton.gameObject)
            .ToArray();
        Assert.That(decorativeImages.Length, Is.EqualTo(3));
        foreach (Image image in decorativeImages)
        {
            RectTransform imageRect = image.transform as RectTransform;
            Assert.That(imageRect, Is.Not.Null, image.name);
            Assert.That(imageRect.anchoredPosition.x,
                Is.EqualTo(0f).Within(0.01f),
                $"{image.name} must be centered under the remove-button transform");
            Assert.That(image.raycastTarget, Is.False,
                $"decorative remove icon {image.name} must not intercept Button events");
        }

        Image panelBackground = panelRect.GetComponent<Image>();
        Assert.That(panelBackground, Is.Not.Null);
        Assert.That(panelBackground.raycastTarget, Is.False,
            "the transparent two-button panel must not leave an invisible blocker when upgrade is hidden");
    }

    [Test]
    public void WallActionPanelAssignsFieldCameraToEveryNestedWorldSpaceCanvas()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WallActionPanelPath);
        Assert.That(prefab, Is.Not.Null, WallActionPanelPath);

        GameObject cameraObject = new GameObject("WallActionInputTestCamera");
        GameObject panelInstance = null;
        try
        {
            Camera camera = cameraObject.AddComponent<Camera>();
            panelInstance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            Assert.That(panelInstance, Is.Not.Null);

            WallRemovePanelController controller = panelInstance.GetComponent<WallRemovePanelController>();
            var applyCamera = typeof(WallRemovePanelController).GetMethod(
                "ApplyCanvasCamera",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(applyCamera, Is.Not.Null);
            applyCamera.Invoke(controller, new object[] { camera });

            Canvas[] canvases = panelInstance.GetComponentsInChildren<Canvas>(true);
            Assert.That(canvases.Count(canvas => canvas.renderMode == RenderMode.WorldSpace), Is.GreaterThan(1),
                "the regression requires the prefab's nested world-space canvas layout");
            Assert.That(canvases
                .Where(canvas => canvas.renderMode == RenderMode.WorldSpace)
                .All(canvas => canvas.worldCamera == camera), Is.True);
        }
        finally
        {
            if (panelInstance != null)
            {
                UnityEngine.Object.DestroyImmediate(panelInstance);
            }
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }

    [Test]
    public void ManualFallbackAcceptsOnlyItsOwnTopmostUiHierarchy()
    {
        GameObject root = new GameObject("SelectionHierarchyRoot");
        GameObject lower = new GameObject("LowerAction");
        GameObject upper = new GameObject("UpperBlocker");
        try
        {
            lower.transform.SetParent(root.transform);
            upper.transform.SetParent(root.transform);
            var sameHierarchy = typeof(MdfInput).GetMethod(
                "IsSameUiHierarchy",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.That(sameHierarchy, Is.Not.Null);
            Assert.That((bool)sameHierarchy.Invoke(null, new object[] { lower.transform, upper.transform }), Is.False);

            GameObject icon = new GameObject("ActionIcon");
            icon.transform.SetParent(lower.transform);
            Assert.That((bool)sameHierarchy.Invoke(null, new object[] { lower.transform, icon.transform }), Is.True);
            UnityEngine.Object.DestroyImmediate(icon);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }
}
#endif
