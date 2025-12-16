// Assets/Scripts/Game/Allegiance.cs (새 파일)
using UnityEngine;

/// <summary>
/// 이 컴포넌트가 부착된 오브젝트의 소속과 적대 관계를 정의합니다.
/// 스킬 시스템이 시전자의 적이 누구인지 동적으로 판단하는 데 사용됩니다.
/// </summary>
public class Allegiance : MonoBehaviour
{
    [Tooltip("이 오브젝트가 적으로 간주하는 대상들의 레이어 마스크입니다.")]
    public LayerMask EnemyLayer;

    [Tooltip("이 오브젝트가 아군으로 간주하는 대상들의 레이어 마스크입니다.")]
    public LayerMask AllyLayer;
}