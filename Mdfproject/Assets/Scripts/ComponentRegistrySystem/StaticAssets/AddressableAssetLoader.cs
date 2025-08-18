using Cysharp.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Addressable을 통한 에셋 로더
/// AssetRegistry와 연동하여 동적 로딩 지원
/// </summary>
public class AddressableAssetLoader : MonoBehaviour
{
    public static AddressableAssetLoader Instance { get; private set; }
    [Header("로드 설정")]
    [SerializeField] private bool loadOnStart = true;
    [SerializeField] private bool logProgress = true;

    // 타일 베이스 리스트
    [Header("타일 에셋들")]
    public List<string> tileBaseList = new List<string>
    {
        "Ground",
        "Wall",
        "BreakWall",
        "Start",
        "End",
    };

    private bool isLoading = false;
    public bool IsLoading => isLoading;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private async void Start()
    {
        if (loadOnStart)
        {
            await LoadAllAssets();
        }
    }
    /// <summary>
    /// 모든 에셋 리스트를 로드합니다.
    /// </summary>
    public async UniTask LoadAllAssets()
    {
        if (isLoading)
        {
            Debug.LogWarning("[AddressableAssetLoader] 이미 로딩 중입니다.");
            return;
        }

        isLoading = true;

        if (logProgress)
            Debug.Log("[AddressableAssetLoader] 에셋 로딩 시작");

        try
        {
            // 1. 타일들 로드
            await AssetRegistry.LoadAndRegisterTile("BreakWall");

            // TODO : sprites, prefabs, sounds 나중에 로드 추가
            await AssetRegistry.LoadAndRegisterSprite("HeroSprite");

            if (logProgress)
                Debug.Log("[AddressableAssetLoader] ✅ 모든 에셋 로딩 완료");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[AddressableAssetLoader] ❌ 로딩 중 오류: {e.Message}");
        }
        finally
        {
            isLoading = false;
        }
    }

    
}
