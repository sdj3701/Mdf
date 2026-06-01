using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Fusion;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

public class RankingUIController : MonoBehaviour
{
    private const string LayoutResourcePath = "UI/PlayerRanking/PlayerRankingPanel";
    private const string StyleResourcePath = "UI/PlayerRanking/PlayerRankingPanelStyles";
    private const string ThemeResourcePath = "UI/GamePrepare/GamePrepareRuntimeTheme";
    private const string RuntimePanelSettingsName = "PlayerRankingRuntimePanelSettings";
    private const int PanelSortingOrder = 260;
    private const float ReferenceWidth = 1600f;
    private const float ReferenceHeight = 900f;
    private const float ToolkitRefreshInterval = 0.25f;
    private const int MaxToolkitReserveCards = 2;
    private const int FallbackPlayerMaxHealth = 80;
    private const string AttackBattleRoleIconClass = "ranking-battle-role-icon-attacker";
    private const string DefenseBattleRoleIconClass = "ranking-battle-role-icon-defender";

    [Header("Legacy UI Parent Containers")]
    [SerializeField] private Transform leftSideContainer;
    [SerializeField] private Transform rightSideContainer;

    [Header("UI Toolkit")]
    [SerializeField] private bool useToolkitRanking = true;
    [SerializeField] private UIDocument toolkitDocument;
    [SerializeField] private VisualTreeAsset toolkitLayout;
    [SerializeField] private StyleSheet toolkitStyle;

    private readonly List<PlayerRankSlot> allSlots = new List<PlayerRankSlot>();
    private readonly List<PlayerManager> allPlayers = new List<PlayerManager>();
    private readonly List<RankingCardView> toolkitCards = new List<RankingCardView>(4);
    private readonly List<int> lastPlayerHealths = new List<int>();

    private VisualElement toolkitRoot;
    private VisualElement designSpace;
    private RankingCardView selfCard;
    private RankingCardView opponentCard;
    private RankingCardView reserveCard0;
    private RankingCardView reserveCard1;

    private bool isInitialized;
    private bool isInitializing;
    private bool toolkitReady;
    private float nextInitializeRetryTime;
    private float nextToolkitRefreshTime;
    private int lastToolkitCardClickFrame = -1;
    private static RankingUIController activeToolkitInstance;

    public static int GetLeftSideSlotCountForDisplay(int playerCount)
    {
        if (playerCount <= 1)
        {
            return Mathf.Max(0, playerCount);
        }

        return (playerCount + 1) / 2;
    }

    public static bool ShouldPlaceDisplayIndexOnLeft(int displayIndex, int playerCount)
    {
        return displayIndex >= 0
            && displayIndex < playerCount
            && displayIndex < GetLeftSideSlotCountForDisplay(playerCount);
    }

    public static int GetTopRightReserveCount(int playerCount)
    {
        return Mathf.Clamp(playerCount - 2, 0, 2);
    }

    public static bool IsPointerOverBlockingElement(Vector2 screenPosition)
    {
        var controller = activeToolkitInstance;
        if (controller == null ||
            !controller.isActiveAndEnabled ||
            !controller.useToolkitRanking ||
            controller.toolkitRoot == null ||
            controller.toolkitRoot.resolvedStyle.display == DisplayStyle.None)
        {
            return false;
        }

        return controller.FindToolkitCardAtScreenPosition(screenPosition) != null;
    }

    public static bool IsToolkitRaycastObject(GameObject target)
    {
        var controller = activeToolkitInstance;
        if (target == null ||
            controller == null ||
            !controller.isActiveAndEnabled ||
            !controller.useToolkitRanking)
        {
            return false;
        }

        return target == controller.gameObject
               || (controller.toolkitDocument != null && target == controller.toolkitDocument.gameObject)
               || controller.IsRuntimePanelRaycasterObject(target)
               || target.GetComponentInParent<RankingUIController>() != null;
    }

    private void Awake()
    {
        activeToolkitInstance = this;
        if (useToolkitRanking)
        {
            EnsureToolkit();
        }
    }

    private void OnEnable()
    {
        activeToolkitInstance = this;
        GameManagers.OnPlayersDataReady += OnPlayersDataReady;
        GameEvents.OnGameManagersReady += OnGameManagersReady;
        GameEvents.OnGameStateChanged += OnGameStateChanged;
        GameEvents.OnRoundStart += OnRoundStart;
        GameEvents.OnBattleSequenceStarted += OnBattleSequenceStarted;

        if (useToolkitRanking)
        {
            EnsureToolkit();
            RefreshToolkitDisplay(true);
        }
    }

