#if UNITY_EDITOR
using System;
using Newtonsoft.Json.Linq;
using UnityCliConnector;
using UnityCliConnector.Tools;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class AttackSlashAnimatorDefaultsTool
{
    public const string UnitBaseControllerPath = "Assets/Resource/Animations/Unit_Base_Controller.controller";
    private const string AttackTriggerParameter = "AttackTrigger";

    [MenuItem("Tools/MDF/VFX/Fix Unit Attack Trigger Defaults")]
    public static void FixUnitAttackTriggerDefaultsMenu()
    {
        FixUnitAttackTriggerDefaults(true);
    }

    public static AttackSlashAnimatorDefaultsResult FixUnitAttackTriggerDefaults(bool saveAssets)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(UnitBaseControllerPath);
        if (controller == null)
        {
            return new AttackSlashAnimatorDefaultsResult
            {
                success = false,
                controllerPath = UnitBaseControllerPath,
                message = "Unit base animator controller not found."
            };
        }

        var serializedController = new SerializedObject(controller);
        SerializedProperty parameters = serializedController.FindProperty("m_AnimatorParameters");
        bool found = false;
        bool changed = false;

        for (int i = 0; i < parameters.arraySize; i++)
        {
            SerializedProperty parameter = parameters.GetArrayElementAtIndex(i);
            SerializedProperty nameProperty = parameter.FindPropertyRelative("m_Name");
            SerializedProperty typeProperty = parameter.FindPropertyRelative("m_Type");
            SerializedProperty defaultBoolProperty = parameter.FindPropertyRelative("m_DefaultBool");

            if (nameProperty == null ||
                typeProperty == null ||
                defaultBoolProperty == null ||
                !string.Equals(nameProperty.stringValue, AttackTriggerParameter, StringComparison.Ordinal))
            {
                continue;
            }

            found = true;
            if (typeProperty.intValue == (int)AnimatorControllerParameterType.Trigger && defaultBoolProperty.boolValue)
            {
                defaultBoolProperty.boolValue = false;
                changed = true;
            }
        }

        if (changed)
        {
            serializedController.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(controller);
            if (saveAssets)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(UnitBaseControllerPath, ImportAssetOptions.ForceUpdate);
            }
        }

        return new AttackSlashAnimatorDefaultsResult
        {
            success = found,
            controllerPath = UnitBaseControllerPath,
            changed = changed,
            attackTriggerDefaultBool = ReadAttackTriggerDefaultBool(controller),
            message = found
                ? (changed ? "AttackTrigger defaultBool was reset to false." : "AttackTrigger defaultBool was already false.")
                : "AttackTrigger parameter not found."
        };
    }

    private static bool ReadAttackTriggerDefaultBool(AnimatorController controller)
    {
        if (controller == null)
        {
            return false;
        }

        AnimatorControllerParameter[] parameters = controller.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i] != null && string.Equals(parameters[i].name, AttackTriggerParameter, StringComparison.Ordinal))
            {
                return parameters[i].defaultBool;
            }
        }

        return false;
    }
}

public sealed class AttackSlashAnimatorDefaultsResult
{
    public bool success;
    public string controllerPath;
    public bool changed;
    public bool attackTriggerDefaultBool;
    public string message;
}

[UnityCliTool(Name = "attack_slash_fix_animator_defaults", Description = "Reset unsafe unit attack animator trigger defaults.")]
public static class AttackSlashFixAnimatorDefaultsCliTool
{
    public static object HandleCommand(JObject parameters)
    {
        return AttackSlashAnimatorDefaultsTool.FixUnitAttackTriggerDefaults(true);
    }
}
#endif
