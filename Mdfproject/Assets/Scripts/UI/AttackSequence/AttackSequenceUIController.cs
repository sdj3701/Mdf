// Assets/Scripts/UI/AttackSequence/AttackSequenceUIController.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Cysharp.Threading.Tasks;

/// <summary>
/// 공격 시퀀스 UI 패널을 관리하는 컨트롤러.
/// 몬스터 풀 슬롯들을 표시하고 선택 상태를 관리합니다.
/// UIManagers를 통해 동적으로 로드됩니다.
/// </summary>
public class AttackSequenceUIController : MonoBehaviour
{
    #region 정적 헬퍼 (동적 로드)
    private const string UI_NAME = "UI_Pnl_AttackSequence";
    private static AttackSequenceUIController _instance;
    public static AttackSequenceUIController Instance => _instance;

    /// <summary>
    /// UI를 동적으로 로드하고 초기화합니다.
    /// </summary>
    public static async UniTask<AttackSequenceUIController> GetOrCreateAsync(PlayerManager playerManager, AttackSequenceManager attackSequenceManager)
    {
        if (_instance != null)
        {
            _instance.Initialize(playerManager, attackSequenceManager);
            return _instance;
        }

        if (UIManagers.Instance == null)
        {
            // Debug.LogWarning("[AttackSequenceUIController] UIManagers.Instance가 없습니다");
            return null;
        }

        var uiObject = await UIManagers.Instance.GetUIElement(UI_NAME);
        if (uiObject == null)
        {
            // Debug.LogWarning($"[AttackSequenceUIController] '{UI_NAME}' UI를 로드할 수 없습니다");
            return null;
        }

        _instance = uiObject.GetComponent<AttackSequenceUIController>();
        if (_instance != null)
        {
            _instance.Initialize(playerManager, attackSequenceManager);
        }

        return _instance;
    }

    /// <summary>
    /// UI를 반환합니다 (풀로 돌려보냄).
    /// </summary>
    public static void ReturnUI()
    {
        if (_instance != null)
        {
            _instance.Hide();
            UIManagers.Instance?.ReturnUIElement(UI_NAME);
            _instance = null;
        }
    }
    #endregion

