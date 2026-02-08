// Assets/Scripts/UI/AttackSequence/MagicScrollSlotUI.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Cysharp.Threading.Tasks;

/// <summary>
/// 공격 시퀀스에서 개별 마법 스크롤 슬롯을 표시하는 UI 컴포넌트.
/// MonsterSlotUI와 유사한 구조를 사용합니다.
/// </summary>
public class MagicScrollSlotUI : MonoBehaviour
{
    #region UI 요소
    [Header("UI 참조")]
    [SerializeField] private Image scrollIconImage;
    [SerializeField] private TextMeshProUGUI scrollNameText;
    [SerializeField] private Image slotBackgroundImage;
    [SerializeField] private Button slotButton;
    
    [Header("상태 색상")]
    [SerializeField] private Color normalColor = new Color(0.4f, 0.2f, 0.8f); // 보라색
    [SerializeField] private Color selectedColor = new Color(0.8f, 0.4f, 1f); // 밝은 보라색
    [SerializeField] private Color emptyColor = new Color(0.3f, 0.3f, 0.3f, 0.5f); // 어두운 회색
    #endregion

    #region 필드
    private MagicScrollData _scrollData;
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
    /// 마법 스크롤 데이터로 슬롯 UI를 업데이트합니다.
    /// </summary>
    public async UniTask UpdateSlot(MagicScrollData scrollData)
    {
        _scrollData = scrollData;

        if (scrollData == null)
        {
            SetEmpty();
            return;
        }

        // 이름 표시
        if (scrollNameText != null)
        {
            scrollNameText.text = scrollData.scrollName;
        }

        // 아이콘 로드
        await LoadScrollIcon(scrollData);

        // 상태 업데이트
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
        if (scrollIconImage != null)
        {
            scrollIconImage.sprite = null;
            scrollIconImage.color = emptyColor;
        }
        if (scrollNameText != null)
        {
            scrollNameText.text = "";
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
        bool isEmpty = _scrollData == null;

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
        if (scrollIconImage != null)
        {
            Color iconColor = scrollIconImage.color;
            iconColor.a = isEmpty ? 0.3f : 1f;
            scrollIconImage.color = iconColor;
        }

        // 버튼 활성화
        if (slotButton != null)
        {
            slotButton.interactable = !isEmpty;
        }

        // 텍스트 투명도
        if (scrollNameText != null)
        {
            Color textColor = scrollNameText.color;
            textColor.a = isEmpty ? 0.3f : 1f;
            scrollNameText.color = textColor;
        }
    }

    private async UniTask LoadScrollIcon(MagicScrollData scrollData)
    {
        if (scrollIconImage == null) return;
        
        if (scrollData.icon != null)
        {
            scrollIconImage.sprite = scrollData.icon;
            scrollIconImage.color = Color.white;
            return;
        }

        // 아이콘 참조가 없으면 기본 이미지 유지
        scrollIconImage.color = normalColor;
        await UniTask.CompletedTask;
    }
    #endregion

    #region 이벤트
    private void OnSlotClicked()
    {
        if (_scrollData == null) return;
        _controller?.OnScrollSlotSelected(_slotIndex, _scrollData);
    }
    #endregion

    #region 공개 프로퍼티
    public MagicScrollData ScrollData => _scrollData;
    public int SlotIndex => _slotIndex;
    #endregion
}
