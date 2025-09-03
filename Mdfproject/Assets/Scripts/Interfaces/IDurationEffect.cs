// Assets/Scripts/Interfaces/IDurationEffect.cs (새 파일)

/// <summary>
/// 지속 시간을 가지는 스킬 효과들이 구현해야 하는 인터페이스입니다.
/// </summary>
public interface IDurationEffect
{
    /// <summary>
    /// 효과의 지속 시간(초)입니다.
    /// </summary>
    float Duration { get; }
}