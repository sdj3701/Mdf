using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class PooledNetworkObjectProvider : Fusion.Behaviour, INetworkObjectProvider
{
    [Tooltip("If true, Acquire will Retry while the scene manager is busy.")]
    public bool DelayIfSceneManagerIsBusy = true;

    [Tooltip("0 or negative keeps all released instances in the pool.")]
    [SerializeField] private int maxPoolCount = 0;

    private readonly Dictionary<NetworkPrefabId, Queue<NetworkObject>> _free = new Dictionary<NetworkPrefabId, Queue<NetworkObject>>();

    protected virtual NetworkObject InstantiatePrefab(NetworkRunner runner, NetworkObject prefab, NetworkPrefabId prefabId)
    {
        // 풀에서 재사용 가능한 인스턴스 확인
        if (_free.TryGetValue(prefabId, out var freeQueue) && freeQueue.Count > 0)
        {
            var instance = freeQueue.Dequeue();
            // 풀에 있던 인스턴스가 파괴되었는지 확인
            if (instance == null || instance.gameObject == null)
            {
                // Debug.Log($"[PooledNetworkObjectProvider] 풀에서 가져온 인스턴스가 null입니다. PrefabId: {prefabId}. 새로 생성합니다.");
                // 계속해서 새 인스턴스 생성으로 진행
            }
            else
            {
                instance.gameObject.SetActive(true);
                return instance;
            }
        }

        if (!_free.ContainsKey(prefabId))
        {
            _free.Add(prefabId, new Queue<NetworkObject>());
        }

        // 프리팹이 null인지 확인
        if (prefab == null)
        {
            // Debug.LogError($"[PooledNetworkObjectProvider] ❌ 프리팹이 null입니다! PrefabId: {prefabId}. Fusion 프리팹 테이블을 확인하세요.");
            return null;
        }

        // Debug.Log($"[PooledNetworkObjectProvider] 새 인스턴스 생성: {prefab.name}, PrefabId: {prefabId}");
        return Instantiate(prefab);
    }

    protected virtual void DestroyPrefabInstance(NetworkRunner runner, NetworkPrefabId prefabId, NetworkObject instance)
    {
        if (!_free.TryGetValue(prefabId, out var freeQueue))
        {
            Destroy(instance.gameObject);
            return;
        }

        if (maxPoolCount > 0 && freeQueue.Count >= maxPoolCount)
        {
            Destroy(instance.gameObject);
            return;
        }

        instance.gameObject.SetActive(false);
        freeQueue.Enqueue(instance);
    }

    public NetworkObjectAcquireResult AcquirePrefabInstance(NetworkRunner runner, in NetworkPrefabAcquireContext context,
        out NetworkObject instance)
    {
        instance = null;

        if (DelayIfSceneManagerIsBusy && runner.SceneManager != null && runner.SceneManager.IsBusy)
        {
            return NetworkObjectAcquireResult.Retry;
        }

        NetworkObject prefab;
        try
        {
            prefab = runner.Prefabs.Load(context.PrefabId, isSynchronous: context.IsSynchronous);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load prefab: {ex}");
            return NetworkObjectAcquireResult.Failed;
        }

        if (!prefab)
        {
            return NetworkObjectAcquireResult.Retry;
        }

        instance = InstantiatePrefab(runner, prefab, context.PrefabId);
        Assert.Check(instance);

        if (context.DontDestroyOnLoad)
        {
            runner.MakeDontDestroyOnLoad(instance.gameObject);
        }
        else
        {
            runner.MoveToRunnerScene(instance.gameObject);
        }

        runner.Prefabs.AddInstance(context.PrefabId);
        return NetworkObjectAcquireResult.Success;
    }

    public void ReleaseInstance(NetworkRunner runner, in NetworkObjectReleaseContext context)
    {
        var instance = context.Object;

        if (!context.IsBeingDestroyed)
        {
            if (context.TypeId.IsPrefab)
            {
                DestroyPrefabInstance(runner, context.TypeId.AsPrefabId, instance);
            }
            else
            {
                Destroy(instance.gameObject);
            }
        }

        if (context.TypeId.IsPrefab)
        {
            runner.Prefabs.RemoveInstance(context.TypeId.AsPrefabId);
        }
    }

    public NetworkPrefabId GetPrefabId(NetworkRunner runner, NetworkObjectGuid prefabGuid)
    {
        if (runner == null)
        {
            return default;
        }
        return runner.Prefabs.GetId(prefabGuid);
    }

    public void Initialize(NetworkRunner runner)
    {
    }

    public void Shutdown(NetworkRunner runner)
    {
        _free.Clear();
    }

    public void SetMaxPoolCount(int count)
    {
        maxPoolCount = count;
    }
}