    private void OnDisable()
    {
        if (activeToolkitInstance == this)
        {
            activeToolkitInstance = null;
        }

        GameManagers.OnPlayersDataReady -= OnPlayersDataReady;
        GameEvents.OnGameManagersReady -= OnGameManagersReady;
        GameEvents.OnGameStateChanged -= OnGameStateChanged;
        GameEvents.OnRoundStart -= OnRoundStart;
        GameEvents.OnBattleSequenceStarted -= OnBattleSequenceStarted;
    }

    private void Update()
    {
        if (GameManagers.Instance == null || !GameManagers.Instance.IsReadyForNetworkAccess)
        {
            return;
        }

        if (GameManagers.Instance.GetGameState() == GameManagers.GameState.GameOver)
        {
            SetToolkitVisible(false);
            return;
        }

        if (useToolkitRanking && EnsureToolkit())
        {
            HandleToolkitPointerInput();

            if (Time.unscaledTime >= nextToolkitRefreshTime)
            {
                nextToolkitRefreshTime = Time.unscaledTime + ToolkitRefreshInterval;
                RefreshToolkitDisplay(false);
            }

            return;
        }

        UpdateLegacyRanking();
    }

    private void OnPlayersDataReady()
    {
        if (useToolkitRanking)
        {
            RefreshToolkitDisplay(true);
            return;
        }

        if (!isInitialized)
        {
            InitializePlayersAndSlots();
        }
        else
        {
            SortAndDisplayPlayers();
        }
    }

    private void OnGameManagersReady()
    {
        RefreshToolkitDisplay(true);
    }

    private void OnGameStateChanged(GameManagers.GameState state)
    {
        RefreshToolkitDisplay(true);
    }

    private void OnRoundStart(int round)
    {
        RefreshToolkitDisplay(true);
    }

    private void OnBattleSequenceStarted(bool isAttacking)
    {
        RefreshToolkitDisplay(true);
    }

    private void UpdateLegacyRanking()
    {
        if (!isInitialized)
        {
            if (Time.unscaledTime >= nextInitializeRetryTime)
            {
                nextInitializeRetryTime = Time.unscaledTime + 0.5f;
                InitializePlayersAndSlots();
            }

            return;
        }

        if (HasPlayerStateChanged())
        {
            SortAndDisplayPlayers();
        }

        foreach (var slot in allSlots)
        {
            if (slot != null && slot.gameObject.activeInHierarchy)
            {
                slot.UpdateUI();
            }
        }
    }

