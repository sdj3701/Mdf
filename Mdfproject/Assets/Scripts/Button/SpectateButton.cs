using UnityEngine;

public class SpectateButton : BaseButton
{
    [Tooltip("반환할 UI 패널의 이름 (Addressable Key)")]
    public string panelName = "UI_Pnl_Defeat";

    public override void OnClick()
    {
        Debug.Log("관전하기 버튼 클릭됨");
        if (!string.IsNullOrEmpty(panelName))
        {
            UIManagers.Instance.ReturnUIElement(panelName);
        }
        else
        {
            Debug.LogError("반환할 UI 패널 이름이 지정되지 않았습니다.");
        }
    }
}
