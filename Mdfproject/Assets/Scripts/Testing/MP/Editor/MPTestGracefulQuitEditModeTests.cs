#if UNITY_EDITOR
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class MPTestGracefulQuitEditModeTests
{
    [TestCase(true, false, false, true)]
    [TestCase(true, false, true, false)]
    [TestCase(true, true, false, false)]
    [TestCase(false, false, false, false)]
    public void WindowCloseInterceptionIsLimitedToTestPlayers(
        bool mpTestEnabled,
        bool isEditor,
        bool allowImmediateQuit,
        bool expected)
    {
        Assert.That(
            MPTestGracefulQuit.ShouldInterceptWindowClose(mpTestEnabled, isEditor, allowImmediateQuit),
            Is.EqualTo(expected));
    }

    [Test]
    public void GracefulQuitHooksNativeWindowCloseAndKeepsProductionGate()
    {
        Assert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                typeof(MPTestGracefulQuit),
                typeof(Application),
                "add_wantsToQuit"),
            Is.True);
        Assert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                typeof(MPTestBootstrap),
                typeof(MPTestGracefulQuit),
                nameof(MPTestGracefulQuit.Configure)),
            Is.True);

        string source = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Testing/MP/MPTestGracefulQuit.cs");
        Assert.That(source, Does.StartWith("#if UNITY_EDITOR || DEVELOPMENT_BUILD"));
    }

    [Test]
    public void RunnerShutdownUsesOneBoundedDeadline()
    {
        FieldInfo timeout = typeof(MPTestGracefulQuit).GetField(
            "RunnerShutdownTimeoutSeconds",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.That(timeout, Is.Not.Null);
        Assert.That((float)timeout.GetRawConstantValue(), Is.InRange(0.1f, 8f));
    }
}
#endif
