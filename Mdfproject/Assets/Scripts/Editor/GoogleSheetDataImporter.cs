using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using UnityEngine.Networking;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using System; // Enum.TryParse를 위해 추가

public class GoogleSheetDataImporter
{
    private const string AppScriptURL = "https://script.google.com/macros/s/AKfycbwrlOnW-wDK9m4w7LOw8RIkyt9XPlFda1-A1ewzq2MGBAOqtZ49t_KEASzK9KXheIEY/exec";

    [MenuItem("Game Data/Import All Data from Google Sheets")]
    public static async void ImportAllData()
    {
        Debug.Log("Google Sheets에서 데이터 임포트를 시작합니다...");
        
        // UnitData 임포트
        await ImportData<UnitData>("Units", (so, item) => {
            // --- 각 필드별로 안전하게 파싱 ---
            
            // 문자열 (String)
            so.unitName = GetString(item, "unitName");
            so.unitIcon = GetString(item, "unitIcon");

            // 숫자 (Number)
            so.cost = GetInt(item, "cost");
            so.baseHealth = GetFloat(item, "baseHealth");
            so.baseAttackDamage = GetFloat(item, "baseAttackDamage");
            so.attackSpeed = GetFloat(item, "attackSpeed");
            so.attackRange = GetFloat(item, "attackRange");
            so.defense = GetFloat(item, "defense");
            so.magicResistance = GetFloat(item, "magicResistance");
            so.blockCount = GetInt(item, "blockCount");
            so.manaOnAttack = GetFloat(item, "manaOnAttack");
            so.manaPerSecond = GetFloat(item, "manaPerSecond");

            // 열거형 (Enum)
            so.unitType = GetEnum<UnitType>(item, "unitType");
            so.damageType = GetEnum<DamageType>(item, "damageType");
            so.manaRegenType = GetEnum<ManaRegenType>(item, "manaRegenType");

            // 에셋 참조 배열 (Asset Reference Array) - [핵심 로직]
            so.prefabsByStarLevel = GetStringArray(item, "prefabsByStarLevel");
            so.skillsByStarLevel = GetStringArray(item, "skillsByStarLevel");
            ApplyProjectileVfxConfigFromSheet(so, GetStringArray(item, "projectilePrefabsByStarLevel"));
            so.EnsureBasicAttackVfxConfigArray();
        });

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("<color=cyan>모든 데이터 임포트 완료!</color>");
    }
    
    // --- 데이터 파싱 헬퍼 함수들 ---
    private static string[] GetStringArray(Dictionary<string, object> item, string key)
    {
        if (!item.ContainsKey(key) || item[key] == null) return new string[0];

        string rawString = item[key].ToString();
        if (string.IsNullOrEmpty(rawString)) return new string[0];

        // "key1;key2;key3" 와 같은 문자열을 ["key1", "key2", "key3"] 배열로 분리
        return rawString.Split(';').ToArray();
    }

    private static void ApplyProjectileVfxConfigFromSheet(UnitData unitData, string[] projectileKeys)
    {
        if (unitData == null)
        {
            return;
        }

        unitData.EnsureProjectileVfxConfig();
        unitData.projectileVfxConfig.projectileKey = projectileKeys == null
            ? string.Empty
            : projectileKeys.FirstOrDefault(key => !string.IsNullOrWhiteSpace(key)) ?? string.Empty;
    }

    private static string GetString(Dictionary<string, object> item, string key)
    {
        return item.ContainsKey(key) && item[key] != null ? item[key].ToString() : "";
    }

    private static int GetInt(Dictionary<string, object> item, string key)
    {
        if (item.ContainsKey(key) && item[key] != null && int.TryParse(item[key].ToString(), out int result))
        {
            return result;
        }
        return 0;
    }

    private static float GetFloat(Dictionary<string, object> item, string key)
    {
        if (item.ContainsKey(key) && item[key] != null && float.TryParse(item[key].ToString(), out float result))
        {
            return result;
        }
        return 0f;
    }
    
    private static T GetEnum<T>(Dictionary<string, object> item, string key) where T : struct, Enum
    {
        if (item.ContainsKey(key) && item[key] != null && Enum.TryParse<T>(item[key].ToString(), true, out T result))
        {
            return result;
        }
        return default; // Enum의 기본값 (보통 0)
    }

    private static T[] GetAssetArrayFromSheet<T>(Dictionary<string, object> item, string key, string assetFolder) where T : UnityEngine.Object
    {
        if (!item.ContainsKey(key) || item[key] == null) return new T[0];

        string rawString = item[key].ToString();
        if (string.IsNullOrEmpty(rawString)) return new T[0];

        // 시트의 "key1;key2;" 와 같은 문자열을 ["key1", "key2", ""] 로 분리
        string[] addressableKeys = rawString.Split(';');

        return addressableKeys.Select(assetKey => {
            if (string.IsNullOrEmpty(assetKey))
            {
                return null; // 빈 문자열은 null 참조로 변환
            }
            // 에셋 키를 기반으로 프로젝트 내에서 실제 에셋을 찾습니다.
            // 참고: 이 경로는 Addressable 주소와 일치할 필요는 없지만, 일관성을 위해 맞추는 것이 좋습니다.
            // 실제로는 Addressable 시스템의 API를 사용하여 경로를 찾는 것이 더 정확합니다.
            // 여기서는 규칙 기반으로 경로를 구성하는 간단한 예시를 사용합니다.
            string path = $"Assets/{assetFolder}/{assetKey}.asset";
            if (typeof(T) == typeof(GameObject)) path = $"Assets/{assetFolder}/{assetKey}.prefab";
            
            return AssetDatabase.LoadAssetAtPath<T>(path);

        }).ToArray();
    }


    // --- 웹 요청 및 기본 파싱 로직 (이전과 동일) ---
    private static async Task ImportData<T>(string sheetName, System.Action<T, Dictionary<string, object>> processAction) where T : ScriptableObject
    {
        string url = $"{AppScriptURL}?sheetName={sheetName}";
        
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Yield();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Error fetching {sheetName}: {request.error}");
                return;
            }

            string json = request.downloadHandler.text;
            var dataList = MiniJSON.Json.Deserialize(json) as List<object>;

            string assetPath = $"Assets/GameData/{sheetName}";
            if (!Directory.Exists(assetPath))
            {
                Directory.CreateDirectory(assetPath);
            }

            foreach (var entry in dataList)
            {
                var item = entry as Dictionary<string, object>;
                if (!item.ContainsKey("documentId") || string.IsNullOrEmpty(item["documentId"].ToString())) continue;
                
                string documentId = item["documentId"].ToString();

                string filePath = $"{assetPath}/{documentId}.asset";
                T so = AssetDatabase.LoadAssetAtPath<T>(filePath);
                if (so == null)
                {
                    so = ScriptableObject.CreateInstance<T>();
                    AssetDatabase.CreateAsset(so, filePath);
                }

                processAction(so, item);

                EditorUtility.SetDirty(so);
            }
        }
    }
}
