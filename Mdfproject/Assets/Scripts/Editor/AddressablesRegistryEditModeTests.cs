#if UNITY_EDITOR
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MDF.Runtime.Assets;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine.Tilemaps;

public sealed class AddressablesRegistryEditModeTests
{
    private const string BreakWallAssetPath = "Assets/Resource/Image/Tiles/BreakWall.asset";
    private const string BreakWallAddress = "BreakWall";

    [Test]
    public void AddressableLeaseDisposeIsAtomicAndIdempotentAcrossThreads()
    {
        ConstructorInfo constructor = typeof(AddressableAssetLease<object>).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new[] { typeof(object), typeof(Action) },
            null);
        Assert.That(constructor, Is.Not.Null);

        int releaseCount = 0;
        var lease = (AddressableAssetLease<object>)constructor.Invoke(new object[]
        {
            new object(),
            (Action)(() => Interlocked.Increment(ref releaseCount))
        });

        Parallel.For(0, 64, _ => lease.Dispose());

        Assert.That(releaseCount, Is.EqualTo(1));
    }

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
    public void AssetRegistryOwnsLoadedAddressablesAndReleasesThemOnClear()
    {
        FieldInfo ownerField = typeof(AssetRegistry).GetField("assetOwner", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(ownerField, Is.Not.Null);

        var original = (AddressableAssetOwner)ownerField.GetValue(null);
        Assert.That(original, Is.Not.Null);
        Assert.That(original.IsDisposed, Is.False);

        AssetRegistry.ClearAll();

        var replacement = (AddressableAssetOwner)ownerField.GetValue(null);
        Assert.That(original.IsDisposed, Is.True);
        Assert.That(replacement, Is.Not.Null.And.Not.SameAs(original));
        Assert.That(replacement.IsDisposed, Is.False);

        MethodInfo tileLoad = typeof(AssetRegistry).GetMethod(nameof(AssetRegistry.LoadAndRegisterTile));
        MethodInfo spriteLoad = typeof(AssetRegistry).GetMethod(nameof(AssetRegistry.LoadAndRegisterSprite));
        Assert.That(tileLoad.ReturnType, Is.EqualTo(typeof(Task<TileBase>)));
        Assert.That(spriteLoad.ReturnType, Is.EqualTo(typeof(Task<UnityEngine.Sprite>)));
    }
}
#endif
