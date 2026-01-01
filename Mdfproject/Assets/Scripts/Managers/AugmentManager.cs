// Assets/Scripts/Managers/AugmentManager.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using System;
using Cysharp.Threading.Tasks; // [추가] UniTask 사용을 위해 네임스페이스 추가

public class AugmentManager : MonoBehaviour
{
    public PlayerManager playerManager;

    private List<AugmentData> silverAugments = new List<AugmentData>();
    private List<AugmentData> goldAugments = new List<AugmentData>();
    private List<AugmentData> prismaticAugments = new List<AugmentData>();
    private bool isDataLoaded = false;
    private List<AugmentData> presentedAugments = new List<AugmentData>();

    public Cysharp.Threading.Tasks.UniTask WaitUntilAugmentDataLoaded()
    {
        return Cysharp.Threading.Tasks.UniTask.WaitUntil(() => isDataLoaded);
    }

    public List<AugmentData> GetPresentedAugments()
    {
        return presentedAugments;
    }

    /// <summary>
    /// 증강 데이터 로딩을 보장하고, 증강 목록이 있는지 확인합니다.
    /// 서버에서는 생성, 클라이언트에서는 서버 데이터 도착을 대기합니다.
    /// </summary>
    public async UniTask EnsureAugmentsPresentedAsync(IEnumerable<string> augmentNamesFromServer = null)
    {
        // 1. 증강 데이터(Addressables) 로드가 완료될 때까지 기다림
        await WaitUntilAugmentDataLoaded();

        // 2. 서버에서 이름 목록을 받았으면 적용
        if (augmentNamesFromServer != null && augmentNamesFromServer.Any())
        {
            await SetPresentedAugmentsByNamesAsync(augmentNamesFromServer);
            return;
        }
        
        // 3. 이미 증강 목록이 있으면 대기 없이 반환
        if (presentedAugments.Count > 0) return;
        
        // 4. 서버 데이터 도착을 최대 5초간 대기 (클라이언트용)
        float waited = 0f;
        while (presentedAugments.Count == 0 && waited < 5f)
        {
            await UniTask.Delay(100);
            waited += 0.1f;
        }
        
        if (presentedAugments.Count == 0)
        {
            Debug.LogWarning("[AugmentManager] 증강체 데이터 대기 타임아웃");
        }
    }

    /// <summary>
    /// 서버(호스트)에서 브로드캐스트된 증강 이름 목록을 기반으로 현재 제시 증강을 동기화합니다.
    /// 데이터 로딩이 완료될 때까지 대기합니다.
    /// </summary>
    public async UniTask SetPresentedAugmentsByNamesAsync(IEnumerable<string> augmentNames)
    {
        // 데이터 로딩 완료 대기
        await WaitUntilAugmentDataLoaded();

        Debug.Log("SetPresentedAugmentsByNamesAsync: 데이터 로딩 완료, 동기화 시작");

        // 가능한 모든 풀을 하나로 묶어 빠르게 조회할 수 있도록 딕셔너리 구성
        // 중복 이름이 없다는 전제(augmentName 유니크)를 가정합니다.
        var all = new List<AugmentData>(silverAugments.Count + goldAugments.Count + prismaticAugments.Count);
        all.AddRange(silverAugments);
        all.AddRange(goldAugments);
        all.AddRange(prismaticAugments);

        var nameToAugment = new Dictionary<string, AugmentData>(StringComparer.Ordinal);
        foreach (var a in all)
        {
            if (a != null && !string.IsNullOrEmpty(a.augmentName) && !nameToAugment.ContainsKey(a.augmentName))
            {
                nameToAugment[a.augmentName] = a;
            }
        }

        presentedAugments.Clear();
        foreach (var name in augmentNames)
        {
            if (string.IsNullOrEmpty(name)) continue;
            if (nameToAugment.TryGetValue(name, out var data) && data != null)
            {
                presentedAugments.Add(data);
            }
            else
            {
                Debug.LogWarning($"서버가 보낸 증강 '{name}'을(를) 찾지 못했습니다. (라벨/이름 불일치)");
            }
        }

        if (presentedAugments.Count > 0)
        {
            string presentedNames = string.Join(", ", presentedAugments.Select(aug => aug.augmentName));
            Debug.Log($"[동기화] Player {playerManager.playerId} 제시 증강 동기화: {presentedNames}");
        }
    }

    // Start에서 자동 로딩 제거 - SetupGameUI에서 명시적으로 호출
    // void Start() { }
    
    /// <summary>
    /// 증강 데이터를 Addressables에서 로드합니다. 외부에서 명시적으로 호출해야 합니다.
    /// </summary>
    public async UniTask LoadAllAugmentsAsync()
    {
        Debug.Log($"Player {playerManager.playerId}: 어드레서블에서 증강 데이터 로딩을 시작합니다...");
        
        AsyncOperationHandle<IList<AugmentData>> handle = Addressables.LoadAssetsAsync<AugmentData>("Augment", null);
        await handle.Task;

        if (handle.Status == AsyncOperationStatus.Succeeded)
        {
            foreach (var augment in handle.Result)
            {
                switch (augment.tier)
                {
                    case AugmentTier.Silver:
                        silverAugments.Add(augment);
                        break;
                    case AugmentTier.Gold:
                        goldAugments.Add(augment);
                        break;
                    case AugmentTier.Prismatic:
                        prismaticAugments.Add(augment);
                        break;
                }
            }
            isDataLoaded = true;
            Debug.Log($"<color=cyan>Player {playerManager.playerId}: 증강 데이터 로드 완료. " +
                      $"실버: {silverAugments.Count}개, 골드: {goldAugments.Count}개, 프리즘: {prismaticAugments.Count}개</color>");
        }
        else
        {
            Debug.LogError($"어드레서블에서 증강 데이터 로딩 실패: {handle.OperationException}");
        }
    }

