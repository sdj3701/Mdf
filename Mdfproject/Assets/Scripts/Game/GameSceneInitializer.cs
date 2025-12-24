using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

/// <summary>
/// 인게임 씬을 직접 실행했을 때 싱글플레이 모드로 게임을 시작하는 초기화 스크립트
/// </summary>
public class GameSceneInitializer : MonoBehaviour
{
    [Header("싱글플레이 설정")]
    [Tooltip("인게임 씬을 직접 실행했을 때 자동으로 싱글플레이 모드로 시작할지 여부")]
    public bool autoStartSinglePlayer = true;

    [Tooltip("싱글플레이 시 플레이어 수 (나머지는 AI로 채워짐)")]
    [Range(1, 4)]
    public int singlePlayerCount = 2;

    [Header("필수 프리팹 참조")]
    public GameObject gameManagersPrefab;

    private NetworkRunner _runner;
    private bool _isInitialized = false;
    private bool _isOwnerOfRunner = false; // 이 스크립트가 Runner를 직접 생성했는지 여부

    async void Start()
    {
        // 중복 등록 경고를 막기 위해 씬 시작 시 레지스트리를 초기화합니다.
        ComponentRegistry.Clear();

        // NetworkManager가 이미 존재하면 멀티플레이 모드로 진입한 것이므로 초기화하지 않음
        if (NetworkManager.Instance != null && NetworkManager.Instance.IsGameRunnerActive)
        {
            Debug.Log("[GameSceneInitializer] 멀티플레이 모드로 진입. 싱글플레이 초기화를 건너뜁니다.");
            await StartMultiPlayerMode();
            return;
        }

        // 자동 시작이 활성화되어 있으면 싱글플레이 모드로 게임 시작
        if (autoStartSinglePlayer)
        {
            Debug.Log("[GameSceneInitializer] 싱글플레이 모드로 게임을 시작합니다...");
            await StartSinglePlayerMode();
        }
    }

    /// <summary>
    /// 싱글플레이 모드로 게임을 시작합니다.
    /// </summary>
    private async UniTask StartSinglePlayerMode()
    {
        if (_isInitialized)
        {
            Debug.LogWarning("[GameSceneInitializer] 이미 초기화되었습니다.");
            return;
        }

        _isInitialized = true;

        // NetworkRunner 생성
        _runner = gameObject.AddComponent<NetworkRunner>();
        _runner.ProvideInput = true;

        var objectProvider = gameObject.GetComponent<PooledNetworkObjectProvider>();
        if (objectProvider == null)
        {
            objectProvider = gameObject.AddComponent<PooledNetworkObjectProvider>();
        }

        // [수정] 현재 씬의 빌드 인덱스를 가져옵니다
        Scene currentScene = SceneManager.GetActiveScene();
        int currentSceneIndex = currentScene.buildIndex;
        SceneRef sceneRef = SceneRef.FromIndex(currentSceneIndex);

        Debug.Log($"[GameSceneInitializer] 현재 씬: {currentScene.name} (빌드 인덱스: {currentSceneIndex})");
        Debug.Log($"[GameSceneInitializer] 🎮 싱글플레이 모드 플레이어 수 설정: {singlePlayerCount}명");

        // [수정] Single 모드로 게임 시작 (완전한 오프라인 로컬 싱글플레이)
        var result = await _runner.StartGame(new StartGameArgs()
        {
            GameMode = GameMode.Single, // 오프라인 싱글플레이 모드 (Photon 서버 연결 안함)
            SessionName = "SinglePlayerSession",
            Scene = sceneRef, // 현재 씬을 명시적으로 지정
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>(),
            PlayerCount = singlePlayerCount, // GameSceneInitializer의 설정을 따름
            ObjectProvider = objectProvider
        });

        if (result.Ok)
        {
            Debug.Log("[GameSceneInitializer] ✅ 싱글플레이 모드 시작 성공!");

            // GameManagers 스폰
            await SpawnGameManagers();
        }
        else
        {
            Debug.LogError($"[GameSceneInitializer] ❌ 싱글플레이 모드 시작 실패: {result.ShutdownReason}");
        }
    }

    private async UniTask StartMultiPlayerMode()
    {
        if (_isInitialized)
        {
            Debug.LogWarning("[GameSceneInitializer] 이미 초기화되었습니다.");
            return;
        }

        _isInitialized = true;
        // 한 프레임 기다려서 Runner의 상태가 안정화될 시간을 줍니다.
        await UniTask.Yield(); 

        // NetworkManager를 통해 이미 존재하는 Runner를 가져옵니다.
        _runner = NetworkManager.Instance._runner;
        if (_runner == null || !_runner.IsRunning)
        {
            Debug.LogError("[GameSceneInitializer] 멀티플레이 모드지만 Runner가 실행 중이지 않습니다.");
            return;
        }

        if (_runner.GameMode != GameMode.Host)
        {
            Debug.Log("서버가 아니니까 생성 할 필요 없어");
            return;
        }

        Debug.Log("[GameSceneInitializer] ✅ 멀티플레이 모드 시작 성공!");
        await SpawnGameManagers();
    }

    /// <summary>
    /// GameManagers 네트워크 객체를 스폰합니다.
    /// </summary>
    private async UniTask SpawnGameManagers()
    {
        if (_runner == null || !_runner.IsRunning)
        {
            Debug.LogError("[GameSceneInitializer] Runner가 실행 중이지 않습니다.");
            return;
        }

        // GameManagers 프리팹 찾기
        if (gameManagersPrefab == null)
        {
            Debug.LogError("[GameSceneInitializer] GameManagers 프리팹이 할당되지 않았습니다!");
            return;
        }

        NetworkObject gameManagersNO = gameManagersPrefab.GetComponent<NetworkObject>();
        if (gameManagersNO == null)
        {
            Debug.LogError("[GameSceneInitializer] GameManagers 프리팹에 NetworkObject 컴포넌트가 없습니다!");
            return;
        }

        // GameManagers 스폰
        Debug.Log("[GameSceneInitializer] GameManagers를 스폰합니다...");
        NetworkObject spawnedGameManagers = await _runner.SpawnAsync(gameManagersPrefab, Vector3.zero, Quaternion.identity);

        if (spawnedGameManagers != null)
        {
            Debug.Log("[GameSceneInitializer] ✅ GameManagers 스폰 완료!");
            
            // 싱글플레이어 모드인 경우, GameManagers의 singlePlayerModeCount를 설정
            if (_runner.GameMode == GameMode.Single)
            {
                var gameManagers = spawnedGameManagers.GetComponent<GameManagers>();
                if (gameManagers != null)
                {
                    gameManagers.Rpc_SetSinglePlayerModeCount(singlePlayerCount);
                }
            }
        }
        else
        {
            Debug.LogError("[GameSceneInitializer] ❌ GameManagers 스폰 실패!");
        }
    }

    private void OnDestroy()
    {
        // 씬이 종료될 때 Runner 정리
        if (_runner != null && _runner.IsRunning)
        {
            _runner.Shutdown();
        }
    }
}
