using Fusion.Photon.Realtime;
using UnityEngine;

/// <summary>
/// Matchmaking protocol boundary for MDF. Change this value whenever a network command or
/// persistent snapshot wire contract becomes incompatible with an already released build.
/// Photon isolates different AppVersion values into separate virtual applications.
/// </summary>
public static class MdfNetworkProtocol
{
    public const string AppVersion = "mdf-p2-content-id";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplyBeforeSceneLoad()
    {
        if (!ApplyToGlobalSettings(out string reason))
        {
            Debug.LogError($"[MdfNetworkProtocol] Failed to apply matchmaking protocol version. reason={reason}");
        }
    }

    public static bool ApplyToGlobalSettings(out string reason)
    {
        PhotonAppSettings settings = PhotonAppSettings.Global;
        if (settings == null)
        {
            reason = "photon_app_settings_missing";
            return false;
        }

        FusionAppSettings appSettings = settings.AppSettings;
        if (appSettings == null)
        {
            reason = "fusion_app_settings_missing";
            return false;
        }

        appSettings.AppVersion = AppVersion;
        settings.AppSettings = appSettings;
        reason = string.Empty;
        return string.Equals(settings.AppSettings.AppVersion, AppVersion, System.StringComparison.Ordinal);
    }
}
