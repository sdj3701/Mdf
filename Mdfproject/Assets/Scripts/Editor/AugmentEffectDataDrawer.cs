#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps composed augment assets readable by showing only the payload authored for each effect.
/// </summary>
[CustomPropertyDrawer(typeof(AugmentEffectData))]
public sealed class AugmentEffectDataDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        Rect line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        SerializedProperty typeProperty = property.FindPropertyRelative("effectType");
        string effectName = typeProperty != null &&
                            typeProperty.enumValueIndex >= 0 &&
                            typeProperty.enumDisplayNames.Length > typeProperty.enumValueIndex
            ? typeProperty.enumDisplayNames[typeProperty.enumValueIndex]
            : "Invalid Effect";
        property.isExpanded = EditorGUI.Foldout(line, property.isExpanded, $"{label.text}: {effectName}", true);

        if (property.isExpanded)
        {
            EditorGUI.indentLevel++;
            foreach (SerializedProperty child in EnumerateVisibleFields(property))
            {
                line.y += line.height + EditorGUIUtility.standardVerticalSpacing;
                line.height = EditorGUI.GetPropertyHeight(child, includeChildren: true);
                EditorGUI.PropertyField(line, child, includeChildren: true);
            }
            EditorGUI.indentLevel--;
        }

        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float height = EditorGUIUtility.singleLineHeight;
        if (!property.isExpanded)
        {
            return height;
        }

        foreach (SerializedProperty child in EnumerateVisibleFields(property))
        {
            height += EditorGUIUtility.standardVerticalSpacing +
                      EditorGUI.GetPropertyHeight(child, includeChildren: true);
        }

        return height;
    }

    private static IEnumerable<SerializedProperty> EnumerateVisibleFields(SerializedProperty property)
    {
        SerializedProperty targetType = property.FindPropertyRelative("targetType");
        SerializedProperty effectType = property.FindPropertyRelative("effectType");
        yield return targetType;
        yield return effectType;

        EffectType type = (EffectType)effectType.intValue;
        switch (type)
        {
            case EffectType.SpawnMonsterOnEnemyField:
                SerializedProperty isBoss = property.FindPropertyRelative("isBossSummon");
                yield return isBoss;
                yield return property.FindPropertyRelative(
                    isBoss.boolValue ? "bossMonsterData" : "monsterSpawnEntries");
                break;

            case EffectType.GrantMagicScroll:
                yield return property.FindPropertyRelative("magicScrollData");
                break;

            case EffectType.StrengthenMonsterType:
                yield return property.FindPropertyRelative("strengthenedMonsterData");
                yield return property.FindPropertyRelative("monsterHealthBonusPercent");
                yield return property.FindPropertyRelative("monsterDamageBonusPercent");
                yield return property.FindPropertyRelative("monsterMoveSpeedBonusPercent");
                break;

            case EffectType.StrengthenKing:
                yield return property.FindPropertyRelative("kingDamageBonusPercent");
                yield return property.FindPropertyRelative("kingAttackSpeedBonusPercent");
                yield return property.FindPropertyRelative("kingSkillPowerBonusPercent");
                break;

            default:
                yield return property.FindPropertyRelative("value");
                break;
        }
    }
}
#endif
