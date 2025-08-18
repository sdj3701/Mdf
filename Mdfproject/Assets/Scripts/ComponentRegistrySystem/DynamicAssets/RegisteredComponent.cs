using UnityEngine;

/// <summary>
/// 자동 등록을 위한 베이스 클래스
/// </summary>
public abstract class RegisteredComponent : MonoBehaviour
{
    [Header("자동 등록 설정")]
    [SerializeField] protected string componentId;
    [SerializeField] protected bool autoRegister = true;

    protected virtual void Awake()
    {
        if (autoRegister)
        {
            if (string.IsNullOrEmpty(componentId))
            {
                componentId = GenerateDefaultId();
            }
            RegisterSelf();
        }
    }

    protected virtual void OnDestroy()
    {
        if (autoRegister && !string.IsNullOrEmpty(componentId))
        {
            UnregisterSelf();
        }
    }

    protected abstract void RegisterSelf();
    protected abstract void UnregisterSelf();

    protected virtual string GenerateDefaultId()
    {
        return $"{GetType().Name}_{GetInstanceID()}";
    }
}
