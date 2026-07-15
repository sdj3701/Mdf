using UnityEngine;

public static class MatchLoadingProgressPolicy
{
    public const float LocalContentCeiling = 0.92f;
    public const float PeerReadyCeiling = 0.97f;
    public const float SceneTransitionTarget = 0.985f;
    public const float SceneLoadedTarget = 0.995f;

    public static float ClampLocalProgress(float progress)
    {
        if (float.IsNaN(progress) || float.IsInfinity(progress))
        {
            return 0f;
        }

        return Mathf.Clamp(progress, 0f, LocalContentCeiling);
    }

    public static float ResolvePeerTarget(float localProgress, int readyPeers, int totalPeers)
    {
        float safeLocalProgress = ClampLocalProgress(localProgress);
        if (safeLocalProgress < LocalContentCeiling - 0.0001f || totalPeers <= 0)
        {
            return safeLocalProgress;
        }

        float readyRatio = Mathf.Clamp01((float)Mathf.Clamp(readyPeers, 0, totalPeers) / totalPeers);
        return Mathf.Lerp(LocalContentCeiling, PeerReadyCeiling, readyRatio);
    }
}
