using System;
using UnityEngine;

public sealed class LocalStringAuthService : IAuthService
{
    public AuthResult SignInWithDisplayName(string displayNameInput)
    {
        try
        {
            string displayName = displayNameInput;
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = $"{AuthDefine.DefaultNicknamePrefix}{UnityEngine.Random.Range(AuthDefine.RandomSuffixMin, AuthDefine.RandomSuffixMaxExclusive)}";
            }

            string userId = PlayerPrefs.GetString(PlayerPrefsDefine.PlayerUuidKey, string.Empty);
            if (string.IsNullOrEmpty(userId))
            {
                userId = Guid.NewGuid().ToString();
                PlayerPrefs.SetString(PlayerPrefsDefine.PlayerUuidKey, userId);
            }

            PlayerPrefs.SetString(PlayerPrefsDefine.NicknameKey, displayName);
            PlayerPrefs.SetString(PlayerPrefsDefine.LastAuthProviderKey, AuthDefine.ProviderLocalString);
            PlayerPrefs.SetString(PlayerPrefsDefine.LastLoginUserIdKey, userId);
            PlayerPrefs.Save();

            return AuthResult.Succeeded(userId, displayName);
        }
        catch (Exception e)
        {
            return AuthResult.Failed(AuthDefine.ErrorLocalAuthException, e.Message);
        }
    }
}
