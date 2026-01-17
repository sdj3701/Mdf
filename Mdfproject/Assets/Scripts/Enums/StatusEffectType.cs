// Assets/Scripts/Enums/StatusEffectType.cs
using System;

/// <summary>
/// 런타임에 적용되는 상태 효과 타입입니다.
/// 스킬, 증강 등에 의해 일시적으로 적용됩니다.
/// </summary>
[Flags]
public enum StatusEffectType
{
    None = 0,
    
    /// <summary>기절: 이동/공격/스킬 불가</summary>
    Stunned = 1 << 0,
    
    /// <summary>슬로우: 이동속도 감소</summary>
    Slowed = 1 << 1,
    
    /// <summary>속박: 이동 불가, 공격/스킬 가능</summary>
    Rooted = 1 << 2,
    
    /// <summary>침묵: 스킬 사용 불가</summary>
    Silenced = 1 << 3,
    
    /// <summary>화상: 마법 지속 피해</summary>
    Burning = 1 << 4,
    
    /// <summary>동상: 마법 지속 피해 + 슬로우</summary>
    Frostbitten = 1 << 5,
    
    /// <summary>출혈: 물리 지속 피해</summary>
    Bleeding = 1 << 6,
    
    /// <summary>중독: 마법 지속 피해</summary>
    Poisoned = 1 << 7,
}
