using UnityEngine;

public static class BasicAttackVfxRuntimeUtility
{
    private const uint PrimarySlashRandomSeed = 1u;

    public static Quaternion ResolveAttackRotation(Transform basis, Vector3 direction, BasicAttackVfxRotationMode rotationMode)
    {
        if (rotationMode == BasicAttackVfxRotationMode.UnitForward && basis != null)
        {
            Vector3 unitForward = basis.forward;
            unitForward.y = 0f;
            if (unitForward.sqrMagnitude > 1e-6f)
            {
                return Quaternion.LookRotation(unitForward.normalized, Vector3.up);
            }
        }

        direction.y = 0f;
        if (direction.sqrMagnitude <= 1e-6f && basis != null)
        {
            direction = basis.forward;
            direction.y = 0f;
        }

        return Quaternion.LookRotation(direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward, Vector3.up);
    }

    public static float ResolvePlaybackSpeed(float configuredPlaybackSpeed, float animationPlaybackSpeed, float playbackSpeedCap)
    {
        float configured = Mathf.Max(0.01f, configuredPlaybackSpeed);
        float animation = Mathf.Max(0.01f, animationPlaybackSpeed);
        float uncapped = configured * animation;
        float cap = playbackSpeedCap > 0f ? playbackSpeedCap : uncapped;
        return Mathf.Max(0.01f, Mathf.Min(uncapped, cap));
    }

    public static float ResolveLifetimeSeconds(float baseLifetimeSeconds, float playbackSpeed, float minimumVisibleSeconds)
    {
        float resolvedBaseLifetime = Mathf.Max(0.05f, baseLifetimeSeconds);
        float resolvedPlaybackSpeed = Mathf.Max(0.01f, playbackSpeed);
        float resolvedMinimum = Mathf.Max(0f, minimumVisibleSeconds);
        return Mathf.Max(resolvedMinimum, resolvedBaseLifetime / resolvedPlaybackSpeed);
    }

    public static void RestartParticles(GameObject instance, Vector3 primaryRendererFlip, float playbackSpeed = 1f)
    {
        if (instance == null)
        {
            return;
        }

        var trails = instance.GetComponentsInChildren<TrailRenderer>(true);
        for (int i = 0; i < trails.Length; i++)
        {
            trails[i].Clear();
        }

        StopParticles(instance);

        var particles = instance.GetComponentsInChildren<ParticleSystem>(true);
        float resolvedPlaybackSpeed = Mathf.Max(0.01f, playbackSpeed);
        for (int i = 0; i < particles.Length; i++)
        {
            PreparePrimarySlashParticle(particles[i], primaryRendererFlip);

            var main = particles[i].main;
            main.loop = false;
            main.simulationSpeed = resolvedPlaybackSpeed;
            particles[i].Play(true);
        }
    }

    public static void StopParticles(GameObject instance)
    {
        if (instance == null)
        {
            return;
        }

        var particles = instance.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particles.Length; i++)
        {
            var main = particles[i].main;
            main.loop = false;
            particles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    private static void PreparePrimarySlashParticle(ParticleSystem system, Vector3 primaryRendererFlip)
    {
        if (system == null)
        {
            return;
        }

        var renderer = system.GetComponent<ParticleSystemRenderer>();
        if (renderer == null ||
            renderer.renderMode != ParticleSystemRenderMode.Mesh ||
            renderer.mesh == null ||
            !NameContains(renderer.mesh.name, "Slash"))
        {
            return;
        }

        Material material = renderer.sharedMaterial;
        if (material != null && !NameContains(material.name, "SwordSlash"))
        {
            return;
        }

        system.useAutoRandomSeed = false;
        system.randomSeed = PrimarySlashRandomSeed;
        renderer.flip = primaryRendererFlip;
    }

    private static bool NameContains(string value, string pattern)
    {
        return !string.IsNullOrEmpty(value) &&
            value.IndexOf(pattern, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
