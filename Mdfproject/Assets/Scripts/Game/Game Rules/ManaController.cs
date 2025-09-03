// Assets/Scripts/Game/Game Rules/ManaController.cs (수정된 전체 코드)
using UnityEngine;
using System;

public class ManaController : MonoBehaviour, IMana
{
    // [수정] [SerializeField]를 추가하여 인스펙터에서 private 변수를 볼 수 있도록 함
    [SerializeField]
    private float currentMana;
    [SerializeField]
    private float maxMana;

    public float CurrentMana => currentMana;
    public float MaxMana => maxMana;
    public bool IsManaFull => currentMana >= maxMana;

    public event Action OnManaFull;
    public event Action<float, float> OnManaChanged;

    public void Initialize(float maxMana)
    {
        this.maxMana = maxMana;
        this.currentMana = 0;
        OnManaChanged?.Invoke(currentMana, this.maxMana);
    }

    public void GainManaOverTime(float amountPerSecond)
    {
        if (IsManaFull) return;
        GainMana(amountPerSecond * Time.deltaTime);
    }

    public void GainMana(float amount)
    {
        if (IsManaFull || amount <= 0) return;

        bool wasManaFullBefore = IsManaFull;
        currentMana = Mathf.Min(currentMana + amount, maxMana);
        OnManaChanged?.Invoke(currentMana, maxMana);

        if (!wasManaFullBefore && IsManaFull)
        {
            OnManaFull?.Invoke();
        }
    }

    public bool UseMana(float amount)
    {
        if (CurrentMana >= amount)
        {
            currentMana = 0; 
            OnManaChanged?.Invoke(currentMana, maxMana);
            return true;
        }
        return false;
    }
}