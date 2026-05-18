#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(AttackSlashTuningPreview))]
public sealed class AttackSlashTuningPreviewEditor : Editor
{
    private static bool showAdvancedSettings;

    private SerializedProperty unitData;
    private SerializedProperty starLevel;
    private SerializedProperty applyToAllStarLevels;
    private SerializedProperty slashPrefab;
    private SerializedProperty slashPrefabAddress;
    private SerializedProperty spawnOrigin;
    private SerializedProperty targetOverride;
    private SerializedProperty localPositionOffset;
    private SerializedProperty rotationOffsetEuler;
    private SerializedProperty rotationMode;
    private SerializedProperty scaleMultiplier;
    private SerializedProperty playbackSpeed;
    private SerializedProperty primaryRendererFlip;
    private SerializedProperty spawnWhenAnimatorAttackStatePlays;
    private SerializedProperty attackStateName;
    private SerializedProperty attackTriggerName;
    private SerializedProperty attackSpawnNormalizedTime;
    private SerializedProperty replacePreviousPreview;
    private SerializedProperty autoDestroyPreviewInstances;
    private SerializedProperty previewLifetimeSeconds;
    private SerializedProperty loopAttackAndVfx;
    private SerializedProperty loopIntervalSeconds;
    private SerializedProperty restartExistingPreviewInstance;

    private void OnEnable()
    {
        unitData = serializedObject.FindProperty("unitData");
        starLevel = serializedObject.FindProperty("starLevel");
        applyToAllStarLevels = serializedObject.FindProperty("applyToAllStarLevels");
        slashPrefab = serializedObject.FindProperty("slashPrefab");
        slashPrefabAddress = serializedObject.FindProperty("slashPrefabAddress");
        spawnOrigin = serializedObject.FindProperty("spawnOrigin");
        targetOverride = serializedObject.FindProperty("targetOverride");
        localPositionOffset = serializedObject.FindProperty("localPositionOffset");
        rotationOffsetEuler = serializedObject.FindProperty("rotationOffsetEuler");
        rotationMode = serializedObject.FindProperty("rotationMode");
        scaleMultiplier = serializedObject.FindProperty("scaleMultiplier");
        playbackSpeed = serializedObject.FindProperty("playbackSpeed");
        primaryRendererFlip = serializedObject.FindProperty("primaryRendererFlip");
        spawnWhenAnimatorAttackStatePlays = serializedObject.FindProperty("spawnWhenAnimatorAttackStatePlays");
        attackStateName = serializedObject.FindProperty("attackStateName");
        attackTriggerName = serializedObject.FindProperty("attackTriggerName");
        attackSpawnNormalizedTime = serializedObject.FindProperty("attackSpawnNormalizedTime");
        replacePreviousPreview = serializedObject.FindProperty("replacePreviousPreview");
        autoDestroyPreviewInstances = serializedObject.FindProperty("autoDestroyPreviewInstances");
        previewLifetimeSeconds = serializedObject.FindProperty("previewLifetimeSeconds");
        loopAttackAndVfx = serializedObject.FindProperty("loopAttackAndVfx");
        loopIntervalSeconds = serializedObject.FindProperty("loopIntervalSeconds");
        restartExistingPreviewInstance = serializedObject.FindProperty("restartExistingPreviewInstance");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawScriptField();
        DrawSaveTargetSection();
        DrawEffectTuningSection();
        DrawPreviewSection();
        DrawAdvancedSection();

        serializedObject.ApplyModifiedProperties();

        var preview = (AttackSlashTuningPreview)target;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Pull From UnitData"))
            {
                Undo.RecordObject(preview, "Pull Attack Slash Tuning");
                preview.PullFromUnitData();
                EditorUtility.SetDirty(preview);
            }

            if (GUILayout.Button("Spawn Preview"))
            {
                preview.SpawnPreview(true);
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Trigger Attack"))
            {
                preview.TriggerAttack();
            }

            if (GUILayout.Button("Replay VFX"))
            {
                preview.ReplayPreview(true);
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Save To UnitData"))
            {
                preview.CopySettingsToUnitData(true);
            }
        }

        if (preview.LastPreviewInstance != null)
        {
            EditorGUILayout.HelpBox("Move, rotate, or scale the selected preview object, then use its Capture And Save To UnitData button.", MessageType.Info);
        }
    }

    private void DrawScriptField()
    {
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
        }
    }

