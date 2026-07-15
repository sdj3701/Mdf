// Assets/Scripts/ComponentRegistrySystem/GameAssets.cs
using UnityEngine;

/// <summary>
/// 게임 내 에셋들에 대한 정적 접근을 제공하는 중앙 클래스입니다.
/// 이름 기반으로 에셋과 컴포넌트를 가져옵니다.
/// </summary>
public static class GameAssets
{
    #region Cameras
    public static class Cameras
    {
        public static Camera MainCamera =>
            ComponentRegistry.Get<Camera>("Main Camera", false) ??
            Camera.main ??
            Object.FindObjectOfType<Camera>();
    }
    #endregion
}
