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
    /// 증강 데이터 로딩을 보장하고, 멀티플레이/싱글플레이 상황에 맞게 증강 제시를 요청합니다.
    /// </summary>
    public async UniTask EnsureAugmentsPresentedAsync(IEnumerable<string> augmentNamesFromServer = null)
    {
        // 1. 증강 데이터(Addressables) 로드가 완료될 때까지 기다림
        // isDataLoaded가 true가 될 때까지 기다립니다. (LoadAllAugmentsFromAddressables의 완료 보장)
        await WaitUntilAugmentDataLoaded(); 

        // 2. 증강 목록을 채우는 로직 실행
        if (augmentNamesFromServer != null && augmentNamesFromServer.Any())
        {
            // 서버/호스트로부터 동기화할 이름 목록이 있을 경우
            SetPresentedAugmentsByNames(augmentNamesFromServer);
        }
        else
        {
            // 이름 목록이 없을 경우 (싱글 플레이이거나 호스트가 직접 제시하는 경우)
            PresentAugments();
        }
        
        // 이 시점에는 presentedAugments.Count가 1 이상이 될 확률이 높습니다.
        if (presentedAugments.Count == 0)
        {
            // 예외 상황: 로드는 끝났는데 제시할 증강이 없는 경우 (데이터 오류/룰렛 문제 등)
            Debug.LogError("로딩은 완료되었으나, 제시 가능한 증강이 없어 presentedAugments가 비어있습니다.");
        }
    }

    /// <summary>
    /// 서버(호스트)에서 브로드캐스트된 증강 이름 목록을 기반으로 현재 제시 증강을 동기화합니다.
    /// 클라이언트의 어드레서블 로딩이 끝난 후에 적용됩니다.
    /// </summary>
    public void SetPresentedAugmentsByNames(IEnumerable<string> augmentNames)
    {
        if (!isDataLoaded)
        {
            Debug.LogWarning("증강 데이터 로딩 전 동기화 요청이 도착했습니다. 로딩 완료 후 적용을 시도합니다.");
        }

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
                Debug.LogWarning($"서버가 보낸 증강 '{name}'을(를) 찾지 못했습니다. (아직 로드 중이거나 라벨/이름 불일치)");
            }
        }

        // 동기화 결과는 UI에서 표시되므로 여기서는 추가 로그를 남기지 않습니다.
    }

    // [수정] void Start() -> async void Start()
    async void Start()
    {
        // [추가] playerManager 참조가 할당될 때까지 비동기적으로 기다립니다.
        // 이렇게 하면 NullReferenceException을 방지할 수 있습니다.
        await UniTask.WaitUntil(() => playerManager != null);
        
        LoadAllAugmentsFromAddressables();
    }
    

    private async void LoadAllAugmentsFromAddressables()
    {
        
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
        
    }

    public void SelectAndApplyAugment(AugmentData chosenAugment)
    {
        playerManager.chosenAugments.Add(chosenAugment);

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
            case EffectType.IncreaseMyUnitAttack:
            case EffectType.IncreaseMyUnitAttackSpeed:
                break;
            case EffectType.SpawnBossOnEnemyField:
                if (augment.prefabToSpawn != null && target.monsterSpawner != null)
                {
                    target.monsterSpawner.SpawnSpecificMonster(augment.prefabToSpawn);
                }
                break;
            case EffectType.IncreaseEnemyHealth:
            case EffectType.IncreaseEnemyMoveSpeed:
                 break;
        }
    }
}