using UnityEngine;

public static class AuthServiceFactory
{
    public static IAuthService CreateFromDefine()
    {
        return Create(AuthDefine.DefaultProviderMode);
    }

    public static IAuthService Create(AuthProviderMode mode)
    {
        switch (mode)
        {
            case AuthProviderMode.Firebase:
                if (AuthDefine.EnableFirebaseAuth)
                {
                    return new FirebaseAuthService();
                }

                if (AuthDefine.FallbackToLocalOnFirebaseUnavailable)
                {
                    Debug.LogWarning("[AuthServiceFactory] Firebase auth is disabled. Fallback to local string auth.");
                    return new LocalStringAuthService();
                }

                return new FirebaseAuthService();

            case AuthProviderMode.LocalString:
            default:
                return new LocalStringAuthService();
        }
    }
}
