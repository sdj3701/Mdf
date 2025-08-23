// Assets/Scripts/Game/Game Rules/DamageCalculator.cs
using UnityEngine;

/// <summary>
/// 게임의 모든 데미지 계산을 중앙에서 처리하는 정적 클래스입니다.
/// </summary>
public static class DamageCalculator
{
    private const float MINIMUM_DAMAGE_PERCENTAGE = 0.05f; // 최소 데미지 보장 (공격력의 5%)

    /// <summary>
    /// 최종 데미지를 계산합니다.
    /// </summary>
    /// <param name="baseDamage">공격자의 기본 공격력</param>
    /// <param name="damageType">공격의 속성 (물리/마법)</param>
    /// <param name="targetDefense">피격자의 방어력</param>
    /// <param name="targetMagicResistance">피격자의 마법 저항력</param>
    /// <returns>방어력이 적용된 최종 데미지</returns>
    public static int CalculateDamage(float baseDamage, DamageType damageType, float targetDefense, float targetMagicResistance)
    {
        float finalDamage;

        switch (damageType)
        {
            case DamageType.Physical:
                // 방어력 공식: Damage = BaseDamage * (100 / (100 + Defense))
                // 방어력이 100이면 데미지가 50%로 감소합니다.
                finalDamage = baseDamage * (100f / (100f + targetDefense));
                break;

            case DamageType.Magic:
                // 마법 저항력 공식: Damage = BaseDamage * (1 - MagicResistance / 100)
                // 마법 저항력이 30이면 데미지가 30% 감소합니다.
                float resistance = Mathf.Clamp(targetMagicResistance, 0, 100); // 0~100 사이 값으로 제한
                finalDamage = baseDamage * (1f - (resistance / 100f));
                break;
            
            default:
                finalDamage = baseDamage;
                break;
        }

        // 최소 데미지 보장
        float minimumDamage = baseDamage * MINIMUM_DAMAGE_PERCENTAGE;
        if (finalDamage < minimumDamage)
        {
            finalDamage = minimumDamage;
        }

        return Mathf.Max(1, Mathf.RoundToInt(finalDamage)); // 최소 1의 데미지는 항상 들어가도록 반올림
    }
}