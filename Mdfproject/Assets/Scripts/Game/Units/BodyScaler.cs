using UnityEngine;
using System.Collections.Generic;

public class BodyScaler : MonoBehaviour
{
    [Header("--- 뼈 연결 (매우 중요!) ---")]
    [Tooltip("몸통을 구성하는 모든 뼈를 연결하세요. (pelvis, spine_01, spine_02, spine_03)")]
    public Transform[] bodyBones;

    [Tooltip("크기를 조절할 머리 뼈를 여기에 연결하세요. (head)")]
    public Transform headBone;

    [Header("--- 몸통 스케일 값 조절 ---")]
    [Tooltip("캐릭터의 몸통 너비(좌우)를 조절합니다.")]
    [Range(0.5f, 3.0f)]
    public float bodyWidth = 1.0f;

    [Tooltip("캐릭터의 몸통 높이(상하)를 조절합니다.")]
    [Range(0.5f, 3.0f)]
    public float bodyHeight = 1.0f;

    [Tooltip("캐릭터의 몸통 두께(앞뒤)를 조절합니다.")]
    [Range(0.5f, 3.0f)]
    public float bodyDepth = 1.0f;

    [Header("--- 머리 스케일 값 조절 ---")]
    [Tooltip("머리의 너비(좌우)를 조절합니다.")]
    [Range(0.5f, 3.0f)]
    public float headWidth = 1.0f;

    [Tooltip("머리의 높이(상하)를 조절합니다.")]
    [Range(0.5f, 3.0f)]
    public float headHeight = 1.0f;

    [Tooltip("머리의 깊이(앞뒤)를 조절합니다.")]
    [Range(0.5f, 3.0f)]
    public float headDepth = 1.0f;

    private Dictionary<Transform, Vector3> originalLocalScales = new Dictionary<Transform, Vector3>();
    private Vector3 originalHeadWorldScale;
    private bool isScalingNeeded = false;
    private Transform headBoneParent;

    void Awake()
    {
        if (bodyBones != null)
        {
            foreach (Transform bone in bodyBones)
            {
                if (bone != null && !originalLocalScales.ContainsKey(bone))
                {
                    originalLocalScales.Add(bone, bone.localScale);
                }
            }
        }
        
        if (headBone != null)
        {
            if (!originalLocalScales.ContainsKey(headBone))
            {
                originalLocalScales.Add(headBone, headBone.localScale);
            }
            originalHeadWorldScale = headBone.lossyScale;
            headBoneParent = headBone.parent;
        }
        CheckIfScalingIsNeeded();
    }

    void LateUpdate()
    {
        if (!isScalingNeeded) return;

        // 1. 몸통 뼈들의 스케일을 조절합니다.
        if (bodyBones != null)
        {
            foreach (Transform bone in bodyBones)
            {
                if (bone != null && originalLocalScales.TryGetValue(bone, out Vector3 originalScale))
                {
                    // [최종 축 매핑] 모든 피드백을 반영한 최종 축 설정
                    bone.localScale = new Vector3(
                        originalScale.x * bodyHeight, // X축 -> 높이(Height)
                        originalScale.y * bodyDepth,  // Y축 -> 깊이(Depth)
                        originalScale.z * bodyWidth   // Z축 -> 너비(Width)
                    );
                }
            }
        }

        // 2. 머리 뼈의 월드 스케일을 목표값으로 강제로 맞춥니다.
        if (headBone != null && headBoneParent != null)
        {
            // 몸통과 동일한 최종 축 매핑 규칙을 머리에 적용합니다.
            Vector3 targetHeadWorldScale = new Vector3(
                originalHeadWorldScale.x * headHeight, // X축 -> 높이(Height)
                originalHeadWorldScale.y * headDepth,  // Y축 -> 깊이(Depth)
                originalHeadWorldScale.z * headWidth   // Z축 -> 너비(Width)
            );
            
            Vector3 parentWorldScale = headBoneParent.lossyScale;

            float newLocalScaleX = (parentWorldScale.x == 0) ? 0 : targetHeadWorldScale.x / parentWorldScale.x;
            float newLocalScaleY = (parentWorldScale.y == 0) ? 0 : targetHeadWorldScale.y / parentWorldScale.y;
            float newLocalScaleZ = (parentWorldScale.z == 0) ? 0 : targetHeadWorldScale.z / parentWorldScale.z;

            headBone.localScale = new Vector3(newLocalScaleX, newLocalScaleY, newLocalScaleZ);
        }
    }
    
    void CheckIfScalingIsNeeded()
    {
        isScalingNeeded = !Mathf.Approximately(bodyWidth, 1.0f) 
                          || !Mathf.Approximately(bodyHeight, 1.0f)
                          || !Mathf.Approximately(bodyDepth, 1.0f) 
                          || !Mathf.Approximately(headWidth, 1.0f)
                          || !Mathf.Approximately(headHeight, 1.0f)
                          || !Mathf.Approximately(headDepth, 1.0f);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        CheckIfScalingIsNeeded();
    }
#endif
}
