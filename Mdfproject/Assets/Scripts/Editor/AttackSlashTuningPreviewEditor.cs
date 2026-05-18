#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(AttackSlashTuningPreview))]
public sealed class AttackSlashTuningPreviewEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var preview = (AttackSlashTuningPreview)target;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Tuning Actions", EditorStyles.boldLabel);

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
            EditorGUILayout.HelpBox("Loop Attack And Vfx keeps restarting the selected preview object in Play Mode, so move/rotate/scale it while the particles replay. Use the preview object's inspector button to capture the transform back to this UnitData.", MessageType.Info);
        }
    }
}

[CustomEditor(typeof(AttackSlashTuningPreviewInstance))]
public sealed class AttackSlashTuningPreviewInstanceEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var instance = (AttackSlashTuningPreviewInstance)target;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Capture", EditorStyles.boldLabel);

        if (GUILayout.Button("Capture To Tuning Component"))
        {
            instance.CaptureToOwner(false);
            if (instance.Owner != null)
            {
                Selection.activeObject = instance.Owner;
            }
        }

        if (GUILayout.Button("Capture And Save To UnitData"))
        {
            instance.CaptureToOwner(true);
        }
    }
}
#endif
