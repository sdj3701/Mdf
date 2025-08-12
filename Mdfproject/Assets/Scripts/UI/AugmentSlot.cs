// Assets/Scripts/UI/AugmentSlot.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class AugmentSlot : MonoBehaviour
{
    [Header("UI 요소")]
    public Image iconImage;
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI descriptionText;
    public Button selectButton;

    /// <summary>
    /// 증강 데이터를 받아와 UI에 내용을 채웁니다.
    /// </summary>
    public void Display(AugmentData data)
    {
        if (data == null)
        {
            gameObject.SetActive(false);
            return;
        }

        gameObject.SetActive(true);
        iconImage.sprite = data.icon;
        nameText.text = data.augmentName;
        descriptionText.text = data.description;
    }
}