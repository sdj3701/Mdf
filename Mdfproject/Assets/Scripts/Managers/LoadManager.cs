using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Cysharp.Threading.Tasks;

public class LoadManager : MonoBehaviour
{
    public static LoadManager Instance { get; private set; }

    [SerializeField] private List<UnitData> inspectorUnitData = new List<UnitData>();

    private bool _isReady = false;
    private List<UnitData> _allUnits = new List<UnitData>();
    private Dictionary<string, UnitData> _unitByKey = new Dictionary<string, UnitData>();
    private UniTaskCompletionSource<bool> _unitLoadTcs = new UniTaskCompletionSource<bool>();

    /// <summary>
    /// 싱글톤 인스턴스를 초기화하고 씬 전환 시에도 유지되도록 설정합니다.
    /// </summary>
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public bool IsReady => _isReady;

    /// <summary>
    /// 인스펙터 또는 Addressables에서 UnitData를 로드하고 조회용 캐시를 구성합니다.
    /// </summary>
    public async UniTask InitializeAsync()
    {
        if (_isReady) return;
        try
        {
            if (inspectorUnitData != null && inspectorUnitData.Count > 0)
            {
                _allUnits = inspectorUnitData.Where(u => u != null).ToList();
                _unitByKey = _allUnits.Where(u => u != null).GroupBy(u => u.name).ToDictionary(g => g.Key, g => g.First());
                for (int i = 0; i < _allUnits.Count; i++)
                {
                    var u = _allUnits[i];
                    if (u == null)
                    {
                        Debug.LogWarning($"[LoadManager] #{i + 1} Null UnitData entry");
                    }
                }
                _isReady = true;
                _unitLoadTcs.TrySetResult(true);
                return;
            }
            var handle = Addressables.LoadAssetsAsync<UnitData>("UnitData", null);
            var result = await handle.Task;
            _allUnits = result != null ? result.ToList() : new List<UnitData>();
            _unitByKey = _allUnits.Where(u => u != null).GroupBy(u => u.name).ToDictionary(g => g.Key, g => g.First());
            for (int i = 0; i < _allUnits.Count; i++)
            {
                var u = _allUnits[i];
                if (u == null)
                {
                    Debug.LogWarning($"[LoadManager] #{i + 1} Null UnitData entry");
                }
            }
            _isReady = true;
            _unitLoadTcs.TrySetResult(true);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[LoadManager] Failed to load UnitData assets: {e.Message}");
            _unitLoadTcs.TrySetException(e);
        }
    }

    /// <summary>
    /// InitializeAsync가 완료되면 끝나는 작업을 반환합니다. 이미 준비되었다면 즉시 완료됩니다.
    /// </summary>
    public UniTask WaitUntilReady()
    {
        if (_isReady) return UniTask.CompletedTask;
        return _unitLoadTcs.Task.AsUniTask();
    }

    /// <summary>
    /// 로드된 모든 UnitData의 읽기 전용 리스트를 반환합니다.
    /// </summary>
    public IReadOnlyList<UnitData> GetAllUnitData()
    {
        return _allUnits;
    }

    /// <summary>
    /// 키(이름)로 UnitData를 조회합니다. 없거나 키가 유효하지 않으면 null을 반환합니다.
    /// </summary>
    public UnitData GetUnitData(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        _unitByKey.TryGetValue(key, out var data);
        return data;
    }
}
