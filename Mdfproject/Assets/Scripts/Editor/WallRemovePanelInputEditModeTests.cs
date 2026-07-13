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
        Assert.That(source, Does.Contain("MdfInput.PrimaryPointerWasReleasedThisFrame()"));
        Assert.That(source, Does.Contain("IsPointerOverButton(upgradeButton, pointerPosition)"));
        Assert.That(source, Does.Contain("IsPointerOverButton(removeButton, pointerPosition)"));
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
