public interface IHealth
{
    float CurrentHealth { get; }
    float MaxHealth { get; }
    event System.Action<float, float> OnHealthChanged; // Current, Max
}
