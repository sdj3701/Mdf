//// Assets/Scripts/UI/UIManagers.cs - 개선된 버전
//using System.Collections;
//using System.Collections.Generic;
//using UnityEngine;
//using Cysharp.Threading.Tasks;

//public class UIManagers : MonoBehaviour
//{
//    public static UIManagers Instance = null;

//    [Header("기본 UI 리스트 (인스펙터 등록용)")]
//    public List<GameObject> UILists;
    
//    [Header("동적 로딩 설정")]
//    [SerializeField] private bool useAddressableForMissingUI = true;
//    [SerializeField] private bool logUIOperations = true;

//    private Dictionary<string, UIPool> uiPools;
//    private Canvas mainCanvas;

//    private void Awake()
//    {
//        if (Instance == null)
//        {
//            Instance = this;
//            DontDestroyOnLoad(this.gameObject);
//            InitializeUIPools();
//        }
//        else
//        {
//            Destroy(this.gameObject);
//        }
//    }

//    /// <summary>
//    /// UI 풀들을 초기화합니다.
//    /// </summary>
//    private void InitializeUIPools()
//    {
//        uiPools = new Dictionary<string, UIPool>();

//        // 인스펙터에 등록된 UI들을 풀에 추가
//        foreach (GameObject ui in UILists)
//        {
//            if (ui != null)
//            {
//                UIPool.PoolSettings settings = new UIPool.PoolSettings
//                {
//                    initialPoolSize = 1,
//                    maxPoolSize = 3,
//                    allowDynamicExpansion = true
//                };
                
//                uiPools.Add(ui.name, new UIPool(ui, null, settings));
                
//                if (logUIOperations)
//                    Debug.Log($"[UIManager] 풀 등록: {ui.name}");
//            }
//        }
//    }

//    /// <summary>
//    /// 지정된 이름의 UI 요소를 찾은 MainCanvas 아래에 활성화합니다.
//    /// </summary>
//    /// <param name="uiName">가져올 UI의 이름</param>
//    /// <param name="forceReload">강제로 새 인스턴스를 생성할지 여부</param>
//    /// <returns>활성화된 UI 게임 오브젝트</returns>
//    public async UniTask<GameObject> GetUIElement(string uiName, bool forceReload = false)
//    {
//        try
//        {
//            // MainCanvas 찾기 또는 캐시 사용
//            if (mainCanvas == null || !mainCanvas.gameObject.activeInHierarchy)
//            {
//                mainCanvas = FindMainCanvas();
//                if (mainCanvas == null)
//                {
//                    Debug.LogError("[UIManager] MainCanvas를 찾을 수 없습니다! UI를 표시할 수 없습니다.");
//                    return null;
//                }
//            }

//            // 기존 풀에 있는지 확인
//            if (uiPools.ContainsKey(uiName))
//            {
//                var ui = await uiPools[uiName].GetObject(uiName, mainCanvas.transform);
//                if (logUIOperations)
//                    Debug.Log($"[UIManager] UI 활성화: {uiName}");
//                return ui;
//            }
//            // 풀에 없고 Addressable 사용이 활성화된 경우
//            else if (useAddressableForMissingUI)
//            {
//                if (logUIOperations)
//                    Debug.Log($"[UIManager] 동적 로딩 시도: {uiName}");
                
//                UIPool.PoolSettings settings = new UIPool.PoolSettings();
//                UIPool newPool = new UIPool(null, uiName, settings);
//                uiPools.Add(uiName, newPool);
                
//                var ui = await newPool.GetObject(uiName, mainCanvas.transform);
//                if (ui != null && logUIOperations)
//                    Debug.Log($"[UIManager] 동적 로딩 성공: {uiName}");
                
//                return ui;
//            }
//            else
//            {
//                Debug.LogWarning($"[UIManager] UI '{uiName}'가 등록되어 있지 않으며, 동적 로딩이 비활성화되어 있습니다.");
//                return null;
//            }
//        }
//        catch (System.Exception e)
//        {
//            Debug.LogError($"[UIManager] UI 로딩 중 예외 발생: {uiName} - {e.Message}");
//            return null;
//        }
//    }

//    /// <summary>
//    /// MainCanvas를 찾습니다. 여러 Canvas가 있을 경우 가장 적절한 것을 선택합니다.
//    /// </summary>
//    private Canvas FindMainCanvas()
//    {
//        Canvas[] canvases = FindObjectsOfType<Canvas>();
        
//        // World Space Canvas 제외하고 Screen Space Canvas 우선 선택
//        foreach (Canvas canvas in canvases)
//        {
//            if (canvas.renderMode != RenderMode.WorldSpace && canvas.gameObject.activeInHierarchy)
//            {
//                if (logUIOperations)
//                    Debug.Log($"[UIManager] MainCanvas 설정: {canvas.name}");
//                return canvas;
//            }
//        }
        
