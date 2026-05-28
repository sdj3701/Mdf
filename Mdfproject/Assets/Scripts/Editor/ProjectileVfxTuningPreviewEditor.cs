#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ProjectileVfxTuningPreview))]
public sealed class ProjectileVfxTuningPreviewEditor : Editor
{
    private static bool showAdvanced;

    private SerializedProperty unitData;
    private SerializedProperty firePointOverride;
    private SerializedProperty targetOverride;
    private SerializedProperty muzzleFlashPrefab;
    private SerializedProperty muzzleFlashAddress;
    private SerializedProperty muzzleLocalPositionOffset;
    private SerializedProperty muzzleRotationOffsetEuler;
    private SerializedProperty muzzleScaleMultiplier;
    private SerializedProperty muzzlePlaybackSpeed;
    private SerializedProperty muzzleLifetimeSeconds;
    private SerializedProperty projectilePrefab;
    private SerializedProperty projectileAddress;
    private SerializedProperty projectileLocalPositionOffset;
    private SerializedProperty projectileRotationOffsetEuler;
    private SerializedProperty projectileScaleMultiplier;
    private SerializedProperty projectilePlaybackSpeed;
    private SerializedProperty projectileSpeed;
    private SerializedProperty alignProjectileToDirection;
    private SerializedProperty impactFlashPrefab;
    private SerializedProperty impactFlashAddress;
    private SerializedProperty impactLocalPositionOffset;
    private SerializedProperty impactRotationOffsetEuler;
    private SerializedProperty impactScaleMultiplier;
    private SerializedProperty impactPlaybackSpeed;
    private SerializedProperty impactLifetimeSeconds;
    private SerializedProperty alignImpactToDirection;
    private SerializedProperty loopAttackAndVfx;
    private SerializedProperty previewFinalAttackSpeed;
    private SerializedProperty attackSpawnNormalizedTime;
    private SerializedProperty previewAnimationSpeedCap;
    private SerializedProperty fallbackAttackClipDuration;
    private SerializedProperty attackTriggerName;
    private SerializedProperty pullFromUnitDataOnEnable;
    private SerializedProperty faceTargetOnEnable;
    private SerializedProperty destroyPreviewObjectsOnDisable;

    private void OnEnable()
    {
        unitData = serializedObject.FindProperty("unitData");
        firePointOverride = serializedObject.FindProperty("firePointOverride");
        targetOverride = serializedObject.FindProperty("targetOverride");
        muzzleFlashPrefab = serializedObject.FindProperty("muzzleFlashPrefab");
        muzzleFlashAddress = serializedObject.FindProperty("muzzleFlashAddress");
        muzzleLocalPositionOffset = serializedObject.FindProperty("muzzleLocalPositionOffset");
        muzzleRotationOffsetEuler = serializedObject.FindProperty("muzzleRotationOffsetEuler");
        muzzleScaleMultiplier = serializedObject.FindProperty("muzzleScaleMultiplier");
        muzzlePlaybackSpeed = serializedObject.FindProperty("muzzlePlaybackSpeed");
        muzzleLifetimeSeconds = serializedObject.FindProperty("muzzleLifetimeSeconds");
        projectilePrefab = serializedObject.FindProperty("projectilePrefab");
        projectileAddress = serializedObject.FindProperty("projectileAddress");
        projectileLocalPositionOffset = serializedObject.FindProperty("projectileLocalPositionOffset");
        projectileRotationOffsetEuler = serializedObject.FindProperty("projectileRotationOffsetEuler");
        projectileScaleMultiplier = serializedObject.FindProperty("projectileScaleMultiplier");
        projectilePlaybackSpeed = serializedObject.FindProperty("projectilePlaybackSpeed");
        projectileSpeed = serializedObject.FindProperty("projectileSpeed");
        alignProjectileToDirection = serializedObject.FindProperty("alignProjectileToDirection");
        impactFlashPrefab = serializedObject.FindProperty("impactFlashPrefab");
        impactFlashAddress = serializedObject.FindProperty("impactFlashAddress");
        impactLocalPositionOffset = serializedObject.FindProperty("impactLocalPositionOffset");
        impactRotationOffsetEuler = serializedObject.FindProperty("impactRotationOffsetEuler");
        impactScaleMultiplier = serializedObject.FindProperty("impactScaleMultiplier");
        impactPlaybackSpeed = serializedObject.FindProperty("impactPlaybackSpeed");
        impactLifetimeSeconds = serializedObject.FindProperty("impactLifetimeSeconds");
        alignImpactToDirection = serializedObject.FindProperty("alignImpactToDirection");
        loopAttackAndVfx = serializedObject.FindProperty("loopAttackAndVfx");
        previewFinalAttackSpeed = serializedObject.FindProperty("previewFinalAttackSpeed");
        attackSpawnNormalizedTime = serializedObject.FindProperty("attackSpawnNormalizedTime");
        previewAnimationSpeedCap = serializedObject.FindProperty("previewAnimationSpeedCap");
        fallbackAttackClipDuration = serializedObject.FindProperty("fallbackAttackClipDuration");
        attackTriggerName = serializedObject.FindProperty("attackTriggerName");
        pullFromUnitDataOnEnable = serializedObject.FindProperty("pullFromUnitDataOnEnable");
        faceTargetOnEnable = serializedObject.FindProperty("faceTargetOnEnable");
        destroyPreviewObjectsOnDisable = serializedObject.FindProperty("destroyPreviewObjectsOnDisable");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawScriptField();
        DrawSaveTarget();
        DrawStage("Muzzle Flash", muzzleFlashPrefab, muzzleLocalPositionOffset, muzzleRotationOffsetEuler, muzzleScaleMultiplier, muzzlePlaybackSpeed, muzzleLifetimeSeconds, null);
        DrawStage("Projectile", projectilePrefab, projectileLocalPositionOffset, projectileRotationOffsetEuler, projectileScaleMultiplier, projectilePlaybackSpeed, null, alignProjectileToDirection, projectileSpeed);
        DrawStage("Impact Flash", impactFlashPrefab, impactLocalPositionOffset, impactRotationOffsetEuler, impactScaleMultiplier, impactPlaybackSpeed, impactLifetimeSeconds, alignImpactToDirection);
        DrawLoopPreview();
        DrawAdvanced();

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space();
        if (GUILayout.Button("Save To UnitData"))
        {
            ((ProjectileVfxTuningPreview)target).CopySettingsToUnitData(true);
        }

        EditorGUILayout.HelpBox("Play the test scene to loop attack animation plus muzzle, projectile, and impact VFX. Tune these values here, then Save To UnitData.", MessageType.Info);
    }

