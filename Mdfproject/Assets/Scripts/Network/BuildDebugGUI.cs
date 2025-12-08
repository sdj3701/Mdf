using UnityEngine;
using System.Collections.Generic;

public class BuildDebugGUI : MonoBehaviour
{
    // 싱글톤 인스턴스
    public static BuildDebugGUI Instance { get; private set; }

    // 로그 메시지를 저장할 리스트
    private List<string> logMessages = new List<string>();
    // 화면에 표시할 최대 로그 수
    private int maxLogMessages = 20;

    // GUI 스타일을 미리 설정하여 성능 저하 방지
    private GUIStyle logStyle;
    private bool styleInitialized = false;

    void Awake()
    {
        // 싱글톤 패턴 구현
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // 씬이 바뀌어도 파괴되지 않도록 설정
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // 다른 스크립트에서 로그를 추가할 때 호출할 함수
    public void Log(string message)
    {
        // 타임스탬프와 함께 메시지 추가
        string formattedMessage = $"[{System.DateTime.Now:HH:mm:ss}] {message}";
        logMessages.Add(formattedMessage);

        // 최대 로그 수를 초과하면 가장 오래된 로그 삭제
        while (logMessages.Count > maxLogMessages)
        {
            logMessages.RemoveAt(0);
        }
    }

    // GUI를 그리는 함수 (매 프레임 여러 번 호출될 수 있음)
    void OnGUI()
    {
        // 개발 빌드 또는 유니티 에디터에서만 GUI를 표시하도록 제한
#if DEVELOPMENT_BUILD
        // 스타일 초기화 (첫 OnGUI 호출 시 한 번만 실행)
        if (!styleInitialized)
        {
            logStyle = new GUIStyle(GUI.skin.label);
            logStyle.fontSize = 20; // 폰트 크기 조절
            logStyle.normal.textColor = Color.white; // 폰트 색상
            styleInitialized = true;
        }

        // 화면 좌측 상단에 로그를 표시할 영역 설정
        // new Rect(x, y, width, height)
        Rect logArea = new Rect(10, 200, Screen.width - 20, Screen.height - 20);

        // GUI 영역 시작
        GUILayout.BeginArea(logArea);

        // 배경을 반투명 검은색으로 그려서 가독성 높이기
        GUI.Box(new Rect(0, 0, logArea.width, logArea.height), "");

        // 저장된 모든 로그 메시지를 화면에 출력
        foreach (string message in logMessages)
        {
            GUILayout.Label(message, logStyle);
        }

        // GUI 영역 종료
        GUILayout.EndArea();
#endif
    }
}