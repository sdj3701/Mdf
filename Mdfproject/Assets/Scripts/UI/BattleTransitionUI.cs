// Assets/Scripts/UI/BattleTransitionUI.cs
using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;

/// <summary>
/// 전투 페이즈 전환 시 표시되는 애니메이션 UI.
/// 공격(칼)/수비(방패) 애니메이션을 표시합니다.
/// UIManagers를 통해 프리팹으로 로드됩니다.
/// </summary>
public class BattleTransitionUI : MonoBehaviour
{
    #region 정적 헬퍼 메서드
    private const string UI_NAME = "UI_Pnl_BattleTransition";

    /// <summary>
    /// 공격 전환 애니메이션을 재생합니다. (정적 호출)
    /// </summary>
    public static async UniTask PlayAttackTransitionAsync()
    {
        var ui = await GetOrCreateUI();
        if (ui != null)
        {
            await ui.PlayAttackTransition();
            ReturnUI();
        }
    }

    /// <summary>
    /// 수비 전환 애니메이션을 재생합니다. (정적 호출)
    /// </summary>
    public static async UniTask PlayDefenseTransitionAsync()
    {
        var ui = await GetOrCreateUI();
        if (ui != null)
        {
            await ui.PlayDefenseTransition();
            ReturnUI();
        }
    }

    public static async UniTask PlaySequenceTransitionAsync(
        GameManagers.GameState fromState,
        GameManagers.GameState toState,
        float durationSeconds)
    {
        var ui = await GetOrCreateUI();
        if (ui != null)
        {
            await ui.PlaySequenceTransition(fromState, toState, durationSeconds);
            ReturnUI();
        }
    }

    private static async UniTask<BattleTransitionUI> GetOrCreateUI()
    {
        if (UIManagers.Instance == null)
        {
            Debug.LogWarning("[BattleTransitionUI] UIManagers.Instance가 없습니다");
            return null;
        }

        var uiObject = await UIManagers.Instance.GetUIElement(UI_NAME);
        if (uiObject == null)
        {
            Debug.LogWarning($"[BattleTransitionUI] '{UI_NAME}' UI를 로드할 수 없습니다");
            return null;
        }

        return uiObject.GetComponent<BattleTransitionUI>();
    }

    private static void ReturnUI()
    {
        UIManagers.Instance?.ReturnUIElement(UI_NAME);
    }
    #endregion

