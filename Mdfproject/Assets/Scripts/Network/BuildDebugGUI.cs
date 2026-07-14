using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class BuildDebugGUI : MonoBehaviour
{
    public static BuildDebugGUI Instance { get; private set; }

    [SerializeField] private int maxLogMessages = 40;
    [SerializeField] private bool visible = true;
    [SerializeField] private KeyCode toggleKey = KeyCode.F8;
    [SerializeField] private KeyCode copyKey = KeyCode.C;
    [SerializeField] private KeyCode clearKey = KeyCode.K;

    private readonly List<string> _logMessages = new List<string>();
    private readonly Dictionary<string, float> _throttleTimes = new Dictionary<string, float>();
    private GUIStyle _logStyle;
    private GUIStyle _toolbarStyle;
    private string _statusMessage = string.Empty;
    private float _statusMessageUntil;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            ApplyCommandLineVisibility();
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void ApplyCommandLineVisibility()
    {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        var options = MPTestCommandLine.GetOptions();
        if (options.HideBuildDebugGUI)
        {
            visible = false;
        }
#endif
    }

    private void Update()
    {
        if (MdfInput.GetKeyDown(toggleKey))
        {
            visible = !visible;
        }

        bool ctrlOrCmd =
            MdfInput.GetKey(KeyCode.LeftControl) ||
            MdfInput.GetKey(KeyCode.RightControl) ||
            MdfInput.GetKey(KeyCode.LeftCommand) ||
            MdfInput.GetKey(KeyCode.RightCommand);

        if (visible && ctrlOrCmd && MdfInput.GetKeyDown(copyKey))
        {
            CopyLogsToClipboard();
        }

        if (visible && ctrlOrCmd && MdfInput.GetKeyDown(clearKey))
        {
            ClearLogs();
        }
    }

    public void Log(string message)
    {
        string formatted = $"[{DateTime.Now:HH:mm:ss}] {message}";
        _logMessages.Add(formatted);

        while (_logMessages.Count > maxLogMessages)
        {
            _logMessages.RemoveAt(0);
        }
    }

    private void ClearLogs()
    {
        _logMessages.Clear();
        SetStatus("Logs cleared.");
    }

    public void SetVisible(bool value)
    {
        visible = value;
    }

    private void CopyLogsToClipboard()
    {
        var builder = new StringBuilder();
        for (int i = 0; i < _logMessages.Count; i++)
        {
            if (i > 0)
            {
                builder.AppendLine();
            }
            builder.Append(_logMessages[i]);
        }

        GUIUtility.systemCopyBuffer = builder.ToString();
        SetStatus($"Copied {_logMessages.Count} logs to clipboard.");
    }

    private void SetStatus(string message)
    {
        _statusMessage = message;
        _statusMessageUntil = Time.realtimeSinceStartup + 2.5f;
    }

    public static void LogClient(string message)
    {
#if DEVELOPMENT_BUILD
        if (!TryGetClientTag(out string tag))
        {
            return;
        }

        string line = $"{tag} {message}";
        if (Instance != null)
        {
            Instance.Log(line);
        }

        Debug.Log(line);
#endif
    }

    public static void LogClientThrottled(string key, string message, float minIntervalSeconds = 1f)
    {
#if DEVELOPMENT_BUILD
        if (!TryGetClientTag(out _))
        {
            return;
        }

        if (Instance == null)
        {
            LogClient(message);
            return;
        }

        float now = Time.realtimeSinceStartup;
        if (Instance._throttleTimes.TryGetValue(key, out float last) && now - last < minIntervalSeconds)
        {
            return;
        }

        Instance._throttleTimes[key] = now;
        LogClient(message);
#endif
    }

    private static bool TryGetClientTag(out string tag)
    {
        tag = "[CLIENT]";
        var gm = GameManagers.Instance;

        var runner = gm != null ? gm.Runner : null;
        if ((runner == null || !runner.IsRunning) && NetworkManager.Instance != null)
        {
            runner = NetworkManager.Instance._runner;
        }

        if (runner == null || !runner.IsRunning || runner.IsServer)
        {
            return false;
        }

        string round = "?";
        string state = "?";
        string local = "null";

        try
        {
            if (gm != null)
            {
                round = gm.currentRound.ToString();
            }
        }
        catch (Exception)
        {
        }

        try
        {
            if (gm != null)
            {
                state = gm.currentState.ToString();
            }
        }
        catch (Exception)
        {
        }

        try
        {
            if (gm != null && gm.localPlayer != null && gm.localPlayer.Object != null && gm.localPlayer.Object.IsValid)
            {
                local = gm.localPlayer.playerId.ToString();
            }
        }
        catch (Exception)
        {
            local = "?";
        }

        tag = $"[CLIENT r={round} s={state} lp={local}]";
        return true;
    }

    private void OnGUI()
    {
#if DEVELOPMENT_BUILD
        if (!visible)
        {
            return;
        }

        if (_logStyle == null)
        {
            _logStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                richText = true
            };
            _logStyle.normal.textColor = Color.white;
        }

        if (_toolbarStyle == null)
        {
            _toolbarStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                richText = false
            };
        }

        Rect area = new Rect(10, 180, Screen.width - 20, Screen.height - 190);
        GUILayout.BeginArea(area);
        GUI.Box(new Rect(0, 0, area.width, area.height), "");

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Copy Logs (Ctrl/Cmd+C)", _toolbarStyle, GUILayout.Height(28)))
        {
            CopyLogsToClipboard();
        }
        if (GUILayout.Button("Clear Logs (Ctrl/Cmd+K)", _toolbarStyle, GUILayout.Width(210), GUILayout.Height(28)))
        {
            ClearLogs();
        }
        GUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(_statusMessage) && Time.realtimeSinceStartup <= _statusMessageUntil)
        {
            GUILayout.Label(_statusMessage, _logStyle);
        }

        foreach (string message in _logMessages)
        {
            GUILayout.Label(message, _logStyle);
        }

        GUILayout.EndArea();
#endif
    }
}
