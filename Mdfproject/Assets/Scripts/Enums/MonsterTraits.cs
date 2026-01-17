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
    
    // === 향후 확장 예시 ===
    // Armored = 1 << 1,      // 갑옷: 물리 피해 감소
    // MagicImmune = 1 << 2,  // 마법 면역
}