//        // 그래도 없으면 첫 번째 활성 Canvas 사용
//        foreach (Canvas canvas in canvases)
//        {
//            if (canvas.gameObject.activeInHierarchy)
//            {
//                if (logUIOperations)
//                    Debug.Log($"[UIManager] MainCanvas 설정 (백업): {canvas.name}");
//                return canvas;
//            }
//        }
        
//        return null;
//    }

//    /// <summary>
//    /// 사용이 끝난 UI 요소를 비활성화하여 풀에 반환합니다.
//    /// </summary>
//    /// <param name="uiName">반환할 UI의 이름</param>
//    public void ReturnUIElement(string uiName)
//    {
//        if (uiPools.ContainsKey(uiName))
//        {
//            uiPools[uiName].ReturnObject(uiName);
//            if (logUIOperations)
//                Debug.Log($"[UIManager] UI 비활성화: {uiName}");
//        }
//        else
//        {
//            Debug.LogWarning($"[UIManager] UI '{uiName}'가 풀에 등록되어 있지 않습니다.");
//        }
//    }

//    /// <summary>
//    /// 특정 UI가 현재 활성화되어 있는지 확인합니다.
//    /// </summary>
//    public bool IsUIActive(string uiName)
//    {
//        if (uiPools.ContainsKey(uiName))
//        {
//            return uiPools[uiName].IsUIActive(uiName);
//        }
//        return false;
//    }

//    /// <summary>
//    /// 모든 활성 UI를 비활성화합니다.
//    /// </summary>
//    public void HideAllUI()
//    {
//        foreach (var pool in uiPools.Values)
//        {
//            pool.DeactivateAllUI();
//        }
        
//        if (logUIOperations)
//            Debug.Log("[UIManager] 모든 UI 숨김 완료");
//    }

//    /// <summary>
//    /// 씬 전환 시 UI 시스템을 정리합니다.
//    /// </summary>
//    public void CleanupForSceneTransition()
//    {
//        // 모든 UI 풀 정리
//        foreach (var pool in uiPools.Values)
//        {
//            pool.ClearPool();
//        }
        
//        // Canvas 참조 초기화 (새 씬에서 다시 찾도록)
//        mainCanvas = null;
        
//        if (logUIOperations)
//            Debug.Log("[UIManager] 씬 전환 정리 완료");
//    }

//    /// <summary>
//    /// UI 시스템 상태를 출력합니다. (디버깅용)
//    /// </summary>
//    public void PrintUIStatus()
//    {
//        Debug.Log($"[UIManager] === UI 시스템 상태 ===");
//        Debug.Log($"등록된 UI 풀 수: {uiPools.Count}");
//        Debug.Log($"MainCanvas: {(mainCanvas != null ? mainCanvas.name : "없음")}");
        
//        foreach (var pool in uiPools)
//        {
//            Debug.Log($" - {pool.Key}: {(IsUIActive(pool.Key) ? "활성" : "비활성")}");
//        }
//    }

//    /// <summary>
//    /// 새로운 UI를 런타임에 등록합니다.
//    /// </summary>
//    /// <param name="uiName">UI 이름</param>
//    /// <param name="prefab">UI 프리팹 (선택사항)</param>
//    public void RegisterNewUI(string uiName, GameObject prefab = null)
//    {
//        if (!uiPools.ContainsKey(uiName))
//        {
//            UIPool.PoolSettings settings = new UIPool.PoolSettings();
//            uiPools.Add(uiName, new UIPool(prefab, uiName, settings));
            
//            if (logUIOperations)
//                Debug.Log($"[UIManager] 새 UI 등록: {uiName}");
//        }
//        else
//        {
//            Debug.LogWarning($"[UIManager] UI '{uiName}'가 이미 등록되어 있습니다.");
//        }
//    }

//    /// <summary>
//    /// UI 등록을 해제합니다.
//    /// </summary>
//    public void UnregisterUI(string uiName)
//    {
//        if (uiPools.ContainsKey(uiName))
//        {
//            uiPools[uiName].ClearPool();
//            uiPools.Remove(uiName);
            
//            if (logUIOperations)
//                Debug.Log($"[UIManager] UI 등록 해제: {uiName}");
//        }
//    }

//    private void OnDestroy()
//    {
//        // 정리 작업
//        if (uiPools != null)
//        {
//            foreach (var pool in uiPools.Values)
//            {
//                pool.ClearPool();
//            }
//            uiPools.Clear();
//        }
//    }
//}
