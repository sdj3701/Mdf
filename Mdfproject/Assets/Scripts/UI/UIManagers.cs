// Assets/Scripts/UI/UIManagers.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;

public class UIManagers : MonoBehaviour
{
    public static UIManagers Instance = null;

    public List<GameObject> UILists;
    private Dictionary<string, UIPool> uiPools;

    // ✅ [핵심 추가] 게임 씬의 메인 캔버스를 저장할 변수
    private Canvas mainCanvas;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(this.gameObject);
        }
        else
        {
            Destroy(this.gameObject);
        }

        uiPools = new Dictionary<string, UIPool>();

        foreach (GameObject ui in UILists)
        {
            if (ui != null)
            {
                uiPools.Add(ui.name, new UIPool(ui));
            }
        }
    }

    /// <summary>
    /// 지정된 이름의 UI 요소를 자동으로 찾은 MainCanvas 아래에 활성화합니다.
    /// </summary>
    /// <param name="uiName">가져올 UI의 이름 (어드레서블 주소)</param>
    /// <returns>활성화된 UI 게임 오브젝트</returns>
    public UniTask<GameObject> GetUIElement(string uiName)
    {
        // ✅ [핵심 수정] MainCanvas를 찾거나 캐시된 값을 사용하는 로직
        // 씬이 바뀌면 mainCanvas 참조가 사라지므로, null일 때마다 다시 찾습니다.
        if (mainCanvas == null)
        {
            mainCanvas = FindObjectOfType<Canvas>();
            if (mainCanvas == null)
            {
                Debug.LogError("MainCanvas를 찾을 수 없습니다! UI를 표시할 수 없습니다.");
                return UniTask.FromResult<GameObject>(null); // 실패 시 null 반환
            }
        }

        // 이제 찾은 mainCanvas.transform 정보를 내부적으로 사용합니다.
        if (uiPools.ContainsKey(uiName))
        {
            return uiPools[uiName].GetObject(uiName, mainCanvas.transform);
        }
        else
        {
            Debug.LogWarning($"UI 요소 '{uiName}'가 풀에 없습니다. Addressables를 통해 로드하고 새로 등록합니다.");
            UIPool ui = new UIPool(null, uiName);
            uiPools.Add(uiName, ui);
            return ui.GetObject(uiName, mainCanvas.transform);
        }
    }

    /// <summary>
    /// 사용이 끝난 UI 요소를 비활성화하여 풀에 반환합니다.
    /// </summary>
    /// <param name="uiName">반환할 UI의 이름</param>
    public void ReturnUIElement(string uiName)
    {
        if (uiPools.ContainsKey(uiName))
        {
            uiPools[uiName].ReturnObject(uiName);
        }
        else
        {
            Debug.LogWarning($"UI 요소 '{uiName}'가 풀에 등록되어 있지 않습니다.");
        }
    }
}