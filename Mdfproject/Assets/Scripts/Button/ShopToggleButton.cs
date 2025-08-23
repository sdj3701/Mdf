// Assets/Scripts/UI/ShopToggleButton.cs
using UnityEngine;
using UnityEngine.UI;

public class ShopToggleButton : MonoBehaviour
{
    private Button toggleButton;

    void Start()
    {
        toggleButton = GetComponent<Button>();
        toggleButton.onClick.AddListener(OpenShopPanel);
    }

    void Update()
    {
        // 준비 단계에서만 버튼을 누를 수 있도록 제어합니다.
        if (GameManagers.Instance != null)
        {
            toggleButton.interactable = (GameManagers.Instance.GetGameState() == GameManagers.GameState.Prepare);
        }
    }

    private async void OpenShopPanel()
    {
        // UIManagers를 통해 "ShopPanel"을 켭니다.
        // UIManagers가 MainCanvas를 알아서 찾아 그 아래에 생성해 줄 것입니다.
        if (UIManagers.Instance != null)
        {
            await UIManagers.Instance.GetUIElement("UI_Pnl_Shop");
        }
    }
}