public readonly struct AuthResult
{
    public bool Success { get; }
    public string UserId { get; }
    public string DisplayName { get; }
    public string ErrorCode { get; }
    public string ErrorMessage { get; }

    private AuthResult(bool success, string userId, string displayName, string errorCode, string errorMessage)
    {
        Success = success;
        UserId = userId;
        DisplayName = displayName;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public static AuthResult Succeeded(string userId, string displayName)
    {
        return new AuthResult(true, userId, displayName, string.Empty, string.Empty);
    }

    public static AuthResult Failed(string errorCode, string errorMessage)
    {
        return new AuthResult(false, string.Empty, string.Empty, errorCode, errorMessage);
    }
}
