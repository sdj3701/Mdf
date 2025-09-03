// Assets/Scripts/Interfaces/IHealth.cs
public interface IHealth
{
    float CurrentHealth { get; }
    float MaxHealth { get; }
    event System.Action<float, float> OnHealthChanged; // Current, Max

    // 체력 회복을 위한 메서드 추가
    void Heal(float amount);
}