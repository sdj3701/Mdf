using System;
using Fusion;
using UnityEngine;

[Serializable]
public struct DemonRuntimeMigrationState
{
    public int SelectedDemonKeyHash;
    public bool SkillUsedThisAttack;
    public int AttackSequenceId;
    public int SkillPresentationSequence;
}

public partial class PlayerManager
{
    [Networked] public int SelectedDemonKeyHash { get; private set; }
    [Networked] private NetworkBool DemonSkillUsedForAttack { get; set; }
    [Networked] private int DemonAttackSequenceId { get; set; }
    [Networked] private int DemonSkillPresentationSequence { get; set; }

    public bool DemonSkillUsedThisAttack => DemonSkillUsedForAttack;
    public int CurrentDemonAttackSequenceId => DemonAttackSequenceId;
    public int CurrentDemonSkillPresentationSequence => DemonSkillPresentationSequence;
    public bool DemonRuntimeDataReady => DemonSelectionCatalog.IsAllowedHash(SelectedDemonKeyHash);
    public DemonSelectionCatalog.Entry SelectedDemon =>
        DemonSelectionCatalog.TryGetByHash(SelectedDemonKeyHash, out DemonSelectionCatalog.Entry entry)
            ? entry
            : default;

    public bool CanUseDemonSkill
    {
        get
        {
            GameManagers gm = GameManagers.Instance;
            return IsDemonAttackPhase(gm)
                   && DemonAttackSequenceId == BlackMagicSequenceId
                   && !DemonSkillUsedForAttack
                   && HasLivingDemonSkillTargets();
        }
    }

    private void InitializeDemonRuntimeOnSpawn()
    {
        if (Object != null
            && Object.IsValid
            && Object.HasStateAuthority
            && !DemonSelectionCatalog.IsAllowedHash(SelectedDemonKeyHash))
        {
            SelectedDemonKeyHash = DemonSelectionCatalog.DefaultKeyHash;
        }
    }

    public void SetSelectedDemonKeyHashAuthoritative(int requestedHash)
    {
        if (Object != null && Object.IsValid && !Object.HasStateAuthority)
        {
            return;
        }

        SelectedDemonKeyHash = DemonSelectionCatalog.NormalizeOrDefaultHash(requestedHash);
    }

    public void BeginDemonAttackSequenceAuthoritative(int sequenceId)
    {
        if (!HasStateAuthorityOrNoNetwork() || sequenceId < 0 || DemonAttackSequenceId == sequenceId)
        {
            return;
        }

        DemonAttackSequenceId = sequenceId;
        DemonSkillUsedForAttack = false;
    }

    public bool HasLivingDemonSkillTargets()
    {
        Transform parent = opponentManager != null
            && opponentManager.monsterSpawner != null
            ? opponentManager.monsterSpawner.monsterParent
            : null;
        if (parent == null)
        {
            return false;
        }

        int childCount = parent.childCount;
        for (int i = 0; i < childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child != null
                && child.gameObject.activeInHierarchy
                && child.TryGetComponent(out Monster monster)
                && monster.CurrentHealth > 0f
                && monster.SnapshotSpawnAttackerPlayerId == playerId)
            {
                return true;
            }
        }

        return false;
    }

    public bool TryActivateDemonSkillAuthoritative()
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority || !CanUseDemonSkill)
        {
            return false;
        }

        if (!DemonSelectionCatalog.TryGetByHash(
                SelectedDemonKeyHash,
                out DemonSelectionCatalog.Entry demon))
        {
            return false;
        }

        Transform parent = opponentManager != null
            && opponentManager.monsterSpawner != null
            ? opponentManager.monsterSpawner.monsterParent
            : null;
        if (parent == null)
        {
            return false;
        }

        int appliedCount = 0;
        int childCount = parent.childCount;
        for (int i = 0; i < childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child == null
                || !child.gameObject.activeInHierarchy
                || !child.TryGetComponent(out Monster monster)
                || monster.CurrentHealth <= 0f
                || monster.SnapshotSpawnAttackerPlayerId != playerId)
            {
                continue;
            }

            monster.ApplyDemonSkillBuff(
                demon.SkillHealthMultiplier,
                demon.SkillMoveSpeedMultiplier,
                demon.SkillDamageMultiplier,
                demon.SkillHealFraction);
            appliedCount++;
        }

        if (appliedCount == 0)
        {
            return false;
        }

        DemonSkillUsedForAttack = true;
        DemonSkillPresentationSequence = DemonSkillPresentationSequence == int.MaxValue
            ? 1
            : DemonSkillPresentationSequence + 1;
        return true;
    }

    private bool IsDemonAttackPhase(GameManagers gm)
    {
        return gm != null
               && IsReadyForPlayerActions
               && DemonRuntimeDataReady
               && BattleCommandValidator.IsBattlePhase(gm)
               && IsActivelyFighting
               && IsAttackerInCurrentBattle
               && BlackMagicSequenceId >= 0;
    }

    public DemonRuntimeMigrationState CaptureDemonRuntimeMigrationState()
    {
        return new DemonRuntimeMigrationState
        {
            SelectedDemonKeyHash = SelectedDemonKeyHash,
            SkillUsedThisAttack = DemonSkillUsedForAttack,
            AttackSequenceId = DemonAttackSequenceId,
            SkillPresentationSequence = DemonSkillPresentationSequence
        };
    }

    public bool RestoreDemonRuntimeAfterHostMigration(DemonRuntimeMigrationState state, string context)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
        {
            Debug.LogError($"[Demon] Host migration restore requires State Authority ({context})", this);
            return false;
        }

        SelectedDemonKeyHash = DemonSelectionCatalog.NormalizeOrDefaultHash(state.SelectedDemonKeyHash);
        DemonSkillUsedForAttack = state.SkillUsedThisAttack;
        DemonAttackSequenceId = state.AttackSequenceId;
        DemonSkillPresentationSequence = Mathf.Max(0, state.SkillPresentationSequence);
        return true;
    }
}