    private void DrawSaveTargetSection()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Save Target", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(unitData, new GUIContent("Unit Data"));
        EditorGUILayout.PropertyField(starLevel, new GUIContent("Preview Star Level"));
        EditorGUILayout.PropertyField(applyToAllStarLevels, new GUIContent("Save To All Stars"));
    }

    private void DrawEffectTuningSection()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Effect Tuning", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(slashPrefab, new GUIContent("Effect Prefab"));
        if (EditorGUI.EndChangeCheck() && slashPrefab.objectReferenceValue is GameObject prefab)
        {
            slashPrefabAddress.stringValue = prefab.name;
        }

        EditorGUILayout.PropertyField(localPositionOffset, new GUIContent("Position Offset"));
        EditorGUILayout.PropertyField(rotationOffsetEuler, new GUIContent("Rotation Offset"));
        EditorGUILayout.PropertyField(scaleMultiplier, new GUIContent("Scale"));
        EditorGUILayout.PropertyField(attackSpawnNormalizedTime, new GUIContent("Spawn Timing"));
        EditorGUILayout.PropertyField(playbackSpeed, new GUIContent("Playback Speed"));
    }

    private void DrawPreviewSection()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(loopAttackAndVfx, new GUIContent("Loop Attack And VFX"));
        if (loopAttackAndVfx.boolValue)
        {
            EditorGUILayout.PropertyField(loopIntervalSeconds, new GUIContent("Loop Interval"));
        }
    }

    private void DrawAdvancedSection()
    {
        EditorGUILayout.Space();
        showAdvancedSettings = EditorGUILayout.Foldout(showAdvancedSettings, "Advanced", true);
        if (!showAdvancedSettings)
        {
            return;
        }

        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(slashPrefabAddress, new GUIContent("Saved Prefab Address"));
        EditorGUILayout.PropertyField(spawnOrigin, new GUIContent("Spawn Origin"));
        EditorGUILayout.PropertyField(targetOverride, new GUIContent("Target Override"));
        EditorGUILayout.PropertyField(rotationMode, new GUIContent("Rotation Mode"));
        EditorGUILayout.PropertyField(primaryRendererFlip, new GUIContent("Renderer Flip"));
        EditorGUILayout.PropertyField(spawnWhenAnimatorAttackStatePlays, new GUIContent("Spawn From Animator State"));
        EditorGUILayout.PropertyField(attackStateName, new GUIContent("Attack State Name"));
        EditorGUILayout.PropertyField(attackTriggerName, new GUIContent("Attack Trigger Name"));
        EditorGUILayout.PropertyField(replacePreviousPreview, new GUIContent("Replace Previous Preview"));
        EditorGUILayout.PropertyField(autoDestroyPreviewInstances, new GUIContent("Auto Destroy Preview"));
        if (autoDestroyPreviewInstances.boolValue)
        {
            EditorGUILayout.PropertyField(previewLifetimeSeconds, new GUIContent("Preview Lifetime"));
        }

        EditorGUILayout.PropertyField(restartExistingPreviewInstance, new GUIContent("Restart Existing Preview"));
        EditorGUI.indentLevel--;
    }
}

[CustomEditor(typeof(AttackSlashTuningPreviewInstance))]
public sealed class AttackSlashTuningPreviewInstanceEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
        }

        serializedObject.ApplyModifiedProperties();

        var instance = (AttackSlashTuningPreviewInstance)target;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Capture", EditorStyles.boldLabel);

        if (GUILayout.Button("Capture And Save To UnitData"))
        {
            instance.CaptureAndSaveToUnitData();
        }
    }
}
#endif