    #region UI 요소
    [Header("전환 UI 요소")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private RectTransform panelRoot;
    
    [Header("공격 전환 (칼)")]
    [SerializeField] private RectTransform leftSword;
    [SerializeField] private RectTransform rightSword;
    [SerializeField] private Image attackFlashImage;
    
    [Header("수비 전환 (방패)")]
    [SerializeField] private RectTransform shieldImage;
    [SerializeField] private Image defenseFlashImage;
    
    [Header("텍스트")]
    [SerializeField] private TMPro.TextMeshProUGUI transitionText;
    
    [Header("애니메이션 설정")]
    [SerializeField] private float transitionDuration = 1.0f;
    [SerializeField] private float flashDuration = 0.1f;
    [SerializeField] private float textDisplayDuration = 0.5f;
    
    [Header("사운드 (옵션)")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip swordClashSound;
    [SerializeField] private AudioClip shieldBangSound;
    #endregion

    #region 필드
    private bool _isPlaying;
    #endregion

    #region 초기화
    private void Start()
    {
        // 초기 상태: 숨김
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
        }
        
        HideAllElements();
    }

    private void HideAllElements()
    {
        if (leftSword != null) leftSword.gameObject.SetActive(false);
        if (rightSword != null) rightSword.gameObject.SetActive(false);
        if (attackFlashImage != null) attackFlashImage.gameObject.SetActive(false);
        if (shieldImage != null) shieldImage.gameObject.SetActive(false);
        if (defenseFlashImage != null) defenseFlashImage.gameObject.SetActive(false);
        if (transitionText != null) transitionText.gameObject.SetActive(false);
    }
    #endregion

    #region 공격 전환 애니메이션
    /// <summary>
    /// 공격 전환 애니메이션을 재생합니다. (칼 부딪힘)
    /// </summary>
    public async UniTask PlayAttackTransition()
    {
        if (_isPlaying) return;
        _isPlaying = true;

        // UI 표시
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.blocksRaycasts = true;
        }

        // 텍스트 표시
        if (transitionText != null)
        {
            transitionText.text = "공격!";
            transitionText.gameObject.SetActive(true);
        }

        // 칼 애니메이션
        if (leftSword != null && rightSword != null)
        {
            leftSword.gameObject.SetActive(true);
            rightSword.gameObject.SetActive(true);

            // 시작 위치 (화면 양쪽 끝)
            Vector2 leftStart = new Vector2(-500f, 0f);
            Vector2 rightStart = new Vector2(500f, 0f);
            Vector2 center = Vector2.zero;

            leftSword.anchoredPosition = leftStart;
            rightSword.anchoredPosition = rightStart;

            // 중앙으로 이동 (충돌)
            float elapsed = 0f;
            float moveDuration = transitionDuration * 0.5f;
            
            while (elapsed < moveDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / moveDuration);
                leftSword.anchoredPosition = Vector2.Lerp(leftStart, center + new Vector2(-50f, 0f), t);
                rightSword.anchoredPosition = Vector2.Lerp(rightStart, center + new Vector2(50f, 0f), t);
                await UniTask.Yield();
            }

            // 플래시 효과
            await PlayFlash(attackFlashImage);

            // 사운드
            PlaySound(swordClashSound);

            // 칼이 물러나면서 화면 분할 효과
            elapsed = 0f;
            while (elapsed < moveDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / moveDuration);
                leftSword.anchoredPosition = Vector2.Lerp(center + new Vector2(-50f, 0f), leftStart, t);
                rightSword.anchoredPosition = Vector2.Lerp(center + new Vector2(50f, 0f), rightStart, t);
                await UniTask.Yield();
            }
        }
        else
        {
            // 칼 이미지가 없으면 간단한 플래시만
            await PlayFlash(attackFlashImage);
            await UniTask.Delay((int)(transitionDuration * 1000));
        }

        // 텍스트 표시 유지
        await UniTask.Delay((int)(textDisplayDuration * 1000));

        // 페이드 아웃
        await FadeOut();

        HideAllElements();
        _isPlaying = false;
    }
    #endregion

    #region 수비 전환 애니메이션
    /// <summary>
    /// 수비 전환 애니메이션을 재생합니다. (방패)
    /// </summary>
    public async UniTask PlayDefenseTransition()
    {
        if (_isPlaying) return;
        _isPlaying = true;

        // UI 표시
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.blocksRaycasts = true;
        }

        // 텍스트 표시
        if (transitionText != null)
        {
            transitionText.text = "수비!";
            transitionText.gameObject.SetActive(true);
        }

        // 방패 애니메이션
        if (shieldImage != null)
        {
            shieldImage.gameObject.SetActive(true);

            // 시작: 작은 크기
            Vector3 startScale = Vector3.one * 0.1f;
            Vector3 endScale = Vector3.one * 1.2f;
            Vector3 finalScale = Vector3.one;

            shieldImage.localScale = startScale;

            // 확대
            float elapsed = 0f;
            float growDuration = transitionDuration * 0.4f;
            
            while (elapsed < growDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / growDuration);
                shieldImage.localScale = Vector3.Lerp(startScale, endScale, t);
                await UniTask.Yield();
            }

            // 플래시
            await PlayFlash(defenseFlashImage);

            // 사운드
            PlaySound(shieldBangSound);

            // 바운스 백
            elapsed = 0f;
            float bounceDuration = transitionDuration * 0.2f;
            
            while (elapsed < bounceDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / bounceDuration);
                shieldImage.localScale = Vector3.Lerp(endScale, finalScale, t);
                await UniTask.Yield();
            }
        }
        else
        {
            // 방패 이미지가 없으면 간단한 플래시만
            await PlayFlash(defenseFlashImage);
            await UniTask.Delay((int)(transitionDuration * 1000));
        }

        // 텍스트 표시 유지
        await UniTask.Delay((int)(textDisplayDuration * 1000));

        // 페이드 아웃
        await FadeOut();

        HideAllElements();
        _isPlaying = false;
    }
    #endregion

    #region 유틸리티
    public async UniTask PlaySequenceTransition(
        GameManagers.GameState fromState,
        GameManagers.GameState toState,
        float durationSeconds)
    {
        if (_isPlaying) return;
        _isPlaying = true;

        HideAllElements();

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = true;
        }

        if (transitionText != null)
        {
            transitionText.text = FormatSequenceTransitionText(fromState, toState);
            transitionText.gameObject.SetActive(true);
        }

        float totalDuration = Mathf.Max(0.1f, durationSeconds);
        float fadeInDuration = Mathf.Min(0.2f, totalDuration * 0.25f);
        float fadeOutDuration = Mathf.Min(0.25f, totalDuration * 0.3f);
        float holdDuration = Mathf.Max(0f, totalDuration - fadeInDuration - fadeOutDuration - flashDuration);

        await FadeCanvas(0f, 1f, fadeInDuration);

        if (attackFlashImage != null)
        {
            await PlayFlash(attackFlashImage);
        }
        else
        {
            await UniTask.Delay((int)(flashDuration * 1000f));
        }

        if (holdDuration > 0f)
        {
            await UniTask.Delay((int)(holdDuration * 1000f));
        }

        await FadeCanvas(1f, 0f, fadeOutDuration);

        HideAllElements();
        if (canvasGroup != null)
        {
            canvasGroup.blocksRaycasts = false;
        }

        _isPlaying = false;
    }

    private static string FormatSequenceTransitionText(GameManagers.GameState fromState, GameManagers.GameState toState)
    {
        switch (toState)
        {
            case GameManagers.GameState.Battle1:
                return "Battle 1";
            case GameManagers.GameState.Battle2:
                return "Battle 2";
            case GameManagers.GameState.Prepare:
                return "Prepare";
            default:
                return $"{fromState} -> {toState}";
        }
    }

    private async UniTask PlayFlash(Image flashImage)
    {
        if (flashImage == null) return;

        flashImage.gameObject.SetActive(true);
        Color startColor = new Color(1f, 1f, 1f, 1f);
        Color endColor = new Color(1f, 1f, 1f, 0f);

        float elapsed = 0f;
        while (elapsed < flashDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / flashDuration;
            flashImage.color = Color.Lerp(startColor, endColor, t);
            await UniTask.Yield();
        }

        flashImage.gameObject.SetActive(false);
    }

    private async UniTask FadeOut()
    {
        if (canvasGroup == null) return;

        float elapsed = 0f;
        float fadeDuration = 0.3f;
        
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / fadeDuration;
            canvasGroup.alpha = Mathf.Lerp(1f, 0f, t);
            await UniTask.Yield();
        }

        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
    }

    private async UniTask FadeCanvas(float fromAlpha, float toAlpha, float durationSeconds)
    {
        if (canvasGroup == null) return;

        if (durationSeconds <= 0f)
        {
            canvasGroup.alpha = toAlpha;
            return;
        }

        float elapsed = 0f;
        while (elapsed < durationSeconds)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / durationSeconds);
            canvasGroup.alpha = Mathf.Lerp(fromAlpha, toAlpha, t);
            await UniTask.Yield();
        }

        canvasGroup.alpha = toAlpha;
    }

    private void PlaySound(AudioClip clip)
    {
        if (audioSource != null && clip != null)
        {
            audioSource.PlayOneShot(clip);
        }
    }
    #endregion

    #region 공개 메서드
    public bool IsPlaying => _isPlaying;
    #endregion
}
