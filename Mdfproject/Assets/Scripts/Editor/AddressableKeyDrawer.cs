using UnityEngine;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using System.Linq; // LINQ를 사용하기 위해 추가

// AddressableKeyAttribute가 붙은 모든 프로퍼티는 이 클래스가 그려줍니다.
[CustomPropertyDrawer(typeof(AddressableKeyAttribute))]
public class AddressableKeyDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        // 이 필드가 string 타입이 아니면 그냥 기본 방식으로 그립니다.
        if (property.propertyType != SerializedPropertyType.String)
        {
            EditorGUI.PropertyField(position, property, label);
            return;
        }

        var attribute = this.attribute as AddressableKeyAttribute;
        var assetType = attribute.AssetType;

        EditorGUI.BeginProperty(position, label, property);

        // 1. 현재 저장된 string(어드레서블 키)을 가져옵니다.
        string currentKey = property.stringValue;
        Object currentAsset = null;

        // 2. 키가 있다면, 해당 키로 실제 에셋을 찾아서 오브젝트 필드에 표시합니다.
        if (!string.IsNullOrEmpty(currentKey))
        {
            // [수정된 부분] 올바른 방법으로 AddressableAssetEntry를 찾습니다.
            AddressableAssetEntry entry = FindEntryByAddress(currentKey);
            if (entry != null)
            {
                currentAsset = AssetDatabase.LoadAssetAtPath(entry.AssetPath, assetType);
            }
        }
        
        // 3. ObjectField를 그립니다. 이것이 바로 드래그앤드롭이 가능한 필드입니다.
        Object newAsset = EditorGUI.ObjectField(position, label, currentAsset, assetType, false);

        // 4. 만약 사용자가 새로운 에셋을 드래그앤드롭 했다면,
        if (newAsset != currentAsset)
        {
            if (newAsset == null)
            {
                // 에셋을 제거했다면 키를 비웁니다.
                property.stringValue = "";
            }
            else
            {
                // 새 에셋의 어드레서블 주소를 찾아서 string 프로퍼티에 저장합니다.
                string path = AssetDatabase.GetAssetPath(newAsset);
                string guid = AssetDatabase.AssetPathToGUID(path);
                AddressableAssetEntry entry = AddressableAssetSettingsDefaultObject.GetSettings(false).FindAssetEntry(guid);
                
                if (entry != null)
                {
                    property.stringValue = entry.address;
                }
                else
                {
                    Debug.LogWarning($"'{newAsset.name}' 에셋은 어드레서블로 등록되지 않았습니다. 키를 저장할 수 없습니다.");
                    property.stringValue = "";
                }
            }
        }
        
        EditorGUI.EndProperty();
    }

    /// <summary>
    /// [새로 추가된 헬퍼 함수]
    /// 모든 어드레서블 그룹을 검색하여 주어진 주소(address)와 일치하는 첫 번째 엔트리를 찾습니다.
    /// </summary>
    private AddressableAssetEntry FindEntryByAddress(string address)
    {
        if (string.IsNullOrEmpty(address) || AddressableAssetSettingsDefaultObject.Settings == null)
            return null;

        // LINQ를 사용하여 모든 그룹의 모든 엔트리 중에서 주소가 일치하는 첫 번째 것을 찾습니다.
        return AddressableAssetSettingsDefaultObject.Settings.groups
            .Where(g => g != null) // 혹시 모를 null 그룹 방지
            .SelectMany(g => g.entries) // 모든 그룹의 엔트리들을 하나의 목록으로 펼침
            .FirstOrDefault(e => e.address == address); // 주소가 일치하는 첫 번째 엔트리 반환
    }
}