// Assets/Scripts/Managers/AugmentManager.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using System;

public class AugmentManager : MonoBehaviour
{
    public PlayerManager playerManager;
    public event Action OnAugmentChosen;

    private List<AugmentData> silverAugments = new List<AugmentData>();
    private List<AugmentData> goldAugments = new List<AugmentData>();
    private List<AugmentData> prismaticAugments = new List<AugmentData>();
    private bool isDataLoaded = false;
    private List<AugmentData> presentedAugments = new List<AugmentData>();

    // ✅ [추가] GameManager가 제시된 증강 목록을 가져갈 수 있도록 public 함수를 만듭니다.
    public List<AugmentData> GetPresentedAugments()
    {
        return presentedAugments;
    }

    void Start()
    {
        LoadAllAugmentsFromAddressables();
    }

    private async void LoadAllAugmentsFromAddressables()
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

    public void SelectAndApplyAugment(int choiceIndex)
    {
        if (choiceIndex < 0 || choiceIndex >= presentedAugments.Count)
        {
            Debug.LogError($"잘못된 증강 인덱스({choiceIndex})입니다.");
            return;
        }

        AugmentData chosenAugment = presentedAugments[choiceIndex];
        playerManager.chosenAugments.Add(chosenAugment);
        
        Debug.Log($"Player {playerManager.playerId}가 '<color=yellow>{chosenAugment.augmentName}</color>' 증강 선택!");

        PlayerManager target = (chosenAugment.targetType == TargetType.Player) ? playerManager : playerManager.opponentManager;
        ApplyEffect(target, chosenAugment);
        
        presentedAugments.Clear();
        
        OnAugmentChosen?.Invoke();
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
                Debug.Log($"{target.playerId}의 필드에 '{augment.augmentName}' 효과가 추가되었습니다.");
                break;
            case EffectType.SpawnBossOnEnemyField:
                if (augment.prefabToSpawn != null && target.monsterSpawner != null)
                {
                    target.monsterSpawner.SpawnSpecificMonster(augment.prefabToSpawn);
                }
                break;
            case EffectType.IncreaseEnemyHealth:
            case EffectType.IncreaseEnemyMoveSpeed:
                 Debug.Log($"{target.playerId}의 다음 라운드 몬스터에게 '{augment.augmentName}' 효과가 추가되었습니다.");
                 break;
        }
    }
}