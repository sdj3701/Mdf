// Assets/Scripts/Commands/Sync/NotifyAugmentSelectedCommand.cs

using UnityEngine;

/// <summary>
/// 증강 선택을 클라이언트에 알리는 커맨드
/// </summary>
public class NotifyAugmentSelectedCommand : ICommand
{
    public int PlayerId { get; set; }
    public string AugmentName { get; private set; }

    public NotifyAugmentSelectedCommand(int playerId, string augmentName)
    {
        PlayerId = playerId;
        AugmentName = augmentName ?? string.Empty;
    }

    public void Execute()
    {
        var gm = GameManagers.Instance;
        if (gm == null) return;

        var player = gm.GetPlayer(PlayerId);
        if (player?.augmentManager == null) return;

        var chosenAugment = player.augmentManager.FindAugmentByName(AugmentName);
        if (chosenAugment == null)
        {
            Debug.LogWarning($"[NotifyAugmentSelectedCommand] 증강 '{AugmentName}'을 찾을 수 없습니다.");
            return;
        }

        bool isServer = gm.Runner != null && gm.Runner.IsServer;
        
        if (!isServer && chosenAugment.effectType == EffectType.SpawnMonsterOnEnemyField)
        {
            if (chosenAugment.isBossSummon && chosenAugment.bossMonsterData != null)
            {
                player.AddOwnedBoss(chosenAugment);
                Debug.Log($"<color=cyan>[NotifyAugmentSelectedCommand] 클라이언트 Player {PlayerId}: 보스 '{chosenAugment.bossMonsterData.monsterName}' 보유 등록</color>");
            }
            else if (chosenAugment.monsterSpawnEntries != null && chosenAugment.monsterSpawnEntries.Count > 0)
            {
                player.RegisterActiveMonsterSummonAugment(chosenAugment);
                Debug.Log($"<color=cyan>[NotifyAugmentSelectedCommand] 클라이언트 Player {PlayerId}: 일반 몬스터 소환 증강 '{AugmentName}' 등록</color>");
            }
        }

        // 클라이언트에서도 스탯 버프 효과 적용 (호스트와 동기화)
        if (!isServer)
        {
            PlayerManager target = (chosenAugment.targetType == TargetType.Player)
                ? player
                : player.opponentManager;

            switch (chosenAugment.effectType)
            {
                case EffectType.IncreaseMyUnitAttack:
                    if (target != null)
                    {
                        target.permanentAttackDamagePercent += chosenAugment.value;
                        target.ApplyPermanentBonusesToUnitsOnField();
                        Debug.Log($"<color=cyan>[NotifyAugmentSelectedCommand] 클라이언트 Player {target.playerId}: 공격력 버프 +{chosenAugment.value:P0} 적용</color>");
                    }
                    break;
                case EffectType.IncreaseMyUnitAttackSpeed:
                    if (target != null)
                    {
                        target.permanentAttackSpeedPercent += chosenAugment.value;
                        target.ApplyPermanentBonusesToUnitsOnField();
                        Debug.Log($"<color=cyan>[NotifyAugmentSelectedCommand] 클라이언트 Player {target.playerId}: 공격속도 버프 +{chosenAugment.value:P0} 적용</color>");
                    }
                    break;
            }
        }

        GameEvents.TriggerAugmentApplied(player, chosenAugment);
        Debug.Log($"<color=green>[NotifyAugmentSelectedCommand] Player {PlayerId}: '{AugmentName}' 선택 알림</color>");
    }
}
