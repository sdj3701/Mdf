// Assets/Scripts/Game/Interfaces/IEnemy.cs

/// <summary>
/// 게임 내에서 피해를 입을 수 있는 모든 객체(몬스터, 파괴 가능한 벽 등)가
/// 반드시 따라야 하는 규칙(인터페이스)입니다.
/// </summary>
public interface IEnemy
{
    /// <summary>
    /// 지정된 속성과 양의 피해를 입습니다.
    /// </summary>
    /// <param name="baseDamage">가해자의 기본 공격력</param>
    /// <param name="damageType">공격의 속성 (물리/마법)</param>
    void TakeDamage(float baseDamage, DamageType damageType);
}