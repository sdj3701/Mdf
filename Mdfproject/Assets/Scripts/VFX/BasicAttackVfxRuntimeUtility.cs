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

    public static void RestartParticles(GameObject instance, Vector3 primaryRendererFlip)
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
        for (int i = 0; i < particles.Length; i++)
        {
            PreparePrimarySlashParticle(particles[i], primaryRendererFlip);

            var main = particles[i].main;
            main.loop = false;
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
