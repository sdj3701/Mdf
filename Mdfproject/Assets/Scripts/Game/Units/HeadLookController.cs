using UnityEngine;

[RequireComponent(typeof(Animator))]
public class HeadLookController : MonoBehaviour
{
    private Animator animator;
    private Transform _runtimeLookAtTarget;

    public bool HasAppliedLookAt { get; private set; }
    public int LastLookAtAppliedFrame { get; private set; } = -1;
    public Transform RuntimeLookAtTarget => _runtimeLookAtTarget;

    [Range(0, 1)]
    public float lookAtWeight = 1.0f;

    [Tooltip("캐릭터가 얼마나 위를 쳐다볼지 결정합니다. 높을수록 하늘을 봅니다.")]
    [Range(0, 5)]
    public float tiltAngle = 1.0f;

    [Range(0f, 1f)]
    public float lookAtBodyWeight;

    [Range(0f, 1f)]
    public float lookAtHeadWeight = 1f;

    [Tooltip("0 allows the full look-at rotation; 1 completely clamps it.")]
    [Range(0f, 1f)]
    public float lookAtClampWeight = 0.5f;

    void Awake()
    {
        animator = GetComponent<Animator>();
    }

    public bool WasLookAtAppliedRecently(int maximumFrameAge)
    {
        return HasAppliedLookAt
               && LastLookAtAppliedFrame >= 0
               && Time.frameCount - LastLookAtAppliedFrame <= Mathf.Max(0, maximumFrameAge);
    }

    public void SetRuntimeLookAtTarget(Transform target)
    {
        if (_runtimeLookAtTarget != target)
        {
            _runtimeLookAtTarget = target;
        }
    }

    public void ClearRuntimeLookAtTarget()
    {
        _runtimeLookAtTarget = null;
    }

    private Vector3 ResolveLookAtTargetPosition(Vector3 headPosition)
    {
        if (_runtimeLookAtTarget != null
            && (_runtimeLookAtTarget.position - headPosition).sqrMagnitude > 0.000001f)
        {
            return _runtimeLookAtTarget.position;
        }

        Vector3 lookDirection = (transform.forward + Vector3.up * tiltAngle).normalized;
        return headPosition + lookDirection * 100f;
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (animator == null) return;

         Transform headTransform = animator.GetBoneTransform(HumanBodyBones.Head);

        // ★★★ 추가된 부분: 머리 뼈를 찾았는지 확인합니다. ★★★
        if (headTransform == null)
        {
            // 머리 뼈를 못 찾았으면 더 이상 진행하지 않고 함수를 종료합니다.
            // 디버그 로그를 추가하여 원인 파악을 쉽게 할 수 있습니다.
            Debug.LogWarning("Head bone not found. Please check the Humanoid Avatar configuration.");
            return;
        }

        // 1. 캐릭터의 머리 위치를 가져옵니다.
        Vector3 headPosition = headTransform.position;
        // 2. 목표 방향을 계산합니다: 캐릭터의 정면(transform.forward) + 월드 위쪽(Vector3.up)
        // tiltAngle 값으로 위를 보는 정도를 조절할 수 있습니다.
        Vector3 lookAtTargetPosition = ResolveLookAtTargetPosition(headPosition);

        // 디버깅용 선 그리기 (이제 캐릭터의 앞쪽 위로 선이 나갈 것입니다)
        Debug.DrawLine(headPosition, lookAtTargetPosition, Color.yellow);

        animator.SetLookAtWeight(
            lookAtWeight,
            lookAtBodyWeight,
            lookAtHeadWeight,
            0f,
            lookAtClampWeight);
        animator.SetLookAtPosition(lookAtTargetPosition);
        HasAppliedLookAt = true;
        LastLookAtAppliedFrame = Time.frameCount;
    }
}
