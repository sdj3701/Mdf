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
            Debug.LogWarning("[AttackSequenceUIController] UIManagers.Instance가 없습니다");
            return null;
        }

        var uiObject = await UIManagers.Instance.GetUIElement(UI_NAME);
        if (uiObject == null)
        {
            Debug.LogWarning($"[AttackSequenceUIController] '{UI_NAME}' UI를 로드할 수 없습니다");
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
    
    [Header("UI 참조 - 마법 스크롤 (몬스터 상단)")]
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
        
        Debug.Log($"<color=cyan>[AttackSequenceUIController] 초기화 완료. Player {playerManager?.playerId}</color>");
        
        // 현재 공격자 상태면 바로 UI 표시
        if (_playerManager != null && _playerManager.IsAttackerInCurrentBattle)
        {
            Show(true);
        }
    }

    private void CreateSlots()
    {
        // 몬스터 슬롯 생성
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

        // 마법 스크롤 슬롯 생성
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
        if (panelRoot != null)
        {
            panelRoot.SetActive(true);
        }

        // 공격 모드일 때만 슬롯 표시
        if (isAttacking && _playerManager != null)
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
        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
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

        // 첫 번째 활성 슬롯 자동 선택
        if (_selectedSlotIndex < 0 || _selectedSlotIndex >= pool.Count)
        {
            for (int i = 0; i < pool.Count; i++)
            {
                if (!pool[i].IsEmpty)
                {
                    SelectSlot(i);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// 현재 몬스터 풀을 갱신하고 선택 상태를 해제합니다.
    /// 몬스터 소진 시 AttackSequenceManager에서 호출됩니다.
    /// </summary>
    public void RefreshUI()
    {
        if (_playerManager == null) return;
        
        // 모든 슬롯의 수량 업데이트
        for (int i = 0; i < _slots.Count && i < _playerManager.AttackMonsterPool.Count; i++)
        {
            _slots[i].UpdateCount();
        }
        
        // 선택 해제 (UI에서 선택 표시 제거)
        if (_selectedSlotIndex >= 0 && _selectedSlotIndex < _slots.Count)
        {
            _slots[_selectedSlotIndex].SetSelected(false);
        }
        _selectedSlotIndex = -1;
        
        Debug.Log("<color=yellow>[AttackSequenceUIController] UI 갱신 및 선택 해제</color>");
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
        _attackSequenceManager?.SelectMonster(entry);
    }

    private void SelectSlot(int slotIndex)
    {
        // 이전 선택 해제
        if (_selectedSlotIndex >= 0 && _selectedSlotIndex < _slots.Count)
        {
            _slots[_selectedSlotIndex].SetSelected(false);
        }

        // 새 선택
        _selectedSlotIndex = slotIndex;
        if (slotIndex >= 0 && slotIndex < _slots.Count)
        {
            _slots[slotIndex].SetSelected(true);
        }
    }
    #endregion

    #region 이벤트 핸들러
    private void HandleMonsterPoolChanged(int playerId, List<MonsterPoolEntry> pool)
    {
        if (_playerManager == null || playerId != _playerManager.playerId) return;
        
        RefreshSlots(pool);
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

    private void HandleMagicScrollPoolChanged(int playerId, IReadOnlyList<MagicScrollData> scrolls)
    {
        if (_playerManager == null || playerId != _playerManager.playerId) return;
        
        System.Collections.Generic.List<MagicScrollData> scrollList = new System.Collections.Generic.List<MagicScrollData>(scrolls);
        RefreshScrollSlots(scrollList);
    }
    #endregion

    #region 마법 스크롤 슬롯 관리
    private void RefreshScrollSlots(IReadOnlyList<MagicScrollData> scrolls)
    {
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

    /// <summary>
    /// 스크롤 슬롯 클릭 시 호출됨 (MagicScrollSlotUI에서 호출)
    /// </summary>
    public void OnScrollSlotSelected(int slotIndex, MagicScrollData scrollData)
    {
        SelectScrollSlot(slotIndex);
        
        // AttackSequenceManager에 선택 알림
        _attackSequenceManager?.SelectMagicScroll(scrollData);
    }

    private void SelectScrollSlot(int slotIndex)
    {
        // 몬스터 슬롯 선택 해제
        if (_selectedSlotIndex >= 0 && _selectedSlotIndex < _slots.Count)
        {
            _slots[_selectedSlotIndex].SetSelected(false);
        }
        _selectedSlotIndex = -1;

        // 이전 스크롤 슬롯 선택 해제
        if (_selectedScrollSlotIndex >= 0 && _selectedScrollSlotIndex < _scrollSlots.Count)
        {
            _scrollSlots[_selectedScrollSlotIndex].SetSelected(false);
        }

        // 새 스크롤 슬롯 선택
        _selectedScrollSlotIndex = slotIndex;
        if (slotIndex >= 0 && slotIndex < _scrollSlots.Count)
        {
            _scrollSlots[slotIndex].SetSelected(true);
        }
    }
    #endregion
}