    #region UI 요소
    [Header("UI 참조 - 몬스터")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Transform slotContainer;
    [SerializeField] private MonsterSlotUI slotPrefab;

    [Header("UI 참조 - 마법 스크롤")]
    [SerializeField] private Transform scrollSlotContainer;
    [SerializeField] private MagicScrollSlotUI scrollSlotPrefab;
    
    [Header("설정")]
    [SerializeField] private int maxSlots = 9;
    [SerializeField] private int maxScrollSlots = 5;
    #endregion

    #region 필드
    private List<MonsterSlotUI> _slots = new List<MonsterSlotUI>();
    private List<MagicScrollSlotUI> _scrollSlots = new List<MagicScrollSlotUI>();
    private AttackSequenceManager _attackSequenceManager;
    private PlayerManager _playerManager;
    private int _selectedSlotIndex = -1;
    private int _selectedScrollSlotIndex = -1;
    #endregion

    #region 안전 유틸
    private static bool IsPlayerReadable(PlayerManager player)
    {
        if (player == null || player.Object == null || !player.Object.IsValid)
        {
            return false;
        }

        var gm = GameManagers.Instance;
        if (gm != null && gm.Runner != null && player.Runner != null && player.Runner != gm.Runner)
        {
            return false;
        }

        return true;
    }

    private static bool TryGetPlayerIdSafe(PlayerManager player, out int playerId)
    {
        playerId = -1;
        if (!IsPlayerReadable(player))
        {
            return false;
        }

        try
        {
            playerId = player.playerId;
            return true;
        }
        catch (System.InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryGetAttackerStateSafe(PlayerManager player, out bool isAttacker)
    {
        isAttacker = false;
        if (!IsPlayerReadable(player))
        {
            return false;
        }

        try
        {
            isAttacker = player.IsAttackerInCurrentBattle;
            return true;
        }
        catch (System.InvalidOperationException)
        {
            return false;
        }
    }

    private bool TryRebindPlayerReference(string context, bool verboseLog)
    {
        if (IsPlayerReadable(_playerManager))
        {
            return true;
        }

        var candidate = GameManagers.Instance?.localPlayer;
        if (!IsPlayerReadable(candidate))
        {
            return false;
        }

        _playerManager = candidate;
        if (verboseLog)
        {
            // Debug.Log($"[AttackSequenceUIController] _playerManager 재바인딩 완료 ({context})");
        }

        return true;
    }
    #endregion

    #region 초기화
    private void Awake()
    {
        // 슬롯 프리팹으로 슬롯 생성
        CreateSlots();
        
        // 초기에는 숨김
        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }
    }

    private void OnEnable()
    {
        GameEvents.OnMonsterPoolChanged += HandleMonsterPoolChanged;
        GameEvents.OnMagicScrollPoolChanged += HandleMagicScrollPoolChanged;
        GameEvents.OnBattleSequenceStarted += HandleBattleSequenceStarted;
        GameEvents.OnGameStateChanged += HandleGameStateChanged;
    }

    private void OnDisable()
    {
        GameEvents.OnMonsterPoolChanged -= HandleMonsterPoolChanged;
        GameEvents.OnMagicScrollPoolChanged -= HandleMagicScrollPoolChanged;
        GameEvents.OnBattleSequenceStarted -= HandleBattleSequenceStarted;
        GameEvents.OnGameStateChanged -= HandleGameStateChanged;
    }

    public void Initialize(PlayerManager playerManager, AttackSequenceManager attackSequenceManager)
    {
        _playerManager = playerManager;
        _attackSequenceManager = attackSequenceManager;

        TryRebindPlayerReference("Initialize", false);
        string playerIdLabel = TryGetPlayerIdSafe(_playerManager, out int safeId) ? safeId.ToString() : "unspawned";
        // Debug.Log($"<color=cyan>[AttackSequenceUIController] 초기화 완료. Player {playerIdLabel}</color>");

        // 현재 공격자 상태면 바로 UI 표시
        if (TryGetAttackerStateSafe(_playerManager, out bool isAttacker) && isAttacker)
        {
            Show(true);
        }
    }

    private void CreateSlots()
    {
        if (slotContainer != null && slotPrefab != null)
        {
            foreach (var slot in _slots)
            {
                if (slot != null) Destroy(slot.gameObject);
            }
            _slots.Clear();

            for (int i = 0; i < maxSlots; i++)
            {
                MonsterSlotUI slot = Instantiate(slotPrefab, slotContainer);
                slot.Initialize(this, i);
                slot.gameObject.SetActive(false);
                _slots.Add(slot);
            }
        }

        if (scrollSlotContainer != null && scrollSlotPrefab != null)
        {
            foreach (var slot in _scrollSlots)
            {
                if (slot != null) Destroy(slot.gameObject);
            }
            _scrollSlots.Clear();

            for (int i = 0; i < maxScrollSlots; i++)
            {
                MagicScrollSlotUI slot = Instantiate(scrollSlotPrefab, scrollSlotContainer);
                slot.Initialize(this, i);
                slot.gameObject.SetActive(false);
                _scrollSlots.Add(slot);
            }
        }
    }
    #endregion

    #region UI 표시/숨김
    public void Show(bool isAttacking)
    {
        if (GamePrepareUIToolkitController.TryShowAttackSequenceFromLegacy(_playerManager, _attackSequenceManager, isAttacking))
        {
            SetLegacyContentVisibilityOnly(false);
            return;
        }

        if (panelRoot != null)
        {
            panelRoot.SetActive(true);
        }

        // 공격 모드일 때만 슬롯 표시
        if (isAttacking && TryRebindPlayerReference("Show", false))
        {
            RefreshSlots(_playerManager.AttackMonsterPool);
            RefreshScrollSlots(_playerManager.OwnedScrolls);
        }
        else
        {
            HideAllSlots();
            HideAllScrollSlots();
        }
    }

    public void Hide()
    {
        GamePrepareUIToolkitController.TryHideAttackSequenceFromLegacy();

        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }
    }

    public void SetLegacyContentVisibilityOnly(bool visible)
    {
        if (panelRoot != null)
        {
            panelRoot.SetActive(visible);
        }
    }

    private void HideAllSlots()
    {
        foreach (var slot in _slots)
        {
            if (slot != null)
            {
                slot.gameObject.SetActive(false);
            }
        }
    }

    private void HideAllScrollSlots()
    {
        foreach (var slot in _scrollSlots)
        {
            if (slot != null)
            {
                slot.gameObject.SetActive(false);
            }
        }
    }
    #endregion

    #region 슬롯 업데이트
    private void RefreshSlots(List<MonsterPoolEntry> pool)
    {
        if (pool == null)
        {
            HideAllSlots();
            ClearMonsterSelection();
            return;
        }

        for (int i = 0; i < _slots.Count; i++)
        {
            if (i < pool.Count)
            {
                _slots[i].gameObject.SetActive(true);
                _slots[i].UpdateSlot(pool[i]).Forget();
            }
            else
            {
                _slots[i].gameObject.SetActive(false);
            }
        }

        if (_selectedSlotIndex >= pool.Count
            || (_selectedSlotIndex >= 0 && (_selectedSlotIndex >= pool.Count || pool[_selectedSlotIndex] == null || pool[_selectedSlotIndex].IsEmpty)))
        {
            ClearMonsterSelection();
        }

        if (_selectedSlotIndex >= 0)
        {
            SelectSlot(_selectedSlotIndex);
        }
    }

    /// <summary>
    /// 현재 몬스터 풀을 갱신하고 선택 상태를 해제합니다.
    /// 몬스터 소진 시 AttackSequenceManager에서 호출됩니다.
    /// </summary>
    public void RefreshUI()
    {
        if (!TryRebindPlayerReference("RefreshUI", false)) return;

        if (GamePrepareUIToolkitController.TryRefreshAttackSequenceFromLegacy(_playerManager, _attackSequenceManager))
        {
            SetLegacyContentVisibilityOnly(false);
            return;
        }
        
        if (_playerManager.AttackMonsterPool != null)
        {
            for (int i = 0; i < _slots.Count && i < _playerManager.AttackMonsterPool.Count; i++)
            {
                _slots[i].UpdateCount();
            }
        }

        RefreshSlots(_playerManager.AttackMonsterPool);
        RefreshScrollSlots(_playerManager.OwnedScrolls);
        ClearScrollSelection();
        
        // Debug.Log("<color=yellow>[AttackSequenceUIController] UI 갱신 및 선택 해제</color>");
    }



    /// <summary>
    /// 특정 슬롯의 수량만 업데이트합니다A.
    /// </summary>
    public void UpdateSlotCount(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < _slots.Count)
        {
            _slots[slotIndex].UpdateCount();
        }
    }
    #endregion

