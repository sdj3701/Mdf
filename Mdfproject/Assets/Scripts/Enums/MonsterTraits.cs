// Assets/Scripts/Enums/MonsterTraits.cs
using System;

/// <summary>
/// 몬스터의 고유 특성입니다. (영구적, MonsterData에서 설정)
/// Flags로 여러 특성을 조합할 수 있습니다.
/// </summary>
[Flags]
public enum MonsterTraits
{
    None = 0,
    
    /// <summary>
    /// 저지불가: 유닛에 의해 저지되지 않고 통과합니다.
    /// 벽에는 막히며 A* 경로를 따라 이동합니다.
    /// </summary>
    Unblockable = 1 << 0,
    
    /// <summary>
    /// 파괴자: 파괴 가능한 벽을 없는 것처럼 최단 경로로 이동.
    /// 벽을 만나면 멈춰서 공격 애니메이션과 함께 부수고 지나감.
    /// </summary>
    Destroyer = 1 << 1,
    
    // === 향후 확장 예시 ===
    // Armored = 1 << 2,      // 갑옷: 물리 피해 감소
    // MagicImmune = 1 << 3,  // 마법 면역
}
