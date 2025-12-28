using UnityEngine;

public class PooledObject : MonoBehaviour
{
    public GameObject OriginPrefab { get; private set; }

    private VfxPoolManager _pool;

    public void Initialize(VfxPoolManager pool, GameObject originPrefab)
    {
        _pool = pool;
        OriginPrefab = originPrefab;
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
