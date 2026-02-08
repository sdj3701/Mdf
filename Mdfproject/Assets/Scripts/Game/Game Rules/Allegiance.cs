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

    #region 마법 스크롤 지원
    /// <summary>
    /// 이 오브젝트를 몬스터 진영으로 설정합니다.
    /// 마법 스크롤 사용 시 호출되어 TargetingStrategy가 올바르게 작동하도록 합니다.
    /// - 아군(AllyLayer) = Monster 레이어
    /// - 적군(EnemyLayer) = Unit 레이어
    /// </summary>
    public void SetAsMonster()
    {
        // Monster 레이어: 몬스터가 배치되는 레이어
        // Unit 레이어: 플레이어 유닛이 배치되는 레이어
        AllyLayer = LayerMask.GetMask("Monster");
        EnemyLayer = LayerMask.GetMask("Unit");
    }
    #endregion
}