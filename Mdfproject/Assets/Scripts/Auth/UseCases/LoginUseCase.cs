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
            return AuthResult.Failed("AUTH_SERVICE_NULL", "Auth service is not configured.");
        }

        return _authService.SignInWithDisplayName(displayNameInput);
    }
}
