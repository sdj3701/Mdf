// Assets/Scripts/UI/LanguageSelector.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using TMPro;
using Cysharp.Threading.Tasks;

/// <summary>
/// 타이틀 씬에서 언어를 선택할 수 있는 간단한 UI 컨트롤러입니다.
/// 드롭다운 또는 버튼 그룹 방식으로 언어를 선택할 수 있습니다.
/// </summary>
public class LanguageSelector : MonoBehaviour
{
    #region Serialized Fields
    [Header("드롭다운 방식 (선택1)")]
    [SerializeField] private TMP_Dropdown languageDropdown;
    
    [Header("버튼 방식 (선택2)")]
    [SerializeField] private Button koreanButton;
    [SerializeField] private Button englishButton;
    
    [Header("선택된 언어 표시 (선택)")]
    [SerializeField] private TextMeshProUGUI selectedLanguageText;
    
    [Header("설정")]
    [SerializeField] private bool autoSaveToPlayerPrefs = true;
    #endregion

    #region Private Fields
    private const string LANGUAGE_PREF_KEY = "SelectedLanguage";
    private bool _isInitializing = false;
    private List<Locale> _availableLocales;
    #endregion

    #region Unity Lifecycle
    private async void Start()
    {
        await InitializeAsync();
    }
    #endregion

    #region Initialization
    /// <summary>
    /// Localization 시스템 초기화를 기다린 후 UI를 설정합니다.
    /// </summary>
    private async UniTask InitializeAsync()
    {
        _isInitializing = true;
        
        // Localization 시스템이 초기화될 때까지 대기
        await LocalizationSettings.InitializationOperation;
        
        _availableLocales = LocalizationSettings.AvailableLocales.Locales;
        
        // 저장된 언어 설정 불러오기
        LoadSavedLanguage();
        
        // UI 설정
        SetupDropdown();
        SetupButtons();
        UpdateSelectedLanguageText();
        
        _isInitializing = false;
    }
    
    /// <summary>
    /// PlayerPrefs에서 저장된 언어 설정을 불러와 적용합니다.
    /// </summary>
    private void LoadSavedLanguage()
    {
        if (!autoSaveToPlayerPrefs) return;
        
        string savedLocaleCode = PlayerPrefs.GetString(LANGUAGE_PREF_KEY, string.Empty);
        
        if (!string.IsNullOrEmpty(savedLocaleCode))
        {
            Locale savedLocale = LocalizationSettings.AvailableLocales.GetLocale(savedLocaleCode);
            if (savedLocale != null)
            {
                LocalizationSettings.SelectedLocale = savedLocale;
            }
        }
    }
    #endregion

    #region Dropdown Setup
    /// <summary>
    /// 드롭다운 UI 설정
    /// </summary>
    private void SetupDropdown()
    {
        if (languageDropdown == null) return;
        
        languageDropdown.ClearOptions();
        
        List<TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData>();
        int currentIndex = 0;
        
        for (int i = 0; i < _availableLocales.Count; i++)
        {
            Locale locale = _availableLocales[i];
            string displayName = GetLocaleDisplayName(locale);
            options.Add(new TMP_Dropdown.OptionData(displayName));
            
            if (locale == LocalizationSettings.SelectedLocale)
            {
                currentIndex = i;
            }
        }
        
        languageDropdown.AddOptions(options);
        languageDropdown.value = currentIndex;
        languageDropdown.onValueChanged.AddListener(OnDropdownValueChanged);
    }
    
    /// <summary>
    /// 드롭다운 값 변경 이벤트 핸들러
    /// </summary>
    private void OnDropdownValueChanged(int index)
    {
        if (_isInitializing) return;
        if (index < 0 || index >= _availableLocales.Count) return;
        
        SetLocale(_availableLocales[index]);
    }
    #endregion

    #region Button Setup
    /// <summary>
    /// 버튼 UI 설정
    /// </summary>
    private void SetupButtons()
    {
        if (koreanButton != null)
        {
            koreanButton.onClick.AddListener(OnKoreanButtonClicked);
        }
        
        if (englishButton != null)
        {
            englishButton.onClick.AddListener(OnEnglishButtonClicked);
        }
    }
    
