using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UIElements;

/// <summary>
/// UI Toolkit bridge for the 03_Game prepare phase shop and augment choices.
/// Legacy UGUI panels are kept in the scene/pool, but this controller takes over
/// the visible panel and command dispatch path when it is active.
/// </summary>
public sealed class GamePrepareUIToolkitController : MonoBehaviour
{
    public const int ShopCardCount = 5;
    public const int AugmentCardCount = 3;

    private const string LayoutResourcePath = "UI/GamePrepare/GamePreparePanels";
    private const string StyleResourcePath = "UI/GamePrepare/GamePreparePanelsStyles";
    private const string ThemeResourcePath = "UI/GamePrepare/GamePrepareRuntimeTheme";
    private const int PanelSortingOrder = 280;
    private const float ReferenceWidth = 1600f;
    private const float ReferenceHeight = 900f;
    private const float MinResponsiveScale = 0.72f;
    private const float MaxResponsiveScale = 1.18f;
    private const float MinTouchSize = 64f;
    private const float ShopRowTopRatio = 0.16f;
    private const float AugmentRowTopRatio = 0.24f;

    private static GamePrepareUIToolkitController instance;

    [SerializeField] private UIDocument document;
    [SerializeField] private VisualTreeAsset layoutAsset;
    [SerializeField] private StyleSheet styleSheet;

    private readonly ShopCardView[] shopCards = new ShopCardView[ShopCardCount];
    private readonly AugmentCardView[] augmentCards = new AugmentCardView[AugmentCardCount];
    private readonly List<AugmentData> currentAugments = new List<AugmentData>(AugmentCardCount);

    private VisualElement root;
    private VisualElement safeRoot;
    private VisualElement shopPanel;
    private VisualElement shopCardRow;
    private VisualElement augmentPanel;
    private VisualElement augmentCardRow;
    private VisualElement rerollButton;
    private Label rerollLabel;
    private Label shopStatusLabel;
    private VisualElement hudRoot;
    private VisualElement hudShopButton;
    private Label hudShopLabel;
    private VisualElement hudWallButton;
    private Label hudWallLabel;
    private VisualElement hudOptionButton;

    private GameManagers gameManagers;
    private PlayerManager localPlayer;
    private ShopManager localShopManager;
    private bool callbacksBound;
    private bool eventsSubscribed;
    private bool shopVisible;
    private bool augmentVisible;
    private int lastWallCount = int.MinValue;
    private bool lastPrepareButtonsVisible;
    private bool legacyHudHidden;

    public static GamePrepareUIToolkitController Instance => instance;
    public static bool IsToolkitActive => instance != null && instance.isActiveAndEnabled;

    public static bool IsPointerOverBlockingElement(Vector2 screenPosition)
    {
        return IsToolkitActive && instance.IsBlockingElementAt(screenPosition);
    }

    public static bool IsToolkitRaycastObject(GameObject target)
    {
        if (target == null || instance == null)
        {
            return false;
        }

        return target == instance.gameObject
               || (instance.document != null && target == instance.document.gameObject)
               || target.GetComponentInParent<GamePrepareUIToolkitController>() != null;
    }

    public static GamePrepareUIToolkitController EnsureExists()
    {
        if (instance != null)
        {
            return instance;
        }

        var existing = FindObjectOfType<GamePrepareUIToolkitController>(true);
        if (existing != null)
        {
            instance = existing;
            return existing;
        }

        var go = new GameObject("Game Prepare UI Toolkit");
        var uidocument = go.AddComponent<UIDocument>();
        uidocument.visualTreeAsset = Resources.Load<VisualTreeAsset>(LayoutResourcePath);
        uidocument.panelSettings = CreateRuntimePanelSettings();

        var controller = go.AddComponent<GamePrepareUIToolkitController>();
        controller.document = uidocument;
        controller.layoutAsset = uidocument.visualTreeAsset;
        return controller;
    }

    public static bool TryGetShopVisible(out bool visible)
    {
        visible = IsToolkitActive && instance.shopVisible;
        return IsToolkitActive;
    }

