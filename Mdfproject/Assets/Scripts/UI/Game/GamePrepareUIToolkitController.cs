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
    public const int MonsterCardCount = 9;
    public const int ScrollCardCount = 5;

    private const string LayoutResourcePath = "UI/GamePrepare/GamePreparePanels";
    private const string StyleResourcePath = "UI/GamePrepare/GamePreparePanelsStyles";
    private const string ThemeResourcePath = "UI/GamePrepare/GamePrepareRuntimeTheme";
    private const string RuntimePanelSettingsName = "GamePrepareRuntimePanelSettings";
    private const string ShopCardStarClassPrefix = "shop-card-star-";
    private const int PanelSortingOrder = 280;
    private const int MinShopCardStarStyle = 1;
    private const int MaxShopCardStarStyle = 5;
    private const float ReferenceWidth = 1600f;
    private const float ReferenceHeight = 900f;
    private const float MinResponsiveScale = 0.72f;
    private const float MaxResponsiveScale = 1.28f;
    private const float MinTouchSize = 64f;
    private const float ShopCardReferenceWidth = 280f;
    private const float ShopCardReferenceHeight = 350f;
    private const float ShopSlotAspect = 200f / 250f;
    private const float ShopPanelLeftPadding = 120f;
    private const float ShopPanelRightPadding = 20f;
    private const float ShopCardHorizontalMargin = 6f;
    private const float AugmentCardScaleBoost = 1.08f;
    private const float ShopRowTopRatio = 0.16f;
    private const float AugmentRowTopRatio = 0.30f;
    private const float ShopPanelTopMin = 118f;
    private const float ShopPanelTopMax = 230f;
    private const float HudButtonSize = 84f;
    private const float HudActionGroupCardGap = 24f;
    private const float TopHudY = 12f;
    private const float RerollButtonSize = 180f;
    private const float HudEdgeInset = 28f;
    private const float HudBottomInset = 26f;
    private const float ShopToggleButtonSize = RerollButtonSize;
    private const float ShopToggleButtonBottom = HudBottomInset;
    private const float WallButtonSize = RerollButtonSize;
    private const float RoundTimerWidth = 320f;
    private const float RoundTimerHeight = 40f;
    private const float AugmentPanelTopMin = 220f;
    private const float AugmentPanelTopMax = 380f;

    private static GamePrepareUIToolkitController instance;

    [SerializeField] private UIDocument document;
    [SerializeField] private VisualTreeAsset layoutAsset;
    [SerializeField] private StyleSheet styleSheet;

    private readonly ShopCardView[] shopCards = new ShopCardView[ShopCardCount];
    private readonly AugmentCardView[] augmentCards = new AugmentCardView[AugmentCardCount];
    private readonly MonsterCardView[] monsterCards = new MonsterCardView[MonsterCardCount];
    private readonly ScrollCardView[] scrollCards = new ScrollCardView[ScrollCardCount];
    private readonly List<AugmentData> currentAugments = new List<AugmentData>(AugmentCardCount);
    private readonly HashSet<int> pendingShopPurchaseSlots = new HashSet<int>();

    private VisualElement root;
    private VisualElement safeRoot;
    private VisualElement designSpace;
    private VisualElement shopPanel;
    private VisualElement shopCardRow;
    private VisualElement augmentPanel;
    private VisualElement augmentCardRow;
    private VisualElement leftWireframeRail;
    private VisualElement shopControlRow;
    private VisualElement rerollButton;
    private Label rerollLabel;
    private VisualElement rerollGoldRow;
    private Label rerollGoldLabel;
    private Label shopStatusLabel;
    private VisualElement hudRoot;
    private VisualElement hudShopButton;
    private Label hudShopLabel;
    private Label hudShopGoldLabel;
    private VisualElement hudWallButton;
    private Label hudWallLabel;
    private VisualElement hudOptionButton;
    private Label roundTimerLabel;
    private VisualElement resourceRoot;
    private Label resourceGoldValue;
    private Label resourceWallValue;
    private VisualElement attackSequencePanel;
    private VisualElement attackMonsterRow;
    private VisualElement attackScrollRow;

    private GameManagers gameManagers;
    private PlayerManager localPlayer;
    private ShopManager localShopManager;
    private PlayerManager attackSequencePlayer;
    private AttackSequenceManager attackSequenceManager;
    private bool callbacksBound;
    private bool eventsSubscribed;
    private bool shopVisible;
    private bool augmentVisible;
    private bool attackSequenceVisible;
    private bool attackSequenceIsAttacking;
    private int selectedMonsterSlotIndex = -1;
    private int selectedScrollSlotIndex = -1;
    private int lastGoldCount = int.MinValue;
    private int lastResourceWallCount = int.MinValue;
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
               || IsRuntimePanelRaycasterObject(target)
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

    public static bool TryShowAttackSequenceFromLegacy(
        PlayerManager player,
        AttackSequenceManager attackManager,
        bool isAttacking)
    {
        if (!IsToolkitActive)
        {
            return false;
        }

        instance.ShowAttackSequence(player, attackManager, isAttacking);
        return true;
    }

    public static bool TryHideAttackSequenceFromLegacy()
    {
        if (!IsToolkitActive)
        {
            return false;
        }

        instance.SetAttackSequenceVisible(false);
        return true;
    }

    public static bool TryRefreshAttackSequenceFromLegacy(
        PlayerManager player,
        AttackSequenceManager attackManager)
    {
        if (!IsToolkitActive)
        {
            return false;
        }

        instance.RefreshAttackSequence(player, attackManager);
        return true;
    }

    public static bool TrySyncMonsterSelectionFromLegacy(int slotIndex)
    {
        if (!IsToolkitActive)
        {
            return false;
        }

        instance.SelectMonsterCard(slotIndex, false);
        return true;
    }

    public static bool TrySyncScrollSelectionFromLegacy(int slotIndex)
    {
        if (!IsToolkitActive)
        {
            return false;
        }

        instance.SelectScrollCard(slotIndex, false);
        return true;
    }

    public static void SuppressBattleMapInputForCurrentPointer()
    {
        if (!IsToolkitActive)
        {
            return;
        }

        instance.attackSequenceManager?.SuppressBattleMapInputForCurrentPointer();
    }

    public static string FormatCostText(int cost)
    {
        return cost <= 0 ? "\uBB34\uB8CC" : cost.ToString();
    }

    public static string FormatStarText(int star)
    {
        return star <= 1 ? string.Empty : new string('\u2605', Mathf.Clamp(star, 2, 3));
    }

    public static string GetShopCardStarClass(int starLevel)
    {
        return starLevel >= MinShopCardStarStyle && starLevel <= MaxShopCardStarStyle
            ? $"{ShopCardStarClassPrefix}{starLevel}"
            : string.Empty;
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

    public static string FormatAugmentDisplayName(string augmentName)
    {
        if (string.IsNullOrWhiteSpace(augmentName))
        {
            return string.Empty;
        }

        string trimmedName = augmentName.Trim();
        const string bossSummonPrefix = "\uBCF4\uC2A4\uBAAC\uC2A4\uD130 \uC18C\uD658";
        if (!trimmedName.StartsWith(bossSummonPrefix, StringComparison.Ordinal) ||
            !trimmedName.EndsWith(")", StringComparison.Ordinal))
        {
            return trimmedName;
        }

        int suffixIndex = trimmedName.LastIndexOf('(');
        if (suffixIndex <= bossSummonPrefix.Length ||
            trimmedName[suffixIndex - 1] == '\n')
        {
            return trimmedName;
        }

        return trimmedName.Substring(0, suffixIndex).TrimEnd() + "\n" + trimmedName.Substring(suffixIndex);
    }

    public static Vector2 CalculateCardSize(bool isShop, Vector2 screenSize)
    {
        var scale = CalculateResponsiveScale(screenSize);
        if (!isShop)
        {
            return new Vector2(332f * scale * AugmentCardScaleBoost, 480f * scale * AugmentCardScaleBoost);
        }

        var targetWidth = ShopCardReferenceWidth * scale;
        var targetHeight = ShopCardReferenceHeight * scale;
        var rowMargins = ShopCardHorizontalMargin * 2f * scale * ShopCardCount;
        var availableWidth = Mathf.Max(
            MinTouchSize * ShopCardCount,
            screenSize.x - ShopPanelLeftPadding - ShopPanelRightPadding - rowMargins);
        var fittedWidth = Mathf.Min(targetWidth, availableWidth / ShopCardCount);
        var fittedHeight = Mathf.Min(targetHeight, fittedWidth / ShopSlotAspect);
        return new Vector2(fittedWidth, fittedHeight);
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

    public static float CalculateShopTopPadding(Vector2 screenSize)
    {
        var scale = CalculateResponsiveScale(screenSize);
        var hudClearance = TopHudY + HudButtonSize + HudActionGroupCardGap * scale;
        var desiredTop = screenSize.y * ShopRowTopRatio;
        var maxTop = Mathf.Max(hudClearance, ShopPanelTopMax);
        return Mathf.Clamp(Mathf.Max(desiredTop, hudClearance), Mathf.Max(ShopPanelTopMin, hudClearance), maxTop);
    }

    public static float CalculateRerollButtonTop(Vector2 screenSize)
    {
        var scale = CalculateResponsiveScale(screenSize);
        var cardBottom = CalculateShopTopPadding(screenSize) + CalculateCardSize(true, screenSize).y;
        var desiredTop = cardBottom + HudActionGroupCardGap * scale;
        var minTop = TopHudY + HudButtonSize + 12f;
        var maxTop = Mathf.Max(minTop, screenSize.y - RerollButtonSize - 24f);
        return Mathf.Clamp(desiredTop, minTop, maxTop);
    }

    public static float CalculateAugmentTopPadding(Vector2 screenSize)
    {
        return Mathf.Clamp(screenSize.y * AugmentRowTopRatio, AugmentPanelTopMin, AugmentPanelTopMax);
    }

    private bool IsBlockingElementAt(Vector2 screenPosition)
    {
        if (root == null || root.panel == null)
        {
            return false;
        }

        Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(root.panel, ToPanelScreenPosition(screenPosition));
        return IsBlockingElementAtPanelPosition(panelPosition);
    }

    private static Vector2 ToPanelScreenPosition(Vector2 screenPosition)
    {
        return new Vector2(screenPosition.x, Screen.height - screenPosition.y);
    }

    private bool IsBlockingElementAtPanelPosition(Vector2 panelPosition)
    {
        VisualElement picked = root.panel.Pick(panelPosition);
        if (IsBlockingElementOrDescendant(picked))
        {
            return true;
        }

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

        if (attackSequenceVisible)
        {
            if (ContainsPoint(attackMonsterRow, panelPosition) ||
                ContainsPoint(attackScrollRow, panelPosition))
            {
                return true;
            }

            for (int i = 0; i < monsterCards.Length; i++)
            {
                if (ContainsPoint(monsterCards[i].Root, panelPosition))
                {
                    return true;
                }
            }

            for (int i = 0; i < scrollCards.Length; i++)
            {
                if (ContainsPoint(scrollCards[i].Root, panelPosition))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsBlockingElementOrDescendant(VisualElement element)
    {
        if (element == null)
        {
            return false;
        }

        if (IsElementOrChildOf(element, hudShopButton) ||
            IsElementOrChildOf(element, hudWallButton) ||
            IsElementOrChildOf(element, hudOptionButton) ||
            IsElementOrChildOf(element, rerollButton))
        {
            return true;
        }

        for (int i = 0; i < shopCards.Length; i++)
        {
            if (shopVisible && IsElementOrChildOf(element, shopCards[i].Root))
            {
                return true;
            }
        }

        for (int i = 0; i < augmentCards.Length; i++)
        {
            if (augmentVisible && IsElementOrChildOf(element, augmentCards[i].Root))
            {
                return true;
            }
        }

        for (int i = 0; i < monsterCards.Length; i++)
        {
            if (attackSequenceVisible && IsElementOrChildOf(element, monsterCards[i].Root))
            {
                return true;
            }
        }

        for (int i = 0; i < scrollCards.Length; i++)
        {
            if (attackSequenceVisible && IsElementOrChildOf(element, scrollCards[i].Root))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsElementOrChildOf(VisualElement element, VisualElement parent)
    {
        if (!CanBlockPointer(parent))
        {
            return false;
        }

        for (VisualElement current = element; current != null; current = current.parent)
        {
            if (current == parent)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsPoint(VisualElement element, Vector2 panelPosition)
    {
        if (!CanBlockPointer(element))
        {
            return false;
        }

        return element.worldBound.Contains(panelPosition);
    }

    private static bool CanBlockPointer(VisualElement element)
    {
        return element != null &&
               element.pickingMode != PickingMode.Ignore &&
               element.resolvedStyle.display != DisplayStyle.None &&
               element.resolvedStyle.visibility != Visibility.Hidden;
    }

    private static bool IsRuntimePanelRaycasterObject(GameObject target)
    {
        if (target == null || instance?.document == null)
        {
            return false;
        }

        var panelSettings = instance.document.panelSettings;
        return target.name == RuntimePanelSettingsName ||
               (panelSettings != null && target.name == panelSettings.name);
    }

    private static PanelSettings CreateRuntimePanelSettings()
    {
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.name = RuntimePanelSettingsName;
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
        ApplyLayoutScale();
        RegisterCallbacks();
        SubscribeEvents();
        RefreshRuntimeReferences();
        HideLegacyContent();
        SetShopVisible(false);
        SetAugmentVisible(false);
        SetAttackSequenceVisible(false);
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

        if (styleSheet != null && !root.styleSheets.Contains(styleSheet))
        {
            root.styleSheets.Add(styleSheet);
        }
    }

    private void BindElements()
    {
        root = document.rootVisualElement;
        safeRoot = root?.Q<VisualElement>("game-prepare-safe-root");
        designSpace = root?.Q<VisualElement>("game-prepare-design-space");
        shopPanel = root?.Q<VisualElement>("shop-panel");
        shopCardRow = root?.Q<VisualElement>("shop-card-row");
        augmentPanel = root?.Q<VisualElement>("augment-panel");
        augmentCardRow = root?.Q<VisualElement>("augment-card-row");
        leftWireframeRail = root?.Q<VisualElement>("game-left-wireframe-rail");
        shopControlRow = root?.Q<VisualElement>("shop-control-row");
        rerollButton = root?.Q<VisualElement>("shop-reroll-button");
        rerollLabel = root?.Q<Label>("shop-reroll-label");
        rerollGoldRow = root?.Q<VisualElement>("shop-reroll-gold-row");
        rerollGoldLabel = root?.Q<Label>("shop-reroll-gold-count-label");
        shopStatusLabel = root?.Q<Label>("shop-status-label");
        hudRoot = root?.Q<VisualElement>("game-hud-root");
        hudShopButton = root?.Q<VisualElement>("game-shop-toggle-button");
        hudShopLabel = root?.Q<Label>("game-shop-toggle-label");
        hudShopGoldLabel = root?.Q<Label>("game-shop-gold-count-label");
        hudWallButton = root?.Q<VisualElement>("game-wall-button");
        hudWallLabel = root?.Q<Label>("game-wall-count-label");
        hudOptionButton = root?.Q<VisualElement>("game-option-button");
        roundTimerLabel = root?.Q<Label>("game-round-timer-label");
        resourceRoot = root?.Q<VisualElement>("game-resource-root");
        resourceGoldValue = root?.Q<Label>("game-gold-count-value");
        resourceWallValue = root?.Q<Label>("game-wall-count-label");
        attackSequencePanel = root?.Q<VisualElement>("attack-sequence-panel");
        attackMonsterRow = root?.Q<VisualElement>("attack-monster-row");
        attackScrollRow = root?.Q<VisualElement>("attack-scroll-row");

        for (var i = 0; i < ShopCardCount; i++)
        {
            shopCards[i] = new ShopCardView(
                i,
                root?.Q<VisualElement>($"shop-card-{i}"),
                root?.Q<Image>($"shop-icon-{i}"),
                root?.Q<Label>($"shop-star-{i}"),
                root?.Q<Label>($"shop-name-{i}"),
                root?.Q<Label>($"shop-cost-{i}"),
                root?.Q<VisualElement>($"shop-cost-icon-{i}"),
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

        for (var i = 0; i < MonsterCardCount; i++)
        {
            monsterCards[i] = new MonsterCardView(
                i,
                root?.Q<VisualElement>($"attack-monster-card-{i}"),
                root?.Q<Image>($"attack-monster-icon-{i}"),
                root?.Q<Label>($"attack-monster-name-{i}"),
                root?.Q<Label>($"attack-monster-count-{i}"));
        }

        for (var i = 0; i < ScrollCardCount; i++)
        {
            scrollCards[i] = new ScrollCardView(
                i,
                root?.Q<VisualElement>($"attack-scroll-card-{i}"),
                root?.Q<Image>($"attack-scroll-icon-{i}"),
                root?.Q<Label>($"attack-scroll-name-{i}"));
        }
    }

    private void ConfigurePickingModes()
    {
        SetPickingMode(root, PickingMode.Ignore);
        SetPickingMode(safeRoot, PickingMode.Ignore);
        SetPickingMode(designSpace, PickingMode.Ignore);
        SetPickingMode(leftWireframeRail, PickingMode.Ignore);
        SetPickingMode(hudRoot, PickingMode.Ignore);
        SetPickingMode(hudShopButton, PickingMode.Position);
        SetPickingMode(hudWallButton, PickingMode.Position);
        SetPickingMode(hudOptionButton, PickingMode.Position);
        SetPickingMode(roundTimerLabel, PickingMode.Ignore);
        SetPickingMode(resourceRoot, PickingMode.Ignore);
        SetPickingMode(attackSequencePanel, PickingMode.Ignore);
        SetPickingMode(attackMonsterRow, PickingMode.Ignore);
        SetPickingMode(attackScrollRow, PickingMode.Ignore);
        SetPickingMode(rerollButton, PickingMode.Position);

        for (var i = 0; i < shopCards.Length; i++)
        {
            SetPickingMode(shopCards[i].Root, PickingMode.Position);
        }

        for (var i = 0; i < augmentCards.Length; i++)
        {
            SetPickingMode(augmentCards[i].Root, PickingMode.Position);
        }

        for (var i = 0; i < monsterCards.Length; i++)
        {
            SetPickingMode(monsterCards[i].Root, PickingMode.Position);
        }

        for (var i = 0; i < scrollCards.Length; i++)
        {
            SetPickingMode(scrollCards[i].Root, PickingMode.Position);
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
            ApplyLayoutScale();
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

        for (var i = 0; i < monsterCards.Length; i++)
        {
            var slotIndex = i;
            monsterCards[i].Root?.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    SuppressBattleMapInputForCurrentPointer();
                    evt.StopPropagation();
                }
            });
            monsterCards[i].Root?.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    SuppressBattleMapInputForCurrentPointer();
                    HandleMonsterCardClicked(slotIndex);
                    evt.StopPropagation();
                }
            });
        }

        for (var i = 0; i < scrollCards.Length; i++)
        {
            var slotIndex = i;
            scrollCards[i].Root?.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    SuppressBattleMapInputForCurrentPointer();
                    evt.StopPropagation();
                }
            });
            scrollCards[i].Root?.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    SuppressBattleMapInputForCurrentPointer();
                    HandleScrollCardClicked(slotIndex);
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
        GameEvents.OnPurchaseFailed += HandlePurchaseFailed;
        GameEvents.OnAugmentPhaseStart += HandleAugmentPhaseStart;
        GameEvents.OnAugmentApplied += HandleAugmentApplied;
        GameEvents.OnPlayerStatsChanged += HandlePlayerStatsChanged;
        GameEvents.OnPlayerWallCountChanged += HandlePlayerWallCountChanged;
        GameEvents.OnMonsterPoolChanged += HandleMonsterPoolChanged;
        GameEvents.OnMagicScrollPoolChanged += HandleMagicScrollPoolChanged;
        GameEvents.OnBattleSequenceStarted += HandleBattleSequenceStarted;
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
        GameEvents.OnPurchaseFailed -= HandlePurchaseFailed;
        GameEvents.OnAugmentPhaseStart -= HandleAugmentPhaseStart;
        GameEvents.OnAugmentApplied -= HandleAugmentApplied;
        GameEvents.OnPlayerStatsChanged -= HandlePlayerStatsChanged;
        GameEvents.OnPlayerWallCountChanged -= HandlePlayerWallCountChanged;
        GameEvents.OnMonsterPoolChanged -= HandleMonsterPoolChanged;
        GameEvents.OnMagicScrollPoolChanged -= HandleMagicScrollPoolChanged;
        GameEvents.OnBattleSequenceStarted -= HandleBattleSequenceStarted;
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

        if (newState == GameManagers.GameState.Prepare || newState == GameManagers.GameState.GameOver)
        {
            SetAttackSequenceVisible(false);
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

        pendingShopPurchaseSlots.Clear();
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

        pendingShopPurchaseSlots.Remove(slotIndex);
        RefreshShopCards();
    }

    private void HandlePurchaseFailed(int playerID, int slotIndex, string reason)
    {
        if (!TryGetLocalPlayerId(out int localPlayerId) || localPlayerId != playerID)
        {
            return;
        }

        pendingShopPurchaseSlots.Remove(slotIndex);
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

    private void HandlePlayerStatsChanged(int playerId, int newHealth, int newGold)
    {
        if (!IsLocalPlayerId(playerId))
        {
            return;
        }

        UpdateResourceState(true);
    }

    private void HandlePlayerWallCountChanged(int playerId, int newWallCount)
    {
        if (!IsLocalPlayerId(playerId))
        {
            return;
        }

        UpdateResourceState(true);
    }

    private void HandleMonsterPoolChanged(int playerId, List<MonsterPoolEntry> pool)
    {
        if (!attackSequenceVisible || !IsLocalPlayerId(playerId))
        {
            return;
        }

        RefreshAttackSequence(attackSequencePlayer, attackSequenceManager);
    }

    private void HandleMagicScrollPoolChanged(int playerId, IReadOnlyList<MagicScrollData> scrolls)
    {
        if (!attackSequenceVisible || !IsLocalPlayerId(playerId))
        {
            return;
        }

        RefreshAttackSequence(attackSequencePlayer, attackSequenceManager);
    }

    private void HandleBattleSequenceStarted(bool isAttacking)
    {
        RefreshRuntimeReferences();
        var manager = localPlayer != null ? localPlayer.GetComponent<AttackSequenceManager>() : null;
        ShowAttackSequence(localPlayer, manager, isAttacking);
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

    private void ShowAttackSequence(PlayerManager player, AttackSequenceManager manager, bool isAttacking)
    {
        RefreshRuntimeReferences();
        attackSequencePlayer = player != null ? player : localPlayer;
        attackSequenceManager = manager != null
            ? manager
            : attackSequencePlayer != null ? attackSequencePlayer.GetComponent<AttackSequenceManager>() : null;
        attackSequenceIsAttacking = isAttacking;

        if (!isAttacking || attackSequencePlayer == null || attackSequenceManager == null)
        {
            SetAttackSequenceVisible(false);
            return;
        }

        SetShopVisible(false);
        SetAugmentVisible(false);
        RefreshAttackSequence(attackSequencePlayer, attackSequenceManager);
        SetAttackSequenceVisible(true);
        HideLegacyAttackSequenceContent();
    }

    private void RefreshAttackSequence(PlayerManager player, AttackSequenceManager manager)
    {
        RefreshRuntimeReferences();
        attackSequencePlayer = player != null ? player : attackSequencePlayer ?? localPlayer;
        attackSequenceManager = manager != null
            ? manager
            : attackSequenceManager != null
                ? attackSequenceManager
                : attackSequencePlayer != null ? attackSequencePlayer.GetComponent<AttackSequenceManager>() : null;

        BindMonsterCards(attackSequencePlayer?.AttackMonsterPool);
        BindScrollCards(attackSequencePlayer?.OwnedScrolls);

        if (selectedScrollSlotIndex >= 0)
        {
            SelectScrollCard(selectedScrollSlotIndex, false);
            return;
        }

        if (selectedMonsterSlotIndex >= 0)
        {
            SelectMonsterCard(selectedMonsterSlotIndex, false);
            return;
        }

        int resolvedSlot = ResolveSelectedMonsterSlot();
        if (resolvedSlot >= 0)
        {
            SelectMonsterCard(resolvedSlot, false);
        }
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

    private void BindMonsterCards(List<MonsterPoolEntry> pool)
    {
        for (var i = 0; i < monsterCards.Length; i++)
        {
            var entry = pool != null && i < pool.Count ? pool[i] : null;
            bool hasSlot = entry != null && entry.MonsterData != null;
            SetVisible(monsterCards[i].Root, hasSlot);
            monsterCards[i].Bind(entry);
        }

        if (selectedMonsterSlotIndex >= 0 &&
            (pool == null ||
             selectedMonsterSlotIndex >= pool.Count ||
             pool[selectedMonsterSlotIndex] == null ||
             pool[selectedMonsterSlotIndex].IsEmpty))
        {
            selectedMonsterSlotIndex = -1;
        }
    }

    private void BindScrollCards(IReadOnlyList<MagicScrollData> scrolls)
    {
        for (var i = 0; i < scrollCards.Length; i++)
        {
            var scroll = scrolls != null && i < scrolls.Count ? scrolls[i] : null;
            SetVisible(scrollCards[i].Root, scroll != null);
            scrollCards[i].Bind(scroll);
        }

        if (selectedScrollSlotIndex >= 0 &&
            (scrolls == null ||
             selectedScrollSlotIndex >= scrolls.Count ||
             scrolls[selectedScrollSlotIndex] == null))
        {
            selectedScrollSlotIndex = -1;
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
        if (slotIndex < 0
            || slotIndex >= items.Count
            || localShopManager.IsSlotSold(slotIndex)
            || pendingShopPurchaseSlots.Contains(slotIndex))
        {
            return;
        }

        pendingShopPurchaseSlots.Add(slotIndex);
        var command = new BuyUnitCommand(playerId, slotIndex);
        if (GameManagers.Instance?.CommandProcessor != null)
        {
            GameManagers.Instance.CommandProcessor.RequestCommandExecution(command);
        }
        else
        {
            pendingShopPurchaseSlots.Remove(slotIndex);
        }
    }

    private void HandleRerollClicked()
    {
        RefreshRuntimeReferences();
        if (!shopVisible || localShopManager == null || !TryGetLocalPlayerId(out var playerId))
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

        bool enablingWallMode = localPlayer.fieldManager.GetPlacementMode() != PlacementMode.Wall;
        if (enablingWallMode && CameraManager.Instance != null && !CameraManager.Instance.IsViewingOwnField)
        {
            CameraManager.Instance.ReturnToOwnField();
        }

        if (enablingWallMode && shopVisible)
        {
            SetShopVisible(false);
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

    private void HandleMonsterCardClicked(int slotIndex)
    {
        if (SelectMonsterCard(slotIndex, true))
        {
            HideLegacyAttackSequenceContent();
        }
    }

    private void HandleScrollCardClicked(int slotIndex)
    {
        if (SelectScrollCard(slotIndex, true))
        {
            HideLegacyAttackSequenceContent();
        }
    }

    private bool SelectMonsterCard(int slotIndex, bool notifyManager)
    {
        var pool = attackSequencePlayer?.AttackMonsterPool;
        if (pool == null || slotIndex < 0 || slotIndex >= pool.Count)
        {
            return false;
        }

        var entry = pool[slotIndex];
        if (entry == null || entry.IsEmpty)
        {
            return false;
        }

        selectedScrollSlotIndex = -1;
        selectedMonsterSlotIndex = slotIndex;

        for (int i = 0; i < scrollCards.Length; i++)
        {
            scrollCards[i].SetSelected(false);
        }

        for (int i = 0; i < monsterCards.Length; i++)
        {
            monsterCards[i].SetSelected(i == slotIndex);
        }

        if (notifyManager)
        {
            attackSequenceManager?.SelectMonsterSlot(slotIndex);
        }

        return true;
    }

    private bool SelectScrollCard(int slotIndex, bool notifyManager)
    {
        var scrolls = attackSequencePlayer?.OwnedScrolls;
        if (scrolls == null || slotIndex < 0 || slotIndex >= scrolls.Count || scrolls[slotIndex] == null)
        {
            return false;
        }

        selectedMonsterSlotIndex = -1;
        selectedScrollSlotIndex = slotIndex;

        for (int i = 0; i < monsterCards.Length; i++)
        {
            monsterCards[i].SetSelected(false);
        }

        for (int i = 0; i < scrollCards.Length; i++)
        {
            scrollCards[i].SetSelected(i == slotIndex);
        }

        if (notifyManager)
        {
            attackSequenceManager?.SelectMagicScroll(scrolls[slotIndex]);
        }

        return true;
    }

    private int ResolveSelectedMonsterSlot()
    {
        var selected = attackSequenceManager != null ? attackSequenceManager.GetSelectedMonster() : null;
        var pool = attackSequencePlayer?.AttackMonsterPool;
        if (selected == null || pool == null)
        {
            return -1;
        }

        for (int i = 0; i < pool.Count; i++)
        {
            if (ReferenceEquals(pool[i], selected))
            {
                return i;
            }
        }

        string selectedName = selected.MonsterData != null ? selected.MonsterData.name : null;
        if (string.IsNullOrEmpty(selectedName))
        {
            return -1;
        }

        for (int i = 0; i < pool.Count; i++)
        {
            var entry = pool[i];
            if (entry != null &&
                !entry.IsEmpty &&
                entry.MonsterData != null &&
                entry.MonsterData.name == selectedName)
            {
                return i;
            }
        }

        return -1;
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

    private bool IsLocalPlayerId(int playerId)
    {
        if (playerId < 0 || localPlayer == null)
        {
            RefreshRuntimeReferences();
        }

        if (localPlayer == null)
        {
            return false;
        }

        try
        {
            return localPlayer.playerId == playerId;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
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

    private void SetAttackSequenceVisible(bool visible)
    {
        attackSequenceVisible = visible;
        SetVisible(attackSequencePanel, visible);
        SetPickingMode(attackSequencePanel, PickingMode.Ignore);
        SetPickingMode(attackMonsterRow, visible ? PickingMode.Position : PickingMode.Ignore);
        SetPickingMode(attackScrollRow, visible ? PickingMode.Position : PickingMode.Ignore);
        if (!visible)
        {
            selectedMonsterSlotIndex = -1;
            selectedScrollSlotIndex = -1;
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
        rerollLabel.text = "\uC0C8\uB85C\uACE0\uCE68";
        if (rerollGoldLabel != null)
        {
            rerollGoldLabel.text = Mathf.Max(0, cost).ToString();
        }

        SetVisible(rerollGoldRow, cost > 0);
        rerollButton?.EnableInClassList("reroll-gold-mode", !shopVisible);
        SetPickingMode(rerollButton, shopVisible ? PickingMode.Position : PickingMode.Ignore);
    }

    private void UpdateRoundTimerLabel()
    {
        if (roundTimerLabel == null)
        {
            return;
        }

        var manager = gameManagers != null ? gameManagers : GameManagers.Instance;
        var round = 1;
        var remainingTime = 0f;
        if (manager != null)
        {
            try
            {
                round = Mathf.Max(1, manager.currentRound);
                remainingTime = manager.IsSequenceTransitioning
                    ? manager.currentSequenceTransitionTimer
                    : manager.currentPhaseTimer;
            }
            catch (InvalidOperationException)
            {
                remainingTime = 0f;
            }
            catch (MissingReferenceException)
            {
                remainingTime = 0f;
            }
        }

        var seconds = Mathf.Max(0, Mathf.CeilToInt(remainingTime));
        roundTimerLabel.text = $"ROUND {round}  {seconds:00}";
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
        SetVisible(shopControlRow, prepareButtonsVisible);
        SetPickingMode(shopControlRow, PickingMode.Ignore);
        SetVisible(hudShopButton, prepareButtonsVisible);
        SetVisible(hudWallButton, prepareButtonsVisible);
        lastPrepareButtonsVisible = prepareButtonsVisible;

        SetVisible(hudOptionButton, UIManagers.Instance != null);

        TryGetLocalPlayerResources(out var goldCount, out var wallCount);
        if (hudShopLabel != null)
        {
            var shopAction = shopVisible ? "\uB2EB\uAE30" : "\uC5F4\uAE30";
            hudShopLabel.text = $"\uC0C1\uC810\n{shopAction}";
        }

        if (hudShopGoldLabel != null)
        {
            hudShopGoldLabel.text = Mathf.Max(0, goldCount).ToString();
        }

        UpdateRerollLabel();

        var wallModeActive = CanShowPrepareHudActions() && IsWallPlacementActive();
        var wasWallModeActive = hudWallButton != null && hudWallButton.ClassListContains("hud-button-active");
        if (force || wallCount != lastWallCount || wallModeActive != wasWallModeActive)
        {
            if (hudWallLabel != null)
            {
                hudWallLabel.text = Mathf.Max(0, wallCount).ToString();
            }

            hudWallButton?.EnableInClassList("hud-button-active", wallModeActive);
            lastWallCount = wallCount;
        }

        hudShopButton?.EnableInClassList("hud-button-active", shopVisible);
        UpdateRoundTimerLabel();
        UpdateResourceState(force);
        if (force || !legacyHudHidden)
        {
            HideLegacyHudContent();
        }
    }

    private void UpdateResourceState(bool force)
    {
        int goldCount = 0;
        int wallCount = 0;
        bool hasResourceValues = TryGetLocalPlayerResources(out goldCount, out wallCount);
        if (resourceRoot != null)
        {
            resourceRoot.style.display = DisplayStyle.None;
            resourceRoot.pickingMode = PickingMode.Ignore;
        }

        if (!hasResourceValues)
        {
            return;
        }

        if (force || goldCount != lastGoldCount)
        {
            SetText(resourceGoldValue, goldCount.ToString());
            lastGoldCount = goldCount;
        }

        if (force || wallCount != lastResourceWallCount)
        {
            SetText(resourceWallValue, wallCount.ToString());
            lastResourceWallCount = wallCount;
        }
    }

    private bool TryGetLocalPlayerResources(out int goldCount, out int wallCount)
    {
        goldCount = 0;
        wallCount = 0;

        if (localPlayer == null)
        {
            return false;
        }

        try
        {
            if (!localPlayer.IsReadyForPlayerActions)
            {
                return false;
            }

            goldCount = localPlayer.GetGold();
            wallCount = localPlayer.GetWallCount();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (MissingReferenceException)
        {
            return false;
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
        if (gameManagers == null || localPlayer == null)
        {
            return false;
        }

        try
        {
            if (!localPlayer.IsReadyForPlayerActions)
            {
                return false;
            }

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
        var safeXMin = Mathf.Clamp(area.xMin, 0f, screenWidth);
        var safeXMax = Mathf.Clamp(area.xMax, safeXMin, screenWidth);
        var safeYMin = Mathf.Clamp(area.yMin, 0f, screenHeight);
        var safeYMax = Mathf.Clamp(area.yMax, safeYMin, screenHeight);
        safeRoot.style.left = safeXMin;
        safeRoot.style.right = screenWidth - safeXMax;
        safeRoot.style.top = screenHeight - safeYMax;
        safeRoot.style.bottom = safeYMin;
    }

    private void ApplyLayoutScale()
    {
        if (designSpace != null)
        {
            UpdateDesignScale();
            return;
        }

        ApplyResponsiveSize();
    }

    private void UpdateDesignScale()
    {
        if (designSpace == null)
        {
            return;
        }

        var safeArea = Screen.safeArea;
        var containerWidth = safeRoot != null && safeRoot.resolvedStyle.width > 1f
            ? safeRoot.resolvedStyle.width
            : Mathf.Max(1f, safeArea.width);
        var containerHeight = safeRoot != null && safeRoot.resolvedStyle.height > 1f
            ? safeRoot.resolvedStyle.height
            : Mathf.Max(1f, safeArea.height);

        var scale = Mathf.Min(containerWidth / ReferenceWidth, containerHeight / ReferenceHeight);
        if (scale <= 0f || float.IsNaN(scale) || float.IsInfinity(scale))
        {
            scale = 1f;
        }

        var left = Mathf.Max(0f, (containerWidth - ReferenceWidth * scale) * 0.5f);
        var top = Mathf.Max(0f, (containerHeight - ReferenceHeight * scale) * 0.5f);

        designSpace.style.left = left;
        designSpace.style.top = top;
        designSpace.style.width = ReferenceWidth;
        designSpace.style.height = ReferenceHeight;
        designSpace.transform.scale = new Vector3(scale, scale, 1f);
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

        if (hudRoot != null)
        {
            hudRoot.style.width = ShopToggleButtonSize;
            hudRoot.style.height = ShopToggleButtonSize;
            hudRoot.style.left = StyleKeyword.Auto;
            hudRoot.style.right = HudEdgeInset;
            hudRoot.style.top = StyleKeyword.Auto;
            hudRoot.style.bottom = ShopToggleButtonBottom;
        }

        if (hudShopButton != null)
        {
            hudShopButton.style.width = ShopToggleButtonSize;
            hudShopButton.style.height = ShopToggleButtonSize;
        }

        if (shopControlRow != null)
        {
            shopControlRow.style.width = RerollButtonSize;
            shopControlRow.style.height = RerollButtonSize;
            shopControlRow.style.top = CalculateRerollButtonTop(uiSize);
        }

        if (rerollButton != null)
        {
            rerollButton.style.width = RerollButtonSize;
            rerollButton.style.height = RerollButtonSize;
        }

        if (hudWallButton != null)
        {
            hudWallButton.style.width = WallButtonSize;
            hudWallButton.style.height = WallButtonSize;
            hudWallButton.style.left = HudEdgeInset;
            hudWallButton.style.right = StyleKeyword.Auto;
            hudWallButton.style.bottom = HudBottomInset;
        }

        if (roundTimerLabel != null)
        {
            roundTimerLabel.style.left = Mathf.Max(0f, (uiWidth - RoundTimerWidth) * 0.5f);
            roundTimerLabel.style.top = TopHudY;
            roundTimerLabel.style.width = RoundTimerWidth;
            roundTimerLabel.style.height = RoundTimerHeight;
        }

        if (shopPanel != null)
        {
            shopPanel.style.paddingLeft = ShopPanelLeftPadding;
            shopPanel.style.paddingRight = ShopPanelRightPadding;
            shopPanel.style.paddingTop = CalculateShopTopPadding(uiSize);
            shopPanel.style.paddingBottom = Mathf.Max(36f, 42f * scale);
        }

        if (augmentPanel != null)
        {
            augmentPanel.style.paddingTop = CalculateAugmentTopPadding(uiSize);
            augmentPanel.style.paddingBottom = Mathf.Max(48f, 56f * scale);
        }

        foreach (var card in shopCards)
        {
            card.ApplySize(Mathf.Max(shopSize.x, MinTouchSize * 1.85f), Mathf.Max(shopSize.y, MinTouchSize * 2.65f), scale);
        }

        foreach (var card in augmentCards)
        {
            card.ApplySize(Mathf.Max(augmentSize.x, MinTouchSize * 3.4f), Mathf.Max(augmentSize.y, MinTouchSize * 5.4f), scale);
        }

        foreach (var card in monsterCards)
        {
            card.ApplySize(scale);
        }

        foreach (var card in scrollCards)
        {
            card.ApplySize(scale);
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
        hud?.SetLegacyResourceHudVisible(false);

        foreach (var optionButton in FindObjectsOfType<GameOptionButton>(true))
        {
            if (optionButton != null)
            {
                optionButton.gameObject.SetActive(false);
            }
        }

        legacyHudHidden = true;
    }

    private void HideLegacyAttackSequenceContent()
    {
        var attackUi = AttackSequenceUIController.Instance;
        attackUi?.SetLegacyContentVisibilityOnly(false);
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
        private readonly VisualElement topGem;
        private readonly VisualElement body;
        private readonly VisualElement artFrame;
        private readonly VisualElement footer;
        private readonly Label star;
        private readonly Label name;
        private readonly Label cost;
        private readonly VisualElement costIcon;
        private readonly Label soldOverlay;
        private AsyncOperationHandle<Sprite> iconHandle;

        public ShopCardView(int index, VisualElement root, Image icon, Label star, Label name, Label cost, VisualElement costIcon, Label soldOverlay)
        {
            Index = index;
            Root = root;
            this.icon = icon;
            topGem = root?.Q<VisualElement>(className: "card-top-gem");
            body = root?.Q<VisualElement>(className: "card-body");
            artFrame = root?.Q<VisualElement>(className: "shop-art-frame");
            footer = root?.Q<VisualElement>(className: "card-footer");
            this.star = star;
            this.name = name;
            this.cost = cost;
            this.costIcon = costIcon;
            this.soldOverlay = soldOverlay;

            if (this.icon != null)
            {
                this.icon.scaleMode = ScaleMode.ScaleAndCrop;
            }
        }

        public int Index { get; }
        public VisualElement Root { get; }

        public void Bind(ShopItem item, bool hasItem, bool sold)
        {
            ReleaseIconHandle();

            Root?.SetEnabled(hasItem && !sold);
            Root?.EnableInClassList("is-disabled", !hasItem || sold);
            SetVisible(soldOverlay, sold);
            ApplyStarBackground(hasItem && item.UnitData != null ? item.UnitData.cost : 0);

            if (!hasItem || item.UnitData == null)
            {
                if (icon != null)
                {
                    icon.sprite = null;
                }

                SetText(star, string.Empty);
                SetText(name, "-");
                SetText(cost, string.Empty);
                SetVisible(costIcon, false);
                return;
            }

            SetText(star, FormatStarText(item.StarLevel));
            SetText(name, item.UnitData.unitName);
            SetText(cost, FormatCostText(item.CalculatedCost));
            SetVisible(costIcon, item.CalculatedCost > 0);
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
            Root.style.marginLeft = ShopCardHorizontalMargin * scale;
            Root.style.marginRight = ShopCardHorizontalMargin * scale;

            if (icon != null)
            {
                icon.style.position = Position.Absolute;
                icon.style.left = 0f;
                icon.style.right = 0f;
                icon.style.top = 0f;
                icon.style.bottom = 0f;
                icon.style.width = StyleKeyword.Auto;
                icon.style.height = StyleKeyword.Auto;
                icon.style.marginBottom = 0f;
                icon.style.flexShrink = 0f;
                icon.scaleMode = ScaleMode.ScaleAndCrop;
            }

            if (body != null)
            {
                body.style.alignItems = Align.Stretch;
                body.style.justifyContent = Justify.FlexStart;
                body.style.paddingLeft = 10f * scale;
                body.style.paddingRight = 10f * scale;
                body.style.paddingTop = 10f * scale;
                body.style.paddingBottom = 0f;
            }

            if (artFrame != null)
            {
                artFrame.style.flexGrow = 1f;
            }
        }

        private void ApplyStarBackground(int baseCost)
        {
            if (Root == null)
            {
                return;
            }

            int normalizedCost = baseCost > 0
                ? Mathf.Clamp(baseCost, MinShopCardStarStyle, MaxShopCardStarStyle)
                : 0;

            for (int starStyle = MinShopCardStarStyle; starStyle <= MaxShopCardStarStyle; starStyle++)
            {
                Root.EnableInClassList(GetShopCardStarClass(starStyle), starStyle == normalizedCost);
            }

            if (topGem != null)
            {
                topGem.style.display = DisplayStyle.None;
            }

            if (body != null)
            {
                body.style.backgroundColor = Color.clear;
            }

            if (footer != null)
            {
                footer.style.backgroundColor = new Color(0.02f, 0.04f, 0.07f, 0.68f);
                footer.style.borderTopWidth = 0f;
            }
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
            SetText(name, hasAugment ? FormatAugmentDisplayName(augment.augmentName) : "-");
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

    private sealed class MonsterCardView
    {
        private readonly Image icon;
        private readonly Label name;
        private readonly Label count;
        private int bindVersion;

        public MonsterCardView(int index, VisualElement root, Image icon, Label name, Label count)
        {
            Index = index;
            Root = root;
            this.icon = icon;
            this.name = name;
            this.count = count;

            if (this.icon != null)
            {
                this.icon.scaleMode = ScaleMode.ScaleAndCrop;
            }
        }

        public int Index { get; }
        public VisualElement Root { get; }

        public void Bind(MonsterPoolEntry entry)
        {
            bindVersion++;
            bool hasEntry = entry != null && entry.MonsterData != null && !entry.IsEmpty;
            Root?.SetEnabled(hasEntry);
            Root?.EnableInClassList("is-disabled", !hasEntry);

            if (!hasEntry)
            {
                if (icon != null)
                {
                    icon.sprite = null;
                }

                SetText(name, string.Empty);
                SetText(count, string.Empty);
                return;
            }

            SetText(name, entry.MonsterData.monsterName);
            SetText(count, $"{entry.RemainingCount}/{entry.MaxCount}");
            LoadIconAsync(entry.MonsterData, bindVersion).Forget();
        }

        public void SetSelected(bool selected)
        {
            Root?.EnableInClassList("attack-card-selected", selected);
        }

        public void ApplySize(float scale)
        {
            if (Root == null)
            {
                return;
            }

            Root.style.width = Mathf.Max(82f * scale, MinTouchSize);
            Root.style.height = Mathf.Max(96f * scale, MinTouchSize);
            Root.style.marginLeft = 4f * scale;
            Root.style.marginRight = 4f * scale;
        }

        private async UniTask LoadIconAsync(MonsterData monsterData, int version)
        {
            if (icon == null)
            {
                return;
            }

            if (monsterData == null || string.IsNullOrEmpty(monsterData.monsterIcon))
            {
                icon.sprite = null;
                return;
            }

            Sprite loaded = await AssetLoader.LoadAssetAsync<Sprite>(monsterData.monsterIcon);
            if (version == bindVersion && icon != null)
            {
                icon.sprite = loaded;
            }
        }
    }

    private sealed class ScrollCardView
    {
        private readonly Image icon;
        private readonly Label name;

        public ScrollCardView(int index, VisualElement root, Image icon, Label name)
        {
            Index = index;
            Root = root;
            this.icon = icon;
            this.name = name;
        }

        public int Index { get; }
        public VisualElement Root { get; }

        public void Bind(MagicScrollData scroll)
        {
            bool hasScroll = scroll != null;
            Root?.SetEnabled(hasScroll);
            Root?.EnableInClassList("is-disabled", !hasScroll);

            if (icon != null)
            {
                icon.sprite = hasScroll ? scroll.icon : null;
            }

            SetText(name, hasScroll ? scroll.scrollName : string.Empty);
        }

        public void SetSelected(bool selected)
        {
            Root?.EnableInClassList("attack-card-selected", selected);
        }

        public void ApplySize(float scale)
        {
            if (Root == null)
            {
                return;
            }

            Root.style.width = Mathf.Max(82f * scale, MinTouchSize);
            Root.style.height = Mathf.Max(84f * scale, MinTouchSize);
            Root.style.marginLeft = 4f * scale;
            Root.style.marginRight = 4f * scale;
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
