#if UNITY_EDITOR
using System;
using UnityEngine;

public sealed class StatusBarLifecycleProbe : MonoBehaviour, IHealth, IMana
{
    private event Action<float, float> HealthChanged;
    private event Action<float, float> ManaChanged;

    public float CurrentHealth { get; private set; } = 100f;
    public float MaxHealth { get; private set; } = 100f;
    public float CurrentMana { get; private set; } = 20f;
    public float MaxMana { get; private set; } = 40f;
    public int HealthSubscriberCount => HealthChanged?.GetInvocationList().Length ?? 0;
    public int ManaSubscriberCount => ManaChanged?.GetInvocationList().Length ?? 0;

    public event Action<float, float> OnHealthChanged
    {
        add => HealthChanged += value;
        remove => HealthChanged -= value;
    }

    public event Action<float, float> OnManaChanged
    {
        add => ManaChanged += value;
        remove => ManaChanged -= value;
    }

    public void Heal(float amount)
    {
        RaiseHealth(Mathf.Min(MaxHealth, CurrentHealth + Mathf.Max(0f, amount)), MaxHealth);
    }

    public void RaiseHealth(float current, float max)
    {
        CurrentHealth = current;
        MaxHealth = max;
        HealthChanged?.Invoke(current, max);
    }

    public void RaiseMana(float current, float max)
    {
        CurrentMana = current;
        MaxMana = max;
        ManaChanged?.Invoke(current, max);
    }
}
#endif
