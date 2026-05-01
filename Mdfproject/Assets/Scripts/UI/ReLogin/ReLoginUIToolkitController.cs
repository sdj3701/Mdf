using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public sealed class ReLoginUIToolkitController : MonoBehaviour
{
    private const float DesignWidth = 1536f;
    private const float DesignHeight = 1024f;
    private const int MinimumPasswordLength = 4;

    private const string RememberLoginKey = "ReLogin.RememberLogin";
    private const string LastAccountInputKey = "ReLogin.LastAccountInput";

    [SerializeField] private UIDocument document;
    [SerializeField] private string nextSceneName = SceneDefine.MatchingLobby;
    [SerializeField] private bool loadSceneOnLoginSuccess = true;
    [SerializeField] private bool requirePasswordForAccountLogin = true;

    private VisualElement root;
    private VisualElement designSpace;
    private VisualElement accountInputArea;
    private VisualElement passwordInputArea;
    private VisualElement accountInputHitbox;
    private VisualElement passwordInputHitbox;
    private VisualElement rememberUncheckedCover;
    private VisualElement focusSink;

    private TextField accountField;
    private TextField passwordField;
    private TextField activeTextField;

    private Label accountPlaceholder;
    private Label passwordPlaceholder;
    private Label statusLabel;

    private VisualElement passwordVisibilityButton;
    private VisualElement rememberButton;
    private VisualElement forgotPasswordButton;
    private VisualElement loginButton;
    private VisualElement googleLoginButton;
    private VisualElement appleLoginButton;
    private VisualElement guestLoginButton;
    private VisualElement settingsButton;
    private VisualElement customerCenterButton;
    private VisualElement languageButton;
    private VisualElement accountFindButton;

    private LoginUseCase loginUseCase;
    private bool rememberLogin = true;
    private bool passwordVisible;
    private bool isSubmitting;

    private enum StatusKind
    {
        Info,
        Success,
        Error
    }

    private void Reset()
    {
        document = GetComponent<UIDocument>();
    }

    private void Awake()
    {
        if (document == null)
        {
            document = GetComponent<UIDocument>();
        }

        loginUseCase = new LoginUseCase(AuthServiceFactory.CreateFromDefine());
    }

    private void OnEnable()
    {
        if (document == null || document.rootVisualElement == null)
        {
            Debug.LogError("[ReLoginUIToolkitController] UIDocument is missing.");
            return;
        }

        root = document.rootVisualElement;
        BindElements();
        ConfigureInitialState();
        RegisterCallbacks();
        UpdateDesignScale();
    }

    private void OnDisable()
    {
        UnregisterCallbacks();
    }

    private void BindElements()
    {
        designSpace = Query<VisualElement>("relogin-design-space");
        accountInputArea = Query<VisualElement>("accountInputArea");
        passwordInputArea = Query<VisualElement>("passwordInputArea");
        accountInputHitbox = Query<VisualElement>("accountInputHitbox");
        passwordInputHitbox = Query<VisualElement>("passwordInputHitbox");
        rememberUncheckedCover = Query<VisualElement>("rememberUncheckedCover");

        accountField = Query<TextField>("accountField");
        passwordField = Query<TextField>("passwordField");

        accountPlaceholder = Query<Label>("accountPlaceholder");
        passwordPlaceholder = Query<Label>("passwordPlaceholder");
        statusLabel = Query<Label>("statusLabel");

        passwordVisibilityButton = Query<VisualElement>("passwordVisibilityButton");
        rememberButton = Query<VisualElement>("rememberButton");
        forgotPasswordButton = Query<VisualElement>("forgotPasswordButton");
        loginButton = Query<VisualElement>("loginButton");
        googleLoginButton = Query<VisualElement>("googleLoginButton");
        appleLoginButton = Query<VisualElement>("appleLoginButton");
        guestLoginButton = Query<VisualElement>("guestLoginButton");
        settingsButton = Query<VisualElement>("settingsButton");
        customerCenterButton = Query<VisualElement>("customerCenterButton");
        languageButton = Query<VisualElement>("languageButton");
        accountFindButton = Query<VisualElement>("accountFindButton");
    }

    private T Query<T>(string elementName) where T : VisualElement
    {
        T element = root.Q<T>(elementName);
        if (element == null)
        {
            Debug.LogError($"[ReLoginUIToolkitController] UXML element not found: {elementName}");
        }

        return element;
    }

    private void ConfigureInitialState()
    {
        rememberLogin = PlayerPrefs.GetInt(RememberLoginKey, 1) == 1;

        if (accountField != null)
        {
            string savedAccount = rememberLogin ? PlayerPrefs.GetString(LastAccountInputKey, string.Empty) : string.Empty;
            accountField.SetValueWithoutNotify(savedAccount);
        }

        if (passwordField != null)
        {
            passwordField.SetValueWithoutNotify(string.Empty);
            passwordField.isPasswordField = true;
        }

        ConfigureTextFieldPicking(accountField, PickingMode.Ignore);
        ConfigureTextFieldPicking(passwordField, PickingMode.Ignore);
        SetPickingMode(accountInputArea, PickingMode.Position);
        SetPickingMode(passwordInputArea, PickingMode.Position);
        SetPickingMode(accountInputHitbox, PickingMode.Position);
        SetPickingMode(passwordInputHitbox, PickingMode.Position);
        SetPickingMode(accountPlaceholder, PickingMode.Ignore);
        SetPickingMode(passwordPlaceholder, PickingMode.Ignore);
        SetPickingMode(rememberUncheckedCover, PickingMode.Ignore);
        ConfigureHitboxPicking();
        EnsureFocusSink();

        passwordVisible = false;
        SetRememberVisualState();
        UpdatePlaceholders();
        HideStatus();
    }

    private void RegisterCallbacks()
    {
        root?.RegisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
        root?.RegisterCallback<PointerDownEvent>(OnRootPointerDown);

        accountInputHitbox?.RegisterCallback<PointerDownEvent>(OnAccountInputHitboxPointerDown);
        passwordInputHitbox?.RegisterCallback<PointerDownEvent>(OnPasswordInputHitboxPointerDown);

        accountField?.RegisterValueChangedCallback(OnAccountChanged);
        passwordField?.RegisterValueChangedCallback(OnPasswordChanged);
        accountField?.RegisterCallback<FocusInEvent>(OnTextFieldFocusIn);
        passwordField?.RegisterCallback<FocusInEvent>(OnTextFieldFocusIn);
        accountField?.RegisterCallback<FocusOutEvent>(OnTextFieldFocusOut);
        passwordField?.RegisterCallback<FocusOutEvent>(OnTextFieldFocusOut);
        accountField?.RegisterCallback<KeyDownEvent>(OnInputKeyDown);
        passwordField?.RegisterCallback<KeyDownEvent>(OnInputKeyDown);

        passwordVisibilityButton?.RegisterCallback<PointerUpEvent>(OnPasswordVisibilityPointerUp);
        rememberButton?.RegisterCallback<PointerUpEvent>(OnRememberPointerUp);
        forgotPasswordButton?.RegisterCallback<PointerUpEvent>(OnForgotPasswordPointerUp);
        loginButton?.RegisterCallback<PointerUpEvent>(OnLoginPointerUp);
        googleLoginButton?.RegisterCallback<PointerUpEvent>(OnGoogleLoginPointerUp);
        appleLoginButton?.RegisterCallback<PointerUpEvent>(OnAppleLoginPointerUp);
        guestLoginButton?.RegisterCallback<PointerUpEvent>(OnGuestLoginPointerUp);
        settingsButton?.RegisterCallback<PointerUpEvent>(OnSettingsPointerUp);
        customerCenterButton?.RegisterCallback<PointerUpEvent>(OnCustomerCenterPointerUp);
        languageButton?.RegisterCallback<PointerUpEvent>(OnLanguagePointerUp);
        accountFindButton?.RegisterCallback<PointerUpEvent>(OnAccountFindPointerUp);
    }

    private void UnregisterCallbacks()
    {
        root?.UnregisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
        root?.UnregisterCallback<PointerDownEvent>(OnRootPointerDown);

        accountInputHitbox?.UnregisterCallback<PointerDownEvent>(OnAccountInputHitboxPointerDown);
        passwordInputHitbox?.UnregisterCallback<PointerDownEvent>(OnPasswordInputHitboxPointerDown);

        accountField?.UnregisterValueChangedCallback(OnAccountChanged);
        passwordField?.UnregisterValueChangedCallback(OnPasswordChanged);
        accountField?.UnregisterCallback<FocusInEvent>(OnTextFieldFocusIn);
        passwordField?.UnregisterCallback<FocusInEvent>(OnTextFieldFocusIn);
        accountField?.UnregisterCallback<FocusOutEvent>(OnTextFieldFocusOut);
        passwordField?.UnregisterCallback<FocusOutEvent>(OnTextFieldFocusOut);
        accountField?.UnregisterCallback<KeyDownEvent>(OnInputKeyDown);
        passwordField?.UnregisterCallback<KeyDownEvent>(OnInputKeyDown);

        passwordVisibilityButton?.UnregisterCallback<PointerUpEvent>(OnPasswordVisibilityPointerUp);
        rememberButton?.UnregisterCallback<PointerUpEvent>(OnRememberPointerUp);
        forgotPasswordButton?.UnregisterCallback<PointerUpEvent>(OnForgotPasswordPointerUp);
        loginButton?.UnregisterCallback<PointerUpEvent>(OnLoginPointerUp);
        googleLoginButton?.UnregisterCallback<PointerUpEvent>(OnGoogleLoginPointerUp);
        appleLoginButton?.UnregisterCallback<PointerUpEvent>(OnAppleLoginPointerUp);
        guestLoginButton?.UnregisterCallback<PointerUpEvent>(OnGuestLoginPointerUp);
        settingsButton?.UnregisterCallback<PointerUpEvent>(OnSettingsPointerUp);
        customerCenterButton?.UnregisterCallback<PointerUpEvent>(OnCustomerCenterPointerUp);
        languageButton?.UnregisterCallback<PointerUpEvent>(OnLanguagePointerUp);
        accountFindButton?.UnregisterCallback<PointerUpEvent>(OnAccountFindPointerUp);
    }

    private void OnRootGeometryChanged(GeometryChangedEvent evt)
    {
        UpdateDesignScale();
        ConfigureAllTextFieldPicking();
    }

    private void OnRootPointerDown(PointerDownEvent evt)
    {
        if (IsPointerInside(accountInputHitbox, evt.position) || IsPointerInside(passwordInputHitbox, evt.position))
        {
            return;
        }

        BlurAllTextFields();
    }

    private void OnAccountInputHitboxPointerDown(PointerDownEvent evt)
    {
        FocusTextFieldFromHitbox(accountField);
        evt.StopPropagation();
    }

    private void OnPasswordInputHitboxPointerDown(PointerDownEvent evt)
    {
        FocusTextFieldFromHitbox(passwordField);
        evt.StopPropagation();
    }

    private void ConfigureHitboxPicking()
    {
        SetPickingMode(passwordVisibilityButton, PickingMode.Position);
        SetPickingMode(rememberButton, PickingMode.Position);
        SetPickingMode(forgotPasswordButton, PickingMode.Position);
        SetPickingMode(loginButton, PickingMode.Position);
        SetPickingMode(googleLoginButton, PickingMode.Position);
        SetPickingMode(appleLoginButton, PickingMode.Position);
        SetPickingMode(guestLoginButton, PickingMode.Position);
        SetPickingMode(settingsButton, PickingMode.Position);
        SetPickingMode(customerCenterButton, PickingMode.Position);
        SetPickingMode(languageButton, PickingMode.Position);
        SetPickingMode(accountFindButton, PickingMode.Position);
    }

    private void ConfigureAllTextFieldPicking()
    {
        ConfigureTextFieldPicking(accountField, PickingMode.Ignore);
        ConfigureTextFieldPicking(passwordField, PickingMode.Ignore);
    }

    private static void ConfigureTextFieldPicking(TextField field, PickingMode pickingMode)
    {
        if (field == null)
        {
            return;
        }

        SetPickingModeRecursive(field, pickingMode);
        field.schedule.Execute(() => SetPickingModeRecursive(field, pickingMode)).ExecuteLater(0);
        field.schedule.Execute(() => SetPickingModeRecursive(field, pickingMode)).ExecuteLater(50);
    }

    private static void SetPickingModeRecursive(VisualElement element, PickingMode pickingMode)
    {
        if (element == null)
        {
            return;
        }

        element.pickingMode = pickingMode;

        foreach (VisualElement child in element.Children())
        {
            SetPickingModeRecursive(child, pickingMode);
        }
    }

    private void FocusTextFieldFromHitbox(TextField field)
    {
        if (field == null)
        {
            return;
        }

        activeTextField = field;

        if (field == accountField)
        {
            BlurTextField(passwordField);
        }
        else if (field == passwordField)
        {
            BlurTextField(accountField);
        }

        ConfigureAllTextFieldPicking();
        field.Focus();
        FocusTextFieldInput(field);

        field.schedule.Execute(() =>
        {
            activeTextField = field;
            ConfigureTextFieldPicking(field, PickingMode.Ignore);
            field.Focus();
            FocusTextFieldInput(field);
        }).ExecuteLater(0);
    }

    private static void FocusTextFieldInput(TextField field)
    {
        VisualElement textInput = FindTextFieldInput(field);
        textInput?.Focus();
    }

    private static void BlurTextField(TextField field)
    {
        if (field == null)
        {
            return;
        }

        field.Blur();
        FindTextFieldInput(field)?.Blur();
    }

    private void BlurAllTextFields()
    {
        activeTextField = null;
        BlurTextField(accountField);
        BlurTextField(passwordField);
        FocusSink();
    }

    private void EnsureFocusSink()
    {
        if (root == null)
        {
            return;
        }

        if (focusSink == null)
        {
            focusSink = new VisualElement
            {
                name = "reloginFocusSink",
                focusable = true,
                pickingMode = PickingMode.Ignore
            };

            focusSink.style.position = Position.Absolute;
            focusSink.style.left = -10000f;
            focusSink.style.top = -10000f;
            focusSink.style.width = 1f;
            focusSink.style.height = 1f;
            focusSink.style.opacity = 0f;
        }

        if (focusSink.parent != root)
        {
            focusSink.RemoveFromHierarchy();
            root.Add(focusSink);
        }
    }

    private void FocusSink()
    {
        EnsureFocusSink();

        if (focusSink == null)
        {
            return;
        }

        focusSink.Focus();
        focusSink.schedule.Execute(() => focusSink.Focus()).ExecuteLater(0);
    }

    private void OnTextFieldFocusIn(FocusInEvent evt)
    {
        TextField focusedField = evt.currentTarget as TextField;
        if (focusedField == null || focusedField == activeTextField)
        {
            return;
        }

        focusedField.schedule.Execute(() =>
        {
            if (focusedField != activeTextField)
            {
                BlurTextField(focusedField);
                FocusSink();
            }
        }).ExecuteLater(0);
    }

    private void OnTextFieldFocusOut(FocusOutEvent evt)
    {
        if (evt.currentTarget == activeTextField)
        {
            activeTextField = null;
        }
    }

    private static bool IsPointerInside(VisualElement element, Vector3 pointerPosition)
    {
        if (element == null)
        {
            return false;
        }

        return element.worldBound.Contains(new Vector2(pointerPosition.x, pointerPosition.y));
    }

    private static VisualElement FindTextFieldInput(TextField field)
    {
        VisualElement textInput = field.Q(className: "unity-text-field__input");
        if (textInput == null)
        {
            textInput = field.Q(className: "unity-base-text-field__input");
        }

        return textInput;
    }

    private void UpdateDesignScale()
    {
        if (root == null || designSpace == null)
        {
            return;
        }

        float rootWidth = root.resolvedStyle.width;
        float rootHeight = root.resolvedStyle.height;

        if (rootWidth <= 0f)
        {
            rootWidth = Screen.width;
        }

        if (rootHeight <= 0f)
        {
            rootHeight = Screen.height;
        }

        float scale = Mathf.Min(rootWidth / DesignWidth, rootHeight / DesignHeight);
        float left = (rootWidth - DesignWidth * scale) * 0.5f;
        float top = (rootHeight - DesignHeight * scale) * 0.5f;

        designSpace.style.left = left;
        designSpace.style.top = top;
        designSpace.transform.scale = new Vector3(scale, scale, 1f);
    }

    private void OnAccountChanged(ChangeEvent<string> evt)
    {
        UpdatePlaceholders();
        HideStatus();
    }

    private void OnPasswordChanged(ChangeEvent<string> evt)
    {
        UpdatePlaceholders();
        HideStatus();
    }

    private void OnInputKeyDown(KeyDownEvent evt)
    {
        if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter)
        {
            return;
        }

        SubmitLogin(accountField?.value, passwordField?.value, false);
        evt.StopPropagation();
    }

    private void UpdatePlaceholders()
    {
        SetDisplay(accountPlaceholder, string.IsNullOrEmpty(accountField?.value));
        SetDisplay(passwordPlaceholder, string.IsNullOrEmpty(passwordField?.value));
    }

    private void OnPasswordVisibilityPointerUp(PointerUpEvent evt)
    {
        passwordVisible = !passwordVisible;

        if (passwordField != null)
        {
            passwordField.isPasswordField = !passwordVisible;
            ConfigureTextFieldPicking(passwordField, PickingMode.Ignore);
        }
    }

    private void OnRememberPointerUp(PointerUpEvent evt)
    {
        rememberLogin = !rememberLogin;
        SetRememberVisualState();
        PlayerPrefs.SetInt(RememberLoginKey, rememberLogin ? 1 : 0);
        PlayerPrefs.Save();
    }

    private void SetRememberVisualState()
    {
        SetDisplay(rememberUncheckedCover, !rememberLogin);
    }

    private void OnLoginPointerUp(PointerUpEvent evt)
    {
        SubmitLogin(accountField?.value, passwordField?.value, false);
    }

    private void OnGuestLoginPointerUp(PointerUpEvent evt)
    {
        SubmitLogin(string.Empty, string.Empty, true);
    }

    private void SubmitLogin(string accountInput, string passwordInput, bool guestLogin)
    {
        if (isSubmitting)
        {
            return;
        }

        if (!guestLogin && !ValidateAccountLoginInput(accountInput, passwordInput))
        {
            return;
        }

        isSubmitting = true;
        loginButton?.SetEnabled(false);
        guestLoginButton?.SetEnabled(false);
        ShowStatus("로그인 중입니다.", StatusKind.Info);

        // 현재 Auth 구조는 이메일/비밀번호가 아니라 displayName 기반 로컬 로그인이다.
        // 이메일/비밀번호 실인증은 IAuthService 확장 후 여기에서 교체한다.
        AuthResult result = loginUseCase.Execute(guestLogin ? string.Empty : accountInput);

        if (result.Success)
        {
            SaveLoginPrefs(accountInput);
            CompleteLogin(result);
        }
        else
        {
            ShowStatus(AuthErrorMapper.ToUserMessage(result), StatusKind.Error);
            loginButton?.SetEnabled(true);
            guestLoginButton?.SetEnabled(true);
            isSubmitting = false;
        }
    }

    private bool ValidateAccountLoginInput(string accountInput, string passwordInput)
    {
        if (string.IsNullOrWhiteSpace(accountInput))
        {
            ShowStatus("이메일 또는 계정을 입력해 주세요.", StatusKind.Error);
            return false;
        }

        if (!requirePasswordForAccountLogin)
        {
            return true;
        }

        if (string.IsNullOrEmpty(passwordInput))
        {
            ShowStatus("비밀번호를 입력해 주세요.", StatusKind.Error);
            return false;
        }

        if (passwordInput.Length < MinimumPasswordLength)
        {
            ShowStatus($"비밀번호는 {MinimumPasswordLength}자 이상 입력해 주세요.", StatusKind.Error);
            return false;
        }

        return true;
    }

    private void SaveLoginPrefs(string accountInput)
    {
        PlayerPrefs.SetInt(RememberLoginKey, rememberLogin ? 1 : 0);

        if (rememberLogin && !string.IsNullOrWhiteSpace(accountInput))
        {
            PlayerPrefs.SetString(LastAccountInputKey, accountInput);
        }
        else
        {
            PlayerPrefs.DeleteKey(LastAccountInputKey);
        }

        PlayerPrefs.Save();
    }

    private void CompleteLogin(AuthResult result)
    {
        ShowStatus($"로그인 성공: {result.DisplayName}", StatusKind.Success);

        if (!loadSceneOnLoginSuccess || string.IsNullOrEmpty(nextSceneName))
        {
            loginButton?.SetEnabled(true);
            guestLoginButton?.SetEnabled(true);
            isSubmitting = false;
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(nextSceneName))
        {
            Debug.LogWarning($"[ReLoginUIToolkitController] Scene is not in Build Settings: {nextSceneName}");
            ShowStatus($"로그인 성공. 다음 씬({nextSceneName})은 Build Settings에 없습니다.", StatusKind.Info);
            loginButton?.SetEnabled(true);
            guestLoginButton?.SetEnabled(true);
            isSubmitting = false;
            return;
        }

        SceneManager.LoadScene(nextSceneName);
    }

    private void OnForgotPasswordPointerUp(PointerUpEvent evt)
    {
        ShowDeferredMessage("비밀번호 찾기");
    }

    private void OnGoogleLoginPointerUp(PointerUpEvent evt)
    {
        ShowDeferredMessage("Google 로그인");
    }

    private void OnAppleLoginPointerUp(PointerUpEvent evt)
    {
        ShowDeferredMessage("Apple 로그인");
    }

    private void OnSettingsPointerUp(PointerUpEvent evt)
    {
        ShowDeferredMessage("설정");
    }

    private void OnCustomerCenterPointerUp(PointerUpEvent evt)
    {
        ShowDeferredMessage("고객센터");
    }

    private void OnLanguagePointerUp(PointerUpEvent evt)
    {
        ShowDeferredMessage("언어 선택");
    }

    private void OnAccountFindPointerUp(PointerUpEvent evt)
    {
        ShowDeferredMessage("계정 찾기");
    }

    private void ShowDeferredMessage(string featureName)
    {
        ShowStatus($"{featureName} 기능은 추후 구현 예정입니다.", StatusKind.Info);
    }

    private void HideStatus()
    {
        if (statusLabel == null)
        {
            return;
        }

        statusLabel.text = string.Empty;
        statusLabel.style.display = DisplayStyle.None;
        statusLabel.RemoveFromClassList("status-label--error");
        statusLabel.RemoveFromClassList("status-label--success");
    }

    private void ShowStatus(string message, StatusKind kind)
    {
        if (statusLabel == null)
        {
            return;
        }

        statusLabel.text = message;
        statusLabel.style.display = DisplayStyle.Flex;
        statusLabel.RemoveFromClassList("status-label--error");
        statusLabel.RemoveFromClassList("status-label--success");

        if (kind == StatusKind.Error)
        {
            statusLabel.AddToClassList("status-label--error");
        }
        else if (kind == StatusKind.Success)
        {
            statusLabel.AddToClassList("status-label--success");
        }
    }

    private static void SetDisplay(VisualElement element, bool visible)
    {
        if (element != null)
        {
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    private static void SetPickingMode(VisualElement element, PickingMode pickingMode)
    {
        if (element != null)
        {
            element.pickingMode = pickingMode;
        }
    }
}