    #region 슬롯 선택
    public void OnMonsterSlotSelected(int slotIndex, MonsterPoolEntry entry)
    {
        SelectSlot(slotIndex);
        
        // AttackSequenceManager에 선택 알림
        _attackSequenceManager?.SelectMonsterSlot(slotIndex);
    }

    public void SyncMonsterSelectionFromManager(int slotIndex)
    {
        if (GamePrepareUIToolkitController.TrySyncMonsterSelectionFromLegacy(slotIndex))
        {
            SetLegacyContentVisibilityOnly(false);
            return;
        }

        SelectSlot(slotIndex);
    }

    private void SelectSlot(int slotIndex)
    {
        ClearScrollSelection();

        // 이전 선택 해제
        ClearMonsterSelection();

        // 새 선택
        _selectedSlotIndex = slotIndex;
        if (slotIndex >= 0 && slotIndex < _slots.Count)
        {
            _slots[slotIndex].SetSelected(true);
        }
    }

    private void ClearMonsterSelection()
    {
        if (_selectedSlotIndex >= 0 && _selectedSlotIndex < _slots.Count)
        {
            _slots[_selectedSlotIndex].SetSelected(false);
        }
        _selectedSlotIndex = -1;
    }

    private void ClearScrollSelection()
    {
        if (_selectedScrollSlotIndex >= 0 && _selectedScrollSlotIndex < _scrollSlots.Count)
        {
            _scrollSlots[_selectedScrollSlotIndex].SetSelected(false);
        }
        _selectedScrollSlotIndex = -1;
    }
    #endregion

