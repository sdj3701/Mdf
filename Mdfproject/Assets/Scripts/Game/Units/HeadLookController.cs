using UnityEngine;

[RequireComponent(typeof(Animator))]
public class HeadLookController : MonoBehaviour
{
    private Animator animator;

    [Range(0, 1)]
    public float lookAtWeight = 1.0f;

    [Tooltip("캐릭터가 얼마나 위를 쳐다볼지 결정합니다. 높을수록 하늘을 봅니다.")]
    [Range(0, 2)]
    public float tiltAngle = 1.0f;

    void Awake()
    {
        animator = GetComponent<Animator>();
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (animator == null) return;

        // 1. 캐릭터의 머리 위치를 가져옵니다.
        Vector3 headPosition = animator.GetBoneTransform(HumanBodyBones.Head).position;

        // 2. 목표 방향을 계산합니다: 캐릭터의 정면(transform.forward) + 월드 위쪽(Vector3.up)
        // tiltAngle 값으로 위를 보는 정도를 조절할 수 있습니다.
        Vector3 lookDirection = (transform.forward + Vector3.up * tiltAngle).normalized;

        // 3. 목표 지점을 계산합니다.
        // 머리 위치에서 계산된 방향으로 100 유닛 떨어진 곳을 목표로 합니다.
        Vector3 lookAtTargetPosition = headPosition + lookDirection * 100f;

        // 디버깅용 선 그리기 (이제 캐릭터의 앞쪽 위로 선이 나갈 것입니다)
        Debug.DrawLine(headPosition, lookAtTargetPosition, Color.yellow);

        animator.SetLookAtWeight(lookAtWeight, 0f, 1f, 0f, 0.5f);
        animator.SetLookAtPosition(lookAtTargetPosition);
    }
}