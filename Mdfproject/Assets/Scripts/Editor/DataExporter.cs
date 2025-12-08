using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;
using System.Reflection;
using System.Linq; // .Cast<object>()와 .Select()를 사용하기 위해 이 네임스페이스를 추가합니다.

public class DataExporter
{
    [MenuItem("Game Data/Export All SO to CSV")]
    public static void ExportAllDataToCSV()
    {
        // UnitData를 CSV로 내보냅니다.
        ExportSOToCSV<UnitData>("Units");
        
        // MonsterData도 같은 방식으로 추가할 수 있습니다.
        // ExportSOToCSV<MonsterData>("Monsters");

        Debug.Log("<color=cyan>모든 ScriptableObject 데이터를 CSV로 내보내기 완료!</color> 프로젝트 최상위 폴더를 확인하세요.");
    }

    private static void ExportSOToCSV<T>(string sheetName) where T : ScriptableObject
    {
        // 1. 프로젝트 내의 모든 T 타입 ScriptableObject를 찾습니다.
        string[] guids = AssetDatabase.FindAssets("t:" + typeof(T).Name);
        if (guids.Length == 0)
        {
            Debug.LogWarning($"{typeof(T).Name} 타입의 ScriptableObject를 찾을 수 없습니다.");
            return;
        }

        // CSV 파일에 쓸 내용을 담을 StringBuilder를 생성합니다.
        StringBuilder sb = new StringBuilder();

        // 2. 헤더 행 생성 (리플렉션을 사용하여 SO의 모든 public 필드를 가져옵니다)
        FieldInfo[] fields = typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance);
        sb.Append("documentId"); // 첫 열은 파일 이름으로 고정
        foreach (var field in fields)
        {
            sb.Append("," + field.Name);
        }
        sb.AppendLine(); // 줄바꿈

        // 3. 데이터 행 생성
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            T so = AssetDatabase.LoadAssetAtPath<T>(path);

            // 첫 열에는 SO 파일의 이름을 documentId로 넣습니다.
            sb.Append(Path.GetFileNameWithoutExtension(path));

            foreach (var field in fields)
            {
                sb.Append(",");
                object value = field.GetValue(so);

                // 배열/리스트 타입인 경우 쉼표로 구분된 문자열로 변환합니다.
                if (value is System.Collections.IEnumerable && !(value is string))
                {
                    // .Cast<object>() 와 .Select() 모두 LINQ 기능입니다.
                    // [수정된 부분] 각 요소(o)가 null인지 확인하는 로직 추가
                    var stringArray = (value as System.Collections.IEnumerable).Cast<object>()
                                            .Select(o => o != null ? o.ToString() : ""); // <--- 여기가 수정되었습니다!
                                            
                    sb.Append(string.Join(";", stringArray));
                }
                else
                {
                    // 값에 쉼표가 포함될 수 있으므로 큰따옴표로 감싸줍니다.
                    string stringValue = (value != null) ? value.ToString() : "";
                    // 큰따옴표 자체를 이스케이프 처리
                    stringValue = stringValue.Replace("\"", "\"\"");
                    sb.Append($"\"{stringValue}\"");
                }
            }
            sb.AppendLine();
        }

        // 4. CSV 파일로 저장
        string filePath = Path.Combine(Application.dataPath, "..", $"{sheetName}.csv"); // Unity 프로젝트 폴더 최상위에 저장
        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
    }
}