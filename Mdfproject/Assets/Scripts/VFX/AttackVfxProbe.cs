using UnityEngine;

public sealed class AttackVfxProbe : MonoBehaviour
{
    [Tooltip("Optional slash spawn origin. For weapons, place it near the grip. For kicks or punches, place it near the joint or contact center.")]
    public Transform slashOrigin;

    [Tooltip("Start of the striking part. This can be a weapon grip, fist, ankle, or foot base.")]
    public Transform weaponBase;

    [Tooltip("End of the striking part. This can be a blade tip, fist front, toe, or the leading point of a kick.")]
    public Transform weaponTip;
}
