using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class UIPool
{
    // Addressables 로드를 위한 매니저
    private AddressablesManager addressablesManager = new AddressablesManager();

    // 생성된 UI 객체들을 저장하는 정적(static) 딕셔너리 (모든 풀이 공유)
    private static Dictionary<string, GameObject> pool = new Dictionary<string, GameObject>();
    private GameObject prefab; // 원본 프리팹

    /// <summary>
    /// UIPool 생성자
    /// </summary>
    /// <param name="prefab">미리 로드할 UI 프리팹</param>
    /// <param name="uiname">동적으로 로드할 UI 이름</param>
    public UIPool(GameObject prefab, string uiname = null)
    {
        // 1. 미리 로드하는 경우 (프리팹이 주어짐)
        if (uiname == null && prefab != null)
        {
            this.prefab = prefab;
            string name = prefab.name;

            // 풀에 아직 등록되지 않았다면 추가
            if (!pool.ContainsKey(name))
            {
                pool.Add(name, prefab);
            }
            // 이미 있다면 경고 메시지 후 덮어쓰기
            else
            {
                Debug.LogWarning($"⚠️ UI '{name}'가 이미 등록되어 있습니다. 덮어쓰기 합니다.");
                pool[name] = prefab;
            }
        }
        // 2. 동적으로 로드할 준비만 하는 경우 (이름만 주어짐)
        else if (prefab == null && uiname != null)
        {
            // 이 경우에는 GetObject가 호출될 때 로드되므로 지금은 아무것도 하지 않음
        }
        else
        {
            Debug.LogError("UIPool: 프리팹과 UI이름이 모두 없습니다.");
        }
    }

    /// <summary>
    /// 풀에서 UI 객체를 가져옵니다. 없으면 새로 로드합니다.
    /// </summary>
    public UniTask<GameObject> GetObject(string name)
    {
        // 풀에 이미 있는지 확인
        if (pool.ContainsKey(name))
        {
            GameObject obj = pool[name];
            obj.SetActive(true); // 활성화 상태로 전환
            return UniTask.FromResult(obj); // 즉시 반환
        }
        // 풀에 없다면 Addressables로 로드
        else
        {
            return AddGetObject(name);
        }
    }

    /// <summary>
    /// Addressables를 통해 UI를 새로 로드하고 풀에 추가한 뒤 반환합니다.
    /// </summary>
    private async UniTask<GameObject> AddGetObject(string name, GameObject currentposition = null)
    {
        // Addressables를 통해 비동기 로드
        GameObject newInstance = await addressablesManager.LoadObject(name);
        if (newInstance != null)
        {
            newInstance.name = name; // 이름 정리
            pool[name] = newInstance; // 풀에 새로 추가
            newInstance.SetActive(true); // 활성화
        }
        return newInstance;
    }

    /// <summary>
    /// 사용이 끝난 UI 객체를 비활성화하여 풀에 반환합니다.
    /// </summary>
    public void ReturnObject(string name)
    {
        // 씬에서 이름이 일치하고 활성화된 모든 인스턴스를 찾아 비활성화
        GameObject[] allObjects = Object.FindObjectsOfType<GameObject>();
        bool foundAny = false;

        foreach (GameObject obj in allObjects)
        {
            // "(Clone)" 접미사를 제거하고 이름 비교
            string cleanName = obj.name.Replace("(Clone)", "").Trim();
            if (cleanName == name && obj.activeInHierarchy)
            {
                obj.SetActive(false); // 비활성화하여 풀에 반납하는 효과
                foundAny = true;
            }
        }

        if (!foundAny)
        {
            Debug.LogWarning($"⚠️ 활성화된 '{name}' 인스턴스를 찾을 수 없음");
        }
    }
}