    public static bool TryToggleShopFromLegacy(out bool visible)
    {
        visible = false;
        if (!IsToolkitActive)
        {
            return false;
        }

        visible = instance.ToggleShopPanelFromLegacy();
        return true;
    }

    public static bool TrySetShopVisibilityFromLegacy(bool visible, out bool resolvedVisible)
    {
        resolvedVisible = false;
        if (!IsToolkitActive)
        {
            return false;
        }

        if (visible)
        {
            instance.ShowShopPanelAsync().Forget();
            resolvedVisible = true;
        }
        else
        {
            instance.SetShopVisible(false);
            resolvedVisible = false;
        }

        return true;
    }

    public static bool TryShowShopFromLegacy(List<ShopItem> items, out bool visible)
    {
        visible = false;
        if (!IsToolkitActive)
        {
            return false;
        }

        instance.RefreshRuntimeReferences();
        instance.BindShopCards(items);
        instance.SetShopVisible(true);
        visible = true;
        return true;
    }

    public static bool TryShowAugmentsFromLegacy(PlayerManager player, List<AugmentData> choices)
    {
        if (!IsToolkitActive)
        {
            return false;
        }

        return instance.ShowAugments(player, choices);
    }

    public static string FormatCostText(int cost)
    {
        return cost <= 0 ? "\uBB34\uB8CC" : $"{cost} \uACE8\uB4DC";
    }

    public static string FormatStarText(int star)
    {
        return star <= 0 ? "-" : $"{star}\uC131";
    }
    public static string FormatAugmentTierText(AugmentTier tier)
    {
        return tier switch
        {
            AugmentTier.Silver => "\uC2E4\uBC84 \uC99D\uAC15",
            AugmentTier.Gold => "\uACE8\uB4DC \uC99D\uAC15",
            AugmentTier.Prismatic => "\uD504\uB9AC\uC998 \uC99D\uAC15",
            _ => "\uC99D\uAC15"
        };
    }

    public static Vector2 CalculateCardSize(bool isShop, Vector2 screenSize)
    {
        var scale = CalculateResponsiveScale(screenSize);
        return isShop
            ? new Vector2(246f * scale, 336f * scale)
            : new Vector2(332f * scale, 480f * scale);
    }

    public static float CalculateResponsiveScale(Vector2 screenSize)
    {
        if (screenSize.x <= 0f || screenSize.y <= 0f)
        {
            return 1f;
        }

        var scaleByWidth = screenSize.x / ReferenceWidth;
        var scaleByHeight = screenSize.y / ReferenceHeight;
        return Mathf.Clamp(Mathf.Min(scaleByWidth, scaleByHeight), MinResponsiveScale, MaxResponsiveScale);
    }

