// Assets/Scripts/UI/AttackSequence/MonsterSlotUI.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Cysharp.Threading.Tasks;
using MDF.Runtime.Assets;

/// <summary>
/// 공격 시퀀스에서 개별 몬스터 슬롯을 표시하는 UI 컴포넌트.
/// </summary>
public class MonsterSlotUI : MonoBehaviour
{
    private AddressableAssetLease<Sprite> _iconLease;
    private int _iconLoadVersion;
    #region UI 요소
    [Header("UI 참조")]
    [SerializeField] private Image monsterIconImage;
    [SerializeField] private TextMeshProUGUI countText;
    [SerializeField] private Image slotBackgroundImage;
    [SerializeField] private Button slotButton;
    
    [Header("상태 색상")]
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color selectedColor = new Color(1f, 0.8f, 0.2f); // 노란색
    [SerializeField] private Color emptyColor = new Color(0.3f, 0.3f, 0.3f, 0.5f); // 어두운 회색
    #endregion

    #region 필드
    private MonsterPoolEntry _poolEntry;
    private AttackSequenceUIController _controller;
    private int _slotIndex;
    private bool _isSelected;
    #endregion

    #region 초기화
    public void Initialize(AttackSequenceUIController controller, int slotIndex)
    {
        _controller = controller;
        _slotIndex = slotIndex;
        
        // 버튼 클릭 이벤트
        if (slotButton != null)
        {
            slotButton.onClick.RemoveAllListeners();
            slotButton.onClick.AddListener(OnSlotClicked);
        }
    }
    #endregion

    #region 데이터 업데이트
    /// <summary>
    /// 몬스터 풀 데이터로 슬롯 UI를 업데이트합니다.
    /// </summary>
    public async UniTask UpdateSlot(MonsterPoolEntry entry)
    {
        int iconLoadVersion = ++_iconLoadVersion;
        _iconLease?.Dispose();
        _iconLease = null;
        _poolEntry = entry;

        if (entry == null || entry.MonsterData == null)
        {
            SetEmpty();
            return;
        }

        // 수량 표시
        UpdateCount();

        // 아이콘 로드
        await LoadMonsterIcon(entry.MonsterData, iconLoadVersion);

        // 비어있으면 어둡게 처리
        UpdateVisualState();
    }

    /// <summary>
    /// 수량만 업데이트합니다. (소환 후 갱신용)
    /// </summary>
    public void UpdateCount()
    {
        if (_poolEntry == null) return;
        
        if (countText != null)
        {
            countText.text = _poolEntry.IsBoss
                ? $"x{_poolEntry.RemainingCount}"
                : $"흑마력 {Mathf.Max(0, _poolEntry.MonsterData.blackMagicCost)}";
        }

        UpdateVisualState();
    }

    /// <summary>
    /// 선택 상태를 설정합니다.
    /// </summary>
    public void SetSelected(bool selected)
    {
        _isSelected = selected;
        UpdateVisualState();
    }

    private void SetEmpty()
    {
        if (monsterIconImage != null)
        {
            monsterIconImage.sprite = null;
            monsterIconImage.color = emptyColor;
        }
        if (countText != null)
        {
            countText.text = "";
        }
        if (slotBackgroundImage != null)
        {
            slotBackgroundImage.color = emptyColor;
        }
        if (slotButton != null)
        {
            slotButton.interactable = false;
        }
    }

    private void UpdateVisualState()
    {
        if (_poolEntry == null) return;

        bool isEmpty = _poolEntry.IsEmpty;
        bool canAfford = CanAffordCurrentEntry();

        // 배경 색상
        if (slotBackgroundImage != null)
        {
            if (isEmpty)
            {
                slotBackgroundImage.color = emptyColor;
            }
            else if (_isSelected)
            {
                slotBackgroundImage.color = selectedColor;
            }
            else
            {
                slotBackgroundImage.color = normalColor;
            }
        }

        // 아이콘 투명도
        if (monsterIconImage != null)
        {
            Color iconColor = monsterIconImage.color;
            iconColor.a = isEmpty || !canAfford ? 0.3f : 1f;
            monsterIconImage.color = iconColor;
        }

        // 버튼 활성화
        if (slotButton != null)
        {
            slotButton.interactable = !isEmpty && canAfford;
        }

        // 텍스트 투명도
        if (countText != null)
        {
            Color countColor = countText.color;
            countColor.a = isEmpty || !canAfford ? 0.3f : 1f;
            countText.color = countColor;
        }
    }

    private bool CanAffordCurrentEntry()
    {
        if (_poolEntry == null || _poolEntry.MonsterData == null || _poolEntry.IsEmpty)
        {
            return false;
        }

        if (_poolEntry.IsBoss)
        {
            return true;
        }

        PlayerManager localPlayer = GameManagers.Instance?.localPlayer;
        if (localPlayer == null)
        {
            return false;
        }

        try
        {
            return localPlayer.AppliedBlackMagicCurrent >= Mathf.Max(0, _poolEntry.MonsterData.blackMagicCost);
        }
        catch (System.InvalidOperationException)
        {
            return false;
        }
    }

    private async UniTask LoadMonsterIcon(MonsterData monsterData, int iconLoadVersion)
    {
        if (monsterIconImage == null) return;
        
        if (string.IsNullOrEmpty(monsterData.monsterIcon))
        {
            // 아이콘이 없으면 기본 이미지 유지
            return;
        }

        AddressableAssetLease<Sprite> loadedLease =
            await AssetLoader.AcquireAssetAsync<Sprite>(monsterData.monsterIcon);
        if (iconLoadVersion != _iconLoadVersion || monsterIconImage == null)
        {
            loadedLease?.Dispose();
            return;
        }

        _iconLease = loadedLease;
        if (_iconLease?.Asset != null)
        {
            monsterIconImage.sprite = _iconLease.Asset;
            monsterIconImage.color = Color.white;
        }
    }
    #endregion

    #region 이벤트
    private void OnSlotClicked()
    {
        if (_poolEntry == null || _poolEntry.IsEmpty || !CanAffordCurrentEntry()) return;
        _controller?.OnMonsterSlotSelected(_slotIndex, _poolEntry);
    }

    private void OnDestroy()
    {
        _iconLoadVersion++;
        _iconLease?.Dispose();
        _iconLease = null;
    }
    #endregion

    #region 공개 프로퍼티
    public MonsterPoolEntry PoolEntry => _poolEntry;
    public int SlotIndex => _slotIndex;
    #endregion
}
