public sealed class LoginUseCase
{
    private readonly IAuthService _authService;

    public LoginUseCase(IAuthService authService)
    {
        _authService = authService;
    }

    public AuthResult Execute(string displayNameInput)
    {
        if (_authService == null)
        {
            return AuthResult.Failed(AuthDefine.ErrorAuthServiceNull, "Auth service is not configured.");
        }

        return _authService.SignInWithDisplayName(displayNameInput);
    }
}

public static class AuthErrorMapper
{
    public static string ToUserMessage(AuthResult result)
    {
        switch (result.ErrorCode)
        {
            case AuthDefine.ErrorFirebaseAuthDisabled:
                return "Firebase login is disabled. Local login fallback is active.";
            case AuthDefine.ErrorFirebaseAuthNotImplemented:
                return "Firebase login connection is prepared, but runtime sign-in is not enabled yet.";
            case AuthDefine.ErrorAuthServiceNull:
                return "Login service is not initialized.";
            case AuthDefine.ErrorLocalAuthException:
                return "Local login failed due to an unexpected error.";
            default:
                return string.IsNullOrEmpty(result.ErrorMessage)
                    ? "Login failed."
                    : result.ErrorMessage;
        }
    }
}
