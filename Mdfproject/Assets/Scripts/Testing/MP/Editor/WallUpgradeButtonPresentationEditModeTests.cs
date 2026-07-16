#if UNITY_EDITOR
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public sealed class WallUpgradeButtonPresentationEditModeTests
{
    private const string WallActionPanelPath =
        "Assets/Prefabs/UI/Unit/UI_Can_WallRemove.prefab";
    private const string UnitSellPanelPath =
        "Assets/Prefabs/UI/Unit/UI_Can_UnitSell.prefab";
    private const string EconomyGoldSpritePath =
        "Assets/Resource/Image/UI/SlotUI/Spr_UnitCost.png";

    [Test]
    public void WallUpgradeButtonReusesEconomyGoldSpriteAtCompactNonBlockingSize()
    {
        GameObject wallActionPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WallActionPanelPath);
        GameObject unitSellPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(UnitSellPanelPath);
        Assert.That(wallActionPrefab, Is.Not.Null, WallActionPanelPath);
        Assert.That(unitSellPrefab, Is.Not.Null, UnitSellPanelPath);

        WallRemovePanelController controller = wallActionPrefab.GetComponent<WallRemovePanelController>();
        Assert.That(controller, Is.Not.Null);
        var serializedController = new SerializedObject(controller);
        TMP_Text label = serializedController.FindProperty("upgradeLabel").objectReferenceValue as TMP_Text;
        Image icon = serializedController.FindProperty("upgradeGoldIcon").objectReferenceValue as Image;
        Assert.That(label, Is.Not.Null);
        Assert.That(icon, Is.Not.Null);

        Image sellGoldIcon = unitSellPrefab.transform.Find("UI_Pnl_UnitSell/gold")?.GetComponent<Image>();
        Assert.That(sellGoldIcon, Is.Not.Null, "the established sell-price gold icon must remain available");
        Assert.That(icon.sprite, Is.SameAs(sellGoldIcon.sprite));
        Assert.That(AssetDatabase.GetAssetPath(icon.sprite), Is.EqualTo(EconomyGoldSpritePath));
        Assert.That(icon.preserveAspect, Is.True);
        Assert.That(icon.raycastTarget, Is.False, "the decorative icon must not intercept wall-action clicks");
        Assert.That(icon.transform.IsChildOf(label.transform.parent), Is.True);

        RectTransform labelRect = label.rectTransform;
        RectTransform iconRect = icon.rectTransform;
        Assert.That(iconRect.sizeDelta.x, Is.InRange(12f, 16f));
        Assert.That(iconRect.sizeDelta.y, Is.InRange(12f, 16f));
        Assert.That(labelRect.anchoredPosition.x, Is.LessThan(0f));
        Assert.That(iconRect.anchoredPosition.x, Is.GreaterThan(0f));
        Assert.That(label.text, Is.EqualTo("UP 2"));
        Assert.That(label.text, Does.Not.EndWith("G"));
    }

    [Test]
    public void WallUpgradeCostPresentationShowsIconOnlyWhenAQuoteExists()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WallActionPanelPath);
        Assert.That(prefab, Is.Not.Null, WallActionPanelPath);
        GameObject instance = Object.Instantiate(prefab);
        try
        {
            instance.SetActive(false);
            WallRemovePanelController controller = instance.GetComponent<WallRemovePanelController>();
            var serializedController = new SerializedObject(controller);
            TMP_Text label = serializedController.FindProperty("upgradeLabel").objectReferenceValue as TMP_Text;
            Image icon = serializedController.FindProperty("upgradeGoldIcon").objectReferenceValue as Image;
            MethodInfo updatePresentation = typeof(WallRemovePanelController).GetMethod(
                "SetUpgradeCostPresentation",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(label, Is.Not.Null);
            Assert.That(icon, Is.Not.Null);
            Assert.That(updatePresentation, Is.Not.Null);

            updatePresentation.Invoke(controller, new object[] { true, 3 });
            Assert.That(label.text, Is.EqualTo("UP 3"));
            Assert.That(icon.gameObject.activeSelf, Is.True);

            updatePresentation.Invoke(controller, new object[] { false, 0 });
            Assert.That(label.text, Is.EqualTo("MAX"));
            Assert.That(icon.gameObject.activeSelf, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }
}
#endif
