// Assets/Scripts/Game/Skills/Targeting/TargetingStrategy.cs (새 폴더 및 파일)
using UnityEngine;
using System.Collections.Generic;

public abstract class TargetingStrategy : ScriptableObject
{
    /// <summary>
    /// 지정된 위치를 기준으로 스킬의 대상들을 찾습니다.
    /// </summary>
    /// <param name="caster">스킬 시전자</param>
    /// <param name="targetPosition">스킬이 발동되는 중심 위치</param>
    /// <param name="range">SkillData로부터 전달받는 스킬의 유효 범위</param>
    /// <returns>찾아낸 대상들의 리스트</returns>
    public abstract List<GameObject> FindTargets(GameObject caster, Vector3 targetPosition, float range);
}
