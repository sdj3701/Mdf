#if UNITY_EDITOR
using NUnit.Framework;

public sealed class WallRemovePanelInputEditModeTests
{
    [Test]
    public void WallRemovePanelProvidesToolkitRaycastFallbackClickPath()
    {
        string source = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/UI/WallRemovePanelController.cs");

        Assert.That(source, Does.Contain("ActiveControllers"));
        Assert.That(source, Does.Contain("IsPointerOverActiveActionButton"));
        Assert.That(source, Does.Contain("MdfInput.TryGetPrimaryPointerPressThisFrame"));
        Assert.That(source, Does.Contain("MdfInput.TryGetPrimaryPointerReleaseThisFrame"));
        Assert.That(source, Does.Contain("_capturedPointerId == releasedPointerId"));
        Assert.That(source, Does.Contain("Time.frameCount > _fallbackCaptureBlockedThroughFrame"),
            "the wall-selection press must not arm a button that appeared later in the same frame");
        Assert.That(source, Does.Contain("ResolveFallbackButton(pressedPosition)"));
        Assert.That(source, Does.Contain("SetActionRaycastBlocking(false)"));
        Assert.That(source, Does.Contain("_actionsAwaitingPointerRelease"));
        Assert.That(source, Does.Contain("Time.frameCount > actionGateFrame"));
        Assert.That(source, Does.Contain("MdfInput.IsTopmostVisibleUiTarget"));
        Assert.That(source, Does.Contain("OnRemoveButtonClicked();"));
        Assert.That(source, Does.Contain("OnUpgradeButtonClicked();"));
        Assert.That(source, Does.Contain("RectTransformUtility.RectangleContainsScreenPoint"));
        Assert.That(source, Does.Contain("_removeRequested"));
    }

    [Test]
    public void FieldBlockingInputChecksWallRemoveButtonBeforeToolkitPassthrough()
    {
        string source = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/MdfInput.cs");
        int methodStart = source.IndexOf("public static bool IsPointerOverFieldBlockingUI()", System.StringComparison.Ordinal);
        Assert.That(methodStart, Is.GreaterThanOrEqualTo(0));

        string methodSource = source.Substring(methodStart);
        int wallButtonCheck = methodSource.IndexOf("WallRemovePanelController.IsPointerOverActiveActionButton(pointerPosition)", System.StringComparison.Ordinal);
        int toolkitCheck = methodSource.IndexOf("GamePrepareUIToolkitController.IsPointerOverBlockingElement(pointerPosition)", System.StringComparison.Ordinal);

        Assert.That(wallButtonCheck, Is.GreaterThanOrEqualTo(0));
        Assert.That(toolkitCheck, Is.GreaterThanOrEqualTo(0));
        Assert.That(wallButtonCheck, Is.LessThan(toolkitCheck));
    }
}
#endif
