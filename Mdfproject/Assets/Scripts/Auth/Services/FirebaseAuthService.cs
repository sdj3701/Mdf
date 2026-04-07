public sealed class FirebaseAuthService : IAuthService
{
    public AuthResult SignInWithDisplayName(string displayNameInput)
    {
        if (!AuthDefine.EnableFirebaseAuth)
        {
            return AuthResult.Failed(
                AuthDefine.ErrorFirebaseAuthDisabled,
                "Firebase auth is currently disabled by configuration.");
        }

        // Step 3: keep this path disabled until real Firebase runtime flow is wired.
        return AuthResult.Failed(
            AuthDefine.ErrorFirebaseAuthNotImplemented,
            "Firebase auth mode is enabled, but runtime sign-in flow is not wired yet.");
    }
}
