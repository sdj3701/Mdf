#if UNITY_EDITOR
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

public sealed class RuntimePerformanceGuardsEditModeTests
{
    [Test]
    public void PrepareHudTextWriterKeepsExistingStringWhenContentDidNotChange()
    {
        MethodInfo setText = typeof(GamePrepareUIToolkitController).GetMethod(
            "SetText",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(setText, Is.Not.Null);

        var label = new Label();
        string firstValue = new string(new[] { '1', '2', '3' });
        setText.Invoke(null, new object[] { label, firstValue });
        string storedValue = label.text;

        string equalValue = new string(new[] { '1', '2', '3' });
        setText.Invoke(null, new object[] { label, equalValue });

        Assert.That(label.text, Is.SameAs(storedValue));
    }

    [Test]
    public void PrepareHudRoundTimerPolicySkipsUnchangedValues()
    {
        MethodInfo policy = typeof(GamePrepareUIToolkitController).GetMethod(
            "ShouldRefreshRoundTimer",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(policy, Is.Not.Null);

        Assert.That(policy.Invoke(null, new object[] { false, 4, 12, 4, 12 }), Is.False);
        Assert.That(policy.Invoke(null, new object[] { false, 5, 12, 4, 12 }), Is.True);
        Assert.That(policy.Invoke(null, new object[] { false, 4, 11, 4, 12 }), Is.True);
        Assert.That(policy.Invoke(null, new object[] { true, 4, 12, 4, 12 }), Is.True);
    }

    [TestCase(false, true, true, false)]
    [TestCase(true, true, false, true)]
    [TestCase(true, false, false, false)]
    [TestCase(true, false, true, true)]
    public void GridVisualizationPolicyRequiresDevelopmentOrExplicitReleaseOptIn(
        bool requested,
        bool isDevelopmentBuild,
        bool explicitlyAllowedInRelease,
        bool expected)
    {
        MethodInfo policy = typeof(FieldManager).GetMethod(
            "IsGridDebugVisualizationAllowed",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(policy, Is.Not.Null);

        bool actual = (bool)policy.Invoke(
            null,
            new object[] { requested, isDevelopmentBuild, explicitlyAllowedInRelease });

        Assert.That(actual, Is.EqualTo(expected));
    }
}
#endif
