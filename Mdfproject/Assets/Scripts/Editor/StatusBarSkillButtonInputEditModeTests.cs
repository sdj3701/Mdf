#if UNITY_EDITOR
using NUnit.Framework;

public sealed class StatusBarSkillButtonInputEditModeTests
{
    [Test]
    public void StatusBarSkillButtonProvidesToolkitRaycastFallbackClickPath()
    {
        string source = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/UI/StatusBarUI.cs");

        Assert.That(source, Does.Contain("ActiveStatusBars"));
        Assert.That(source, Does.Contain("IsPointerOverActiveSkillButton"));
        Assert.That(source, Does.Contain("MdfInput.PrimaryPointerWasReleasedThisFrame()"));
        Assert.That(source, Does.Contain("IsPointerOverSkillButton(MdfInput.PointerPosition)"));
        Assert.That(source, Does.Contain("RequestSkillActivation(unitComponent);"));
        Assert.That(source, Does.Contain("RectTransformUtility.RectangleContainsScreenPoint"));
        Assert.That(source, Does.Contain("lastSkillRequestFrame == Time.frameCount"));
    }

    [Test]
    public void FieldBlockingInputChecksSkillButtonBeforeToolkitPassthrough()
    {
        string source = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/MdfInput.cs");
        int methodStart = source.IndexOf("public static bool IsPointerOverFieldBlockingUI()", System.StringComparison.Ordinal);
        Assert.That(methodStart, Is.GreaterThanOrEqualTo(0));

        string methodSource = source.Substring(methodStart);
        int skillButtonCheck = methodSource.IndexOf("StatusBarUI.IsPointerOverActiveSkillButton(pointerPosition)", System.StringComparison.Ordinal);
        int toolkitCheck = methodSource.IndexOf("GamePrepareUIToolkitController.IsPointerOverBlockingElement(pointerPosition)", System.StringComparison.Ordinal);

        Assert.That(skillButtonCheck, Is.GreaterThanOrEqualTo(0));
        Assert.That(toolkitCheck, Is.GreaterThanOrEqualTo(0));
        Assert.That(skillButtonCheck, Is.LessThan(toolkitCheck));
    }

    [Test]
    public void StatusBarPassthroughKeepsOnlySkillButtonBlockingFieldInput()
    {
        string source = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/MdfInput.cs");
        Assert.That(source, Does.Contain("return !StatusBarUI.IsPointerOverActiveSkillButton(pointerPosition);"));
    }
}
#endif
