public interface IMana
{
    float CurrentMana { get; }
    float MaxMana { get; }
    event System.Action<float, float> OnManaChanged; // Current, Max
}
