using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

public static class UITKTextSettingsSetup
{
    private const string SourceFontPath = "Assets/Resource/Fonts/NotoSansKR-VariableFont_wght.ttf";
    private const string FontAssetPath = "Assets/Resources/Fonts & Materials/NotoSansKR-VariableFont_wght_UITK.asset";
    private const string TextSettingsPath = "Assets/UITK Text Settings.asset";

    private static readonly string[] PanelSettingsPaths =
    {
        "Assets/UI/ReLogin/ReLoginPanelSettings.asset",
        "Assets/UI/TestMatching/TestMatchingPanelSettings.asset",
        "Assets/UI/JoinLobby/JoinLobbyPanelSettings.asset",
    };

    [MenuItem("Tools/MDF/Setup UI Toolkit Text Settings")]
    public static void Setup()
    {
        EnsureFolder("Assets", "Resources");
        EnsureFolder("Assets/Resources", "Fonts & Materials");

        Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
        if (sourceFont == null)
        {
            throw new System.InvalidOperationException($"Source font not found: {SourceFontPath}");
        }

        FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<FontAsset>(FontAssetPath);
        if (fontAsset == null || IsIncomplete(fontAsset))
        {
            AssetDatabase.DeleteAsset(FontAssetPath);
            fontAsset = FontAsset.CreateFontAsset(
                sourceFont,
                90,
                9,
                GlyphRenderMode.SDFAA,
                4096,
                4096,
                AtlasPopulationMode.Dynamic,
                true);

            fontAsset.name = "NotoSansKR-VariableFont_wght_UITK";
            AssetDatabase.CreateAsset(fontAsset, FontAssetPath);
            AddSubAsset(fontAsset.material, FontAssetPath);
            foreach (Texture2D atlasTexture in fontAsset.atlasTextures)
            {
                AddSubAsset(atlasTexture, FontAssetPath);
            }
        }

        fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        fontAsset.isMultiAtlasTexturesEnabled = true;

        SerializedObject fontAssetSerialized = new SerializedObject(fontAsset);
        SerializedProperty clearDynamicData = fontAssetSerialized.FindProperty("m_ClearDynamicDataOnBuild");
        if (clearDynamicData != null)
        {
            clearDynamicData.boolValue = false;
            fontAssetSerialized.ApplyModifiedPropertiesWithoutUndo();
        }

        EditorUtility.SetDirty(fontAsset);

        PanelTextSettings textSettings = AssetDatabase.LoadAssetAtPath<PanelTextSettings>(TextSettingsPath);
        if (textSettings == null)
        {
            textSettings = ScriptableObject.CreateInstance<PanelTextSettings>();
            AssetDatabase.CreateAsset(textSettings, TextSettingsPath);
        }

        textSettings.defaultFontAsset = fontAsset;
        textSettings.defaultFontAssetPath = "Fonts & Materials/";
        textSettings.fallbackFontAssets = new List<FontAsset> { fontAsset };
        textSettings.clearDynamicDataOnBuild = false;
        textSettings.defaultSpriteAssetPath = "Sprite Assets/";
        textSettings.styleSheetsResourcePath = "Text Style Sheets/";
        textSettings.defaultColorGradientPresetsPath = "Text Color Gradients/";
        EditorUtility.SetDirty(textSettings);

        foreach (string panelSettingsPath in PanelSettingsPaths)
        {
            PanelSettings panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(panelSettingsPath);
            if (panelSettings == null)
            {
                Debug.LogWarning($"PanelSettings not found: {panelSettingsPath}");
                continue;
            }

            panelSettings.textSettings = textSettings;
            EditorUtility.SetDirty(panelSettings);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"UI Toolkit text settings configured with font asset: {FontAssetPath}");
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = $"{parent}/{child}";
        if (!AssetDatabase.IsValidFolder(path))
        {
            AssetDatabase.CreateFolder(parent, child);
        }
    }

    private static bool IsIncomplete(FontAsset fontAsset)
    {
        if (fontAsset.material == null)
        {
            return true;
        }

        Texture2D[] atlasTextures = fontAsset.atlasTextures;
        return atlasTextures == null || atlasTextures.Length == 0 || atlasTextures[0] == null;
    }

    private static void AddSubAsset(Object asset, string path)
    {
        if (asset == null || AssetDatabase.Contains(asset))
        {
            return;
        }

        asset.hideFlags = HideFlags.None;
        AssetDatabase.AddObjectToAsset(asset, path);
    }
}
