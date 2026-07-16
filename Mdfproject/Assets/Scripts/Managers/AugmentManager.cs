// Assets/Scripts/Managers/AugmentManager.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using System;
using System.Threading;
using Cysharp.Threading.Tasks; // [추가] UniTask 사용을 위해 네임스페이스 추가

public class AugmentManager : MonoBehaviour
{
    public PlayerManager playerManager;

    private List<AugmentData> silverAugments = new List<AugmentData>();
    private List<AugmentData> goldAugments = new List<AugmentData>();
    private List<AugmentData> prismaticAugments = new List<AugmentData>();
    private readonly Dictionary<string, AugmentData> augmentsByContentId =
        new Dictionary<string, AugmentData>(StringComparer.Ordinal);
    private bool isDataLoaded = false;
    private bool isDataLoading = false;
    private AsyncOperationHandle<IList<AugmentData>> _augmentDataHandle;
    private bool _hasAugmentDataHandle;
    private List<AugmentData> presentedAugments = new List<AugmentData>();
    private const int PresentedAugmentCapacity = 3;
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

    private async UniTask<bool> WaitUntilAugmentDataLoadedInternal(
        float timeoutSeconds = AugmentDataWaitTimeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (isDataLoaded)
        {
            return true;
        }

        float waited = 0f;
        int startAttempts = 0;
        float nextRetryAt = 0f;

        while (!isDataLoaded && waited < timeoutSeconds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!isDataLoading && startAttempts < 3 && waited >= nextRetryAt)
            {
                startAttempts++;
                nextRetryAt = waited + 1.5f;
                LoadAllAugmentsAsync().Forget();
            }

            await UniTask.Delay(AugmentDataPollMilliseconds, cancellationToken: cancellationToken);
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
    /// Clears the peer-local presentation cache after State Authority has accepted a choice.
    /// The selected augment is retained in the durable selected-augment snapshot; this method
    /// only prevents a delayed UI sync retry from presenting the consumed choices again.
    /// </summary>
    public void ApplyAuthoritativeSelectionNotification()
    {
        presentedAugments.Clear();
    }

    /// <summary>
    /// 증강 데이터 로딩을 보장하고, 증강 목록이 있는지 확인합니다.
    /// 서버에서는 생성, 클라이언트에서는 서버 데이터 도착을 대기합니다.
    /// </summary>
    public async UniTask EnsureAugmentsPresentedAsync(IEnumerable<string> augmentContentIdsFromServer = null)
    {
        // 1. 증강 데이터(Addressables) 로드가 완료될 때까지 기다림
        bool loaded = await WaitUntilAugmentDataLoadedInternal();
        if (!loaded)
        {
            return;
        }

        // 2. 서버에서 이름 목록을 받았으면 적용
        if (augmentContentIdsFromServer != null && augmentContentIdsFromServer.Any())
        {
            bool applied = await SetPresentedAugmentsByContentIdsAsync(augmentContentIdsFromServer);
            if (!applied)
            {
                Debug.LogError($"[AugmentManager] Exact presented augment sync failed. owner={OwnerLabel()}");
            }
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
    /// Synchronizes presented augments by immutable content id.
    /// </summary>
    public UniTask<bool> SetPresentedAugmentsByContentIdsAsync(IEnumerable<string> augmentContentIds)
    {
        return TrySetPresentedAugmentsByContentIdsExactAsync(
            augmentContentIds,
            CancellationToken.None);
    }

    public UniTask<bool> SetPresentedAugmentsByContentIdsAsync(
        IEnumerable<string> augmentContentIds,
        CancellationToken cancellationToken)
    {
        return TrySetPresentedAugmentsByContentIdsExactAsync(
            augmentContentIds,
            cancellationToken);
    }

    /// <summary>
    /// Host-migration restore path. Resolves the complete immutable-id set before replacing the
    /// non-networked runtime list, so cancellation or one bad id cannot leave a partial cache.
    /// </summary>
    public async UniTask<bool> TrySetPresentedAugmentsByContentIdsExactAsync(
        IEnumerable<string> augmentContentIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (playerManager == null)
        {
            playerManager = GetComponentInParent<PlayerManager>();
        }

        bool loaded = await WaitUntilAugmentDataLoadedInternal(
            AugmentDataWaitTimeoutSeconds,
            cancellationToken);
        if (!loaded)
        {
            return false;
        }

        var resolved = new List<AugmentData>();
        var uniqueContentIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (string rawContentId in augmentContentIds ?? Enumerable.Empty<string>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (resolved.Count >= PresentedAugmentCapacity)
            {
                return false;
            }
            string contentId = StableDataKeyUtility.NormalizeContentId(rawContentId);
            if (string.IsNullOrWhiteSpace(contentId) || !uniqueContentIds.Add(contentId))
            {
                return false;
            }

            AugmentData data = FindAugmentByContentId(contentId);
            if (data == null || !string.Equals(data.ContentId, contentId, StringComparison.Ordinal))
            {
                return false;
            }

            resolved.Add(data);
        }

        cancellationToken.ThrowIfCancellationRequested();
        presentedAugments.Clear();
        presentedAugments.AddRange(resolved);
        return true;
    }

    /// <summary>
    /// Compatibility path for legacy snapshots. New network synchronization must send content ids.
    /// </summary>
    public UniTask SetPresentedAugmentsByNamesAsync(IEnumerable<string> augmentNames)
    {
        return SetPresentedAugmentsByReferencesAsync(
            augmentNames,
            allowLegacyNames: true,
            CancellationToken.None);
    }

    private async UniTask SetPresentedAugmentsByReferencesAsync(
        IEnumerable<string> augmentReferences,
        bool allowLegacyNames,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (playerManager == null)
        {
            playerManager = GetComponentInParent<PlayerManager>();
        }

        int ownerId = playerManager != null ? playerManager.playerId : -1;

        // 데이터 로딩 완료 대기
        bool loaded = await WaitUntilAugmentDataLoadedInternal(
            AugmentDataWaitTimeoutSeconds,
            cancellationToken);
        if (!loaded)
        {
            presentedAugments.Clear();
            return;
        }

        Debug.Log($"SetPresentedAugments: data loaded, sync start (Player {ownerId}, legacy={allowLegacyNames})");

        var restoredAugments = new List<AugmentData>();
        foreach (var rawReference in augmentReferences ?? Enumerable.Empty<string>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string reference = rawReference?.Trim();
            if (string.IsNullOrEmpty(reference)) continue;

            AugmentData data = FindAugmentByContentId(reference);
            if (data == null && allowLegacyNames)
            {
                data = FindAugmentByName(reference);
            }

            if (data != null)
            {
                restoredAugments.Add(data);
            }
            else
            {
                Debug.LogWarning($"[AugmentManager] Unknown augment reference '{reference}'. legacy={allowLegacyNames}");
                return;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        presentedAugments.Clear();
        presentedAugments.AddRange(restoredAugments);
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
                augmentsByContentId.Clear();

                foreach (var augment in handle.Result)
                {
                    if (!TryRegisterContentId(augment))
                    {
                        continue;
                    }

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

    private bool TryRegisterContentId(AugmentData augment)
    {
        if (augment == null || string.IsNullOrWhiteSpace(augment.ContentId))
        {
            Debug.LogError($"[AugmentManager] Augment asset is missing immutable contentId. asset={augment?.name ?? "null"}");
            return false;
        }

        if (augmentsByContentId.TryGetValue(augment.ContentId, out AugmentData existing) && existing != augment)
        {
            Debug.LogError(
                $"[AugmentManager] Duplicate augment contentId '{augment.ContentId}'. " +
                $"assets={existing.name},{augment.name}");
            return false;
        }

        augmentsByContentId[augment.ContentId] = augment;
        return true;
    }

    public AugmentData FindAugmentByContentId(string contentId)
    {
        string normalized = StableDataKeyUtility.NormalizeContentId(contentId);
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        if (augmentsByContentId.TryGetValue(normalized, out AugmentData indexed))
        {
            return indexed;
        }

        return EnumerateLoadedAugments().FirstOrDefault(
            augment => augment != null && string.Equals(augment.ContentId, normalized, StringComparison.Ordinal));
    }

    /// <summary>
    /// 증강 이름으로 AugmentData를 검색합니다.
    /// 모든 티어(Silver, Gold, Prismatic)에서 검색합니다.
    /// </summary>
    public AugmentData FindAugmentByName(string augmentName)
    {
        if (string.IsNullOrWhiteSpace(augmentName))
        {
            return null;
        }

        string expectedName = augmentName.Trim();
        AugmentData resolved = null;
        string resolvedContentId = string.Empty;
        foreach (AugmentData candidate in EnumerateLoadedAugments())
        {
            if (candidate == null
                || !string.Equals(candidate.augmentName?.Trim(), expectedName, StringComparison.Ordinal))
            {
                continue;
            }

            string candidateContentId = StableDataKeyUtility.NormalizeContentId(candidate.ContentId);
            if (string.IsNullOrEmpty(candidateContentId))
            {
                Debug.LogWarning(
                    $"[AugmentManager] Legacy name '{expectedName}' matched an augment without ContentId; rejected.");
                return null;
            }

            if (resolved == null)
            {
                resolved = candidate;
                resolvedContentId = candidateContentId;
                continue;
            }

            if (!string.Equals(resolvedContentId, candidateContentId, StringComparison.Ordinal))
            {
                Debug.LogWarning(
                    $"[AugmentManager] Legacy name '{expectedName}' is ambiguous across ContentIds; rejected.");
                return null;
            }
        }

        return resolved;
    }

    public MonsterData FindMonsterDataByName(string monsterDataName)
    {
        if (string.IsNullOrWhiteSpace(monsterDataName))
        {
            return null;
        }

        foreach (var augment in EnumerateLoadedAugments())
        {
            if (augment == null)
            {
                continue;
            }

            for (int effectIndex = 0; effectIndex < augment.EffectCount; effectIndex++)
            {
                AugmentEffectData effect = augment.GetEffect(effectIndex);
                if (effect == null)
                {
                    continue;
                }

                if (MatchesMonsterData(effect.bossMonsterData, monsterDataName))
                {
                    return effect.bossMonsterData;
                }

                if (MatchesMonsterData(effect.strengthenedMonsterData, monsterDataName))
                {
                    return effect.strengthenedMonsterData;
                }

                var entries = effect.monsterSpawnEntries;
                if (entries == null)
                {
                    continue;
                }

                foreach (var entry in entries)
                {
                    if (MatchesMonsterData(entry?.monsterData, monsterDataName))
                    {
                        return entry.monsterData;
                    }
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

        PlayerManager opponentTarget = null;
        bool opponentResolved = false;
        for (int effectIndex = 0; effectIndex < chosenAugment.EffectCount; effectIndex++)
        {
            AugmentEffectData effect = chosenAugment.GetEffect(effectIndex);
            if (effect == null)
            {
                Debug.LogWarning($"[AugmentManager] '{chosenAugment.augmentName}' contains a null effect at index {effectIndex}; skipped.");
                continue;
            }

            PlayerManager target = playerManager;
            if (effect.targetType == TargetType.Opponent)
            {
                if (!opponentResolved)
                {
                    opponentTarget = ResolveOpponentTarget();
                    opponentResolved = true;
                }

                target = opponentTarget;
            }

            ApplyEffect(target, chosenAugment, effect);
        }
        
        presentedAugments.Clear();

        // 다른 시스템(UI 등)에 상태 변경을 알립니다.
        GameEvents.TriggerAugmentApplied(this.playerManager, chosenAugment);
    }

    private PlayerManager ResolveOpponentTarget()
    {
        PlayerManager target = playerManager.opponentManager;
        // In matches with more than two players, choose one opponent once for the entire composed augment.
        if (target == null && GameManagers.Instance != null && GameManagers.Instance.AllPlayers.Count() > 1)
        {
            var otherPlayers = GameManagers.Instance.AllPlayers.Where(candidate => candidate != playerManager).ToList();
            if (otherPlayers.Count > 0)
            {
                target = otherPlayers[UnityEngine.Random.Range(0, otherPlayers.Count)];
            }
        }

        return target;
    }

    private void ApplyEffect(PlayerManager target, AugmentData augment, AugmentEffectData effect)
    {
        switch (effect.effectType)
        {
            case EffectType.SpawnMonsterOnEnemyField:
                if (effect.isBossSummon)
                {
                    if (effect.bossMonsterData != null)
                    {
                        playerManager.AddOwnedBoss(augment);
                        Debug.Log($"<color=red>[AugmentManager] 보스 증강 등록! Player {playerManager.playerId}가 보스 '{effect.bossMonsterData.monsterName}' 보유</color>");
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
                if (effect.strengthenedMonsterData == null)
                {
                    Debug.LogWarning($"[AugmentManager] Monster strengthening augment '{augment.augmentName}' has no strengthenedMonsterData.");
                }
                return;
            case EffectType.StrengthenKing:
                if (target == null)
                {
                    Debug.LogError($"[AugmentManager] King strengthening augment '{augment.augmentName}' has no target player.");
                    return;
                }

                target.ApplyKingAugment(
                    effect.kingDamageBonusPercent,
                    effect.kingAttackSpeedBonusPercent,
                    effect.kingSkillPowerBonusPercent);
                return;
            case EffectType.GrantMagicScroll:
                if (effect.magicScrollData != null)
                {
                    playerManager.AddMagicScroll(effect.magicScrollData);
                    Debug.Log($"<color=magenta>[AugmentManager] Player {playerManager.playerId}가 마법 스크롤 '{effect.magicScrollData.scrollName}' 획득!</color>");
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

        switch (effect.effectType)
        {
            case EffectType.AddGold:
                target.AddGold((int)effect.value);
                break;
            case EffectType.AddWallPlacementCount:
                int addWalls = Mathf.Max(0, Mathf.RoundToInt(effect.value));
                if (addWalls > 0)
                {
                    target.AddWalls(addWalls);
                    Debug.Log($"<color=cyan>[AugmentManager] Player {target.playerId} gained +{addWalls} walls from '{augment.augmentName}' (stock={target.GetWallCount()})</color>");
                }
                break;
            case EffectType.GrantPermanentWallPlacementCount:
                int permanentWalls = Mathf.Max(0, Mathf.RoundToInt(effect.value));
                if (permanentWalls > 0)
                {
                    target.AddPermanentWallPlacementCount(permanentWalls);
                    Debug.Log($"[AugmentManager] Player {target.playerId} gained +{permanentWalls} permanent wall placements from '{augment.augmentName}' (stock={target.GetPermanentWallPlacementCount()}).");
                }
                break;
            case EffectType.IncreaseMyUnitAttack:
                target.AddPermanentAttackDamagePercent(effect.value);
                Debug.Log($"{target.playerId}의 필드에 '{augment.augmentName}' 영구 공격력 버프 적용 (+{effect.value:P0})");
                break;
            case EffectType.IncreaseMyUnitAttackSpeed:
                target.AddPermanentAttackSpeedPercent(effect.value);
                Debug.Log($"{target.playerId}의 필드에 '{augment.augmentName}' 영구 공격속도 버프 적용 (+{effect.value:P0})");
                break;
            case EffectType.IncreaseBlackMagicMaximum:
                int blackMagicBonus = Mathf.Max(0, Mathf.RoundToInt(effect.value));
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