    private bool IsBlockingElementAt(Vector2 screenPosition)
    {
        if (root == null || root.panel == null)
        {
            return false;
        }

        Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(root.panel, screenPosition);

        if (ContainsPoint(hudShopButton, panelPosition) ||
            ContainsPoint(hudWallButton, panelPosition) ||
            ContainsPoint(hudOptionButton, panelPosition))
        {
            return true;
        }

        if (shopVisible)
        {
            if (ContainsPoint(rerollButton, panelPosition))
            {
                return true;
            }

            for (int i = 0; i < shopCards.Length; i++)
            {
                if (ContainsPoint(shopCards[i].Root, panelPosition))
                {
                    return true;
                }
            }
        }

        if (augmentVisible)
        {
            for (int i = 0; i < augmentCards.Length; i++)
            {
                if (ContainsPoint(augmentCards[i].Root, panelPosition))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ContainsPoint(VisualElement element, Vector2 panelPosition)
    {
        if (element == null ||
            element.pickingMode == PickingMode.Ignore ||
            element.resolvedStyle.display == DisplayStyle.None ||
            element.resolvedStyle.visibility == Visibility.Hidden)
        {
            return false;
        }

        return element.worldBound.Contains(panelPosition);
    }

    private static PanelSettings CreateRuntimePanelSettings()
    {
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.name = "GamePrepareRuntimePanelSettings";
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        settings.sortingOrder = PanelSortingOrder;
        settings.referenceResolution = new Vector2Int((int)ReferenceWidth, (int)ReferenceHeight);
        var theme = Resources.Load<ThemeStyleSheet>(ThemeResourcePath);
        if (theme != null)
        {
            settings.themeStyleSheet = theme;
        }

        return settings;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        document ??= GetComponent<UIDocument>();
        if (document == null)
        {
            document = gameObject.AddComponent<UIDocument>();
        }

        layoutAsset ??= Resources.Load<VisualTreeAsset>(LayoutResourcePath);
        styleSheet ??= Resources.Load<StyleSheet>(StyleResourcePath);

        if (document.visualTreeAsset == null)
        {
            document.visualTreeAsset = layoutAsset;
        }

        if (document.panelSettings == null)
        {
            document.panelSettings = CreateRuntimePanelSettings();
        }
    }

    private void OnEnable()
    {
        EnsureVisualTree();
        BindElements();
        ConfigurePickingModes();
        ApplySafeArea();
        ApplyResponsiveSize();
        RegisterCallbacks();
        SubscribeEvents();
        RefreshRuntimeReferences();
        HideLegacyContent();
        SetShopVisible(false);
        SetAugmentVisible(false);
        UpdateHudState(true);
    }

    private void Update()
    {
        RefreshRuntimeReferences();
        UpdateHudState(false);
    }

    private void OnDisable()
    {
        UnsubscribeEvents();
        ReleaseIconHandles();
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private void EnsureVisualTree()
    {
        root = document.rootVisualElement;
        if (root == null)
        {
            return;
        }

        if (root.Q<VisualElement>("game-prepare-root") == null && layoutAsset != null)
        {
            root.Clear();
            layoutAsset.CloneTree(root);
        }

        if (styleSheet != null)
        {
            root.styleSheets.Add(styleSheet);
        }
    }

    private void BindElements()
    {
        root = document.rootVisualElement;
        safeRoot = root?.Q<VisualElement>("game-prepare-safe-root");
        shopPanel = root?.Q<VisualElement>("shop-panel");
        shopCardRow = root?.Q<VisualElement>("shop-card-row");
        augmentPanel = root?.Q<VisualElement>("augment-panel");
        augmentCardRow = root?.Q<VisualElement>("augment-card-row");
        rerollButton = root?.Q<VisualElement>("shop-reroll-button");
        rerollLabel = root?.Q<Label>("shop-reroll-label");
        shopStatusLabel = root?.Q<Label>("shop-status-label");
        hudRoot = root?.Q<VisualElement>("game-hud-root");
        hudShopButton = root?.Q<VisualElement>("game-shop-toggle-button");
        hudShopLabel = root?.Q<Label>("game-shop-toggle-label");
        hudWallButton = root?.Q<VisualElement>("game-wall-button");
        hudWallLabel = root?.Q<Label>("game-wall-label");
        hudOptionButton = root?.Q<VisualElement>("game-option-button");

        for (var i = 0; i < ShopCardCount; i++)
        {
            shopCards[i] = new ShopCardView(
                i,
                root?.Q<VisualElement>($"shop-card-{i}"),
                root?.Q<Image>($"shop-icon-{i}"),
                root?.Q<Label>($"shop-star-{i}"),
                root?.Q<Label>($"shop-name-{i}"),
                root?.Q<Label>($"shop-cost-{i}"),
                root?.Q<Label>($"shop-sold-{i}"));
        }

        for (var i = 0; i < AugmentCardCount; i++)
        {
            augmentCards[i] = new AugmentCardView(
                i,
                root?.Q<VisualElement>($"augment-card-{i}"),
                root?.Q<Image>($"augment-icon-{i}"),
                root?.Q<Label>($"augment-tier-{i}"),
                root?.Q<Label>($"augment-name-{i}"),
                root?.Q<Label>($"augment-description-{i}"));
        }
    }

    private void ConfigurePickingModes()
    {
        SetPickingMode(root, PickingMode.Ignore);
        SetPickingMode(safeRoot, PickingMode.Ignore);
        SetPickingMode(hudRoot, PickingMode.Ignore);
        SetPickingMode(hudShopButton, PickingMode.Position);
        SetPickingMode(hudWallButton, PickingMode.Position);
        SetPickingMode(hudOptionButton, PickingMode.Position);
        SetPickingMode(rerollButton, PickingMode.Position);

        for (var i = 0; i < shopCards.Length; i++)
        {
            SetPickingMode(shopCards[i].Root, PickingMode.Position);
        }

        for (var i = 0; i < augmentCards.Length; i++)
        {
            SetPickingMode(augmentCards[i].Root, PickingMode.Position);
        }
    }

    private void RegisterCallbacks()
    {
        if (callbacksBound)
        {
            return;
        }

        safeRoot?.RegisterCallback<GeometryChangedEvent>(_ =>
        {
            ApplySafeArea();
            ApplyResponsiveSize();
        });

        for (var i = 0; i < shopCards.Length; i++)
        {
            var slotIndex = i;
            shopCards[i].Root?.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    HandleShopCardClicked(slotIndex);
                    evt.StopPropagation();
                }
            });
        }

        for (var i = 0; i < augmentCards.Length; i++)
        {
            var augmentIndex = i;
            augmentCards[i].Root?.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    HandleAugmentCardClicked(augmentIndex);
                    evt.StopPropagation();
                }
            });
        }

        rerollButton?.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (evt.button == 0)
            {
                HandleRerollClicked();
                evt.StopPropagation();
            }
        });

        hudShopButton?.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (evt.button == 0)
            {
                HandleHudShopClicked();
                evt.StopPropagation();
            }
        });

        hudWallButton?.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (evt.button == 0)
            {
                HandleHudWallClicked();
                evt.StopPropagation();
            }
        });

        hudOptionButton?.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (evt.button == 0)
            {
                OpenOptionCanvasAsync().Forget();
                evt.StopPropagation();
            }
        });

        callbacksBound = true;
    }

    private void SubscribeEvents()
    {
        if (eventsSubscribed)
        {
            return;
        }

        GameEvents.OnGameManagersReady += HandleGameManagersReady;
        GameEvents.OnGameStateChanged += HandleGameStateChanged;
        GameEvents.OnGameStateRestored += HandleGameStateRestored;
        GameEvents.OnHostMigrationCompleted += HandleHostMigrationCompleted;
        GameEvents.OnShopRefreshed += HandleShopRefreshed;
        GameEvents.OnUnitPurchaseSucceeded += HandleUnitPurchaseSucceeded;
        GameEvents.OnAugmentPhaseStart += HandleAugmentPhaseStart;
        GameEvents.OnAugmentApplied += HandleAugmentApplied;
        eventsSubscribed = true;
    }

    private void UnsubscribeEvents()
    {
        if (!eventsSubscribed)
        {
            return;
        }

        GameEvents.OnGameManagersReady -= HandleGameManagersReady;
        GameEvents.OnGameStateChanged -= HandleGameStateChanged;
        GameEvents.OnGameStateRestored -= HandleGameStateRestored;
        GameEvents.OnHostMigrationCompleted -= HandleHostMigrationCompleted;
        GameEvents.OnShopRefreshed -= HandleShopRefreshed;
        GameEvents.OnUnitPurchaseSucceeded -= HandleUnitPurchaseSucceeded;
        GameEvents.OnAugmentPhaseStart -= HandleAugmentPhaseStart;
        GameEvents.OnAugmentApplied -= HandleAugmentApplied;
        eventsSubscribed = false;
    }

    private void HandleGameManagersReady()
    {
        RefreshRuntimeReferences();
        HideLegacyContent();
        RefreshShopCards();
        UpdateHudState(true);
    }

    private void HandleGameStateChanged(GameManagers.GameState newState)
    {
        RefreshRuntimeReferences();
        if (newState != GameManagers.GameState.Prepare)
        {
            SetShopVisible(false);
            SetAugmentVisible(false);
        }

        UpdateHudState(true);
    }

    private void HandleGameStateRestored(GameManagers.GameState restoredState)
    {
        HandleGameStateChanged(restoredState);
    }

    private void HandleHostMigrationCompleted(bool isNewHost)
    {
        RefreshRuntimeReferences();
        RefreshShopCards();
    }

    private void HandleShopRefreshed(PlayerManager player)
    {
        RefreshRuntimeReferences();
        if (player != null && localPlayer != null && player != localPlayer)
        {
            return;
        }

        BindShopCards(localShopManager?.GetCurrentShopItems());
        UpdateRerollLabel();
    }

    private void HandleUnitPurchaseSucceeded(int playerID, ShopItem item, int slotIndex)
    {
        RefreshRuntimeReferences();
        if (localPlayer != null)
        {
            try
            {
                if (localPlayer.playerId != playerID)
                {
                    return;
                }
            }
            catch (InvalidOperationException)
            {
                return;
            }
        }

        RefreshShopCards();
    }

    private void HandleAugmentPhaseStart(PlayerManager player, List<AugmentData> choices)
    {
        ShowAugments(player, choices);
    }

    private void HandleAugmentApplied(PlayerManager player, AugmentData augment)
    {
        RefreshRuntimeReferences();
        if (player != null && localPlayer != null && player != localPlayer)
        {
            return;
        }

        SetAugmentVisible(false);
    }

    private bool ToggleShopPanelFromLegacy()
    {
        if (shopVisible)
        {
            SetShopVisible(false);
            return false;
        }

        SetShopVisible(true);
        ShowShopPanelAsync().Forget();
        return true;
    }

    private async UniTask ShowShopPanelAsync()
    {
        RefreshRuntimeReferences();
        SetShopVisible(true);
        BindShopCards(localShopManager?.GetCurrentShopItems());
        UpdateRerollLabel();

        if (localShopManager == null)
        {
            SetStatusText("\uC0C1\uC810 \uC900\uBE44 \uC911");
            return;
        }

        try
        {
            await localShopManager.EnsureShopRerolledAsync();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[GamePrepareUIToolkitController] Failed to prepare shop items: {ex.Message}");
        }

        RefreshShopCards();
    }

    private bool ShowAugments(PlayerManager player, List<AugmentData> choices)
    {
        RefreshRuntimeReferences();
        if (player != null && localPlayer != null && player != localPlayer)
        {
            return false;
        }

        currentAugments.Clear();
        if (choices != null)
        {
            currentAugments.AddRange(choices);
        }

        BindAugmentCards(currentAugments);
        SetShopVisible(false);
        SetAugmentVisible(currentAugments.Count > 0);
        return true;
    }

    private void RefreshRuntimeReferences()
    {
        gameManagers = GameManagers.Instance;
        localPlayer = gameManagers != null ? gameManagers.localPlayer : null;
        localShopManager = localPlayer != null ? localPlayer.shopManager : null;
    }

    private void RefreshShopCards()
    {
        RefreshRuntimeReferences();
        BindShopCards(localShopManager?.GetCurrentShopItems());
        UpdateRerollLabel();
    }

    private void BindShopCards(List<ShopItem> items)
    {
        for (var i = 0; i < shopCards.Length; i++)
        {
            var hasItem = items != null && i < items.Count && items[i].UnitData != null;
            var sold = localShopManager != null && localShopManager.IsSlotSold(i);
            shopCards[i].Bind(hasItem ? items[i] : default, hasItem, sold);
        }

        SetStatusText(items == null || items.Count == 0 ? "\uC0C1\uC810\uC774 \uBE44\uC5B4 \uC788\uC2B5\uB2C8\uB2E4." : string.Empty);
    }

    private void BindAugmentCards(List<AugmentData> choices)
    {
        for (var i = 0; i < augmentCards.Length; i++)
        {
            var augment = choices != null && i < choices.Count ? choices[i] : null;
            augmentCards[i].Bind(augment);
        }
    }

    private void HandleShopCardClicked(int slotIndex)
    {
        RefreshRuntimeReferences();
        if (localShopManager == null || !TryGetLocalPlayerId(out var playerId))
        {
            return;
        }

        var items = localShopManager.GetCurrentShopItems();
        if (slotIndex < 0 || slotIndex >= items.Count || localShopManager.IsSlotSold(slotIndex))
        {
            return;
        }

        var command = new BuyUnitCommand(playerId, slotIndex);
        GameManagers.Instance.CommandProcessor.RequestCommandExecution(command);
    }

    private void HandleRerollClicked()
    {
        RefreshRuntimeReferences();
        if (localShopManager == null || !TryGetLocalPlayerId(out var playerId))
        {
            return;
        }

        var command = new RerollShopCommand(playerId);
        GameManagers.Instance.CommandProcessor.RequestCommandExecution(command);
    }

    private void HandleHudShopClicked()
    {
        ToggleShopPanelFromLegacy();
        UpdateHudState(true);
    }

    private void HandleHudWallClicked()
    {
        RefreshRuntimeReferences();
        if (!CanUsePrepareHudActions() || localPlayer == null || localPlayer.fieldManager == null)
        {
            return;
        }

        localPlayer.fieldManager.TogglePlacementMode(PlacementMode.Wall);
        UpdateHudState(true);
    }

    private async UniTask OpenOptionCanvasAsync()
    {
        if (UIManagers.Instance != null)
        {
            await UIManagers.Instance.GetUIElement("OptionCanvas");
        }
    }

    private void HandleAugmentCardClicked(int index)
    {
        RefreshRuntimeReferences();
        if (index < 0 || index >= currentAugments.Count || !TryGetLocalPlayerId(out var playerId))
        {
            return;
        }

        var command = new SelectAugmentCommand(playerId, index);
        GameManagers.Instance.CommandProcessor.RequestCommandExecution(command);
        SetAugmentVisible(false);
    }

    private bool TryGetLocalPlayerId(out int playerId)
    {
        playerId = -1;
        RefreshRuntimeReferences();
        if (localPlayer == null || GameManagers.Instance == null || GameManagers.Instance.CommandProcessor == null)
        {
            return false;
        }

        playerId = localPlayer.playerId;
        return playerId >= 0;
    }

    private void SetShopVisible(bool visible)
    {
        shopVisible = visible;
        SetVisible(shopPanel, visible);
        UpdateHudState(true);
        if (visible)
        {
            HideLegacyShopContent();
        }
    }

    private void SetAugmentVisible(bool visible)
    {
        augmentVisible = visible;
        SetVisible(augmentPanel, visible);
        UpdateHudState(true);
        if (visible)
        {
            HideLegacyAugmentContent();
        }
    }

    private static void SetVisible(VisualElement element, bool visible)
    {
        if (element == null)
        {
            return;
        }

        element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        element.EnableInClassList("hidden", !visible);
        element.pickingMode = visible ? PickingMode.Position : PickingMode.Ignore;
    }

    private static void SetPickingMode(VisualElement element, PickingMode mode)
    {
        if (element != null)
        {
            element.pickingMode = mode;
        }
    }

    private void UpdateRerollLabel()
    {
        if (rerollLabel == null)
        {
            return;
        }

        var cost = localShopManager != null ? localShopManager.GetRerollCost() : 0;
        rerollLabel.text = cost > 0 ? $"\uC0C8\uB85C\uACE0\uCE68 {cost}G" : "\uC0C8\uB85C\uACE0\uCE68";
    }

    private void SetStatusText(string text)
    {
        if (shopStatusLabel != null)
        {
            shopStatusLabel.text = text ?? string.Empty;
        }
    }

    private void UpdateHudState(bool force)
    {
        var prepareButtonsVisible = CanShowPrepareHudActions() && !augmentVisible;
        SetVisible(hudShopButton, prepareButtonsVisible);
        SetVisible(hudWallButton, prepareButtonsVisible);
        lastPrepareButtonsVisible = prepareButtonsVisible;

        SetVisible(hudOptionButton, UIManagers.Instance != null);

        if (hudShopLabel != null)
        {
            hudShopLabel.text = shopVisible ? "\uC0C1\uC810 \uB2EB\uAE30" : "\uC0C1\uC810";
        }

        var wallCount = localPlayer != null ? localPlayer.GetWallCount() : 0;
        var wallModeActive = IsWallPlacementActive();
        var wasWallModeActive = hudWallButton != null && hudWallButton.ClassListContains("hud-button-active");
        if (force || wallCount != lastWallCount || wallModeActive != wasWallModeActive)
        {
            if (hudWallLabel != null)
            {
                hudWallLabel.text = wallModeActive ? "\uBCBD \uD574\uC81C" : $"\uBCBD {wallCount}";
            }

            hudWallButton?.EnableInClassList("hud-button-active", wallModeActive);
            lastWallCount = wallCount;
        }

        hudShopButton?.EnableInClassList("hud-button-active", shopVisible);
        if (force || !legacyHudHidden)
        {
            HideLegacyHudContent();
        }
    }

    private bool CanUsePrepareHudActions()
    {
        if (!CanShowPrepareHudActions())
        {
            return false;
        }

        try
        {
            return !gameManagers.IsSequenceTransitioning;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private bool CanShowPrepareHudActions()
    {
        if (gameManagers == null || localPlayer == null || !localPlayer.IsReadyForPlayerActions)
        {
            return false;
        }

        try
        {
            return gameManagers.GetGameState() == GameManagers.GameState.Prepare;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private bool IsWallPlacementActive()
    {
        return localPlayer != null &&
               localPlayer.fieldManager != null &&
               localPlayer.fieldManager.GetPlacementMode() == PlacementMode.Wall;
    }

    private void ApplySafeArea()
    {
        var screenWidth = Mathf.Max(1f, Screen.width);
        var screenHeight = Mathf.Max(1f, Screen.height);
        if (root != null)
        {
            root.style.position = Position.Absolute;
            root.style.left = 0f;
            root.style.right = 0f;
            root.style.top = 0f;
            root.style.bottom = 0f;
            root.style.width = screenWidth;
            root.style.height = screenHeight;
        }

        if (safeRoot == null)
        {
            return;
        }

        var area = Screen.safeArea;
        safeRoot.style.left = area.xMin;
        safeRoot.style.right = screenWidth - area.xMax;
        safeRoot.style.top = screenHeight - area.yMax;
        safeRoot.style.bottom = area.yMin;
    }

    private void ApplyResponsiveSize()
    {
        var uiWidth = root != null && root.resolvedStyle.width > 1f ? root.resolvedStyle.width : Screen.width;
        var uiHeight = root != null && root.resolvedStyle.height > 1f ? root.resolvedStyle.height : Screen.height;
        var uiSize = new Vector2(uiWidth, uiHeight);
        var scale = CalculateResponsiveScale(uiSize);
        var shopSize = CalculateCardSize(true, uiSize);
        var augmentSize = CalculateCardSize(false, uiSize);

        if (shopCardRow != null)
        {
            shopCardRow.style.marginLeft = 0f;
        }

        if (augmentCardRow != null)
        {
            augmentCardRow.style.marginLeft = 0f;
        }

        if (shopPanel != null)
        {
            shopPanel.style.paddingTop = Mathf.Clamp(uiHeight * ShopRowTopRatio, 118f, 230f);
            shopPanel.style.paddingBottom = Mathf.Max(36f, 42f * scale);
        }

        if (augmentPanel != null)
        {
            augmentPanel.style.paddingTop = Mathf.Clamp(uiHeight * AugmentRowTopRatio, 170f, 300f);
            augmentPanel.style.paddingBottom = Mathf.Max(48f, 56f * scale);
        }

        foreach (var card in shopCards)
        {
            card.ApplySize(Mathf.Max(shopSize.x, MinTouchSize * 2.8f), Mathf.Max(shopSize.y, MinTouchSize * 4.6f), scale);
        }

        foreach (var card in augmentCards)
        {
            card.ApplySize(Mathf.Max(augmentSize.x, MinTouchSize * 3.4f), Mathf.Max(augmentSize.y, MinTouchSize * 5.4f), scale);
        }
    }

    private void HideLegacyContent()
    {
        HideLegacyShopContent();
        HideLegacyAugmentContent();
        HideLegacyHudContent();
    }

    private void HideLegacyShopContent()
    {
        var legacyShop = FindObjectOfType<ShopUIController>(true);
        legacyShop?.SetLegacyContentVisibilityOnly(false);
    }

    private void HideLegacyAugmentContent()
    {
        var legacyAugment = FindObjectOfType<AugmentUIController>(true);
        legacyAugment?.InitializeAndHide();
    }

    private void HideLegacyHudContent()
    {
        var hud = FindObjectOfType<PlayerHUDController>(true);
        hud?.SetLegacyHudButtonsVisible(false);

        foreach (var optionButton in FindObjectsOfType<GameOptionButton>(true))
        {
            if (optionButton != null)
            {
                optionButton.gameObject.SetActive(false);
            }
        }

        legacyHudHidden = true;
    }

    private void ReleaseIconHandles()
    {
        foreach (var shopCard in shopCards)
        {
            shopCard.ReleaseIconHandle();
        }
    }

    private sealed class ShopCardView
    {
        private readonly Image icon;
        private readonly Label star;
        private readonly Label name;
        private readonly Label cost;
        private readonly Label soldOverlay;
        private AsyncOperationHandle<Sprite> iconHandle;

        public ShopCardView(int index, VisualElement root, Image icon, Label star, Label name, Label cost, Label soldOverlay)
        {
            Index = index;
            Root = root;
            this.icon = icon;
            this.star = star;
            this.name = name;
            this.cost = cost;
            this.soldOverlay = soldOverlay;
        }

        public int Index { get; }
        public VisualElement Root { get; }

        public void Bind(ShopItem item, bool hasItem, bool sold)
        {
            ReleaseIconHandle();

            Root?.SetEnabled(hasItem && !sold);
            Root?.EnableInClassList("is-disabled", !hasItem || sold);
            SetVisible(soldOverlay, sold);

            if (!hasItem || item.UnitData == null)
            {
                if (icon != null)
                {
                    icon.sprite = null;
                }

                SetText(star, "-");
                SetText(name, "-");
                SetText(cost, string.Empty);
                return;
            }

            SetText(star, FormatStarText(item.StarLevel));
            SetText(name, item.UnitData.unitName);
            SetText(cost, FormatCostText(item.CalculatedCost));
            LoadIconAsync(item.UnitData.unitIcon).Forget();
        }

        public void ApplySize(float width, float height, float scale)
        {
            if (Root == null)
            {
                return;
            }

            Root.style.width = width;
            Root.style.height = height;
            Root.style.marginLeft = 14f * scale;
            Root.style.marginRight = 14f * scale;
        }

        public void ReleaseIconHandle()
        {
            if (iconHandle.IsValid())
            {
                Addressables.Release(iconHandle);
            }

            iconHandle = default;
        }

        private async UniTask LoadIconAsync(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                if (icon != null)
                {
                    icon.sprite = null;
                }

                return;
            }

            var handle = Addressables.LoadAssetAsync<Sprite>(key);
            iconHandle = handle;
            try
            {
                await handle.Task;
                if (icon != null &&
                    iconHandle.Equals(handle) &&
                    handle.IsValid() &&
                    handle.Status == AsyncOperationStatus.Succeeded)
                {
                    icon.sprite = handle.Result;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GamePrepareUIToolkitController] Failed to load unit icon: {ex.Message}");
            }
        }
    }

    private sealed class AugmentCardView
    {
        private readonly Image icon;
        private readonly Label tier;
        private readonly Label name;
        private readonly Label description;

        public AugmentCardView(int index, VisualElement root, Image icon, Label tier, Label name, Label description)
        {
            Index = index;
            Root = root;
            this.icon = icon;
            this.tier = tier;
            this.name = name;
            this.description = description;
        }

        public int Index { get; }
        public VisualElement Root { get; }

        public void Bind(AugmentData augment)
        {
            var hasAugment = augment != null;
            Root?.SetEnabled(hasAugment);
            Root?.EnableInClassList("is-disabled", !hasAugment);

            if (icon != null)
            {
                icon.sprite = hasAugment ? augment.icon : null;
            }

            SetText(tier, hasAugment ? FormatAugmentTierText(augment.tier) : "-");
            SetText(name, hasAugment ? augment.augmentName : "-");
            SetText(description, hasAugment ? augment.description : string.Empty);
        }

        public void ApplySize(float width, float height, float scale)
        {
            if (Root == null)
            {
                return;
            }

            Root.style.width = width;
            Root.style.height = height;
            Root.style.marginLeft = 18f * scale;
            Root.style.marginRight = 18f * scale;
        }
    }

    private static void SetText(Label label, string text)
    {
        if (label != null)
        {
            label.text = text ?? string.Empty;
        }
    }
}
