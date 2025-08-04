using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;


public class Player : MonoBehaviour
{
    public Button button;
    [Header("설정")]
    public int imageSize = 512;
    public Image targetImage; // 결과를 표시할 UI Image
    TMP_Text text;

    void Awake()
    {
        text = button.GetComponentInChildren<TMP_Text>();
    }

    public void TestCheck()
    {
        Create2DImage(text.text);
    }

    private void Create2DImage(string text, Color bgColor = default, Color textColor = default, int fontSize = 150)
    {
        if (bgColor == default) bgColor = Color.white;
        if (textColor == default) textColor = Color.black;

        Texture2D texture = GenerateTextTexture(text, bgColor, textColor, fontSize);

        if (targetImage != null && texture != null)
        {
            targetImage.sprite = Sprite.Create(texture, new Rect(0, 0, imageSize, imageSize), Vector2.one * 0.5f);
            Debug.Log($"✅ 텍스트 이미지 생성: '{text}'");
        }
    }
    
    Texture2D GenerateTextTexture(string text, Color backgroundColor, Color textColor, int fontSize)
    {
        // 임시 UI 생성
        GameObject canvasObj = new GameObject("TempCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        
        // 캔버스 설정
        RectTransform canvasRect = canvas.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(imageSize, imageSize);
        canvasRect.position = Vector3.forward * 10;
        
        // 배경 생성
        GameObject bgObj = new GameObject("Background");
        bgObj.transform.SetParent(canvas.transform, false);
        Image bgImage = bgObj.AddComponent<Image>();
        bgImage.color = backgroundColor;
        
        RectTransform bgRect = bgObj.GetComponent<RectTransform>();
        bgRect.sizeDelta = new Vector2(imageSize, imageSize);
        
        // 텍스트 생성
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(bgObj.transform, false);
        TextMeshProUGUI tmpText = textObj.AddComponent<TextMeshProUGUI>();
        
        tmpText.text = text;
        tmpText.fontSize = fontSize;
        tmpText.color = textColor;
        tmpText.alignment = TextAlignmentOptions.Center;
        tmpText.enableWordWrapping = true;
        
        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.sizeDelta = new Vector2(imageSize - 20, imageSize - 20);
        
        // 렌더링
        GameObject cameraObj = new GameObject("TempCamera");
        Camera renderCam = cameraObj.AddComponent<Camera>();
        renderCam.clearFlags = CameraClearFlags.SolidColor;
        renderCam.backgroundColor = Color.clear;
        renderCam.orthographic = true;
        renderCam.orthographicSize = imageSize / 2f;
        renderCam.transform.position = Vector3.zero;
        renderCam.transform.LookAt(canvasRect.position);
        
        RenderTexture renderTexture = new RenderTexture(imageSize, imageSize, 24);
        renderCam.targetTexture = renderTexture;
        renderCam.Render();
        
        RenderTexture.active = renderTexture;
        Texture2D result = new Texture2D(imageSize, imageSize);
        result.ReadPixels(new Rect(0, 0, imageSize, imageSize), 0, 0);
        result.Apply();
        
        // 정리
        RenderTexture.active = null;
        renderCam.targetTexture = null;
        renderTexture.Release();
        
        DestroyImmediate(canvasObj);
        DestroyImmediate(cameraObj);
        
        return result;
    }


}
