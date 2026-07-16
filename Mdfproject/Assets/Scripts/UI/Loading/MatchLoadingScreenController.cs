using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public sealed class MatchLoadingScreenController : MonoBehaviour
{
    private const string VisualTreeResourcePath = "UI/MatchLoading/MatchLoadingScreen";
    private const string StyleSheetResourcePath = "UI/MatchLoading/MatchLoadingScreen";
    private const float TipIntervalSeconds = 5f;
    private const float CompletionHoldSeconds = 0.35f;
    private const float FadeDurationSeconds = 0.3f;

    private static readonly string[] Tips =
    {
        "몬스터가 Goal에 도달하면 플레이어의 체력이 감소합니다.",
        "준비 단계에서 유닛 배치와 미로를 미리 완성해 두세요.",
        "같은 성급의 유닛 세 기를 합치면 더 높은 성급으로 성장합니다.",
        "원거리 유닛은 벽 뒤에서 안전하게 공격할 수 있습니다.",
        "마법 스크롤은 상대 전장의 흐름을 뒤집는 강력한 수단입니다.",
        "모든 길이 막히면 몬스터는 벽을 부수며 새로운 경로를 만듭니다."
    };

    public static MatchLoadingScreenController Instance { get; private set; }

    private UIDocument _document;
    private StyleSheet _styleSheet;
    private VisualElement _root;
    private Label _stageLabel;
    private Label _tipLabel;
    private Label _percentLabel;
    private Label _peerLabel;
    private VisualElement _progressFill;
    private VisualElement _runnerMarker;
    private LoadManager _loadManager;

    private float _displayedProgress;
    private float _targetProgress = 0.02f;
    private float _localProgress;
    private float _opacity;
    private float _nextTipAt;
    private float _completeAt = -1f;
    private float _fadeStartedAt = -1f;
    private int _tipIndex;
    private bool _sceneTransitionStarted;
    private bool _gameSceneLoaded;
    private bool _gameManagersReady;
    private bool _cancelRequested;
    private bool _completionRequested;

    public float DisplayedProgress => _displayedProgress;
    public float TargetProgress => _targetProgress;
    public bool IsSceneTransitionStarted => _sceneTransitionStarted;

    public static MatchLoadingScreenController Show(PanelSettings panelSettings)
    {
        if (Instance != null)
        {
            Instance.EnsureVisible();
            return Instance;
        }

        if (panelSettings == null)
        {
            Debug.LogError("[MatchLoadingScreen] PanelSettings is unavailable.");
            return null;
        }

        VisualTreeAsset visualTree = Resources.Load<VisualTreeAsset>(VisualTreeResourcePath);
        StyleSheet styleSheet = Resources.Load<StyleSheet>(StyleSheetResourcePath);
        if (visualTree == null || styleSheet == null)
        {
            Debug.LogError(
                $"[MatchLoadingScreen] Loading UI resources are missing. " +
                $"tree={VisualTreeResourcePath}, style={StyleSheetResourcePath}");
            return null;
        }

        var screenObject = new GameObject(nameof(MatchLoadingScreenController));
        screenObject.SetActive(false);
        DontDestroyOnLoad(screenObject);

        UIDocument document = screenObject.AddComponent<UIDocument>();
        document.panelSettings = panelSettings;
        document.visualTreeAsset = visualTree;
        document.sortingOrder = 10000;

        MatchLoadingScreenController controller =
            screenObject.AddComponent<MatchLoadingScreenController>();
        controller._document = document;
        controller._styleSheet = styleSheet;
        Instance = controller;

        screenObject.SetActive(true);
        controller.BindVisualTree();
        controller.SubscribeToLoadManager();
        controller.EnsureVisible();
        return controller;
    }

    public static void SetPeerReadiness(int readyPeers, int totalPeers)
    {
        Instance?.ApplyPeerReadiness(readyPeers, totalPeers);
    }

    public static void BeginSceneTransition()
    {
        if (Instance == null)
        {
            return;
        }

        Instance._sceneTransitionStarted = true;
        Instance.SetTargetProgress(
            MatchLoadingProgressPolicy.SceneTransitionTarget,
            "전장으로 이동하는 중");
    }

    public static void NotifySceneLoadStart()
    {
        BeginSceneTransition();
    }

    public static void NotifySceneLoadDone()
    {
        if (Instance == null)
        {
            return;
        }

        if (!MatchLoadingProgressPolicy.IsGameScene(SceneManager.GetActiveScene().name))
        {
            return;
        }

        Instance.MarkGameSceneLoaded();
        Instance.SetTargetProgress(
            MatchLoadingProgressPolicy.SceneLoadedTarget,
            "지휘관과 전장을 배치하는 중");
    }

    public static void CancelFromLobby()
    {
        if (Instance == null || Instance._sceneTransitionStarted)
        {
            return;
        }

        Instance._cancelRequested = true;
        Instance._fadeStartedAt = Time.unscaledTime;
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        GameEvents.OnGameManagersReady += HandleGameManagersReady;
        ObserveActiveScene();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        GameEvents.OnGameManagersReady -= HandleGameManagersReady;
    }

    private void OnDestroy()
    {
        UnsubscribeFromLoadManager();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        ObserveActiveScene();

        if (_gameSceneLoaded
            && !_completionRequested
            && (_gameManagersReady || IsGameRuntimeReady()))
        {
            RequestCompletion();
        }

        if (_root == null)
        {
            BindVisualTree();
        }

        float progressSpeed = Mathf.Max(0.16f, Mathf.Abs(_targetProgress - _displayedProgress) * 2.6f);
        _displayedProgress = Mathf.MoveTowards(
            _displayedProgress,
            _targetProgress,
            progressSpeed * Time.unscaledDeltaTime);

        if (!_cancelRequested && _fadeStartedAt < 0f)
        {
            _opacity = Mathf.MoveTowards(_opacity, 1f, Time.unscaledDeltaTime / FadeDurationSeconds);
        }

        if (MatchLoadingProgressPolicy.ShouldBeginCompletionFade(
                _completionRequested,
                _completeAt,
                Time.unscaledTime,
                _displayedProgress,
                _fadeStartedAt))
        {
            _fadeStartedAt = Time.unscaledTime;
        }

        if (_fadeStartedAt >= 0f)
        {
            float fadeProgress = Mathf.Clamp01(
                (Time.unscaledTime - _fadeStartedAt) / FadeDurationSeconds);
            _opacity = 1f - fadeProgress;
            if (fadeProgress >= 1f)
            {
                Destroy(gameObject);
                return;
            }
        }

        if (Time.unscaledTime >= _nextTipAt)
        {
            _tipIndex = (_tipIndex + 1) % Tips.Length;
            ApplyTip();
            _nextTipAt = Time.unscaledTime + TipIntervalSeconds;
        }

        RefreshVisuals();
    }

    private void EnsureVisible()
    {
        _cancelRequested = false;
        _fadeStartedAt = -1f;
        if (_nextTipAt <= 0f)
        {
            _tipIndex = (Environment.TickCount & int.MaxValue) % Tips.Length;
            _nextTipAt = Time.unscaledTime + TipIntervalSeconds;
            ApplyTip();
        }

        if (_loadManager != null)
        {
            ReportLocalProgress(
                _loadManager.MatchContentPrewarmProgress,
                _loadManager.MatchContentPrewarmStage);
        }
    }

    private void BindVisualTree()
    {
        if (_document == null || _document.rootVisualElement == null)
        {
            return;
        }

        _root = _document.rootVisualElement;
        if (_styleSheet != null && !_root.styleSheets.Contains(_styleSheet))
        {
            _root.styleSheets.Add(_styleSheet);
        }

        _stageLabel = _root.Q<Label>("match-loading-stage");
        _tipLabel = _root.Q<Label>("match-loading-tip");
        _percentLabel = _root.Q<Label>("match-loading-percent");
        _peerLabel = _root.Q<Label>("match-loading-peers");
        _progressFill = _root.Q<VisualElement>("match-loading-fill");
        _runnerMarker = _root.Q<VisualElement>("match-loading-runner");
        ApplyTip();
        RefreshVisuals();
    }

    private void SubscribeToLoadManager()
    {
        LoadManager manager = LoadManager.Instance;
        if (_loadManager == manager)
        {
            return;
        }

        UnsubscribeFromLoadManager();
        _loadManager = manager;
        if (_loadManager != null)
        {
            _loadManager.MatchContentPrewarmProgressChanged += ReportLocalProgress;
        }
    }

    private void UnsubscribeFromLoadManager()
    {
        if (_loadManager != null)
        {
            _loadManager.MatchContentPrewarmProgressChanged -= ReportLocalProgress;
            _loadManager = null;
        }
    }

    private void ReportLocalProgress(float progress, string stage)
    {
        _localProgress = MatchLoadingProgressPolicy.ClampLocalProgress(progress);
        SetTargetProgress(_localProgress, stage);
    }

    private void ApplyPeerReadiness(int readyPeers, int totalPeers)
    {
        int safeTotal = Mathf.Max(0, totalPeers);
        int safeReady = Mathf.Clamp(readyPeers, 0, safeTotal);
        if (_peerLabel != null)
        {
            _peerLabel.text = safeTotal > 0
                ? $"동료 준비 {safeReady} / {safeTotal}"
                : "동료 연결 확인 중";
        }

        float peerTarget = MatchLoadingProgressPolicy.ResolvePeerTarget(
            _localProgress,
            safeReady,
            safeTotal);
        if (peerTarget > _targetProgress)
        {
            _targetProgress = peerTarget;
        }

        if (_localProgress >= MatchLoadingProgressPolicy.LocalContentCeiling - 0.0001f
            && safeTotal > 0
            && safeReady < safeTotal)
        {
            SetStage("다른 지휘관을 기다리는 중");
        }
    }

    private void SetTargetProgress(float progress, string stage)
    {
        if (!float.IsNaN(progress) && !float.IsInfinity(progress))
        {
            _targetProgress = Mathf.Max(_targetProgress, Mathf.Clamp01(progress));
        }

        if (!string.IsNullOrWhiteSpace(stage))
        {
            SetStage(stage);
        }
    }

    private void SetStage(string stage)
    {
        if (_stageLabel != null && !string.IsNullOrWhiteSpace(stage))
        {
            _stageLabel.text = stage;
        }
    }

    private void ApplyTip()
    {
        if (_tipLabel != null && Tips.Length > 0)
        {
            _tipLabel.text = Tips[Mathf.Clamp(_tipIndex, 0, Tips.Length - 1)];
        }
    }

    private void RefreshVisuals()
    {
        if (_root != null)
        {
            _root.style.opacity = _opacity;
        }

        if (_progressFill != null)
        {
            _progressFill.style.width = Length.Percent(_displayedProgress * 100f);
        }

        if (_runnerMarker != null)
        {
            _runnerMarker.style.left = Length.Percent(Mathf.Lerp(1f, 91f, _displayedProgress));
            _runnerMarker.style.top = -54f + Mathf.Sin(Time.unscaledTime * 12f) * 4f;
        }

        if (_percentLabel != null)
        {
            _percentLabel.text = $"{Mathf.FloorToInt(_displayedProgress * 100f):00}%";
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (MatchLoadingProgressPolicy.IsGameScene(scene.name))
        {
            MarkGameSceneLoaded();
            SetTargetProgress(
                MatchLoadingProgressPolicy.SceneLoadedTarget,
                "전장 동기화를 마무리하는 중");
            return;
        }

        if (scene.name == SceneDefine.Title || scene.name == SceneDefine.MatchingLobby)
        {
            _cancelRequested = true;
            _fadeStartedAt = Time.unscaledTime;
        }
    }

    private void ObserveActiveScene()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.IsValid() && MatchLoadingProgressPolicy.IsGameScene(activeScene.name))
        {
            MarkGameSceneLoaded();
        }
    }

    private void MarkGameSceneLoaded()
    {
        _gameSceneLoaded = true;
    }

    private void HandleGameManagersReady()
    {
        _gameManagersReady = true;
        ObserveActiveScene();
    }

    private void RequestCompletion()
    {
        _completionRequested = true;
        _targetProgress = 1f;
        _completeAt = Time.unscaledTime + CompletionHoldSeconds;
        SetStage("전장 준비 완료");
    }

    private static bool IsGameRuntimeReady()
    {
        GameManagers managers = GameManagers.Instance;
        return managers != null
               && managers.Object != null
               && managers.Object.IsValid
               && managers.IsReadyForNetworkAccess
               && managers.currentState != GameManagers.GameState.Setup
               && managers.currentState != GameManagers.GameState.DataLoading;
    }
}
