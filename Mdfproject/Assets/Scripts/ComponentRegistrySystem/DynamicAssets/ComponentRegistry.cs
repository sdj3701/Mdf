using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using TMPro;


/// <summary>
/// 성능 최적화된 컴포넌트 레지스트리 시스템
/// O(1) 접근 속도, 메모리 효율 최고
/// </summary>
public static class ComponentRegistry
{
    // 타입별 컴포넌트 레지스트리(O(1) 접근)
    private static Dictionary<System.Type, Dictionary<string, Component>> registries =
        new Dictionary<System.Type, Dictionary<string, Component>>();

    // 빠른 접근을 위한 캐시
    private static Dictionary<string, Component> globalCache = new Dictionary<string, Component>();

    // 성능 모니터링
    private static int totalRegistrations = 0;
    private static int totalLookups = 0;
    private static int cacheMisses = 0;

    #region Registration (컴포넌트들이 스스로 등록)

    /// <summary>
    /// 컴포넌트 등록 (각 컴포넌트가 Awake에서 호출)
    /// </summary>
    public static void Register<T>(string id, T component) where T : Component
    {
        if (component == null || string.IsNullOrEmpty(id))
        {
            Debug.LogError($"❌ 잘못된 등록 시도: {id}, {typeof(T)}");
            return;
        }

        var type = typeof(T);

        // 타입별 레지스트리 생성
        if (!registries.ContainsKey(type))
        {
            registries[type] = new Dictionary<string, Component>();
        }

        // 중복 등록 방지
        if (registries[type].ContainsKey(id))
        {
            Debug.LogWarning($"⚠️ 중복 등록: {id} ({type.Name})");
            return;
        }

        // 등록
        registries[type][id] = component;
        globalCache[id] = component;
        totalRegistrations++;
    }

    /// <summary>
    /// 컴포넌트 등록 해제 (OnDestroy에서 호출)
    /// </summary>
    public static void Unregister<T>(string id) where T : Component
    {
        var type = typeof(T);

        if (registries.ContainsKey(type) && registries[type].ContainsKey(id))
        {
            registries[type].Remove(id);
            globalCache.Remove(id);
        }
    }

    #endregion

    #region Lookup (O(1) 성능)

    /// <summary>
    /// 컴포넌트 검색 (O(1) 성능)
    /// </summary>
    public static T Get<T>(string id) where T : Component
    {
        totalLookups++;

        // 1차: 글로벌 캐시에서 검색 (가장 빠름)
        if (globalCache.TryGetValue(id, out Component cachedComponent))
        {
            if (cachedComponent != null && cachedComponent is T)
            {
                return cachedComponent as T;
            }
            else
            {
                // 캐시가 무효하면 제거
                globalCache.Remove(id);
                cacheMisses++;
            }
        }

        // 2차: 타입별 레지스트리에서 검색
        var type = typeof(T);
        if (registries.ContainsKey(type) && registries[type].TryGetValue(id, out Component component))
        {
            if (component != null && component is T)
            {
                globalCache[id] = component; // 캐시 업데이트
                return component as T;
            }
            else
            {
                // 무효한 컴포넌트 제거
                registries[type].Remove(id);
                cacheMisses++;
            }
        }

        Debug.LogWarning($"⚠️ 컴포넌트를 찾을 수 없음: {id} ({type.Name})");
        return null;
    }

    /// <summary>
    /// 컴포넌트 존재 여부 확인 (O(1) 성능)
    /// </summary>
    public static bool Has<T>(string id) where T : Component
    {
        return Get<T>(id) != null;
    }

    /// <summary>
    /// 타입별 모든 컴포넌트 가져오기
    /// </summary>
    public static T[] GetAll<T>() where T : Component
    {
        var type = typeof(T);
        if (!registries.ContainsKey(type))
            return new T[0];

        var result = new List<T>();
        foreach (var component in registries[type].Values)
        {
            if (component != null && component is T)
            {
                result.Add(component as T);
            }
        }

        return result.ToArray();
    }

    #endregion

    #region Utilities

    /// <summary>
    /// 성능 통계 출력
    /// </summary>
    public static void PrintStats()
    {
        Debug.Log("📊 ComponentRegistry 성능 통계:");
        Debug.Log($"   📝 총 등록: {totalRegistrations}개");
        Debug.Log($"   🔍 총 검색: {totalLookups}회");
        Debug.Log($"   ❌ 캐시 미스: {cacheMisses}회");
        Debug.Log($"   💾 캐시 적중률: {(float)(totalLookups - cacheMisses) / totalLookups:P1}");
        Debug.Log($"   🗂️ 등록된 타입: {registries.Keys.Count}개");
    }

    /// <summary>
    /// 모든 레지스트리 초기화
    /// </summary>
    public static void Clear()
    {
        registries.Clear();
        globalCache.Clear();
        totalRegistrations = 0;
        totalLookups = 0;
        cacheMisses = 0;
        Debug.Log("🧹 ComponentRegistry 초기화 완료");
    }

    #endregion
}
