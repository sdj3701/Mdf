// Assets/Scripts/Managers/AugmentManager.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public class AugmentManager : MonoBehaviour
{
    public PlayerManager playerManager;

    // 어드레서블에서 불러온 증강 데이터를 등급별로 나누어 저장합니다.
    private List<AugmentData> silverAugments = new List<AugmentData>();
    private List<AugmentData> goldAugments = new List<AugmentData>();
    private List<AugmentData> prismaticAugments = new List<AugmentData>();
    
    // 데이터 로딩이 완료되었는지 확인하는 상태 변수입니다.
    private bool isDataLoaded = false;

    // 현재 플레이어에게 제시된 3개의 증강 리스트입니다.
    private List<AugmentData> presentedAugments = new List<AugmentData>();

    void Start()
    {
        // 게임이 시작되면, 어드레서블 시스템으로부터 증강 데이터 로딩을 시작합니다.
        LoadAllAugmentsFromAddressables();
    }

    /// <summary>
    /// "Augment" 레이블을 가진 모든 증강 데이터를 어드레서블에서 비동기적으로 불러와 등급별로 분류합니다.
    /// </summary>
    private async void LoadAllAugmentsFromAddressables()
    {
        Debug.Log($"Player {playerManager.playerId}: 어드레서블에서 증강 데이터 로딩을 시작합니다...");
        
        AsyncOperationHandle<IList<AugmentData>> handle = Addressables.LoadAssetsAsync<AugmentData>("Augment", null);
        await handle.Task;

        if (handle.Status == AsyncOperationStatus.Succeeded)
        {
            // 로드 성공 시, 각 증강을 등급에 맞는 리스트에 추가합니다.
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

    /// <summary>
    /// 정해진 확률에 따라 등급을 결정하고, 해당 등급의 증강 3개를 무작위로 제시합니다.
    /// </summary>
    public void PresentAugments()
    {
        if (!isDataLoaded)
        {
            Debug.LogWarning("증강 데이터가 아직 로드되지 않았습니다.");
            return;
        }

        presentedAugments.Clear();
        
        // 1. 확률에 따라 제시할 증강의 등급과 리스트를 결정합니다.
        float roll = Random.value; // 0.0 ~ 1.0 사이의 난수 생성
        List<AugmentData> sourceList;
        string tierName;

        if (roll < 0.10f && prismaticAugments.Count >= 3) // 10% 확률 (프리즘)
        {
            sourceList = prismaticAugments;
            tierName = "프리즘";
        }
        else if (roll < 0.40f && goldAugments.Count >= 3) // 30% 확률 (골드)
        {
            sourceList = goldAugments;
            tierName = "골드";
        }
        else // 60% 확률 (실버) 또는 상위 등급 증강 개수가 부족할 경우
        {
            sourceList = silverAugments;
            tierName = "실버";
        }
        
        // 뽑아야 할 증강 리스트가 비어있거나 부족할 경우에 대한 방어 코드
        if (sourceList == null || sourceList.Count == 0)
        {
            Debug.LogError($"제시할 {tierName} 등급의 증강이 부족하거나 없습니다! 'Augment' 레이블과 등급 설정을 확인하세요.");
            return;
        }

        // 2. 해당 등급 리스트에서 3개를 무작위로 선택합니다.
        int countToTake = Mathf.Min(sourceList.Count, 3);
        presentedAugments = sourceList.OrderBy(x => Random.value).Take(countToTake).ToList();

        // 3. 결과를 로그로 출력하고 UI에 표시할 준비를 합니다.
        string presentedNames = string.Join(", ", presentedAugments.Select(aug => aug.augmentName));
        Debug.Log($"Player {playerManager.playerId}에게 <color=yellow>{tierName} 등급</color> 증강 제시: {presentedNames}");

        // TODO: AugmentSelectionUIController가 이 정보를 UI에 표시하도록 연동
    }

    /// <summary>
    /// 플레이어가 선택한 증강의 효과를 적용합니다. UI 버튼 클릭 시 호출됩니다.
    /// </summary>
    /// <param name="choiceIndex">선택한 증강의 인덱스 (0, 1, 2)</param>
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

        // 효과를 적용할 대상을 결정합니다 (나 또는 상대방).
        PlayerManager target = (chosenAugment.targetType == TargetType.Player) ? playerManager : playerManager.opponentManager;
        ApplyEffect(target, chosenAugment);
        
        // 증강 선택 후에는 더 이상 선택할 수 없도록 리스트를 비웁니다.
        presentedAugments.Clear();
        
        // TODO: 증강 선택 UI를 숨기는 로직 호출 (UIManagers.Instance.ReturnUIElement(...))
    }

    /// <summary>
    /// 증강 타입에 따라 실제 효과를 적용하는 내부 함수입니다.
    /// </summary>
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
                // 이 효과들은 PlayerManager의 버프 목록에 추가되고, 유닛이 생성되거나 공격할 때 이 버프를 참조하여 적용됩니다.
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
                 // 이 효과들은 상대방 PlayerManager의 디버프 목록에 추가되고, 몬스터가 생성될 때 이 효과를 참조하여 적용됩니다.
                 Debug.Log($"{target.playerId}의 다음 라운드 몬스터에게 '{augment.augmentName}' 효과가 추가되었습니다.");
                 break;
            
            // ... 다른 증강 효과들 추가 ...
        }
    }
}