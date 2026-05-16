#if UNITY_EDITOR
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine.Tilemaps;

public sealed class AddressablesRegistryEditModeTests
{
    private const string BreakWallAssetPath = "Assets/Resource/Image/Tiles/BreakWall.asset";
    private const string BreakWallAddress = "BreakWall";

    [Test]
    public void BreakWallTileHasAddressableKey()
    {
        var tile = AssetDatabase.LoadAssetAtPath<TileBase>(BreakWallAssetPath);
        Assert.That(tile, Is.Not.Null);

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        Assert.That(settings, Is.Not.Null);

        string guid = AssetDatabase.AssetPathToGUID(BreakWallAssetPath);
        var entry = settings.FindAssetEntry(guid);
        Assert.That(entry, Is.Not.Null);
        Assert.That(entry.address, Is.EqualTo(BreakWallAddress));
    }

    [Test]
    public void AssetRegistryInitializesAddressablesBeforeDirectSceneTileLoad()
    {
        string source = System.IO.File.ReadAllText("Assets/Scripts/ComponentRegistrySystem/StaticAssets/AssetRegistry.cs");
        Assert.That(source, Does.Contain("EnsureAddressablesInitialized"));
        Assert.That(source, Does.Contain("Addressables.InitializeAsync()"));
        Assert.That(source.IndexOf("await EnsureAddressablesInitialized();", System.StringComparison.Ordinal),
            Is.LessThan(source.IndexOf("Addressables.LoadAssetAsync<TileBase>(addressableKey)", System.StringComparison.Ordinal)));
    }
}
#endif
