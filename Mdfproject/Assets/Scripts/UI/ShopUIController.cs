// Assets/Scripts/UI/ShopUIController.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class ShopUIController : MonoBehaviour
{
    [Header("UI 요소 연결")]
    public ShopSlot[] shopSlots;
    public Button rerollButton;
    public Button closeButton;
    public TextMeshProUGUI rerollCostText;

    private ShopManager localPlayerShopManager;
    private PlayerManager localPlayerManager;

    void OnEnable()
    {
        if (GameManagers.Instance != null && GameManagers.Instance.localPlayer != null)
        {
            localPlayerManager = GameManagers.Instance.localPlayer;
            localPlayerShopManager = localPlayerManager.shopManager;
            SetupUI();
        }
        else
        {
            Debug.LogError("로컬 플레이어를 찾을 수 없어 상점 UI를 초기화할 수 없습니다!");
            gameObject.SetActive(false);
        }
    }

    private void SetupUI()
    {
        if (localPlayerShopManager == null) return;

        rerollButton.onClick.RemoveAllListeners();
        rerollButton.onClick.AddListener(OnRerollButtonClick);

        closeButton.onClick.RemoveAllListeners();
        closeButton.onClick.AddListener(OnCloseButtonClick);

        UpdateShopSlots();
        UpdateInfoText();
    }
    
    void Update()
    {
        if (GameManagers.Instance != null)
        {
            bool isPreparePhase = (GameManagers.Instance.GetGameState() == GameManagers.GameState.Prepare);
            rerollButton.interactable = isPreparePhase;
        }
    }

    public void UpdateShopSlots()
    {
        if (localPlayerShopManager == null) return;

        List<UnitData> currentItems = localPlayerShopManager.GetCurrentShopItems();
        for (int i = 0; i < shopSlots.Length; i++)
        {
            if (i < currentItems.Count)
            {
                shopSlots[i].DisplayUnit(currentItems[i]);
            }
            else
            {
                shopSlots[i].DisplayUnit(null);
            }
        }
    }

    public void UpdateInfoText()
    {
        if (localPlayerShopManager != null)
        {
            rerollCostText.text = $"{localPlayerShopManager.GetRerollCost()} G";
        }
    }

    private void OnRerollButtonClick()
    {
        if (localPlayerShopManager != null)
        {
            localPlayerShopManager.Reroll();
            UpdateShopSlots();
            UpdateInfoText(); 
        }
    }

    private void OnCloseButtonClick()
    {
        if (UIManagers.Instance != null)
        {
            UIManagers.Instance.ReturnUIElement("UI_Pnl_Shop");
        }
    }
}