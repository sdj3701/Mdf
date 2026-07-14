#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using Fusion.Photon.Realtime;
using NUnit.Framework;
using UnityEngine;

public sealed class MPTestReleaseIsolationEditModeTests
{
    [Test]
    public void BootstrapIsEntirelyExcludedFromNormalReleaseBuilds()
    {
        string source = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Testing/MP/MPTestBootstrap.cs");
        Assert.That(source, Does.StartWith("#if UNITY_EDITOR || DEVELOPMENT_BUILD"));
        Assert.That(source.TrimEnd(), Does.EndWith("#endif"));

        MethodInfo initialize = typeof(MPTestBootstrap).GetMethod(
            nameof(MPTestBootstrap.TryInitialize),
            BindingFlags.Static | BindingFlags.Public);
        Assert.That(initialize, Is.Not.Null);
        Assert.That(
            initialize.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false).Any(),
            Is.False,
            "QA bootstrap types must not enter Unity's player runtime-initialize registry.");

        MethodInfo networkAwake = typeof(NetworkManager).GetMethod(
            "Awake",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(networkAwake, Is.Not.Null);
        Assert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                networkAwake,
                typeof(MPTestBootstrap),
                nameof(MPTestBootstrap.TryInitialize)),
            Is.True,
            "Editor/Development bootstrap must be explicitly wired from the persistent network root.");
    }

    [Test]
    public void CommandLineHasHardNonDevelopmentDisabledBranches()
    {
        string source = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Testing/MP/MPTestCommandLine.cs");
        Assert.That(source, Does.Contain("#if !(UNITY_EDITOR || DEVELOPMENT_BUILD)"));
        Assert.That(source, Does.Contain("return default;"));
        Assert.That(source, Does.Contain("#else"));
        Assert.That(source, Does.Contain("return false;"));
    }

    [Test]
    public void DevelopmentParserStillSupportsHarnessOptions()
    {
        MPTestCommandLine.Options options = MPTestCommandLine.Parse(new[]
        {
            "game.exe",
            "--mpTest",
            "--mpAutoStart",
            "--mpExitAfterSeconds", "2",
            "--mpConnectionToken", "release-isolation-test"
        });

        Assert.That(options.Enabled, Is.True);
        Assert.That(options.AutoStart, Is.True);
        Assert.That(options.ExitAfterSeconds, Is.EqualTo(2));
        Assert.That(options.ConnectionToken, Is.EqualTo("release-isolation-test"));
    }

    [Test]
    public void MatchmakingUsesExplicitWireProtocolVersion()
    {
        PhotonAppSettings settings = PhotonAppSettings.Global;
        Assert.That(settings, Is.Not.Null);
        string original = settings.AppSettings.AppVersion;
        try
        {
            Assert.That(MdfNetworkProtocol.ApplyToGlobalSettings(out string reason), Is.True, reason);
            Assert.That(settings.AppSettings.AppVersion, Is.EqualTo(MdfNetworkProtocol.AppVersion));
            Assert.That(MdfNetworkProtocol.AppVersion, Is.Not.Empty);
        }
        finally
        {
            FusionAppSettings appSettings = settings.AppSettings;
            appSettings.AppVersion = original;
            settings.AppSettings = appSettings;
        }
    }

    [Test]
    public void ProductionNegativeRunnerProvesAllReleaseIsolationEffects()
    {
        string source = MdfSourcePolicy.ReadStaticContract(
            "../tools/harness/mp/run_production_negative_automation.py");

        foreach (string contract in new[]
                 {
                     "--mpAutoStart",
                     "--mpConnectionToken",
                     "inspect_player_prefs_for_token",
                     "inspect_build_for_bootstrap_type",
                     "bootstrapAbsent",
                     "runtimeCodeMatches",
                     "metadataCacheMatches",
                     "autostartAbsent",
                     "remainedRunningUntilExternalCleanup",
                     "connectionTokenAbsentFromPlayerPrefs"
                 })
        {
            Assert.That(source, Does.Contain(contract), contract);
        }
    }
}
#endif
