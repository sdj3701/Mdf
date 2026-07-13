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
    private bool isDataLoading = false;
    private AsyncOperationHandle<IList<AugmentData>> _augmentDataHandle;
    private bool _hasAugmentDataHandle;
    private List<AugmentData> presentedAugments = new List<AugmentData>();
    private const float AugmentDataWaitTimeoutSeconds = 12f;
    private const int AugmentDataPollMilliseconds = 100;

    public bool IsDataLoaded => isDataLoaded;

    private bool TryGetOwnerId(out int ownerId)
    {
        ownerId = -1;
        if (playerManager == null || playerManager.Object == null || !playerManager.Object.IsValid)
        {
            return false;
        }

        try
        {
            ownerId = playerManager.playerId;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private string OwnerLabel()
    {
        return TryGetOwnerId(out int ownerId) ? ownerId.ToString() : "unknown";
    }

    private bool IsRunningClientPeerWithoutAuthority()
    {
        var runner = playerManager != null ? playerManager.Runner : null;
        return runner != null && runner.IsRunning && !runner.IsServer;
    }

    private async UniTask<bool> WaitUntilAugmentDataLoadedInternal(float timeoutSeconds = AugmentDataWaitTimeoutSeconds)
    {
        if (isDataLoaded)
        {
            return true;
        }

        float waited = 0f;
        int startAttempts = 0;
        float nextRetryAt = 0f;

        while (!isDataLoaded && waited < timeoutSeconds)
        {
            if (!isDataLoading && startAttempts < 3 && waited >= nextRetryAt)
            {
                startAttempts++;
                nextRetryAt = waited + 1.5f;
                LoadAllAugmentsAsync().Forget();
            }

            await UniTask.Delay(AugmentDataPollMilliseconds);
            waited += AugmentDataPollMilliseconds / 1000f;
        }

        if (isDataLoaded)
        {
            return true;
        }

        string owner = TryGetOwnerId(out int ownerId) ? ownerId.ToString() : "unknown";
        Debug.LogError($"[AugmentManager] Augment data wait timeout. owner={owner}, waited={waited:F1}s, loading={isDataLoading}");
        BuildDebugGUI.LogClient($"[AugmentManager] data wait timeout owner={owner}, waited={waited:F1}s");
        return false;
    }

    public async UniTask WaitUntilAugmentDataLoaded()
    {
        await WaitUntilAugmentDataLoadedInternal();
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
        bool loaded = await WaitUntilAugmentDataLoadedInternal();
        if (!loaded)
        {
            return;
        }

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
        if (playerManager == null)
        {
            playerManager = GetComponentInParent<PlayerManager>();
        }

        int ownerId = playerManager != null ? playerManager.playerId : -1;

        // 데이터 로딩 완료 대기
        bool loaded = await WaitUntilAugmentDataLoadedInternal();
        if (!loaded)
        {
            presentedAugments.Clear();
            return;
        }

        Debug.Log($"SetPresentedAugmentsByNamesAsync: 데이터 로딩 완료, 동기화 시작 (Player {ownerId})");

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
            Debug.Log($"[동기화] Player {ownerId} 제시 증강 동기화: {presentedNames}");
        }
    }

    // Start에서 자동 로딩 제거 - SetupGameUI에서 명시적으로 호출
    // void Start() { }
    
    /// <summary>
    /// 증강 데이터를 Addressables에서 로드합니다. 외부에서 명시적으로 호출해야 합니다.
    /// </summary>
    public async UniTask LoadAllAugmentsAsync()
    {
        if (playerManager == null)
        {
            playerManager = GetComponentInParent<PlayerManager>();
        }

        if (isDataLoaded)
        {
            return;
        }

        if (isDataLoading)
        {
            await UniTask.WaitUntil(() => !isDataLoading);
            return;
        }

        isDataLoading = true;
        int ownerId = playerManager != null ? playerManager.playerId : -1;
        Debug.Log($"Player {ownerId}: 어드레서블에서 증강 데이터 로딩을 시작합니다...");

        try
        {
            ReleaseAugmentDataHandle();
            _augmentDataHandle = Addressables.LoadAssetsAsync<AugmentData>("Augment", null);
            _hasAugmentDataHandle = true;
            AsyncOperationHandle<IList<AugmentData>> handle = _augmentDataHandle;
            await handle.Task;

            if (handle.Status == AsyncOperationStatus.Succeeded)
            {
                silverAugments.Clear();
                goldAugments.Clear();
                prismaticAugments.Clear();

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
                Debug.Log($"<color=cyan>Player {ownerId}: 증강 데이터 로드 완료. " +
                          $"실버: {silverAugments.Count}개, 골드: {goldAugments.Count}개, 프리즘: {prismaticAugments.Count}개</color>");
            }
            else
            {
                Debug.LogError($"어드레서블에서 증강 데이터 로딩 실패: {handle.OperationException}");
            }
        }
        finally
        {
            if (!isDataLoaded)
            {
                ReleaseAugmentDataHandle();
            }
            isDataLoading = false;
        }
    }

    private void OnDestroy()
    {
        ReleaseAugmentDataHandle();
    }

    private void ReleaseAugmentDataHandle()
    {
        if (_hasAugmentDataHandle && _augmentDataHandle.IsValid())
        {
            Addressables.Release(_augmentDataHandle);
        }

        _hasAugmentDataHandle = false;
    }

    /// <summary>
    /// 증강 이름으로 AugmentData를 검색합니다.
    /// 모든 티어(Silver, Gold, Prismatic)에서 검색합니다.
    /// </summary>
    public AugmentData FindAugmentByName(string augmentName)
    {
        if (string.IsNullOrEmpty(augmentName)) return null;
        
        return silverAugments.FirstOrDefault(a => a.augmentName == augmentName)
            ?? goldAugments.FirstOrDefault(a => a.augmentName == augmentName)
            ?? prismaticAugments.FirstOrDefault(a => a.augmentName == augmentName);
    }

    public MonsterData FindMonsterDataByName(string monsterDataName)
    {
        if (string.IsNullOrWhiteSpace(monsterDataName))
        {
            return null;
        }

        foreach (var augment in EnumerateLoadedAugments())
        {
            if (MatchesMonsterData(augment?.bossMonsterData, monsterDataName))
            {
                return augment.bossMonsterData;
            }

            if (MatchesMonsterData(augment?.strengthenedMonsterData, monsterDataName))
            {
                return augment.strengthenedMonsterData;
            }

            var entries = augment?.monsterSpawnEntries;
            if (entries == null) continue;
            foreach (var entry in entries)
            {
                if (MatchesMonsterData(entry?.monsterData, monsterDataName))
                {
                    return entry.monsterData;
                }
            }
        }

        return null;
    }

    private IEnumerable<AugmentData> EnumerateLoadedAugments()
    {
        foreach (var augment in silverAugments)
        {
            yield return augment;
        }

        foreach (var augment in goldAugments)
        {
            yield return augment;
        }

        foreach (var augment in prismaticAugments)
        {
            yield return augment;
        }

        foreach (var augment in presentedAugments)
        {
            yield return augment;
        }
    }

    private static bool MatchesMonsterData(MonsterData data, string monsterDataName)
    {
        if (data == null || string.IsNullOrWhiteSpace(monsterDataName))
        {
            return false;
        }

        return string.Equals(data.name, monsterDataName, StringComparison.Ordinal)
            || string.Equals(data.monsterName, monsterDataName, StringComparison.Ordinal)
            || string.Equals(data.monsterPrefab, monsterDataName, StringComparison.Ordinal);
    }

    public void PresentAugments()
    {
        if (playerManager == null)
        {
            playerManager = GetComponentInParent<PlayerManager>();
        }

        if (IsRunningClientPeerWithoutAuthority())
        {
            Debug.LogWarning($"[AugmentManager] PresentAugments ignored on non-authority peer. owner={OwnerLabel()}");
            return;
        }

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
    }

    public void SelectAndApplyAugment(AugmentData chosenAugment)
    {
        if (playerManager == null)
        {
            playerManager = GetComponentInParent<PlayerManager>();
        }

        if (playerManager == null)
        {
            Debug.LogError("[AugmentManager] playerManager가 null이라 증강을 적용할 수 없습니다.");
            return;
        }

        if (IsRunningClientPeerWithoutAuthority())
        {
            Debug.LogWarning($"[AugmentManager] SelectAndApplyAugment ignored on non-authority peer. owner={OwnerLabel()}");
            return;
        }

        playerManager.chosenAugments.Add(chosenAugment);
        playerManager.PublishSelectedAugmentSnapshot(chosenAugment);
        playerManager.ClearPresentedAugmentSnapshot();
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
        switch (augment.effectType)
        {
            case EffectType.SpawnMonsterOnEnemyField:
                if (augment.isBossSummon)
                {
                    if (augment.bossMonsterData != null)
                    {
                        playerManager.AddOwnedBoss(augment);
                        Debug.Log($"<color=red>[AugmentManager] 보스 증강 등록! Player {playerManager.playerId}가 보스 '{augment.bossMonsterData.monsterName}' 보유</color>");
                    }
                    else
                    {
                        Debug.LogWarning($"[AugmentManager] bossMonsterData가 null입니다.");
                    }
                }
                else
                {
                    Debug.LogWarning($"[AugmentManager] Legacy non-boss summon augment '{augment.augmentName}' was ignored. Non-boss monsters are now available through the Black Magic catalog.");
                }
                return;
            case EffectType.StrengthenMonsterType:
                if (augment.strengthenedMonsterData == null)
                {
                    Debug.LogWarning($"[AugmentManager] Monster strengthening augment '{augment.augmentName}' has no strengthenedMonsterData.");
                }
                return;
            case EffectType.GrantMagicScroll:
                if (augment.magicScrollData != null)
                {
                    playerManager.AddMagicScroll(augment.magicScrollData);
                    Debug.Log($"<color=magenta>[AugmentManager] Player {playerManager.playerId}가 마법 스크롤 '{augment.magicScrollData.scrollName}' 획득!</color>");
                }
                else
                {
                    Debug.LogWarning($"[AugmentManager] 마법 스크롤 증강 '{augment.augmentName}'에 magicScrollData가 설정되지 않았습니다.");
                }
                return;
            default:
                break;
        }
        
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
            case EffectType.IncreaseBlackMagicMaximum:
                int blackMagicBonus = Mathf.Max(0, Mathf.RoundToInt(augment.value));
                if (blackMagicBonus > 0)
                {
                    target.AddBlackMagicMaximumBonus(blackMagicBonus);
                    Debug.Log($"[AugmentManager] Player {target.playerId} gained +{blackMagicBonus} maximum Black Magic from '{augment.augmentName}'. It applies from the next attack sequence refill.");
                }
                break;
            case EffectType.IncreaseEnemyHealth:
            case EffectType.IncreaseEnemyMoveSpeed:
                Debug.Log($"{target.playerId}의 다음 라운드 몬스터에게 '{augment.augmentName}' 효과가 추가되었습니다.");
                break;
        }
    }
}
