// Assets/Scripts/Game/Battle/MonsterPoolEntry.cs
using UnityEngine;

/// <summary>
/// 공격 시퀀스에서 소환 가능한 몬스터 풀 엔트리
/// </summary>
[System.Serializable]
public class MonsterPoolEntry
{
    [Tooltip("몬스터 데이터")]
    public MonsterData MonsterData;
    
    [Tooltip("남은 소환 가능 수량")]
    public int RemainingCount;
    
    [Tooltip("최대 수량 (UI 표시용)")]
    public int MaxCount;

    public MonsterPoolEntry(MonsterData monsterData, int count)
    {
        MonsterData = monsterData;
        RemainingCount = count;
        MaxCount = count;
    }

    /// <summary>
    /// 1마리 소비 시도. 성공하면 true 반환
    /// </summary>
    public bool TryConsume()
    {
        if (RemainingCount <= 0) return false;
        RemainingCount--;
        return true;
    }

    /// <summary>
    /// 남은 수량이 0인지 확인
    /// </summary>
    public bool IsEmpty => RemainingCount <= 0;
}
