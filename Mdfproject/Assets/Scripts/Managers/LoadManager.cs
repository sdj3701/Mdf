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
            // 데이터 소스 결정: 인스펙터 우선, 없으면 Addressables
            if (inspectorUnitData != null && inspectorUnitData.Count > 0)
            {
                _allUnits = inspectorUnitData.Where(u => u != null).ToList();
            }
            else
            {
                var handle = Addressables.LoadAssetsAsync<UnitData>("UnitData", null);
                var result = await handle.Task;
                _allUnits = result?.ToList() ?? new List<UnitData>();
            }

            // 딕셔너리 생성 (공통)
            _unitByKey = _allUnits
                .GroupBy(u => u.name)
                .ToDictionary(g => g.Key, g => g.First());

            _isReady = true;
            _unitLoadTcs.TrySetResult(true);

            Debug.Log($"[LoadManager] {_allUnits.Count}개 UnitData 로드 완료");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[LoadManager] UnitData 로드 실패: {e.Message}");
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
