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
    private SerializedProperty targetOverride;
    private SerializedProperty localPositionOffset;
    private SerializedProperty rotationOffsetEuler;
    private SerializedProperty rotationMode;
    private SerializedProperty scaleMultiplier;
    private SerializedProperty playbackSpeed;
    private SerializedProperty vfxPlaybackSpeedCap;
    private SerializedProperty minimumVisibleSeconds;
    private SerializedProperty primaryRendererFlip;
    private SerializedProperty previewFinalAttackSpeed;
    private SerializedProperty spawnWhenAnimatorAttackStatePlays;
    private SerializedProperty attackStateName;
    private SerializedProperty attackTriggerName;
    private SerializedProperty attackSpawnNormalizedTime;
    private SerializedProperty autoDestroyPreviewInstances;
    private SerializedProperty previewLifetimeSeconds;
    private SerializedProperty loopAttackAndVfx;
    private SerializedProperty useFinalAttackSpeedForLoopInterval;
    private SerializedProperty loopIntervalSeconds;
    private SerializedProperty previewAnimationSpeedCap;
    private SerializedProperty fallbackAttackClipDuration;

    private void OnEnable()
    {
        unitData = serializedObject.FindProperty("unitData");
        starLevel = serializedObject.FindProperty("starLevel");
        applyToAllStarLevels = serializedObject.FindProperty("applyToAllStarLevels");
        slashPrefab = serializedObject.FindProperty("slashPrefab");
        slashPrefabAddress = serializedObject.FindProperty("slashPrefabAddress");
        targetOverride = serializedObject.FindProperty("targetOverride");
        localPositionOffset = serializedObject.FindProperty("localPositionOffset");
        rotationOffsetEuler = serializedObject.FindProperty("rotationOffsetEuler");
        rotationMode = serializedObject.FindProperty("rotationMode");
        scaleMultiplier = serializedObject.FindProperty("scaleMultiplier");
        playbackSpeed = serializedObject.FindProperty("playbackSpeed");
        vfxPlaybackSpeedCap = serializedObject.FindProperty("vfxPlaybackSpeedCap");
        minimumVisibleSeconds = serializedObject.FindProperty("minimumVisibleSeconds");
        primaryRendererFlip = serializedObject.FindProperty("primaryRendererFlip");
        previewFinalAttackSpeed = serializedObject.FindProperty("previewFinalAttackSpeed");
        spawnWhenAnimatorAttackStatePlays = serializedObject.FindProperty("spawnWhenAnimatorAttackStatePlays");
        attackStateName = serializedObject.FindProperty("attackStateName");
        attackTriggerName = serializedObject.FindProperty("attackTriggerName");
        attackSpawnNormalizedTime = serializedObject.FindProperty("attackSpawnNormalizedTime");
        autoDestroyPreviewInstances = serializedObject.FindProperty("autoDestroyPreviewInstances");
        previewLifetimeSeconds = serializedObject.FindProperty("previewLifetimeSeconds");
        loopAttackAndVfx = serializedObject.FindProperty("loopAttackAndVfx");
        useFinalAttackSpeedForLoopInterval = serializedObject.FindProperty("useFinalAttackSpeedForLoopInterval");
        loopIntervalSeconds = serializedObject.FindProperty("loopIntervalSeconds");
        previewAnimationSpeedCap = serializedObject.FindProperty("previewAnimationSpeedCap");
        fallbackAttackClipDuration = serializedObject.FindProperty("fallbackAttackClipDuration");
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
        EditorGUILayout.LabelField("Save", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Save To UnitData"))
            {
                preview.CopySettingsToUnitData(true);
            }
        }

        EditorGUILayout.HelpBox("Adjust Position Offset, Rotation Offset, Scale, Spawn Timing, and Playback Speed here, then Save To UnitData.", MessageType.Info);
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
        EditorGUILayout.PropertyField(vfxPlaybackSpeedCap, new GUIContent("VFX Speed Cap"));
        EditorGUILayout.PropertyField(minimumVisibleSeconds, new GUIContent("Min Visible Seconds"));
    }

    private void DrawPreviewSection()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(previewFinalAttackSpeed, new GUIContent("Final Attack Speed"));
        DrawPreviewSpeedSummary();
        EditorGUILayout.PropertyField(loopAttackAndVfx, new GUIContent("Loop Attack And VFX"));
        if (loopAttackAndVfx.boolValue && !useFinalAttackSpeedForLoopInterval.boolValue)
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
        EditorGUILayout.PropertyField(targetOverride, new GUIContent("Target Override"));
        EditorGUILayout.PropertyField(rotationMode, new GUIContent("Rotation Mode"));
        EditorGUILayout.PropertyField(primaryRendererFlip, new GUIContent("Renderer Flip"));
        EditorGUILayout.PropertyField(useFinalAttackSpeedForLoopInterval, new GUIContent("Use Attack Speed Timing"));
        if (!useFinalAttackSpeedForLoopInterval.boolValue)
        {
            EditorGUILayout.PropertyField(loopIntervalSeconds, new GUIContent("Manual Loop Interval"));
        }

        EditorGUILayout.PropertyField(previewAnimationSpeedCap, new GUIContent("Animation Speed Cap"));
        EditorGUILayout.PropertyField(fallbackAttackClipDuration, new GUIContent("Fallback Attack Clip Duration"));
        EditorGUILayout.PropertyField(spawnWhenAnimatorAttackStatePlays, new GUIContent("Spawn From Animator State"));
        EditorGUILayout.PropertyField(attackStateName, new GUIContent("Attack State Name"));
        EditorGUILayout.PropertyField(attackTriggerName, new GUIContent("Attack Trigger Name"));
        EditorGUILayout.PropertyField(autoDestroyPreviewInstances, new GUIContent("Auto Destroy Preview"));
        if (autoDestroyPreviewInstances.boolValue)
        {
            EditorGUILayout.PropertyField(previewLifetimeSeconds, new GUIContent("Preview Lifetime"));
        }

        EditorGUI.indentLevel--;
    }

    private void DrawPreviewSpeedSummary()
    {
        float finalAttackSpeed = Mathf.Max(0.01f, previewFinalAttackSpeed.floatValue);
        float cap = Mathf.Max(0.01f, previewAnimationSpeedCap.floatValue);
        float vfxCap = Mathf.Max(0.01f, vfxPlaybackSpeedCap.floatValue);
        float clipDuration = Mathf.Max(0.01f, fallbackAttackClipDuration.floatValue);
        float minVisible = Mathf.Max(0f, minimumVisibleSeconds.floatValue);
        float damageInterval = 1f / finalAttackSpeed;
        float presentationRate = Mathf.Min(finalAttackSpeed, cap);
        float presentationInterval = useFinalAttackSpeedForLoopInterval.boolValue
            ? 1f / presentationRate
            : Mathf.Max(0.05f, loopIntervalSeconds.floatValue);
        float animationSpeed = Mathf.Max(0.01f, clipDuration * presentationRate);
        float vfxSpeed = BasicAttackVfxRuntimeUtility.ResolvePlaybackSpeed(playbackSpeed.floatValue, animationSpeed, vfxCap);
        float lifetime = BasicAttackVfxRuntimeUtility.ResolveLifetimeSeconds(previewLifetimeSeconds.floatValue, vfxSpeed, minVisible);

        EditorGUILayout.LabelField(
            "Preview Result",
            $"damage {damageInterval:0.###}s, preview {presentationInterval:0.###}s, anim x{animationSpeed:0.###}, vfx x{vfxSpeed:0.###}, life {lifetime:0.###}s",
            EditorStyles.miniLabel);
    }
}
#endif