    private void DrawScriptField()
    {
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
        }
    }

    private void DrawSaveTarget()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Save Target", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(unitData, new GUIContent("Unit Data"));
        EditorGUILayout.PropertyField(targetOverride, new GUIContent("Target"));
    }

    private static void DrawStage(
        string title,
        SerializedProperty prefab,
        SerializedProperty offset,
        SerializedProperty rotation,
        SerializedProperty scale,
        SerializedProperty playbackSpeed,
        SerializedProperty lifetime,
        SerializedProperty align,
        SerializedProperty travelSpeed = null)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(prefab, new GUIContent("Prefab"));
        if (travelSpeed != null)
        {
            EditorGUILayout.PropertyField(travelSpeed, new GUIContent("Travel Speed"));
        }

        EditorGUILayout.PropertyField(offset, new GUIContent("Position Offset"));
        EditorGUILayout.PropertyField(rotation, new GUIContent("Rotation Offset"));
        EditorGUILayout.PropertyField(scale, new GUIContent("Scale"));
        EditorGUILayout.PropertyField(playbackSpeed, new GUIContent("Playback Speed"));
        if (lifetime != null)
        {
            EditorGUILayout.PropertyField(lifetime, new GUIContent("Lifetime"));
        }

        if (align != null)
        {
            EditorGUILayout.PropertyField(align, new GUIContent("Align To Direction"));
        }
    }

    private void DrawLoopPreview()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Loop Preview", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(loopAttackAndVfx, new GUIContent("Loop Attack And VFX"));
        EditorGUILayout.PropertyField(previewFinalAttackSpeed, new GUIContent("Final Attack Speed"));
        EditorGUILayout.PropertyField(attackSpawnNormalizedTime, new GUIContent("Spawn Timing"));
        EditorGUILayout.LabelField("Preview Rate", BuildPreviewSummary(), EditorStyles.miniLabel);
    }

    private string BuildPreviewSummary()
    {
        float finalAttackSpeed = Mathf.Max(0.01f, previewFinalAttackSpeed.floatValue);
        float cap = Mathf.Max(0.01f, previewAnimationSpeedCap.floatValue);
        float previewRate = Mathf.Min(finalAttackSpeed, cap);
        float interval = 1f / previewRate;
        float spawnDelay = Mathf.Clamp01(attackSpawnNormalizedTime.floatValue) / previewRate;
        return $"attack {interval:0.###}s, spawn +{spawnDelay:0.###}s";
    }

    private void DrawAdvanced()
    {
        EditorGUILayout.Space();
        showAdvanced = EditorGUILayout.Foldout(showAdvanced, "Advanced", true);
        if (!showAdvanced)
        {
            return;
        }

        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(firePointOverride, new GUIContent("Fire Point"));
        EditorGUILayout.PropertyField(muzzleFlashAddress, new GUIContent("Muzzle Address"));
        EditorGUILayout.PropertyField(projectileAddress, new GUIContent("Projectile Address"));
        EditorGUILayout.PropertyField(impactFlashAddress, new GUIContent("Impact Address"));
        EditorGUILayout.PropertyField(previewAnimationSpeedCap, new GUIContent("Animation Speed Cap"));
        EditorGUILayout.PropertyField(fallbackAttackClipDuration, new GUIContent("Fallback Clip Duration"));
        EditorGUILayout.PropertyField(attackTriggerName, new GUIContent("Attack Trigger"));
        EditorGUILayout.PropertyField(pullFromUnitDataOnEnable, new GUIContent("Pull On Enable"));
        EditorGUILayout.PropertyField(faceTargetOnEnable, new GUIContent("Face Target On Enable"));
        EditorGUILayout.PropertyField(destroyPreviewObjectsOnDisable, new GUIContent("Destroy Preview Objects"));
        EditorGUI.indentLevel--;
    }
}
#endif