    /// <summary>
    /// 한국어 버튼 클릭
    /// </summary>
    public void OnKoreanButtonClicked()
    {
        SetLocaleByCode("ko-KR");
    }
    
    /// <summary>
    /// 영어 버튼 클릭
    /// </summary>
    public void OnEnglishButtonClicked()
    {
        SetLocaleByCode("en-US");
    }
    #endregion

    #region Locale Management
    /// <summary>
    /// 언어 코드로 로케일을 설정합니다.
    /// </summary>
    public void SetLocaleByCode(string localeCode)
    {
        Locale locale = LocalizationSettings.AvailableLocales.GetLocale(localeCode);
        if (locale != null)
        {
            SetLocale(locale);
        }
        else
        {
            Debug.LogWarning($"[LanguageSelector] 로케일을 찾을 수 없습니다: {localeCode}");
        }
    }
    
    /// <summary>
    /// 로케일을 설정하고 저장합니다.
    /// </summary>
    public void SetLocale(Locale locale)
    {
        if (locale == null) return;
        
        LocalizationSettings.SelectedLocale = locale;
        
        if (autoSaveToPlayerPrefs)
        {
            PlayerPrefs.SetString(LANGUAGE_PREF_KEY, locale.Identifier.Code);
            PlayerPrefs.Save();
        }
        
        UpdateSelectedLanguageText();
        UpdateDropdownSelection();
        
        Debug.Log($"[LanguageSelector] 언어가 변경되었습니다: {locale.LocaleName}");
    }
    
    /// <summary>
    /// 현재 선택된 언어를 반환합니다.
    /// </summary>
    public Locale GetCurrentLocale()
    {
        return LocalizationSettings.SelectedLocale;
    }
    
    /// <summary>
    /// 현재 선택된 언어 코드를 반환합니다.
    /// </summary>
    public string GetCurrentLocaleCode()
    {
        return LocalizationSettings.SelectedLocale?.Identifier.Code ?? "en-US";
    }
    #endregion

    #region UI Update
    /// <summary>
    /// 선택된 언어 텍스트 업데이트
    /// </summary>
    private void UpdateSelectedLanguageText()
    {
        if (selectedLanguageText == null) return;
        
        Locale currentLocale = LocalizationSettings.SelectedLocale;
        if (currentLocale != null)
        {
            selectedLanguageText.text = GetLocaleDisplayName(currentLocale);
        }
    }
    
    /// <summary>
    /// 드롭다운 선택 동기화
    /// </summary>
    private void UpdateDropdownSelection()
    {
        if (languageDropdown == null) return;
        
        Locale currentLocale = LocalizationSettings.SelectedLocale;
        int index = _availableLocales.IndexOf(currentLocale);
        
        if (index >= 0 && languageDropdown.value != index)
        {
            languageDropdown.SetValueWithoutNotify(index);
        }
    }
    
    /// <summary>
    /// 로케일의 표시용 이름을 반환합니다.
    /// </summary>
    private string GetLocaleDisplayName(Locale locale)
    {
        if (locale == null) return "Unknown";
        
        // 언어 코드에 따라 사용자 친화적인 이름 반환
        string code = locale.Identifier.Code;
        return code switch
        {
            "ko-KR" => "한국어",
            "en-US" => "English",
            "ja-JP" => "日本語",
            "zh-CN" => "简体中文",
            "zh-TW" => "繁體中文",
            _ => locale.LocaleName
        };
    }
    #endregion

    #region Cleanup
    private void OnDestroy()
    {
        // 이벤트 구독 해제
        if (languageDropdown != null)
        {
            languageDropdown.onValueChanged.RemoveListener(OnDropdownValueChanged);
        }
        
        if (koreanButton != null)
        {
            koreanButton.onClick.RemoveListener(OnKoreanButtonClicked);
        }
        
        if (englishButton != null)
        {
            englishButton.onClick.RemoveListener(OnEnglishButtonClicked);
        }
    }
    #endregion
}
