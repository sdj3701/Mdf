// Assets/Scripts/Game/Game Rules/ManaController.cs
using UnityEngine;
using System;
using Fusion;

public class ManaController : NetworkBehaviour, IMana
{
    [Networked] private float _currentMana { get; set; }
    [Networked] private float _maxMana { get; set; }

    private float _lastBroadcastMana = -1f;
    private float _localCurrentMana;
    private float _localMaxMana;
    private bool _hasLocalInitialized;

    public float CurrentMana => CanReadNetworkedMana() ? _currentMana : _localCurrentMana;
    public float MaxMana => CanReadNetworkedMana() ? _maxMana : _localMaxMana;
    public bool IsManaFull => CurrentMana >= MaxMana && MaxMana > 0;

    public event Action OnManaFull;
    public event Action<float, float> OnManaChanged;

    private ChangeDetector _changeDetector;

    public override void Spawned()
    {
        base.Spawned();
        _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);

        if (CanWriteNetworkedMana() && _hasLocalInitialized)
        {
            _currentMana = _localCurrentMana;
            _maxMana = _localMaxMana;
        }
        else
        {
            _localCurrentMana = _currentMana;
            _localMaxMana = _maxMana;
        }
    }

    public override void Render()
    {
        if (_changeDetector == null) return;

        foreach (var change in _changeDetector.DetectChanges(this))
        {
            if (change == nameof(_currentMana) || change == nameof(_maxMana))
            {
                _localCurrentMana = _currentMana;
                _localMaxMana = _maxMana;

                OnManaChanged?.Invoke(_localCurrentMana, _localMaxMana);

                if (IsManaFull && _lastBroadcastMana < _localMaxMana)
                {
                    OnManaFull?.Invoke();
                }

                _lastBroadcastMana = _localCurrentMana;
            }
        }
    }

    public void Initialize(float maxMana)
    {
        if (HasManaAuthorityOrOffline())
        {
            _localMaxMana = maxMana;
            _localCurrentMana = 0;
            _hasLocalInitialized = true;

            if (CanWriteNetworkedMana())
            {
                _maxMana = maxMana;
                _currentMana = 0;
            }

            _lastBroadcastMana = 0;
        }

        OnManaChanged?.Invoke(CurrentMana, MaxMana);
    }

    public void GainManaOverTime(float amountPerSecond)
    {
        if (!HasManaAuthorityOrOffline()) return;
        if (IsManaFull) return;
        GainMana(amountPerSecond * Time.deltaTime);
    }

    public void GainMana(float amount)
    {
        if (!HasManaAuthorityOrOffline()) return;
        if (IsManaFull || amount <= 0) return;

        bool wasManaFullBefore = IsManaFull;
        float maxMana = MaxMana;
        float currentMana = Mathf.Min(CurrentMana + amount, maxMana);
        _localCurrentMana = currentMana;
        _localMaxMana = maxMana;
        _hasLocalInitialized = true;

        if (CanWriteNetworkedMana())
        {
            _currentMana = currentMana;
        }

        OnManaChanged?.Invoke(CurrentMana, MaxMana);

        if (!wasManaFullBefore && IsManaFull)
        {
            OnManaFull?.Invoke();
        }
    }

    public bool UseMana(float amount)
    {
        if (!HasManaAuthorityOrOffline()) return false;

        if (CurrentMana >= amount)
        {
            _localCurrentMana = 0;
            _hasLocalInitialized = true;

            if (CanWriteNetworkedMana())
            {
                _currentMana = 0;
            }

            OnManaChanged?.Invoke(CurrentMana, MaxMana);
            return true;
        }

        return false;
    }

    private bool CanReadNetworkedMana()
    {
        return Object != null
            && Object.IsValid
            && Runner != null
            && Runner.IsRunning;
    }

    private bool CanWriteNetworkedMana()
    {
        return CanReadNetworkedMana() && Object.HasStateAuthority;
    }

    private bool HasManaAuthorityOrOffline()
    {
        if (Object == null || !Object.IsValid || Runner == null || !Runner.IsRunning)
        {
            return true;
        }

        return Object.HasStateAuthority;
    }
}