    private bool EnsureToolkit()
    {
        if (!useToolkitRanking)
        {
            return false;
        }

        toolkitLayout ??= Resources.Load<VisualTreeAsset>(LayoutResourcePath);
        toolkitStyle ??= Resources.Load<StyleSheet>(StyleResourcePath);

        if (toolkitLayout == null || toolkitStyle == null)
        {
            Debug.LogWarning("[RankingUIController] UI Toolkit ranking assets are missing. Falling back to legacy ranking UI.");
            useToolkitRanking = false;
            SetLegacyContainersVisible(true);
            return false;
        }

        toolkitDocument ??= GetComponent<UIDocument>();
        if (toolkitDocument == null)
        {
            toolkitDocument = gameObject.AddComponent<UIDocument>();
        }

        if (toolkitDocument.visualTreeAsset == null)
        {
            toolkitDocument.visualTreeAsset = toolkitLayout;
        }

        if (toolkitDocument.panelSettings == null)
        {
            toolkitDocument.panelSettings = CreateRuntimePanelSettings();
        }

        toolkitRoot = toolkitDocument.rootVisualElement;
        if (toolkitRoot == null)
        {
            return false;
        }

        if (!toolkitRoot.styleSheets.Contains(toolkitStyle))
        {
            toolkitRoot.styleSheets.Add(toolkitStyle);
        }

        if (toolkitReady &&
            selfCard?.IsValid == true &&
            opponentCard?.IsValid == true &&
            reserveCard0?.IsValid == true &&
            reserveCard1?.IsValid == true)
        {
            SetLegacyContainersVisible(false);
            UpdateToolkitScale();
            return true;
        }

        BindToolkitElements();
        RegisterToolkitCallbacks();
        SetLegacyContainersVisible(false);
        toolkitReady = selfCard?.IsValid == true &&
                       opponentCard?.IsValid == true &&
                       reserveCard0?.IsValid == true &&
                       reserveCard1?.IsValid == true;
        return toolkitReady;
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

    private bool IsRuntimePanelRaycasterObject(GameObject target)
    {
        if (target == null || toolkitDocument == null)
        {
            return false;
        }

        var panelSettings = toolkitDocument.panelSettings;
        return target.name == RuntimePanelSettingsName ||
               (panelSettings != null && target.name == panelSettings.name);
    }

    private void BindToolkitElements()
    {
        designSpace = toolkitRoot.Q<VisualElement>("ranking-design-space");

        selfCard = BindCard("ranking-self-card", "ranking-self");
        opponentCard = BindCard("ranking-opponent-card", "ranking-opponent");
        reserveCard0 = BindCard("ranking-reserve-card-0", "ranking-reserve-0");
        reserveCard1 = BindCard("ranking-reserve-card-1", "ranking-reserve-1");

        toolkitCards.Clear();
        AddCardIfValid(selfCard);
        AddCardIfValid(opponentCard);
        AddCardIfValid(reserveCard0);
        AddCardIfValid(reserveCard1);

        SetPickingModeRecursive(toolkitRoot, PickingMode.Ignore);
        designSpace?.RegisterCallback<GeometryChangedEvent>(_ => UpdateToolkitScale());
        UpdateToolkitScale();
    }

    private RankingCardView BindCard(string rootName, string prefix)
    {
        var cardRoot = toolkitRoot.Q<VisualElement>(rootName);
        if (cardRoot == null)
        {
            Debug.LogError($"[RankingUIController] UXML element not found: {rootName}");
            return null;
        }

        return new RankingCardView(
            cardRoot,
            toolkitRoot.Q<Label>($"{prefix}-name"),
            toolkitRoot.Q<Label>($"{prefix}-hp"),
            toolkitRoot.Q<VisualElement>($"{prefix}-battle-role-icon"));
    }

    private void AddCardIfValid(RankingCardView card)
    {
        if (card != null)
        {
            toolkitCards.Add(card);
        }
    }

    private void RegisterToolkitCallbacks()
    {
        foreach (var card in toolkitCards)
        {
            card.RegisterClickHandler(OnToolkitCardClicked);
        }

        // Display-only overlay outside the player cards: it must not block GamePrepare augment/shop input.
    }

    private void OnToolkitCardClicked(RankingCardView card)
    {
        if (lastToolkitCardClickFrame == Time.frameCount)
        {
            return;
        }

        lastToolkitCardClickFrame = Time.frameCount;

        if (card == null || !IsPlayerReadable(card.TrackedPlayer))
        {
            Debug.Log("[RankingUIController] No tracked player for clicked ranking card.");
            return;
        }

        if (CameraManager.Instance == null)
        {
            Debug.LogWarning("[RankingUIController] CameraManager is missing.");
            return;
        }

        if (card.TrackedPlayer == CameraManager.Instance.OwnField)
        {
            CameraManager.Instance.ReturnToOwnField();
            return;
        }

        bool isAttackMode = ShouldUseAttackModeCamera(card.TrackedPlayer);
        CameraManager.Instance.MoveToPlayerField(card.TrackedPlayer, isAttackMode).Forget();
    }

    private void HandleToolkitPointerInput()
    {
        if (!MdfInput.PrimaryPointerWasReleasedThisFrame())
        {
            return;
        }

        var clickedCard = FindToolkitCardAtScreenPosition(MdfInput.PointerPosition);
        if (clickedCard != null)
        {
            OnToolkitCardClicked(clickedCard);
        }
    }

    private RankingCardView FindToolkitCardAtScreenPosition(Vector2 screenPosition)
    {
        if (toolkitRoot == null || toolkitRoot.panel == null)
        {
            return null;
        }

        Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(toolkitRoot.panel, screenPosition);
        var card = FindToolkitCardAtPanelPosition(panelPosition);
        if (card != null)
        {
            return card;
        }

        Vector2 invertedPanelPosition = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
        return FindToolkitCardAtPanelPosition(invertedPanelPosition);
    }

    private RankingCardView FindToolkitCardAtPanelPosition(Vector2 panelPosition)
    {
        foreach (var card in toolkitCards)
        {
            if (card != null && card.ContainsPanelPoint(panelPosition))
            {
                return card;
            }
        }

        return null;
    }

    private static bool ShouldUseAttackModeCamera(PlayerManager targetPlayer)
    {
        var gm = GameManagers.Instance;
        var localPlayer = gm?.localPlayer;
        if (!IsPlayerReadable(localPlayer) || !IsPlayerReadable(targetPlayer))
        {
            return false;
        }

        try
        {
            if (!localPlayer.IsActivelyFighting || !localPlayer.IsAttackerInCurrentBattle)
            {
                return false;
            }
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        if (!TryGetPlayerIdSafe(localPlayer, out int localPlayerId) ||
            !TryGetPlayerIdSafe(targetPlayer, out int targetPlayerId))
        {
            return false;
        }

        return gm.GetBattleOpponent(localPlayerId) == targetPlayerId;
    }

    private static void SetPickingModeRecursive(VisualElement element, PickingMode mode)
    {
        if (element == null)
        {
            return;
        }

        element.pickingMode = mode;
        foreach (var child in element.Children())
        {
            SetPickingModeRecursive(child, mode);
        }
    }

    private void UpdateToolkitScale()
    {
        if (toolkitRoot == null || designSpace == null)
        {
            return;
        }

        float rootWidth = toolkitRoot.resolvedStyle.width > 1f ? toolkitRoot.resolvedStyle.width : Screen.width;
        float rootHeight = toolkitRoot.resolvedStyle.height > 1f ? toolkitRoot.resolvedStyle.height : Screen.height;
        float scale = Mathf.Min(rootWidth / ReferenceWidth, rootHeight / ReferenceHeight);
        if (scale <= 0f || float.IsNaN(scale) || float.IsInfinity(scale))
        {
            scale = 1f;
        }

        float left = Mathf.Max(0f, (rootWidth - ReferenceWidth * scale) * 0.5f);
        float top = Mathf.Max(0f, (rootHeight - ReferenceHeight * scale) * 0.5f);

        designSpace.style.left = left;
        designSpace.style.top = top;
        designSpace.style.width = ReferenceWidth;
        designSpace.style.height = ReferenceHeight;
        designSpace.transform.scale = new Vector3(scale, scale, 1f);
    }

    private void RefreshToolkitDisplay(bool force)
    {
        if (!useToolkitRanking || !EnsureToolkit())
        {
            return;
        }

        var gm = GameManagers.Instance;
        if (gm == null || !gm.IsReadyForNetworkAccess)
        {
            SetToolkitVisible(false);
            return;
        }

        var players = GetValidPlayers(gm);
        if (players.Count == 0)
        {
            ClearToolkitCards();
            SetToolkitVisible(false);
            return;
        }

        SetToolkitVisible(true);

        var networkPlayers = FindObjectsOfType<NetworkPlayer>();
        var local = ResolveLocalPlayer(gm, players);
        var opponent = ResolveOpponent(gm, local, players);

        selfCard.Bind(BuildCardData(local, networkPlayers));
        opponentCard.Bind(opponent != null
            ? BuildCardData(opponent, networkPlayers)
            : RankingCardData.Hidden());

        var reservePlayers = players
            .Where(player => player != local && player != opponent)
            .OrderBy(player => TryGetPlayerIdSafe(player, out int playerId) ? playerId : int.MaxValue)
            .Take(MaxToolkitReserveCards)
            .ToList();

        reserveCard0.Bind(reservePlayers.Count > 0
            ? BuildCardData(reservePlayers[0], networkPlayers)
            : RankingCardData.Hidden());
        reserveCard1.Bind(reservePlayers.Count > 1
            ? BuildCardData(reservePlayers[1], networkPlayers)
            : RankingCardData.Hidden());
    }

    private void SetToolkitVisible(bool visible)
    {
        if (toolkitRoot != null)
        {
            toolkitRoot.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    private void ClearToolkitCards()
    {
        foreach (var card in toolkitCards)
        {
            card.Bind(RankingCardData.Hidden());
        }
    }

    private static List<PlayerManager> GetValidPlayers(GameManagers gm)
    {
        if (gm == null)
        {
            return new List<PlayerManager>();
        }

        return gm.AllPlayers
            .Where(IsPlayerReadable)
            .OrderBy(player => TryGetPlayerIdSafe(player, out int playerId) ? playerId : int.MaxValue)
            .ToList();
    }

    private static PlayerManager ResolveLocalPlayer(GameManagers gm, List<PlayerManager> players)
    {
        if (gm?.localPlayer != null && IsPlayerReadable(gm.localPlayer) && players.Contains(gm.localPlayer))
        {
            return gm.localPlayer;
        }

        if (gm?.Runner != null)
        {
            var localByAuthority = players.FirstOrDefault(player =>
                player.Object != null &&
                player.Object.IsValid &&
                player.Object.InputAuthority == gm.Runner.LocalPlayer);
            if (localByAuthority != null)
            {
                return localByAuthority;
            }
        }

        return players.FirstOrDefault();
    }

    private static PlayerManager ResolveOpponent(GameManagers gm, PlayerManager local, List<PlayerManager> players)
    {
        if (gm == null || local == null || !TryGetPlayerIdSafe(local, out int localId))
        {
            return null;
        }

        int opponentId = gm.GetBattleOpponent(localId);
        if (opponentId < 0 && local.opponentManager != null && TryGetPlayerIdSafe(local.opponentManager, out int fallbackId))
        {
            opponentId = fallbackId;
        }

        if (opponentId >= 0)
        {
            var battleOpponent = players.FirstOrDefault(player => TryGetPlayerIdSafe(player, out int id) && id == opponentId);
            if (battleOpponent != null)
            {
                return battleOpponent;
            }
        }

        return players
            .Where(player => player != local)
            .OrderBy(player => TryGetPlayerIdSafe(player, out int id) ? id : int.MaxValue)
            .FirstOrDefault();
    }

    private static RankingCardData BuildCardData(
        PlayerManager player,
        NetworkPlayer[] networkPlayers)
    {
        if (!IsPlayerReadable(player))
        {
            return RankingCardData.Hidden();
        }

        string name = ResolveNickname(player, networkPlayers);
        int playerId = TryGetPlayerIdSafe(player, out int id) ? id : -1;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = playerId >= 0 ? $"Player {playerId}" : "Player";
        }

        int hp = TryGetHealthSafe(player, out int health) ? health : 0;
        int maxHp = TryGetMaxHealthSafe(player, out int maxHealth) ? maxHealth : FallbackPlayerMaxHealth;
        int displayHp = Mathf.Max(0, hp);
        int displayMaxHp = Mathf.Max(1, maxHp);
        return new RankingCardData(
            true,
            player,
            name,
            displayHp.ToString(),
            GetHealthFillPercentForDisplay(displayHp, displayMaxHp),
            ShouldUseAttackBattleRoleIcon(player));
    }

    public static float GetHealthFillPercentForDisplay(int health, int maxHealth)
    {
        return maxHealth > 0 ? Mathf.Clamp01(health / (float)maxHealth) : 0f;
    }

    public static bool ShouldUseAttackBattleRoleIconForDisplay(
        bool isActivelyFighting,
        bool isAttackerInCurrentBattle)
    {
        return isActivelyFighting && isAttackerInCurrentBattle;
    }

    private static bool ShouldUseAttackBattleRoleIcon(PlayerManager player)
    {
        if (!IsPlayerReadable(player))
        {
            return false;
        }

        var gm = GameManagers.Instance;
        if (TryGetPlayerIdSafe(player, out int playerId) &&
            gm != null &&
            gm.TryGetBattleRoleSnapshot(playerId, out bool isSnapshotFighting, out bool isSnapshotAttacker))
        {
            return ShouldUseAttackBattleRoleIconForDisplay(isSnapshotFighting, isSnapshotAttacker);
        }

        try
        {
            return ShouldUseAttackBattleRoleIconForDisplay(
                player.IsActivelyFighting,
                player.IsAttackerInCurrentBattle);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static string ResolveNickname(PlayerManager player, NetworkPlayer[] networkPlayers)
    {
        if (player == null || networkPlayers == null)
        {
            return null;
        }

        for (int i = 0; i < networkPlayers.Length; i++)
        {
            var networkPlayer = networkPlayers[i];
            if (networkPlayer == null || networkPlayer.Object == null || player.Object == null)
            {
                continue;
            }

            if (networkPlayer.Object.InputAuthority == player.Object.InputAuthority)
            {
                string nickname = networkPlayer.Nickname.ToString();
                if (!string.IsNullOrWhiteSpace(nickname))
                {
                    return nickname;
                }
            }
        }

        return null;
    }

    private void SetLegacyContainersVisible(bool visible)
    {
        if (leftSideContainer != null)
        {
            leftSideContainer.gameObject.SetActive(visible);
        }

        if (rightSideContainer != null)
        {
            rightSideContainer.gameObject.SetActive(visible);
        }
    }

    private static bool IsPlayerReadable(PlayerManager player)
    {
        return player != null
            && player.Object != null
            && player.Object.IsValid;
    }

    private static bool TryGetHealthSafe(PlayerManager player, out int health)
    {
        health = 0;
        if (!IsPlayerReadable(player))
        {
            return false;
        }

        try
        {
            health = player.GetHealth();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryGetMaxHealthSafe(PlayerManager player, out int maxHealth)
    {
        maxHealth = FallbackPlayerMaxHealth;
        if (!IsPlayerReadable(player))
        {
            return false;
        }

        try
        {
            maxHealth = player.GetMaxHealth();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryGetPlayerIdSafe(PlayerManager player, out int playerId)
    {
        playerId = int.MaxValue;
        if (!IsPlayerReadable(player))
        {
            return false;
        }

        try
        {
            playerId = player.playerId;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private bool HasPlayerStateChanged()
    {
        if (allPlayers.Count != lastPlayerHealths.Count)
        {
            UpdateLastPlayerHealths();
            return true;
        }

        for (int i = 0; i < allPlayers.Count; i++)
        {
            if (TryGetHealthSafe(allPlayers[i], out int health) && lastPlayerHealths[i] != health)
            {
                UpdateLastPlayerHealths();
                return true;
            }
        }

        return false;
    }

    private void UpdateLastPlayerHealths()
    {
        lastPlayerHealths.Clear();
        for (int i = 0; i < allPlayers.Count; i++)
        {
            lastPlayerHealths.Add(TryGetHealthSafe(allPlayers[i], out int health) ? health : 0);
        }
    }

    private async void InitializePlayersAndSlots()
    {
        if (isInitialized || isInitializing)
        {
            return;
        }

        isInitializing = true;
        if (GameManagers.Instance == null)
        {
            isInitializing = false;
            nextInitializeRetryTime = Time.unscaledTime + 0.5f;
            return;
        }

        var validPlayers = GetValidPlayers(GameManagers.Instance);
        int actualPlayerCount = GameManagers.Instance.singlePlayerModeCount;
        if (GameManagers.Instance.Runner != null &&
            GameManagers.Instance.Runner.GameMode == GameMode.Single)
        {
            if (actualPlayerCount <= 0)
            {
                actualPlayerCount = validPlayers.Count;
            }
        }
        else
        {
            actualPlayerCount = validPlayers.Count;
        }

        allPlayers.Clear();
        allPlayers.AddRange(validPlayers.Take(actualPlayerCount));

        if (allPlayers.Count == 0)
        {
            isInitializing = false;
            nextInitializeRetryTime = Time.unscaledTime + 0.5f;
            return;
        }

        PlayerRankSlot[] existingSlots = GetComponentsInChildren<PlayerRankSlot>(true);
        foreach (var slot in existingSlots)
        {
            if (slot != null)
            {
                DestroyImmediate(slot.gameObject);
            }
        }

        allSlots.Clear();

        for (int i = 0; i < allPlayers.Count; i++)
        {
            GameObject slotGO = null;
            if (AddressablesManager.Instance != null)
            {
                slotGO = await AddressablesManager.Instance.LoadObject("UI_Slot_PlayerRank", transform);
                if (slotGO != null)
                {
                    slotGO.name = $"PlayerRankSlot_{i}";
                }
            }

            if (slotGO == null)
            {
                slotGO = new GameObject($"PlayerRankSlot_{i}", typeof(RectTransform));
                slotGO.transform.SetParent(transform, false);
            }

            PlayerRankSlot slot = slotGO.GetComponent<PlayerRankSlot>();
            if (slot == null)
            {
                slot = slotGO.AddComponent<PlayerRankSlot>();
            }

            slotGO.SetActive(false);
            allSlots.Add(slot);
        }

        isInitialized = true;
        SortAndDisplayPlayers();
        isInitializing = false;
    }

    private void SortAndDisplayPlayers()
    {
        var sortedPlayers = allPlayers
            .OrderByDescending(p => TryGetHealthSafe(p, out int health) ? health : 0)
            .ThenBy(p => TryGetPlayerIdSafe(p, out int playerId) ? playerId : int.MaxValue)
            .ToList();

        for (int i = 0; i < allSlots.Count; i++)
        {
            PlayerRankSlot currentSlot = allSlots[i];
            if (currentSlot == null)
            {
                continue;
            }

            if (i < sortedPlayers.Count)
            {
                PlayerManager playerForThisSlot = sortedPlayers[i];
                Transform targetParent = ShouldPlaceDisplayIndexOnLeft(i, sortedPlayers.Count)
                    ? leftSideContainer
                    : rightSideContainer;
                targetParent ??= transform;

                if (!targetParent.gameObject.activeInHierarchy)
                {
                    targetParent.gameObject.SetActive(true);
                }

                currentSlot.transform.SetParent(targetParent, false);
                if (!currentSlot.gameObject.activeInHierarchy)
                {
                    currentSlot.gameObject.SetActive(true);
                }

                currentSlot.Initialize(playerForThisSlot);

                Canvas.ForceUpdateCanvases();
                RectTransform rt = targetParent.GetComponent<RectTransform>();
                if (rt != null)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
                }

                currentSlot.UpdateUI();
            }
            else
            {
                currentSlot.gameObject.SetActive(false);
            }
        }
    }

    private readonly struct RankingCardData
    {
        public readonly bool Visible;
        public readonly PlayerManager Player;
        public readonly string Name;
        public readonly string Health;
        public readonly float HealthFillPercent;
        public readonly bool UseAttackBattleRoleIcon;

        public RankingCardData(
            bool visible,
            PlayerManager player,
            string name,
            string health,
            float healthFillPercent,
            bool useAttackBattleRoleIcon)
        {
            Visible = visible;
            Player = player;
            Name = name;
            Health = health;
            HealthFillPercent = healthFillPercent;
            UseAttackBattleRoleIcon = useAttackBattleRoleIcon;
        }

        public static RankingCardData Hidden()
        {
            return new RankingCardData(false, null, string.Empty, string.Empty, 0f, false);
        }
    }

    private sealed class RankingCardView
    {
        private Action<RankingCardView> clickHandler;
        private bool clickRegistered;
        private readonly Label name;
        private readonly Label health;
        private readonly VisualElement battleRoleIcon;

        public RankingCardView(VisualElement root, Label name, Label health, VisualElement battleRoleIcon)
        {
            Root = root;
            this.name = name;
            this.health = health;
            this.battleRoleIcon = battleRoleIcon;

            Root.pickingMode = PickingMode.Ignore;
        }

        public VisualElement Root { get; }
        public bool IsValid => Root != null && name != null && health != null && battleRoleIcon != null;
        public PlayerManager TrackedPlayer { get; private set; }

        public void RegisterClickHandler(Action<RankingCardView> handler)
        {
            if (Root == null || handler == null)
            {
                return;
            }

            clickHandler = handler;
            Root.pickingMode = PickingMode.Position;
            if (!clickRegistered)
            {
                Root.RegisterCallback<PointerUpEvent>(OnPointerUp);
                clickRegistered = true;
            }
        }

        public void Bind(RankingCardData data)
        {
            TrackedPlayer = data.Player;
            Root.style.display = data.Visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!data.Visible)
            {
                return;
            }

            SetText(name, data.Name);
            SetText(health, data.Health);
            SetBattleRoleIcon(data.UseAttackBattleRoleIcon);
        }

        private void SetBattleRoleIcon(bool useAttackIcon)
        {
            if (battleRoleIcon == null)
            {
                return;
            }

            battleRoleIcon.RemoveFromClassList(AttackBattleRoleIconClass);
            battleRoleIcon.RemoveFromClassList(DefenseBattleRoleIconClass);
            battleRoleIcon.AddToClassList(useAttackIcon
                ? AttackBattleRoleIconClass
                : DefenseBattleRoleIconClass);
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            clickHandler?.Invoke(this);
            evt.StopPropagation();
        }

        public bool ContainsPanelPoint(Vector2 panelPosition)
        {
            return Root != null &&
                   Root.resolvedStyle.display != DisplayStyle.None &&
                   Root.resolvedStyle.visibility != Visibility.Hidden &&
                   Root.worldBound.Contains(panelPosition);
        }

        private static void SetText(Label label, string value)
        {
            if (label != null)
            {
                label.text = value ?? string.Empty;
            }
        }
    }
}
