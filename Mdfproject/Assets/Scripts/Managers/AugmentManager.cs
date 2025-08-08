using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets; // 어드레서블 사용을 위해 필수
using UnityEngine.ResourceManagement.AsyncOperations; // 비동기 작업을 위해 필수

public class AugmentManager : MonoBehaviour
{
    public PlayerManager playerManager;

    // 어드레서블에서 불러온 모든 증강 데이터를 저장할 private 리스트입니다.
    // 더 이상 인스펙터에서 수동으로 채울 필요가 없습니다.
    private List<AugmentData> allAugments = new List<AugmentData>();
    
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
    /// "Augment" 레이블을 가진 모든 증강 데이터를 어드레서블에서 비동기적으로 불러옵니다.
    /// </summary>
    private async void LoadAllAugmentsFromAddressables()
    {
        Debug.Log($"Player {playerManager.playerId}: 어드레서블에서 증강 데이터 로딩을 시작합니다...");
        
        // "Augment" 레이블을 가진 모든 AugmentData 타입의 애셋을 로드하라는 핸들을 생성합니다.
        AsyncOperationHandle<IList<AugmentData>> handle = Addressables.LoadAssetsAsync<AugmentData>("Augment", null);

        // 비동기 작업이 끝날 때까지 여기서 기다립니다.
        await handle.Task;

        // 작업의 성공 여부를 확인합니다.
        if (handle.Status == AsyncOperationStatus.Succeeded)
        {
            // 성공했다면, 결과를 리스트에 저장하고 로딩 완료 상태로 변경합니다.
            allAugments = handle.Result.ToList();
            isDataLoaded = true;
            Debug.Log($"<color=cyan>Player {playerManager.playerId}: {allAugments.Count}개의 증강 데이터를 성공적으로 로드했습니다.</color>");
        }
        else
        {
            // 실패했다면, 에러 로그를 남깁니다.
            Debug.LogError($"어드레서블에서 증강 데이터 로딩 실패: {handle.OperationException}");
        }
    }

    /// <summary>
    /// 플레이어에게 3개의 랜덤한 증강을 제시합니다.
    /// </summary>
    public void PresentAugments()
    {
        // 방어 코드: 데이터가 아직 로드되지 않았다면, 함수를 실행하지 않고 경고를 남깁니다.
        if (!isDataLoaded)
        {
            Debug.LogWarning("증강 데이터가 아직 로드되지 않았습니다. 잠시 후 다시 시도됩니다.");
            return;
        }

        presentedAugments.Clear();
        
        if (allAugments == null || allAugments.Count == 0)
        {
            Debug.LogWarning($"Player {playerManager.playerId}의 AugmentManager에 로드할 증강 데이터가 없습니다! 'Augment' 레이블이 붙은 어드레서블 애셋이 있는지 확인하세요.");
            return;
        }

        // TODO: 이미 선택한 증강은 제외하는 로직 추가

        // DB에 있는 증강이 3개 미만일 경우를 대비해, 뽑을 개수를 안전하게 계산합니다.
        int countToTake = Mathf.Min(allAugments.Count, 3);
        
        var randomAugments = allAugments.OrderBy(x => Random.value).Take(countToTake).ToList();
        presentedAugments = randomAugments;

        // 뽑힌 증강이 있을 경우에만 로그를 안전하게 출력합니다.
        if (presentedAugments.Count > 0)
        {
            string presentedNames = string.Join(", ", presentedAugments.Select(aug => aug.augmentName));
            Debug.Log($"Player {playerManager.playerId}에게 증강 제시: {presentedNames}");
        }
        else
        {
            Debug.LogWarning($"Player {playerManager.playerId}에게 제시할 증강을 찾을 수 없습니다.");
        }

        // TODO: UI에 3개의 증강 정보를 표시하고, 버튼에 SelectAndApplyAugment(0), (1), (2)를 연결하는 로직 필요
    }

    /// <summary>
    /// 플레이어가 선택한 증강의 효과를 적용합니다. UI 버튼에서 호출됩니다.
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

        PlayerManager target = (chosenAugment.targetType == TargetType.Player) ? playerManager : playerManager.opponentManager;
        ApplyEffect(target, chosenAugment);
        
        // TODO: 증강 선택 UI를 숨기는 로직 필요
    }

    /// <summary>
    /// 증강 타입에 따라 실제 효과를 적용하는 내부 함수입니다.
    /// </summary>
    private void ApplyEffect(PlayerManager target, AugmentData augment)
    {
        if (target == null)
        {
            Debug.LogError("증강 효과를 적용할 대상(Target)이 없습니다!");
            return;
        }

        switch (augment.effectType)
        {
            case EffectType.AddGold:
                target.AddGold((int)augment.value);
                break;

            case EffectType.IncreaseMyUnitAttack:
                // 실제 적용은 유닛 생성 시 또는 버프 목록을 확인하여 처리해야 합니다.
                Debug.Log($"{target.playerId}의 모든 유닛 공격력 {augment.value * 100}% 증가 효과가 추가되었습니다.");
                break;
                
            case EffectType.SpawnBossOnEnemyField:
                if (augment.prefabToSpawn != null)
                {
                    if(target.monsterSpawner != null)
                        target.monsterSpawner.SpawnSpecificMonster(augment.prefabToSpawn);
                    else
                        Debug.LogError("대상의 MonsterSpawner가 없습니다!");
                }
                break;
            
            case EffectType.IncreaseEnemyHealth:
                 // 실제 적용은 몬스터 생성 시 처리됩니다.
                 Debug.Log($"{target.playerId}의 다음 라운드 몬스터 체력 {augment.value * 100}% 증가 효과가 추가되었습니다.");
                 break;
            
            // ... 다른 증강 효과들 추가 ...
        }
    }
}