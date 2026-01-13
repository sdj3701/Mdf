// Assets/Scripts/UI/AttackSequence/AttackSequenceUIController.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Cysharp.Threading.Tasks;

/// <summary>
/// 공격 시퀀스 UI 패널을 관리하는 컨트롤러.
/// 몬스터 풀 슬롯들을 표시하고 선택 상태를 관리합니다.
/// </summary>
public class AttackSequenceUIController : MonoBehaviour
{
    #region UI 요소
    [Header("UI 참조")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Transform slotContainer;
    [SerializeField] private MonsterSlotUI slotPrefab;
    
    [Header("설정")]
    [SerializeField] private int maxSlots = 9;
    #endregion

    #region 필드
    private List<MonsterSlotUI> _slots = new List<MonsterSlotUI>();
    private AttackSequenceManager _attackSequenceManager;
    private PlayerManager _playerManager;
    private int _selectedSlotIndex = -1;
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
        GameEvents.OnBattleSequenceStarted += HandleBattleSequenceStarted;
        GameEvents.OnGameStateChanged += HandleGameStateChanged;
    }

    private void OnDisable()
    {
        GameEvents.OnMonsterPoolChanged -= HandleMonsterPoolChanged;
        GameEvents.OnBattleSequenceStarted -= HandleBattleSequenceStarted;
        GameEvents.OnGameStateChanged -= HandleGameStateChanged;
    }

    public void Initialize(PlayerManager playerManager, AttackSequenceManager attackSequenceManager)
    {
        _playerManager = playerManager;
        _attackSequenceManager = attackSequenceManager;
        
        Debug.Log($"<color=cyan>[AttackSequenceUIController] 초기화 완료. Player {playerManager?.playerId}</color>");
    }

    private void CreateSlots()
    {
        if (slotContainer == null || slotPrefab == null)
        {
            Debug.LogWarning("[AttackSequenceUIController] slotContainer 또는 slotPrefab이 null입니다");
            return;
        }

        // 기존 슬롯 정리
        foreach (var slot in _slots)
        {
            if (slot != null) Destroy(slot.gameObject);
        }
        _slots.Clear();

        // 새 슬롯 생성
        for (int i = 0; i < maxSlots; i++)
        {
            MonsterSlotUI slot = Instantiate(slotPrefab, slotContainer);
            slot.Initialize(this, i);
            slot.gameObject.SetActive(false);
            _slots.Add(slot);
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
        }
        else
        {
            HideAllSlots();
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
    #endregion
}
