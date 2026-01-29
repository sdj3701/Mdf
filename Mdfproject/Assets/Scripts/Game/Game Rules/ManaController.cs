// Assets/Scripts/Game/Game Rules/ManaController.cs
using UnityEngine;
using System;
using Fusion;

public class ManaController : NetworkBehaviour, IMana
{
    // [Networked] 속성으로 마나 값을 네트워크 동기화
    [Networked] private float _currentMana { get; set; }
    [Networked] private float _maxMana { get; set; }
    
    // 로컬 캐시 (변화 감지용)
    private float _lastBroadcastMana = -1f;

    public float CurrentMana => _currentMana;
    public float MaxMana => _maxMana;
    public bool IsManaFull => _currentMana >= _maxMana && _maxMana > 0;

    public event Action OnManaFull;
    public event Action<float, float> OnManaChanged;

    private ChangeDetector _changeDetector;

    public override void Spawned()
    {
        base.Spawned();
        _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);
    }

    public override void Render()
    {
        if (_changeDetector == null) return;

        foreach (var change in _changeDetector.DetectChanges(this))
        {
            if (change == nameof(_currentMana) || change == nameof(_maxMana))
            {
                // 네트워크에서 변경된 값을 UI에 반영
                OnManaChanged?.Invoke(_currentMana, _maxMana);
                
                // 마나가 가득 찼을 때 이벤트 발생
                if (IsManaFull && _lastBroadcastMana < _maxMana)
                {
                    OnManaFull?.Invoke();
                }
                _lastBroadcastMana = _currentMana;
            }
        }
    }

    public void Initialize(float maxMana)
    {
        // StateAuthority가 있을 때만 값 변경
        if (Object == null || Object.HasStateAuthority)
        {
            _maxMana = maxMana;
            _currentMana = 0;
            _lastBroadcastMana = 0;
        }
        OnManaChanged?.Invoke(_currentMana, _maxMana);
    }

    public void GainManaOverTime(float amountPerSecond)
    {
        if (!HasStateAuthority()) return;
        if (IsManaFull) return;
        GainMana(amountPerSecond * Time.deltaTime);
    }

    public void GainMana(float amount)
    {
        if (!HasStateAuthority()) return;
        if (IsManaFull || amount <= 0) return;

        bool wasManaFullBefore = IsManaFull;
        _currentMana = Mathf.Min(_currentMana + amount, _maxMana);
        
        // 로컬 이벤트도 즉시 발생 (서버에서)
        OnManaChanged?.Invoke(_currentMana, _maxMana);

        if (!wasManaFullBefore && IsManaFull)
        {
            OnManaFull?.Invoke();
        }
    }

    public bool UseMana(float amount)
    {
        if (!HasStateAuthority()) return false;
        
        if (_currentMana >= amount)
        {
            _currentMana = 0;
            OnManaChanged?.Invoke(_currentMana, _maxMana);
            return true;
        }
        return false;
    }

    private bool HasStateAuthority()
    {
        if (Object == null || Runner == null || !Runner.IsRunning)
        {
            return true; // 싱글플레이어 폴백
        }
        return Object.HasStateAuthority;
    }
}