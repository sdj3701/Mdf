using UnityEngine;

public sealed class AttackSlashTuningPreviewInstance : MonoBehaviour
{
    [SerializeField] private AttackSlashTuningPreview owner;
    [SerializeField] private Transform origin;
    [SerializeField] private Quaternion attackRotation = Quaternion.identity;
    [SerializeField] private Vector3 prefabBaseScale = Vector3.one;

    public AttackSlashTuningPreview Owner => owner;
    public Transform Origin => origin;
    public Quaternion AttackRotation => attackRotation;
    public Vector3 PrefabBaseScale => prefabBaseScale;

    public void Initialize(AttackSlashTuningPreview previewOwner, Transform spawnOrigin, Quaternion resolvedAttackRotation, Vector3 sourcePrefabScale)
    {
        owner = previewOwner;
        origin = spawnOrigin;
        attackRotation = resolvedAttackRotation;
        prefabBaseScale = sourcePrefabScale;
    }

    public void CaptureToOwner(bool saveToUnitData)
    {
        if (owner == null)
        {
            Debug.LogWarning("[AttackSlashTuningPreviewInstance] Owner is missing.");
            return;
        }

        owner.CaptureFromPreviewInstance(this);
        if (saveToUnitData)
        {
            owner.CopySettingsToUnitData(true);
        }
    }
}
