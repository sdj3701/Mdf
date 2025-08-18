// Assets/Scripts/UI/AugmentUIController.cs
using System.Collections.Generic;
using UnityEngine;

public class AugmentUIController : MonoBehaviour
{
    [Header("슬롯 설정")]
    // 인스펙터에서 3개의 증강 슬롯 오브젝트를 연결해주세요.
    public AugmentSlot[] augmentSlots;

    private AugmentManager localPlayerAugmentManager;

    /// <summary>
    /// 패널이 활성화될 때마다 호출되어 최신 증강 정보로 UI를 업데이트합니다.
    /// </summary>
    void OnEnable()
    {
        // 로컬 플레이어의 AugmentManager를 찾습니다.
        if (GameManagers.Instance != null && GameManagers.Instance.localPlayer != null)
        {
            localPlayerAugmentManager = GameManagers.Instance.localPlayer.augmentManager;
            UpdateDisplay();
        }
        else
        {
            Debug.LogError("로컬 플레이어를 찾을 수 없어 증강 UI를 초기화할 수 없습니다!");
            // 문제가 있으면 패널을 비활성화합니다.
            gameObject.SetActive(false); 
        }
    }

    /// <summary>
    /// AugmentManager로부터 제시된 증강 목록을 가져와 슬롯에 표시합니다.
    /// </summary>
    public void UpdateDisplay()
    {
        if (localPlayerAugmentManager == null) return;
        
        // AugmentManager에 제시된 증강 목록을 가져오는 함수가 필요합니다.
        // (이 함수는 AugmentManager.cs에 추가해야 합니다)
        // 예시: List<AugmentData> presentedAugments = localPlayerAugmentManager.GetPresentedAugments();
        // 지금은 임시로 AugmentManager의 private 필드에 접근하는 것처럼 가정하겠습니다.
        // 실제로는 public 함수로 만들어주세요.

        // 임시로 AugmentManager에 GetPresentedAugments()가 있다고 가정하고 진행합니다.
        // 이 부분은 AugmentManager.cs를 수정해야 합니다.
        // public List<AugmentData> GetPresentedAugments() { return presentedAugments; }
        
        // 지금은 임시로 AugmentManager에 직접 접근하지 않고,
        // GameManagers를 통해 간접적으로 데이터를 받아오는 방식을 가정할 수 있습니다.
        // 하지만 가장 좋은 방법은 AugmentManager에 public getter를 만드는 것입니다.
        
        // 이 예제에서는 AugmentManager가 직접 UI를 업데이트하도록 위임하는 방식을 사용하겠습니다.
        // 아래 코드는 AugmentManager에서 직접 호출될 때 사용됩니다.
    }

    /// <summary>
    /// AugmentManager에서 직접 호출하여 슬롯을 설정하는 함수입니다.
    /// </summary>
    // Assets/Scripts/UI/AugmentUIController.cs

    public void SetAugmentChoices(List<AugmentData> choices)
    {
        for (int i = 0; i < augmentSlots.Length; i++)
        {
            if (i < choices.Count)
            {
                augmentSlots[i].Display(choices[i]);

                // 리스너가 중복으로 추가되는 것을 막기 위해 항상 먼저 제거합니다.
                augmentSlots[i].selectButton.onClick.RemoveAllListeners();
                
                // 루프 변수 'i'를 새로운 지역 변수에 복사해야 합니다.
                int choiceIndex = i; 
                
                // ✅ [디버깅 포인트 1] 이 로그가 콘솔에 3번 출력되는지 확인하세요.
                Debug.Log($"슬롯 {choiceIndex}번 버튼에 리스너를 등록합니다.");

                augmentSlots[i].selectButton.onClick.AddListener(() => OnAugmentSelected(choiceIndex));
            }
            else
            {
                augmentSlots[i].Display(null);
            }
        }
    }
    /// <summary>
    /// 플레이어가 증강 버튼을 클릭했을 때 호출됩니다.
    /// </summary>
    private void OnAugmentSelected(int index)
    {
        // ✅ [디버깅 포인트 2] 버튼 클릭 시 이 로그가 출력되는지 확인하세요.
        Debug.Log($"<color=cyan>OnAugmentSelected 함수 호출됨! 선택된 인덱스: {index}</color>");

        if(localPlayerAugmentManager == null) 
        {
            // ✅ [디버깅 포인트 3] 만약 위 로그는 뜨는데 아래 로그가 뜬다면, 매니저 참조가 문제입니다.
            Debug.LogError("localPlayerAugmentManager가 null이라서 증강을 적용할 수 없습니다!");
            return;
        }
        
        localPlayerAugmentManager.SelectAndApplyAugment(index);

        if (UIManagers.Instance != null)
        {
            UIManagers.Instance.ReturnUIElement(this.gameObject.name);
        }
    }
}