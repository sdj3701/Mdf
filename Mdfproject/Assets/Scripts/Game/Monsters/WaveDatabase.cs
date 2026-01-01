// Assets/Scripts/Game/Monsters/WaveDatabase.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 라운드별 웨이브 데이터를 정의합니다.
/// </summary>
[System.Serializable]
public class RoundWaveData
{
    [Header("라운드 번호")]
    [Tooltip("이 웨이브가 적용될 라운드 번호")]
    public int roundNumber;
    
    [Header("몬스터 목록")]
    [Tooltip("이 라운드에 소환될 몬스터들과 수량")]
    public List<WaveMonsterEntry> monsters = new List<WaveMonsterEntry>();
    
    [Header("스폰 설정")]
    [Tooltip("몬스터 간 소환 간격 (초)")]
    [Range(0.1f, 2f)]
    public float spawnInterval = 0.5f;
    
    /// <summary>
    /// 이 라운드의 총 몬스터 수를 계산합니다.
    /// </summary>
    public int GetTotalMonsterCount()
    {
        int total = 0;
        foreach (var entry in monsters)
        {
            if (entry != null && entry.monsterPrefab != null)
            {
                total += entry.count;
            }
        }
        return total;
    }
}

/// <summary>
/// 웨이브에서 사용할 몬스터 스폰 정보입니다.
/// MonsterSpawnEntry와 유사하지만 웨이브 전용 추가 옵션을 포함합니다.
/// </summary>
[System.Serializable]
public class WaveMonsterEntry
{
    [Tooltip("소환할 몬스터 프리팹")]
    public GameObject monsterPrefab;
    
    [Tooltip("소환 수량")]
    [Min(1)]
    public int count = 1;
    
    [Header("라운드 스케일링 (선택)")]
    [Tooltip("이 몬스터의 체력 배율")]
    [Range(0.5f, 5f)]
    public float healthMultiplier = 1f;
    
    [Tooltip("이 몬스터의 이동속도 배율")]
    [Range(0.5f, 3f)]
    public float speedMultiplier = 1f;

    [Tooltip("이 몬스터의 공격력 배율")]
    [Range(0.5f, 5f)]
    public float damageMultiplier = 1f;
}

/// <summary>
/// 전체 게임의 웨이브 데이터를 관리하는 ScriptableObject입니다.
/// 인스펙터에서 라운드별 몬스터 구성을 설정할 수 있습니다.
/// </summary>
[CreateAssetMenu(fileName = "WaveDatabase", menuName = "Game/Wave Database")]
public class WaveDatabase : ScriptableObject
{
    [Header("라운드별 웨이브 설정")]
    [Tooltip("각 라운드에 대한 웨이브 데이터")]
    public List<RoundWaveData> rounds = new List<RoundWaveData>();
    
    [Header("폴백 설정")]
    [Tooltip("정의되지 않은 라운드에서 사용할 기본 웨이브")]
    public RoundWaveData fallbackWave;
    
    [Header("폴백 스케일링")]
    [Tooltip("정의되지 않은 라운드에서 폴백 웨이브에 추가할 몬스터 수 (라운드당)")]
    public int additionalMonstersPerRound = 1;
    
    [Header("자동 라운드 스케일링")]
    [Tooltip("라운드 스케일링 활성화")]
    public bool enableAutoScaling = true;
    
    [Tooltip("라운드당 체력 증가율 (0.1 = 라운드당 +10%)")]
    [Range(0f, 0.5f)]
    public float healthScalePerRound = 0.1f;
    
    [Tooltip("라운드당 이동속도 증가율 (0.05 = 라운드당 +5%)")]
    [Range(0f, 0.3f)]
    public float speedScalePerRound = 0.05f;

    [Tooltip("라운드당 공격력 증가율 (0.1 = 라운드당 +10%)")]
    [Range(0f, 0.5f)]
    public float damageScalePerRound = 0.1f;
    
    [Tooltip("스케일링이 시작되는 라운드 (이 라운드부터 증가 시작)")]
    [Min(1)]
    public int scalingStartRound = 1;

    /// <summary>
    /// 특정 라운드의 자동 체력 스케일링 배율을 계산합니다.
    /// </summary>
    public float GetHealthScaleForRound(int round)
    {
        if (!enableAutoScaling || round <= scalingStartRound) return 1f;
        int roundsAboveStart = round - scalingStartRound;
        return 1f + (healthScalePerRound * roundsAboveStart);
    }

    /// <summary>
    /// 특정 라운드의 자동 속도 스케일링 배율을 계산합니다.
    /// </summary>
    public float GetSpeedScaleForRound(int round)
    {
        if (!enableAutoScaling || round <= scalingStartRound) return 1f;
        int roundsAboveStart = round - scalingStartRound;
        return 1f + (speedScalePerRound * roundsAboveStart);
    }

    /// <summary>
    /// 특정 라운드의 자동 공격력 스케일링 배율을 계산합니다.
    /// </summary>
    public float GetDamageScaleForRound(int round)
    {
        if (!enableAutoScaling || round <= scalingStartRound) return 1f;
        int roundsAboveStart = round - scalingStartRound;
        return 1f + (damageScalePerRound * roundsAboveStart);
    }
    
    #region Public Methods
    
    /// <summary>
    /// 특정 라운드의 웨이브 데이터를 가져옵니다.
    /// </summary>
    /// <param name="round">라운드 번호</param>
    /// <returns>해당 라운드의 웨이브 데이터, 없으면 폴백</returns>
    public RoundWaveData GetWaveForRound(int round)
    {
        // 정확히 일치하는 라운드 찾기
        var waveData = rounds.Find(r => r.roundNumber == round);
        
        if (waveData != null)
        {
            return waveData;
        }
        
        // 폴백 사용
        if (fallbackWave != null)
        {
            Debug.Log($"[WaveDatabase] 라운드 {round}에 대한 웨이브 데이터가 없어 폴백 사용");
            return fallbackWave;
        }
        
        Debug.LogWarning($"[WaveDatabase] 라운드 {round}에 대한 웨이브 데이터와 폴백 모두 없음!");
        return null;
    }
    
    /// <summary>
    /// 특정 라운드가 명시적으로 정의되어 있는지 확인합니다.
    /// </summary>
    public bool HasExplicitWaveForRound(int round)
    {
        return rounds.Exists(r => r.roundNumber == round);
    }
    
    /// <summary>
    /// 마지막으로 정의된 라운드 번호를 반환합니다.
    /// </summary>
    public int GetLastDefinedRound()
    {
        if (rounds == null || rounds.Count == 0) return 0;
        
        int maxRound = 0;
        foreach (var wave in rounds)
        {
            if (wave.roundNumber > maxRound)
            {
                maxRound = wave.roundNumber;
            }
        }
        return maxRound;
    }
    
    #endregion
    
    #region Editor Validation
    
    private void OnValidate()
    {
        // 중복 라운드 번호 체크
        HashSet<int> seenRounds = new HashSet<int>();
        foreach (var wave in rounds)
        {
            if (seenRounds.Contains(wave.roundNumber))
            {
                Debug.LogWarning($"[WaveDatabase] 라운드 {wave.roundNumber}가 중복 정의되어 있습니다!");
            }
            seenRounds.Add(wave.roundNumber);
        }
    }
    
    #endregion
}
