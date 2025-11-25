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

    public async UniTask InitializeAsync()
    {
        if (_isReady) return;
        try
        {
            if (inspectorUnitData != null && inspectorUnitData.Count > 0)
            {
                _allUnits = inspectorUnitData.Where(u => u != null).ToList();
                _unitByKey = _allUnits.Where(u => u != null).GroupBy(u => u.name).ToDictionary(g => g.Key, g => g.First());
                int totalI = _allUnits.Count;
                int uniqueI = _unitByKey.Count;
                var duplicatesI = _allUnits.Where(u => u != null)
                    .GroupBy(u => u.name)
                    .Where(g => g.Count() > 1)
                    .Select(g => $"{g.Key} x{g.Count()}")
                    .ToList();
                Debug.Log($"[LoadManager] UnitData preload (Inspector) complete. Total={totalI}, UniqueKeys={uniqueI}, Duplicates={duplicatesI.Count}{(duplicatesI.Count > 0 ? " [" + string.Join(", ", duplicatesI) + "]" : "")}");
                for (int i = 0; i < _allUnits.Count; i++)
                {
                    var u = _allUnits[i];
                    if (u == null)
                    {
                        Debug.Log($"[LoadManager] #{i + 1} Null UnitData entry");
                        continue;
                    }
                    string prefabsI = u.prefabsByStarLevel != null ? string.Join(", ", u.prefabsByStarLevel) : string.Empty;
                    string skillsI = u.skillsByStarLevel != null ? string.Join(", ", u.skillsByStarLevel) : string.Empty;
                    string projectilesI = u.projectilePrefabsByStarLevel != null ? string.Join(", ", u.projectilePrefabsByStarLevel) : string.Empty;
                    Debug.Log($"[LoadManager] #{i + 1} key='{u.name}', unitName='{u.unitName}', cost={u.cost}, type={u.unitType}, icon='{u.unitIcon}', prefabs=[{prefabsI}], skills=[{skillsI}], projectiles=[{projectilesI}]");
                }
                _isReady = true;
                _unitLoadTcs.TrySetResult(true);
                return;
            }
            var handle = Addressables.LoadAssetsAsync<UnitData>("UnitData", null);
            var result = await handle.Task;
            _allUnits = result != null ? result.ToList() : new List<UnitData>();
            _unitByKey = _allUnits.Where(u => u != null).GroupBy(u => u.name).ToDictionary(g => g.Key, g => g.First());
            int total = _allUnits.Count;
            int unique = _unitByKey.Count;
            var duplicates = _allUnits.Where(u => u != null)
                .GroupBy(u => u.name)
                .Where(g => g.Count() > 1)
                .Select(g => $"{g.Key} x{g.Count()}")
                .ToList();
            Debug.Log($"[LoadManager] UnitData preload complete. Total={total}, UniqueKeys={unique}, Duplicates={duplicates.Count}{(duplicates.Count > 0 ? " [" + string.Join(", ", duplicates) + "]" : "")}");
            for (int i = 0; i < _allUnits.Count; i++)
            {
                var u = _allUnits[i];
                if (u == null)
                {
                    Debug.Log($"[LoadManager] #{i + 1} Null UnitData entry");
                    continue;
                }
                string prefabs = u.prefabsByStarLevel != null ? string.Join(", ", u.prefabsByStarLevel) : string.Empty;
                string skills = u.skillsByStarLevel != null ? string.Join(", ", u.skillsByStarLevel) : string.Empty;
                string projectiles = u.projectilePrefabsByStarLevel != null ? string.Join(", ", u.projectilePrefabsByStarLevel) : string.Empty;
                Debug.Log($"[LoadManager] #{i + 1} key='{u.name}', unitName='{u.unitName}', cost={u.cost}, type={u.unitType}, icon='{u.unitIcon}', prefabs=[{prefabs}], skills=[{skills}], projectiles=[{projectiles}]");
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

    public UniTask WaitUntilReady()
    {
        if (_isReady) return UniTask.CompletedTask;
        return _unitLoadTcs.Task.AsUniTask();
    }

    public IReadOnlyList<UnitData> GetAllUnitData()
    {
        return _allUnits;
    }

    public UnitData GetUnitData(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        _unitByKey.TryGetValue(key, out var data);
        return data;
    }
}
