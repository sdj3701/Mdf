using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;

public class UIManagers : MonoBehaviour
{
    public static UIManagers Instance = null;

    // 인스펙터에서 미리 할당할 UI 프리팹 리스트
    public List<GameObject> UILists;
    // UI 이름을 키(Key)로, UI 풀을 값(Value)으로 가지는 딕셔너리
    private Dictionary<string, UIPool> uiPools;

    private void Awake()
    {
        // 싱글톤 패턴 구현
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(this.gameObject); // 씬이 바뀌어도 파괴되지 않음
        }
        else
        {
            Destroy(this.gameObject);
        }

        // 딕셔너리 초기화
        uiPools = new Dictionary<string, UIPool>();

        // 인스펙터에 할당된 UI 리스트를 기반으로 UI 풀 딕셔너리 생성
        foreach (GameObject ui in UILists)
        {
            if (ui != null)
            {
                // UI 풀을 생성하고 딕셔너리에 추가
                uiPools.Add(ui.name, new UIPool(ui));
            }
        }
    }

    /// <summary>
    /// 지정된 이름의 UI 요소를 풀에서 가져와 활성화합니다.
    /// 풀에 없으면 Addressables를 통해 동적으로 로드합니다.
    /// </summary>
    /// <param name="uiName">가져올 UI의 이름</param>
    /// <returns>활성화된 UI 게임 오브젝트</returns>
    public UniTask<GameObject> GetUIElement(string uiName)
    {
        // 요청한 UI가 이미 풀에 등록되어 있는지 확인
        if (uiPools.ContainsKey(uiName))
        {
            return uiPools[uiName].GetObject(uiName);
        }
        else
        {
            // 풀에 없다면, Addressables를 통해 로드할 수 있다고 가정하고 새로운 풀을 생성
            Debug.LogWarning($"UI 요소 '{uiName}'가 풀에 없습니다. Addressables를 통해 로드하고 새로 등록합니다.");
            UIPool ui = new UIPool(null, uiName); // 동적 로드를 위한 UIPool 생성
            uiPools.Add(uiName, ui); // 새로 만든 풀을 딕셔너리에 추가
            return ui.GetObject(uiName);
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