using UnityEngine;

/// <summary>
/// Caches the stable component hierarchy of a projectile or one-shot VFX instance.
/// VFX pool reuse can then reset components without repeated hierarchy scans.
/// </summary>
public sealed class ProjectileVfxComponentCache : MonoBehaviour
{
    public Projectile Projectile { get; private set; }
    public Rigidbody[] Rigidbodies { get; private set; }
    public Collider[] Colliders { get; private set; }
    public MonoBehaviour[] Behaviours { get; private set; }
    public TrailRenderer[] Trails { get; private set; }
    public ParticleSystem[] Particles { get; private set; }
    public Renderer[] Renderers { get; private set; }
    public Light[] Lights { get; private set; }
    public int RefreshCount { get; private set; }

    private void Awake()
    {
        Refresh();
    }

    public void Refresh()
    {
        Projectile = GetComponent<Projectile>();
        Rigidbodies = GetComponentsInChildren<Rigidbody>(true);
        Colliders = GetComponentsInChildren<Collider>(true);
        Behaviours = GetComponentsInChildren<MonoBehaviour>(true);
        Trails = GetComponentsInChildren<TrailRenderer>(true);
        Particles = GetComponentsInChildren<ParticleSystem>(true);
        Renderers = GetComponentsInChildren<Renderer>(true);
        Lights = GetComponentsInChildren<Light>(true);
        RefreshCount++;
    }

    public static ProjectileVfxComponentCache GetOrCreate(GameObject instance)
    {
        if (instance == null)
        {
            return null;
        }

        if (!instance.TryGetComponent(out ProjectileVfxComponentCache cache))
        {
            cache = instance.AddComponent<ProjectileVfxComponentCache>();
        }

        if (cache.RefreshCount == 0)
        {
            cache.Refresh();
        }

        return cache;
    }
}