    #region 이벤트 핸들러
    private void HandleMonsterPoolChanged(int playerId, List<MonsterPoolEntry> pool)
    {
        if (!TryRebindPlayerReference("HandleMonsterPoolChanged", false)) return;
        if (!TryGetPlayerIdSafe(_playerManager, out int localPlayerId) || playerId != localPlayerId) return;

        if (GamePrepareUIToolkitController.TryRefreshAttackSequenceFromLegacy(_playerManager, _attackSequenceManager))
        {
            SetLegacyContentVisibilityOnly(false);
            return;
        }

        RefreshSlots(pool);
    }

    private void HandleMagicScrollPoolChanged(int playerId, IReadOnlyList<MagicScrollData> scrolls)
    {
        if (!TryRebindPlayerReference("HandleMagicScrollPoolChanged", false)) return;
        if (!TryGetPlayerIdSafe(_playerManager, out int localPlayerId) || playerId != localPlayerId) return;

        if (GamePrepareUIToolkitController.TryRefreshAttackSequenceFromLegacy(_playerManager, _attackSequenceManager))
        {
            SetLegacyContentVisibilityOnly(false);
            return;
        }

        RefreshScrollSlots(scrolls);
    }

    private void HandleBattleSequenceStarted(bool isAttacking)
    {
        Show(isAttacking);
    }

    private void HandleGameStateChanged(GameManagers.GameState newState)
    {
        if (newState == GameManagers.GameState.Prepare || newState == GameManagers.GameState.GameOver)
        {
            Hide();
        }
    }
    #endregion

    #region 마법 스크롤 슬롯 관리
    private void RefreshScrollSlots(IReadOnlyList<MagicScrollData> scrolls)
    {
        if (scrolls == null)
        {
            HideAllScrollSlots();
            return;
        }

        for (int i = 0; i < _scrollSlots.Count; i++)
        {
            if (i < scrolls.Count && scrolls[i] != null)
            {
                _scrollSlots[i].gameObject.SetActive(true);
                _scrollSlots[i].UpdateSlot(scrolls[i]).Forget();
            }
            else
            {
                _scrollSlots[i].gameObject.SetActive(false);
            }
        }
    }

    public void OnScrollSlotSelected(int slotIndex, MagicScrollData scrollData)
    {
        SelectScrollSlot(slotIndex);
        _attackSequenceManager?.SelectMagicScroll(scrollData);
    }

    public void SyncScrollSelectionFromManager(int slotIndex)
    {
        if (GamePrepareUIToolkitController.TrySyncScrollSelectionFromLegacy(slotIndex))
        {
            SetLegacyContentVisibilityOnly(false);
            return;
        }

        SelectScrollSlot(slotIndex);
    }

    private void SelectScrollSlot(int slotIndex)
    {
        if (_selectedSlotIndex >= 0 && _selectedSlotIndex < _slots.Count)
        {
            _slots[_selectedSlotIndex].SetSelected(false);
        }
        _selectedSlotIndex = -1;

        if (_selectedScrollSlotIndex >= 0 && _selectedScrollSlotIndex < _scrollSlots.Count)
        {
            _scrollSlots[_selectedScrollSlotIndex].SetSelected(false);
        }

        _selectedScrollSlotIndex = slotIndex;
        if (slotIndex >= 0 && slotIndex < _scrollSlots.Count)
        {
            _scrollSlots[slotIndex].SetSelected(true);
        }
    }
    #endregion
}
