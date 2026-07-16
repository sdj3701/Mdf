#if UNITY_EDITOR
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public sealed class ProjectPerformanceSettingsEditModeTests
{
    [Test]
    public void IncrementalGcIsEnabledForPlayerBuilds()
    {
        Assert.That(PlayerSettings.gcIncremental, Is.True);
    }

    [Test]
    public void QualityDefaultsToBalancedAndKeepsHighSelectable()
    {
        string[] qualityNames = QualitySettings.names;
        int qualityLevel = QualitySettings.GetQualityLevel();
        UniversalRenderPipelineAsset balanced = LoadPipeline("Assets/Settings/URP-Balanced.asset");
        UniversalRenderPipelineAsset high = LoadPipeline("Assets/Settings/URP-HighFidelity.asset");
        SerializedObject settings = LoadQualitySettings();

        Assert.That(qualityNames, Is.EqualTo(new[] { "Performant", "Balanced", "High Fidelity" }));
        Assert.That(qualityLevel, Is.EqualTo(1));
        Assert.That(qualityNames[qualityLevel], Is.EqualTo("Balanced"));
        Assert.That(QualitySettings.renderPipeline, Is.SameAs(balanced));
        Assert.That(QualitySettings.GetRenderPipelineAssetAt(2), Is.SameAs(high));
        Assert.That(qualityNames, Has.Member("High Fidelity"));
        Assert.That(settings.FindProperty("m_CurrentQuality").intValue, Is.EqualTo(1));
        AssertPlatformDefault(settings, "Standalone", 1);
        AssertPlatformDefault(settings, "Server", 0);
        AssertPlatformDefault(settings, "Android", 0);
        AssertPlatformDefault(settings, "iPhone", 0);
        AssertPlatformDefault(settings, "WebGL", 0);
        AssertPlatformDefault(settings, "Nintendo Switch", 0);
    }

    [Test]
    public void UniversalRenderPipelineProfilesHaveDistinctCostTiers()
    {
        UniversalRenderPipelineAsset performant = LoadPipeline("Assets/Settings/URP-Performant.asset");
        UniversalRenderPipelineAsset balanced = LoadPipeline("Assets/Settings/URP-Balanced.asset");
        UniversalRenderPipelineAsset high = LoadPipeline("Assets/Settings/URP-HighFidelity.asset");

        Assert.That(performant.renderScale, Is.EqualTo(0.8f).Within(0.001f));
        Assert.That(balanced.renderScale, Is.EqualTo(1f).Within(0.001f));
        Assert.That(high.renderScale, Is.EqualTo(1f).Within(0.001f));

        Assert.That(performant.msaaSampleCount, Is.EqualTo(1));
        Assert.That(balanced.msaaSampleCount, Is.EqualTo(2));
        Assert.That(high.msaaSampleCount, Is.EqualTo(4));

        Assert.That(performant.supportsHDR, Is.False);
        Assert.That(balanced.supportsHDR, Is.True);
        Assert.That(high.supportsHDR, Is.True);

        Assert.That(performant.shadowDistance, Is.LessThan(balanced.shadowDistance));
        Assert.That(balanced.shadowDistance, Is.LessThan(high.shadowDistance));
        Assert.That(balanced.mainLightShadowmapResolution, Is.LessThan(high.mainLightShadowmapResolution));
        Assert.That(balanced.shadowCascadeCount, Is.LessThan(high.shadowCascadeCount));
    }

    private static UniversalRenderPipelineAsset LoadPipeline(string path)
    {
        UniversalRenderPipelineAsset asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
        Assert.That(asset, Is.Not.Null, path);
        return asset;
    }

    private static SerializedObject LoadQualitySettings()
    {
        var method = typeof(QualitySettings).GetMethod(
            "GetQualitySettings",
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public);
        Assert.That(method, Is.Not.Null);

        Object settings = method.Invoke(null, null) as Object;
        Assert.That(settings, Is.Not.Null);
        return new SerializedObject(settings);
    }

    private static void AssertPlatformDefault(SerializedObject settings, string platform, int expectedLevel)
    {
        SerializedProperty defaults = settings.FindProperty("m_PerPlatformDefaultQuality");
        for (int i = 0; i < defaults.arraySize; i++)
        {
            SerializedProperty entry = defaults.GetArrayElementAtIndex(i);
            if (entry.FindPropertyRelative("first").stringValue != platform)
            {
                continue;
            }

            Assert.That(entry.FindPropertyRelative("second").intValue, Is.EqualTo(expectedLevel), platform);
            return;
        }

        Assert.Fail($"Missing quality default for platform '{platform}'.");
    }
}
#endif
