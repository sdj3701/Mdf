// Assets/Scripts/Game/ManaController.cs
using UnityEngine;
using System; // event 사용을 위해 추가

public class ManaController : MonoBehaviour
{
    public float CurrentMana { get; private set; }
    public int MaxMana { get; private set; }
    public bool IsManaFull => CurrentMana >= MaxMana;

    // 마나가 가득 찼을 때 외부 클래스에 알려주기 위한 이벤트
    public event Action OnManaFull;

    public void Initialize(int maxMana)
    {
        this.MaxMana = maxMana;
        this.CurrentMana = 0;
    }

    /// <summary>
    /// 매 프레임 호출되어 서서히 마나를 채웁니다.
    /// </summary>
    public void GainManaOverTime(float amountPerSecond)
    {
        if (IsManaFull) return;
        GainMana(amountPerSecond * Time.deltaTime);
    }

    /// <summary>
    /// 지정된 양만큼 마나를 획득합니다.
    /// </summary>
    public void GainMana(float amount)
    {
        if (IsManaFull || amount <= 0) return;

        bool wasManaFullBefore = IsManaFull;
        CurrentMana = Mathf.Min(CurrentMana + amount, MaxMana);

        // 마나가 가득 차지 않은 상태였다가 이번에 가득 찼다면 이벤트를 호출
        if (!wasManaFullBefore && IsManaFull)
        {
            OnManaFull?.Invoke();
        }
    }

    /// <summary>
    /// 지정된 양만큼 마나를 소모합니다.
    /// </summary>
    public bool UseMana(int amount)
    {
        if (CurrentMana >= amount)
        {
            CurrentMana -= amount;
            return true;
        }
        return false;
    }
}