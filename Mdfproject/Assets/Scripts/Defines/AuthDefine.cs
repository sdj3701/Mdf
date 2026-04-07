public static class AuthDefine
{
    public const string ProviderLocalString = "local_string";
    public const string ProviderFirebase = "firebase";

    // Step 3 (Firebase): keep disabled for now. Turn on when runtime wiring is ready.
    public const bool EnableFirebaseAuth = false;
    public const AuthProviderMode DefaultProviderMode = AuthProviderMode.LocalString;
    public const bool FallbackToLocalOnFirebaseUnavailable = true;

    public const string ErrorFirebaseAuthDisabled = "FIREBASE_AUTH_DISABLED";
    public const string ErrorFirebaseAuthNotImplemented = "FIREBASE_AUTH_NOT_IMPLEMENTED";
    public const string ErrorAuthServiceNull = "AUTH_SERVICE_NULL";
    public const string ErrorLocalAuthException = "LOCAL_AUTH_EXCEPTION";

    public const string DefaultNicknamePrefix = "Player";
    public const int RandomSuffixMin = 1000;
    public const int RandomSuffixMaxExclusive = 9999;
}
