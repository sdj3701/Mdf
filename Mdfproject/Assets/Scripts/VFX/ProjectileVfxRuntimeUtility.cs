using UnityEngine;

public static class ProjectileVfxRuntimeUtility
{
    private const int BasicAttackVfxSortingOrder = 50;

    public static Quaternion ResolveVfxRotation(Vector3 direction, bool useDirection, Vector3 offsetEuler)
    {
        Quaternion baseRotation = useDirection ? DirectionRotation(direction) : Quaternion.identity;
        return baseRotation * Quaternion.Euler(offsetEuler);
    }

    public static Quaternion DirectionRotation(Vector3 direction)
    {
        if (direction.sqrMagnitude <= 1e-6f)
        {
            return Quaternion.identity;
        }

        return Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    public static Vector3 ApplyLocalOffset(Vector3 position, Quaternion rotation, Vector3 localOffset)
    {
        return position + rotation * localOffset;
    }

    public static Vector3 MultiplyScale(Vector3 baseScale, float multiplier)
    {
        float resolvedMultiplier = multiplier > 0f ? multiplier : 1f;
        return new Vector3(baseScale.x * resolvedMultiplier, baseScale.y * resolvedMultiplier, baseScale.z * resolvedMultiplier);
    }

    public static Vector3 MultiplyScale(Vector3 baseScale, Vector3 multiplier)
    {
        return new Vector3(
            baseScale.x * Mathf.Max(0.01f, multiplier.x),
            baseScale.y * Mathf.Max(0.01f, multiplier.y),
            baseScale.z * Mathf.Max(0.01f, multiplier.z));
    }

    public static void RestartParticles(GameObject instance, float playbackSpeed, float dynamicLightIntensity = 0f, float dynamicLightRange = 0f)
    {
        if (instance == null)
        {
            return;
        }

        PrepareRenderersForVfxVisibility(instance);
        ApplyDynamicLighting(instance, dynamicLightIntensity, dynamicLightRange);

        float resolvedPlaybackSpeed = Mathf.Max(0.01f, playbackSpeed);
        var trails = instance.GetComponentsInChildren<TrailRenderer>(true);
        for (int i = 0; i < trails.Length; i++)
        {
            trails[i].Clear();
        }

        var particles = instance.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particles.Length; i++)
        {
            var main = particles[i].main;
            main.simulationSpeed = resolvedPlaybackSpeed;
            particles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particles[i].Play(true);
        }
    }

    public static void PrepareVisualProjectile(GameObject instance, float dynamicLightIntensity = 0f, float dynamicLightRange = 0f)
    {
        if (instance == null)
        {
            return;
        }

        PrepareRenderersForVfxVisibility(instance);

        var projectile = instance.GetComponent<Projectile>();
        if (projectile != null)
        {
            projectile.SetVisualOnly(true);
        }

        var rigidbodies = instance.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < rigidbodies.Length; i++)
        {
            rigidbodies[i].velocity = Vector3.zero;
            rigidbodies[i].angularVelocity = Vector3.zero;
            rigidbodies[i].isKinematic = true;
        }

        var colliders = instance.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = false;
        }

        ApplyDynamicLighting(instance, dynamicLightIntensity, dynamicLightRange);

        var behaviours = instance.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null)
            {
                continue;
            }

            string typeName = behaviour.GetType().Name;
            if (typeName.IndexOf("ProjectileMover", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                behaviour.enabled = false;
            }
        }
    }

    private static void PrepareRenderersForVfxVisibility(GameObject instance)
    {
        var renderers = instance.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            renderer.allowOcclusionWhenDynamic = false;
            renderer.sortingOrder = Mathf.Max(renderer.sortingOrder, BasicAttackVfxSortingOrder);
        }
    }

    private static void ApplyDynamicLighting(GameObject instance, float dynamicLightIntensity, float dynamicLightRange)
    {
        float resolvedIntensity = Mathf.Max(0f, dynamicLightIntensity);
        float resolvedRange = Mathf.Max(0f, dynamicLightRange);
        bool enableDynamicLighting = resolvedIntensity > 0f && resolvedRange > 0f;

        var lights = instance.GetComponentsInChildren<Light>(true);
        for (int i = 0; i < lights.Length; i++)
        {
            lights[i].enabled = enableDynamicLighting;
            if (enableDynamicLighting)
            {
                lights[i].intensity = resolvedIntensity;
                lights[i].range = resolvedRange;
            }
        }

        var particles = instance.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particles.Length; i++)
        {
            var lightsModule = particles[i].lights;
            lightsModule.enabled = enableDynamicLighting;
        }
    }
}
