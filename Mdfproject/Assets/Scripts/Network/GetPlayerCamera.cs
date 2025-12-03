using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cinemachine;
using UnityEngine.SceneManagement;

public class GetPlayerCamera : MonoBehaviour
{
    // [SerializeField] Transform playerCameraRoot;

    // private void Awake()
    // {
    //     GameObject virtualCamera = GameObject.Find("PlayerFollowCamera");
    //     virtualCamera.GetComponent<CinemachineVirtualCamera>().Follow = playerCameraRoot;
    // }
    public Vector3 cameraOffset = new Vector3(4f, 15f, -12f);
    public Vector3 eulerAngles = new Vector3(45f, 0f, 0f);
    private bool applied;

    void OnEnable()
    {
        GameManagers.OnPlayersDataReady += OnPlayersReady;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TryApply();
    }

    void OnDisable()
    {
        GameManagers.OnPlayersDataReady -= OnPlayersReady;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnPlayersReady()
    {
        TryApply();
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        applied = false;
        TryApply();
    }

    void TryApply()
    {
        if (applied) return;
        var gm = GameManagers.Instance;
        if (gm == null) return;
        var local = gm.localPlayer;
        if (local == null) return;
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 targetPos = local.transform.position;
        cam.transform.position = targetPos + cameraOffset;
        if (eulerAngles != Vector3.zero)
        {
            cam.transform.rotation = Quaternion.Euler(eulerAngles);
        }
        else
        {
            cam.transform.LookAt(targetPos);
        }
        applied = true;
    }
}
