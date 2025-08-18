using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.Tilemaps;

/// <summary>
/// Unity 기본 컴포넌트들을 ComponentRegistry에 자동 등록하는 범용 스크립트
/// Camera, UI, AudioSource 등에 사용
/// </summary>
public class ComponentAutoRegister : MonoBehaviour
{
    [Header("등록 설정")]
    [SerializeField] private string customId; // 커스텀 ID (비어있으면 GameObject 이름 사용)
    [SerializeField] private bool autoDetectComponents = true; // 컴포넌트 자동 감지

    [Header("수동 등록 대상 선택")]
    [SerializeField] private bool registerCamera = false;
    [SerializeField] private bool registerAudioSource = false;
    [SerializeField] private bool registerLight = false;
    [SerializeField] private bool registerTMPText = false;
    [SerializeField] private bool registerButton = false;
    [SerializeField] private bool registerImage = false;

    private string registrationId;

    private void Awake()
    {
        // ID 결정
        registrationId = string.IsNullOrEmpty(customId) ? gameObject.name : customId;

        if (autoDetectComponents)
        {
            AutoRegisterComponents();
        }
        else
        {
            ManualRegisterComponents();
        }
    }

    private void OnDestroy()
    {
        UnregisterComponents();
    }

    /// <summary>
    /// 컴포넌트 자동 감지 및 등록
    /// </summary>
    private void AutoRegisterComponents()
    {

        // Camera 자동 등록
        var camera = GetComponent<Camera>();
        if (camera != null)
        {
            ComponentRegistry.Register<Camera>(registrationId, camera);
        }

        // AudioSource 자동 등록
        var audioSource = GetComponent<AudioSource>();
        if (audioSource != null)
        {
            ComponentRegistry.Register<AudioSource>(registrationId, audioSource);
        }

        // Light 자동 등록
        var light = GetComponent<Light>();
        if (light != null)
        {
            ComponentRegistry.Register<Light>(registrationId, light);
        }

        // TMP_Text 자동 등록
        var tmpText = GetComponent<TMP_Text>();
        if (tmpText != null)
        {
            ComponentRegistry.Register<TMP_Text>(registrationId, tmpText);
        }

        // Button 자동 등록
        var button = GetComponent<Button>();
        if (button != null)
        {
            ComponentRegistry.Register<Button>(registrationId, button);
        }

        // Image 자동 등록
        var image = GetComponent<Image>();
        if (image != null)
        {
            ComponentRegistry.Register<Image>(registrationId, image);
        }
    }

    /// <summary>
    /// 수동 선택된 컴포넌트들만 등록
    /// </summary>
    private void ManualRegisterComponents()
    {
        Debug.Log($"🔧 수동 컴포넌트 등록 시작: {registrationId}");

        if (registerCamera)
        {
            var camera = GetComponent<Camera>();
            if (camera != null)
            {
                ComponentRegistry.Register<Camera>(registrationId, camera);
                Debug.Log($"📷 Camera 수동 등록: {registrationId}");
            }
        }

        if (registerAudioSource)
        {
            var audioSource = GetComponent<AudioSource>();
            if (audioSource != null)
            {
                ComponentRegistry.Register<AudioSource>(registrationId, audioSource);
                Debug.Log($"🔊 AudioSource 수동 등록: {registrationId}");
            }
        }

        if (registerLight)
        {
            var light = GetComponent<Light>();
            if (light != null)
            {
                ComponentRegistry.Register<Light>(registrationId, light);
                Debug.Log($"💡 Light 수동 등록: {registrationId}");
            }
        }

        if (registerTMPText)
        {
            var tmpText = GetComponent<TMP_Text>();
            if (tmpText != null)
            {
                ComponentRegistry.Register<TMP_Text>(registrationId, tmpText);
                Debug.Log($"📝 TMP_Text 수동 등록: {registrationId}");
            }
        }

        if (registerButton)
        {
            var button = GetComponent<Button>();
            if (button != null)
            {
                ComponentRegistry.Register<Button>(registrationId, button);
                Debug.Log($"🔘 Button 수동 등록: {registrationId}");
            }
        }

        if (registerImage)
        {
            var image = GetComponent<Image>();
            if (image != null)
            {
                ComponentRegistry.Register<Image>(registrationId, image);
                Debug.Log($"🖼️ Image 수동 등록: {registrationId}");
            }
        }
    }

    /// <summary>
    /// 등록 해제
    /// </summary>
    private void UnregisterComponents()
    {
        if (GetComponent<Camera>() != null)
            ComponentRegistry.Unregister<Camera>(registrationId);

        if (GetComponent<AudioSource>() != null)
            ComponentRegistry.Unregister<AudioSource>(registrationId);

        if (GetComponent<Light>() != null)
            ComponentRegistry.Unregister<Light>(registrationId);

        if (GetComponent<TMP_Text>() != null)
            ComponentRegistry.Unregister<TMP_Text>(registrationId);

        if (GetComponent<Button>() != null)
            ComponentRegistry.Unregister<Button>(registrationId);

        if (GetComponent<Image>() != null)
            ComponentRegistry.Unregister<Image>(registrationId);

        Debug.Log($"🗑️ 컴포넌트 등록 해제: {registrationId}");
    }

#if UNITY_EDITOR
    [ContextMenu("등록된 컴포넌트 확인")]
    private void DebugRegisteredComponents()
    {
        Debug.Log($"=== {registrationId} 등록 상태 ===");

        CheckComponent<Camera>("Camera");
        CheckComponent<AudioSource>("AudioSource");
        CheckComponent<Light>("Light");
        CheckComponent<TMP_Text>("TMP_Text");
        CheckComponent<Button>("Button");
        CheckComponent<Image>("Image");
    }

    private void CheckComponent<T>(string typeName) where T : Component
    {
        var component = ComponentRegistry.Get<T>(registrationId);
        Debug.Log($"{typeName}: {(component != null ? "✅ 등록됨" : "❌ 없음")}");
    }
#endif
}