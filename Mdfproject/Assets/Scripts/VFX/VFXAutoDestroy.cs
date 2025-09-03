// Assets/Scripts/Game/VFXAutoDestroy.cs (새 파일)
using UnityEngine;

/// <summary>
/// 지정된 시간 후에 이 게임 오브젝트를 자동으로 파괴합니다.
/// 모든 스킬 VFX 프리팹에 이 컴포넌트를 추가해야 합니다.
/// </summary>
public class VFXAutoDestroy : MonoBehaviour
{
    /// <summary>
    /// 이 컴포넌트의 파괴 타이머를 초기화하고 시작합니다.
    /// </summary>
    /// <param name="lifetime">오브젝트가 파괴되기까지의 시간(초)</param>
    public void Initialize(float lifetime)
    {
        // 지정된 lifetime 후에 이 게임 오브젝트를 파괴하도록 예약합니다.
        Destroy(gameObject, lifetime);
    }
}