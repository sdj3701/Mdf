using UnityEngine;

/// <summary>
/// Keeps world-space UI facing the active gameplay camera.
/// </summary>
public class UIBillboard : MonoBehaviour
{
    private Camera overrideCamera;
    private Transform mainCameraTransform;
    private Canvas cachedCanvas;

    private void Awake()
    {
        cachedCanvas = GetComponentInParent<Canvas>();
    }

    private void OnEnable()
    {
        ResolveCameraTransform();
    }

    public void SetCamera(Camera camera)
    {
        overrideCamera = camera;
        mainCameraTransform = camera != null ? camera.transform : null;
        cachedCanvas = GetComponentInParent<Canvas>();

        if (cachedCanvas != null && cachedCanvas.renderMode == RenderMode.WorldSpace)
        {
            cachedCanvas.worldCamera = camera;
        }
    }

    private void LateUpdate()
    {
        if (mainCameraTransform == null)
        {
            ResolveCameraTransform();
        }

        if (mainCameraTransform != null)
        {
            transform.rotation = mainCameraTransform.rotation;
        }
    }

    private void ResolveCameraTransform()
    {
        Camera camera = overrideCamera;
        if (camera == null)
        {
            cachedCanvas = cachedCanvas != null ? cachedCanvas : GetComponentInParent<Canvas>();
            camera = cachedCanvas != null ? cachedCanvas.worldCamera : null;
        }

        if (camera == null)
        {
            camera = Camera.main;
        }

        if (camera == null)
        {
            camera = FindObjectOfType<Camera>();
        }

        mainCameraTransform = camera != null ? camera.transform : null;
    }
}
