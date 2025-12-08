using UnityEngine;

/// <summary>
/// 이 컴포넌트가 부착된 게임 오브젝트가 항상 메인 카메라를 바라보도록 만듭니다.
/// World Space UI가 카메라 각도와 상관없이 항상 정면으로 보이게 할 때 사용합니다.
/// </summary>
public class UIBillboard : MonoBehaviour
{
    private Transform mainCameraTransform;

    void Start()
    {
        // 씬에서 Main Camera 태그를 가진 카메라를 찾아 Transform을 캐싱합니다.
        if (Camera.main != null)
        {
            mainCameraTransform = Camera.main.transform;
        }
        else
        {
            // 메인 카메라가 없는 경우 에러를 출력하고 스크립트를 비활성화합니다.
            Debug.LogError("UIBillboard: 씬에 'MainCamera' 태그가 붙은 카메라가 없습니다!");
            enabled = false;
        }
    }

    // 카메라의 움직임이 모두 끝난 후 마지막에 실행되는 LateUpdate에서 처리해야
    // UI가 떨리거나 한 프레임 늦게 따라오는 현상을 방지할 수 있습니다.
    void LateUpdate()
    {
        // 카메라 Transform이 유효한 경우에만 실행
        if (mainCameraTransform != null)
        {
            // UI의 회전 값을 카메라의 회전 값과 완전히 일치시킵니다.
            transform.rotation = mainCameraTransform.rotation;
        }
    }
}