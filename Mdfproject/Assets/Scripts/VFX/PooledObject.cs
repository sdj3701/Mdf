using UnityEngine;

public class PooledObject : MonoBehaviour
{
    public GameObject OriginPrefab { get; private set; }

    private VfxPoolManager _pool;
    private bool _isInPool;

    public bool IsInPool => _isInPool;

    public void Initialize(VfxPoolManager pool, GameObject originPrefab)
    {
        _pool = pool;
        OriginPrefab = originPrefab;
        _isInPool = false;
    }

    internal void MarkRented()
    {
        _isInPool = false;
    }

    internal bool TryBeginReturn()
    {
        if (_isInPool)
        {
            return false;
        }

        _isInPool = true;
        return true;
    }

    public void ReturnToPool()
    {
        if (_pool != null)
        {
            _pool.Despawn(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
}