    public void PresentAugments()
    {
        if (!isDataLoaded)
        {
            Debug.LogWarning("증강 데이터가 아직 로드되지 않았습니다.");
            return;
        }
        presentedAugments.Clear();
        
        float roll = UnityEngine.Random.value;
        List<AugmentData> sourceList;
        string tierName;

        if (roll < 0.10f && prismaticAugments.Count >= 3)
        {
            sourceList = prismaticAugments;
            tierName = "프리즘";
        }
        else if (roll < 0.40f && goldAugments.Count >= 3)
        {
            sourceList = goldAugments;
            tierName = "골드";
        }
        else
        {
            sourceList = silverAugments;
            tierName = "실버";
        }
        
        if (sourceList == null || sourceList.Count == 0)
        {
            Debug.LogError($"제시할 {tierName} 등급의 증강이 부족하거나 없습니다!");
            return;
        }

        int countToTake = Mathf.Min(sourceList.Count, 3);
        presentedAugments = sourceList.OrderBy(x => UnityEngine.Random.value).Take(countToTake).ToList();

        string presentedNames = string.Join(", ", presentedAugments.Select(aug => aug.augmentName));
        Debug.Log($"Player {playerManager.playerId}에게 <color=yellow>{tierName} 등급</color> 증강 제시: {presentedNames}");
    }

    public void SelectAndApplyAugment(AugmentData chosenAugment)
    {
        playerManager.chosenAugments.Add(chosenAugment);
        Debug.Log($"Player {playerManager.playerId}가 '<color=yellow>{chosenAugment.augmentName}</color>' 증강을 선택했습니다.");

        PlayerManager target;
        if (chosenAugment.targetType == TargetType.Player)
        {
            target = playerManager;
        }
        else
        {
            target = playerManager.opponentManager;
            // 2인 플레이가 아니어서 opponentManager가 설정되지 않은 경우(예: 3인 이상 게임),
            // 자신을 제외한 다른 플레이어 중 한 명을 무작위로 선택합니다.
            if (target == null && GameManagers.Instance.AllPlayers.Count() > 1)
            {
                var otherPlayers = GameManagers.Instance.AllPlayers.Where(p => p != playerManager).ToList();
                if (otherPlayers.Any())
                {
                    target = otherPlayers[(int)UnityEngine.Random.Range(0f, otherPlayers.Count)];
                }
            }
        }
        
        ApplyEffect(target, chosenAugment);
        
        presentedAugments.Clear();

        // 다른 시스템(UI 등)에 상태 변경을 알립니다.
        GameEvents.TriggerAugmentApplied(this.playerManager, chosenAugment);
    }

    private void ApplyEffect(PlayerManager target, AugmentData augment)
    {
        if (target == null)
        {
            Debug.LogError($"증강 효과를 적용할 대상(Target)이 없습니다! (Augment: {augment.augmentName})");
            return;
        }

        switch (augment.effectType)
        {
            case EffectType.AddGold:
                target.AddGold((int)augment.value);
                break;
            case EffectType.AddWallPlacementCount:
                int addWalls = Mathf.Max(0, Mathf.RoundToInt(augment.value));
                if (addWalls > 0)
                {
                    target.AddWalls(addWalls);
                    Debug.Log($"<color=cyan>[AugmentManager] Player {target.playerId} gained +{addWalls} walls from '{augment.augmentName}' (stock={target.GetWallCount()})</color>");
                }
                break;
            case EffectType.IncreaseMyUnitAttack:
                target.AddPermanentAttackDamagePercent(augment.value);
                Debug.Log($"{target.playerId}의 필드에 '{augment.augmentName}' 영구 공격력 버프 적용 (+{augment.value:P0})");
                break;
            case EffectType.IncreaseMyUnitAttackSpeed:
                target.AddPermanentAttackSpeedPercent(augment.value);
                Debug.Log($"{target.playerId}의 필드에 '{augment.augmentName}' 영구 공격속도 버프 적용 (+{augment.value:P0})");
                break;
            case EffectType.SpawnMonsterOnEnemyField:
                if (augment.isBossSummon)
                {
                    // 보스 모드: 즉시 1회 소환 (현재 상대에게)
                    if (augment.bossPrefab != null && target.monsterSpawner != null)
                    {
                        target.monsterSpawner.SpawnBossMonster(augment.bossPrefab, playerManager.playerId);
                        Debug.Log($"<color=red>[AugmentManager] 보스 소환! Player {playerManager.playerId}가 Player {target.playerId}에게 침공</color>");
                    }
                    else
                    {
                        Debug.LogWarning($"[AugmentManager] 보스 프리팹 또는 MonsterSpawner가 null입니다.");
                    }
                }
                else
                {
                    // 일반 몬스터 모드: 매 라운드 소환을 위해 증강 등록 (증강 선택자에게 등록)
                    playerManager.RegisterActiveMonsterSummonAugment(augment);
                    Debug.Log($"<color=orange>[AugmentManager] Player {playerManager.playerId}의 일반 몬스터 소환 증강 '{augment.augmentName}' 등록 (매 라운드 상대 침공)</color>");
                }
                break;
            case EffectType.IncreaseEnemyHealth:
            case EffectType.IncreaseEnemyMoveSpeed:
                 Debug.Log($"{target.playerId}의 다음 라운드 몬스터에게 '{augment.augmentName}' 효과가 추가되었습니다.");
                 break;
        }
    }
}
