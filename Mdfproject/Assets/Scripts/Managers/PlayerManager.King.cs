using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fusion;
using UnityEngine;

[Serializable]
public struct KingRuntimeMigrationState
{
    public int SelectedKingUnitKeyHash;
    public bool SkillUsedThisDefense;
    public int DefenseSequenceId;
    public int DamageReactionSequence;
    public int AttackPresentationSequence;
    public Vector3 AttackPresentationTargetPosition;
    public int SkillPresentationSequence;
    public int AttackDamageBonusPermille;
    public int AttackSpeedBonusPermille;
    public int SkillPowerBonusPermille;
    public float NextAttackRemainingSeconds;
}

public partial class PlayerManager
{
    private const float KING_BONUS_NETWORK_SCALE = 10000f;
    private const float KING_IDLE_TARGET_RETRY_SECONDS = 0.2f;
    private const float KING_MIN_ATTACK_INTERVAL_SECONDS = 0.08f;
    private const float KING_DAMAGE_REACTION_SECONDS = 0.32f;
    private const float KING_LOAD_RETRY_MIN_SECONDS = 0.25f;
    private const float KING_LOAD_RETRY_MAX_SECONDS = 5f;
    private static readonly int KingAttackTrigger = Animator.StringToHash("AttackTrigger");
    private static readonly int KingBaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int KingColor = Shader.PropertyToID("_Color");

    [Networked] public int SelectedKingUnitKeyHash { get; private set; }
    [Networked] private NetworkBool KingSkillUsedForDefense { get; set; }
    [Networked] private int KingDefenseSequenceId { get; set; }
    [Networked] private int KingDamageReactionSequence { get; set; }
    [Networked] private int KingAttackPresentationSequence { get; set; }
    [Networked] private Vector3 KingAttackPresentationTargetPosition { get; set; }
    [Networked] private int KingSkillPresentationSequence { get; set; }
    [Networked] private int KingAttackDamageBonusPermille { get; set; }
    [Networked] private int KingAttackSpeedBonusPermille { get; set; }
    [Networked] private int KingSkillPowerBonusPermille { get; set; }
    [Networked] private TickTimer KingNextAttackTimer { get; set; }

    private KingUnitData _selectedKingData;
    private int _kingLoadGeneration;
    private int _kingLoadRequestedHash;
    private int _kingDataLoadedHash;
    private bool _kingDataLoadInProgress;
    private int _kingDataLoadInProgressHash;
    private int _kingDataLoadFailureCount;
    private float _kingDataRetryNotBefore;
    private int _lastObservedKingHash;
    private int _lastObservedKingDamageReactionSequence;
    private int _lastObservedKingAttackPresentationSequence;
    private int _lastObservedKingSkillPresentationSequence;
    private Transform _kingPresentationAnchor;
    private GameObject _kingPresentation;
    private Animator _kingAnimator;
    private HeadLookController _kingHeadLookController;
    private Renderer[] _kingRenderers = Array.Empty<Renderer>();
    private MaterialPropertyBlock[] _kingPropertyBlocks = Array.Empty<MaterialPropertyBlock>();
    private MaterialPropertyBlock[] _kingOriginalPropertyBlocks = Array.Empty<MaterialPropertyBlock>();
    private Coroutine _kingDamageReactionRoutine;
    private bool _kingDamageReactionPlaying;
    private bool _kingPresentationLoadInProgress;
    private int _kingPresentationLoadGeneration;
    private int _kingPresentationLoadFailureCount;
    private float _kingPresentationRetryNotBefore;
    private Vector3 _kingCanonicalLocalPosition;
    private Quaternion _kingCanonicalLocalRotation = Quaternion.identity;
    private Vector3 _kingCanonicalLocalScale = Vector3.one;
    private Vector3 _kingExpectedWorldScale = Vector3.one;
    private bool _kingHasCanonicalTransform;
    private Transform _kingRigRoot;
    private Vector3 _kingRigLocalPosition;
    private Quaternion _kingRigLocalRotation = Quaternion.identity;
    private Vector3 _kingRigLocalScale = Vector3.one;
    private bool _kingRigPinRequired;
    private bool _kingHasRigLocalRotation;
    private bool _kingFacesCamera;
    private float _kingCameraYawOffsetDegrees;
    private readonly List<Monster> _kingSkillTargetSnapshot = new List<Monster>(64);

    public KingUnitData SelectedKingData => _selectedKingData;
    public UnitData SelectedKingBaseUnitData => _selectedKingData != null ? _selectedKingData.baseUnitData : null;
    public KingBuffData SelectedKingBuff => _selectedKingData != null ? _selectedKingData.kingBuff : null;
    public KingSkillData SelectedKingSkill => _selectedKingData != null ? _selectedKingData.kingSkill : null;
    public bool KingSkillUsedThisDefense => KingSkillUsedForDefense;
    public bool KingRuntimeDataReady => IsKingRuntimeDataReady(out _);

    public bool IsKingRuntimeDataReady(out string reason)
    {
        int selectedHash = SelectedKingUnitKeyHash;
        if (!KingSelectionCatalog.IsAllowedHash(selectedHash))
        {
            reason = $"selectionInvalid:{selectedHash}";
            return false;
        }

        if (_kingLoadRequestedHash != selectedHash)
        {
            reason = $"selectionLoadNotRequested:{selectedHash}";
            return false;
        }

        if (_kingDataLoadInProgress && _kingDataLoadInProgressHash == selectedHash)
        {
            reason = $"dataLoading:{KingSelectionCatalog.GetAssetKey(selectedHash)}";
            return false;
        }

        if (_kingDataLoadedHash != selectedHash || _selectedKingData == null)
        {
            reason = $"dataNotLoaded:{KingSelectionCatalog.GetAssetKey(selectedHash)}";
            return false;
        }

        if (_selectedKingData.baseUnitData == null)
        {
            reason = "baseUnitData=null";
            return false;
        }

        if (_selectedKingData.kingBuff == null)
        {
            reason = "kingBuff=null";
            return false;
        }

        if (_selectedKingData.kingSkill == null)
        {
            reason = "kingSkill=null";
            return false;
        }

        reason = null;
        return true;
    }

    public bool CanUseKingSkill
    {
        get
        {
            GameManagers gm = GameManagers.Instance;
            if (gm == null || SelectedKingSkill == null || !IsKingDefensePhase(gm))
            {
                return false;
            }

            return KingDefenseSequenceId == BuildKingDefenseSequenceId(gm.currentRound, gm.currentState)
                   && !KingSkillUsedForDefense;
        }
    }

    public float CurrentKingAttackDamage
    {
        get
        {
            int round = GameManagers.Instance != null ? Mathf.Max(1, GameManagers.Instance.currentRound) : 1;
            return _selectedKingData != null
                ? _selectedKingData.ResolveAttackDamage(round, DecodeKingBonus(KingAttackDamageBonusPermille))
                : 0f;
        }
    }

    public float CurrentKingAttackSpeed
    {
        get
        {
            int round = GameManagers.Instance != null ? Mathf.Max(1, GameManagers.Instance.currentRound) : 1;
            return _selectedKingData != null
                ? _selectedKingData.ResolveAttackSpeed(round, DecodeKingBonus(KingAttackSpeedBonusPermille))
                : 0f;
        }
    }

    private float CurrentKingSkillPowerMultiplier => 1f + DecodeKingBonus(KingSkillPowerBonusPermille);

    private void InitializeKingRuntimeOnSpawn()
    {
        _kingLoadGeneration++;
        _kingLoadRequestedHash = 0;
        _kingDataLoadedHash = 0;
        _kingDataLoadInProgress = false;
        _kingDataLoadInProgressHash = 0;
        _kingDataLoadFailureCount = 0;
        _kingDataRetryNotBefore = 0f;
        _kingPresentationLoadFailureCount = 0;
        _kingPresentationRetryNotBefore = 0f;
        _lastObservedKingHash = SelectedKingUnitKeyHash;
        _lastObservedKingDamageReactionSequence = KingDamageReactionSequence;
        _lastObservedKingAttackPresentationSequence = KingAttackPresentationSequence;
        _lastObservedKingSkillPresentationSequence = KingSkillPresentationSequence;

        if (Object != null && Object.IsValid && Object.HasStateAuthority && SelectedKingUnitKeyHash == 0)
        {
            SelectedKingUnitKeyHash = KingSelectionCatalog.DefaultKeyHash;
        }

        QueueKingDataLoad(SelectedKingUnitKeyHash);
    }

    private void RenderKingRuntime()
    {
        int selectedHash = SelectedKingUnitKeyHash;
        if (_lastObservedKingHash != selectedHash)
        {
            _lastObservedKingHash = selectedHash;
            QueueKingDataLoad(selectedHash);
        }
        else if (!IsKingRuntimeDataReady(out _))
        {
            // Failed Addressables loads are retried with backoff instead of becoming permanently stale.
            QueueKingDataLoad(selectedHash);
        }

        EnsureKingPresentationAtGoal();

        if (_lastObservedKingDamageReactionSequence != KingDamageReactionSequence)
        {
            if (PlayKingDamageReactionPresentation())
            {
                _lastObservedKingDamageReactionSequence = KingDamageReactionSequence;
            }
        }

        if (_lastObservedKingAttackPresentationSequence != KingAttackPresentationSequence)
        {
            if (PlayKingAttackPresentation(KingAttackPresentationTargetPosition))
            {
                _lastObservedKingAttackPresentationSequence = KingAttackPresentationSequence;
            }
        }

        if (_lastObservedKingSkillPresentationSequence != KingSkillPresentationSequence)
        {
            if (PlayKingSkillPresentation())
            {
                _lastObservedKingSkillPresentationSequence = KingSkillPresentationSequence;
            }
        }
    }

    private void OnKingGoalTransformReady()
    {
        if (_selectedKingData != null)
        {
            EnsureKingPresentationAtGoal();
        }
        else
        {
            QueueKingDataLoad(SelectedKingUnitKeyHash);
        }
    }

    public void SetSelectedKingKeyHashAuthoritative(int requestedHash)
    {
        if (Object != null && Object.IsValid && !Object.HasStateAuthority)
        {
            return;
        }

        int resolvedHash = KingSelectionCatalog.NormalizeOrDefaultHash(requestedHash);
        if (Object != null && Object.IsValid)
        {
            SelectedKingUnitKeyHash = resolvedHash;
        }

        _lastObservedKingHash = resolvedHash;
        QueueKingDataLoad(resolvedHash);
    }

    public void ApplyKingAugment(
        float damagePercentBonus,
        float attackSpeedPercentBonus,
        float skillPowerPercentBonus)
    {
        if (Object != null && Object.IsValid && !Object.HasStateAuthority)
        {
            return;
        }

        if (Object == null || !Object.IsValid)
        {
            return;
        }

        KingAttackDamageBonusPermille = AddEncodedKingBonus(KingAttackDamageBonusPermille, damagePercentBonus);
        KingAttackSpeedBonusPermille = AddEncodedKingBonus(KingAttackSpeedBonusPermille, attackSpeedPercentBonus);
        KingSkillPowerBonusPermille = AddEncodedKingBonus(KingSkillPowerBonusPermille, skillPowerPercentBonus);
    }

    public void TriggerKingDamageReactionAuthoritative()
    {
        if (Object != null && Object.IsValid)
        {
            if (!Object.HasStateAuthority)
            {
                return;
            }

            KingDamageReactionSequence = NextKingPresentationSequence(KingDamageReactionSequence);
            return;
        }

        PlayKingDamageReactionPresentation();
    }

    public bool TryActivateKingSkillAuthoritative()
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
        {
            return false;
        }

        GameManagers gm = GameManagers.Instance;
        if (gm == null || SelectedKingSkill == null || !IsKingDefensePhase(gm))
        {
            return false;
        }

        EnsureKingDefenseSequence(gm);
        if (KingSkillUsedForDefense)
        {
            return false;
        }

        KingSkillUsedForDefense = true;
        KingSkillPresentationSequence = NextKingPresentationSequence(KingSkillPresentationSequence);
        ApplyKingSkillAuthoritative(SelectedKingSkill);
        return true;
    }

    public override void FixedUpdateNetwork()
    {
        if (Object == null || !Object.HasStateAuthority)
        {
            return;
        }

        GameManagers gm = GameManagers.Instance;
        if (gm == null || !IsKingDefensePhase(gm))
        {
            KingNextAttackTimer = TickTimer.None;
            return;
        }

        EnsureKingDefenseSequence(gm);
        if (_selectedKingData == null || _selectedKingData.baseUnitData == null)
        {
            return;
        }

        if (KingNextAttackTimer.IsRunning && !KingNextAttackTimer.Expired(Runner))
        {
            return;
        }

        Monster target = FindHighestThreatLivingMonster();
        if (target == null)
        {
            KingNextAttackTimer = TickTimer.CreateFromSeconds(Runner, KING_IDLE_TARGET_RETRY_SECONDS);
            return;
        }

        float damage = CurrentKingAttackDamage;
        if (damage > 0f)
        {
            target.TakeDamage(damage, _selectedKingData.baseUnitData.damageType);
        }

        KingAttackPresentationTargetPosition = target.transform.position;
        KingAttackPresentationSequence = NextKingPresentationSequence(KingAttackPresentationSequence);
        float attackSpeed = Mathf.Max(0.01f, CurrentKingAttackSpeed);
        float attackInterval = Mathf.Max(KING_MIN_ATTACK_INTERVAL_SECONDS, 1f / attackSpeed);
        KingNextAttackTimer = TickTimer.CreateFromSeconds(Runner, attackInterval);
    }

    private void EnsureKingDefenseSequence(GameManagers gm)
    {
        int expectedSequence = BuildKingDefenseSequenceId(gm.currentRound, gm.currentState);
        if (KingDefenseSequenceId == expectedSequence)
        {
            return;
        }

        KingDefenseSequenceId = expectedSequence;
        KingSkillUsedForDefense = false;
        KingNextAttackTimer = TickTimer.CreateFromSeconds(Runner, 0.1f);
    }

    private bool IsKingDefensePhase(GameManagers gm)
    {
        return gm != null
               && IsReadyForPlayerActions
               && IsKingRuntimeDataReady(out _)
               && BattleCommandValidator.IsBattlePhase(gm)
               && IsActivelyFighting
               && !IsAttackerInCurrentBattle;
    }

    public static int BuildKingDefenseSequenceId(int round, GameManagers.GameState state)
    {
        int phase = state == GameManagers.GameState.Battle2 ? 2 : 1;
        return Mathf.Max(0, round) * 4 + phase;
    }

    private Monster FindHighestThreatLivingMonster()
    {
        Transform parent = monsterSpawner != null ? monsterSpawner.monsterParent : null;
        if (parent == null)
        {
            return null;
        }

        Vector3 goalPosition = goalTransform != null ? goalTransform.position : transform.position;
        Monster best = null;
        float bestDistanceSquared = float.PositiveInfinity;
        int childCount = parent.childCount;
        for (int i = 0; i < childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child == null || !child.gameObject.activeInHierarchy
                || !child.TryGetComponent(out Monster monster)
                || !IsLivingKingTarget(monster))
            {
                continue;
            }

            Vector3 offset = child.position - goalPosition;
            offset.y = 0f;
            float distanceSquared = offset.sqrMagnitude;
            if (distanceSquared < bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                best = monster;
            }
        }

        return best;
    }

    private void ApplyKingSkillAuthoritative(KingSkillData skill)
    {
        if (skill == null)
        {
            return;
        }

        if (skill.HealsOwner)
        {
            int healAmount = Mathf.Max(0, skill.healPlayerAmount);
            if (healAmount > 0)
            {
                health = Mathf.Min(initialHealth, health + healAmount);
            }
            if (Runner == null || !Runner.IsRunning)
            {
                GameEvents.TriggerPlayerStatsChanged(playerId, health, gold);
            }
        }

        Transform parent = monsterSpawner != null ? monsterSpawner.monsterParent : null;
        if (parent == null)
        {
            return;
        }

        Vector3 center = goalTransform != null ? goalTransform.position : transform.position;
        float radiusSquared = Mathf.Max(0f, skill.radius) * Mathf.Max(0f, skill.radius);
        float damage = skill.ResolveDamage(CurrentKingAttackDamage, CurrentKingSkillPowerMultiplier);
        _kingSkillTargetSnapshot.Clear();
        int childCount = parent.childCount;
        for (int i = 0; i < childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child == null || !child.gameObject.activeInHierarchy
                || !child.TryGetComponent(out Monster monster)
                || !IsLivingKingTarget(monster))
            {
                continue;
            }

            if (!skill.TargetsWholeField)
            {
                Vector3 offset = child.position - center;
                offset.y = 0f;
                if (offset.sqrMagnitude > radiusSquared)
                {
                    continue;
                }
            }

            _kingSkillTargetSnapshot.Add(monster);
            if (skill.maxTargets > 0 && _kingSkillTargetSnapshot.Count >= skill.maxTargets)
            {
                break;
            }
        }

        int affected = 0;
        int rejectedStatusEffects = 0;
        int targetCount = _kingSkillTargetSnapshot.Count;
        for (int i = 0; i < targetCount; i++)
        {
            Monster monster = _kingSkillTargetSnapshot[i];
            if (!IsLivingKingTarget(monster))
            {
                continue;
            }

            if (damage > 0f)
            {
                monster.TakeDamage(damage, skill.damageType);
            }

            if (IsLivingKingTarget(monster)
                && skill.HasBoundedStatusEffect
                && monster.TryGetComponent(out BuffManager buffManager))
            {
                bool statusApplied = buffManager.ApplyStatusEffect(
                    skill.statusEffect,
                    skill.statusDuration,
                    gameObject,
                    skill.dotTickInterval,
                    skill.dotDamagePerTick * CurrentKingSkillPowerMultiplier,
                    skill.slowMultiplier,
                    skill.damageType);
                if (!statusApplied)
                {
                    rejectedStatusEffects++;
                }
            }

            affected++;
        }

        _kingSkillTargetSnapshot.Clear();

        if (rejectedStatusEffects > 0)
        {
            Debug.LogWarning(
                $"[PlayerManager.King] Bounded status effects were rejected. playerId={playerId}, skill={skill.name}, rejected={rejectedStatusEffects}, affected={affected}");
        }
    }

    private static bool IsLivingKingTarget(Monster monster)
    {
        return monster != null
               && monster.CurrentHealth > 0f
               && (monster.Object == null || monster.Object.IsValid);
    }

    public float GetKingBuffModifier(
        UnitData candidate,
        KingBuffStat stat,
        KingBuffModifierMode mode)
    {
        KingBuffData buff = SelectedKingBuff;
        if (candidate == null || buff == null || !MatchesSelectedKingBaseUnit(candidate))
        {
            return 0f;
        }

        return buff.GetModifier(stat, mode);
    }

    public bool MatchesSelectedKingBaseUnit(UnitData candidate)
    {
        UnitData selectedBase = SelectedKingBaseUnitData;
        if (candidate == null || selectedBase == null)
        {
            return false;
        }

        if (ReferenceEquals(candidate, selectedBase))
        {
            return true;
        }

        int candidateNameHash = StableDataKeyUtility.StableKeyHash(candidate.name);
        int selectedNameHash = StableDataKeyUtility.StableKeyHash(selectedBase.name);
        if (candidateNameHash != 0 && candidateNameHash == selectedNameHash)
        {
            return true;
        }

        int candidateUnitNameHash = StableDataKeyUtility.StableKeyHash(candidate.unitName);
        int selectedUnitNameHash = StableDataKeyUtility.StableKeyHash(selectedBase.unitName);
        return candidateUnitNameHash != 0 && candidateUnitNameHash == selectedUnitNameHash;
    }

    private void QueueKingDataLoad(int requestedHash)
    {
        int resolvedHash = KingSelectionCatalog.NormalizeOrDefaultHash(requestedHash);
        if (_kingLoadRequestedHash != resolvedHash)
        {
            _kingLoadRequestedHash = resolvedHash;
            _kingLoadGeneration++;
            _kingDataLoadedHash = 0;
            _selectedKingData = null;
            _kingDataLoadInProgress = false;
            _kingDataLoadInProgressHash = 0;
            _kingDataLoadFailureCount = 0;
            _kingDataRetryNotBefore = 0f;
            DestroyKingPresentation();
        }

        if (IsKingDataReadyForHash(resolvedHash))
        {
            EnsureKingPresentationAtGoal();
            return;
        }

        if ((_kingDataLoadInProgress && _kingDataLoadInProgressHash == resolvedHash)
            || Time.unscaledTime < _kingDataRetryNotBefore)
        {
            return;
        }

        int generation = _kingLoadGeneration;
        _kingDataLoadInProgress = true;
        _kingDataLoadInProgressHash = resolvedHash;
        LoadKingDataAndPresentationAsync(resolvedHash, generation).Forget();
    }

    private async UniTask LoadKingDataAndPresentationAsync(int keyHash, int generation)
    {
        try
        {
            string assetKey = KingSelectionCatalog.GetAssetKey(keyHash);
            if (string.IsNullOrWhiteSpace(assetKey))
            {
                RegisterKingDataLoadFailure(keyHash, "catalog key is empty");
                return;
            }

            KingUnitData data = await AssetLoader.LoadAssetAsync<KingUnitData>(assetKey, _assetOwner);
            if (this == null || generation != _kingLoadGeneration || keyHash != _kingLoadRequestedHash)
            {
                return;
            }

            if (data == null
                || data.baseUnitData == null
                || data.kingBuff == null
                || data.kingSkill == null)
            {
                RegisterKingDataLoadFailure(keyHash, $"'{assetKey}' is missing data/base/buff/skill references");
                return;
            }

            _selectedKingData = data;
            _kingDataLoadedHash = keyHash;
            _kingDataLoadFailureCount = 0;
            _kingDataRetryNotBefore = 0f;
            DestroyKingPresentation();
            ApplyKingBuffsToOwnedUnits();
            CreateKingPresentationAsync(generation).Forget();
        }
        catch (Exception exception)
        {
            if (this != null && generation == _kingLoadGeneration && keyHash == _kingLoadRequestedHash)
            {
                RegisterKingDataLoadFailure(keyHash, exception.Message);
            }
        }
        finally
        {
            if (this != null
                && generation == _kingLoadGeneration
                && _kingDataLoadInProgressHash == keyHash)
            {
                _kingDataLoadInProgress = false;
                _kingDataLoadInProgressHash = 0;
            }
        }
    }

    private async UniTask CreateKingPresentationAsync(int generation)
    {
        if (_kingPresentationLoadInProgress
            || _kingPresentation != null
            || goalTransform == null
            || Time.unscaledTime < _kingPresentationRetryNotBefore)
        {
            return;
        }

        _kingPresentationLoadInProgress = true;
        _kingPresentationLoadGeneration = generation;
        GameObject instance = null;
        KingUnitData data = _selectedKingData;
        try
        {
            UnitData baseData = data != null ? data.baseUnitData : null;
            if (baseData == null || baseData.prefabsByStarLevel == null || baseData.prefabsByStarLevel.Length == 0)
            {
                RegisterKingPresentationLoadFailure("base unit has no presentation prefab key");
                return;
            }

            string prefabKey = baseData.prefabsByStarLevel[0];
            if (string.IsNullOrWhiteSpace(prefabKey))
            {
                RegisterKingPresentationLoadFailure("base unit presentation prefab key is empty");
                return;
            }

            GameObject prefab = await AssetLoader.LoadAssetAsync<GameObject>(prefabKey, _assetOwner);
            if (this == null
                || generation != _kingLoadGeneration
                || !ReferenceEquals(data, _selectedKingData))
            {
                return;
            }

            if (prefab == null || goalTransform == null)
            {
                RegisterKingPresentationLoadFailure(prefab == null
                    ? $"prefab load failed: {prefabKey}"
                    : "goal transform is not ready");
                return;
            }

            Transform presentationAnchor = GetOrCreateKingPresentationAnchor();
            if (presentationAnchor == null)
            {
                RegisterKingPresentationLoadFailure("goal presentation anchor is not ready");
                return;
            }

            instance = KingVisualCloneUtility.CreateVisualOnly(
                prefab,
                presentationAnchor,
                out Animator animator,
                out Dictionary<Transform, Transform> transformMap);
            if (instance == null)
            {
                RegisterKingPresentationLoadFailure($"visual-only clone failed: {prefabKey}");
                return;
            }

            instance.name = $"KingPresentation_{baseData.name}";
            Transform instanceTransform = instance.transform;
            instanceTransform.localPosition = data.presentationOffset;
            instanceTransform.localRotation = Quaternion.Euler(data.presentationEulerAngles);
            instanceTransform.localScale = Vector3.Scale(
                prefab.transform.localScale,
                Vector3.one * Mathf.Max(0.1f, data.presentationScale));
            _kingExpectedWorldScale = instanceTransform.localScale;

            _kingPresentation = instance;
            _kingAnimator = animator;
            _kingHeadLookController = instance.GetComponentInChildren<HeadLookController>(true);
            ApplyKingHeadLookTuning(_kingHeadLookController, data);
            RefreshKingHeadLookTarget();
            ConfigureKingRigPresentation(prefab, transformMap);
            CaptureKingCanonicalTransform();
            CaptureKingRendererPropertyBlocks(instance);
            instance.SetActive(true);

            if (_kingAnimator != null)
            {
                _kingAnimator.enabled = true;
                _kingAnimator.applyRootMotion = false;
                // Kings are limited to one per player. Keeping these few animators alive also
                // guarantees that their head-look IK is ready immediately after field switching.
                _kingAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                _kingAnimator.Rebind();
                _kingAnimator.Update(0f);
            }

            EnforceKingRigPresentationTransform();
            _kingPresentationLoadFailureCount = 0;
            _kingPresentationRetryNotBefore = 0f;
            instance = null;
        }
        catch (Exception exception)
        {
            if (this != null && generation == _kingLoadGeneration)
            {
                if (instance != null && _kingPresentation == instance)
                {
                    DestroyKingPresentation();
                    instance = null;
                }
                RegisterKingPresentationLoadFailure(exception.Message);
            }
        }
        finally
        {
            if (instance != null)
            {
                DestroyKingPresentationObject(instance);
            }

            if (this != null && _kingPresentationLoadGeneration == generation)
            {
                _kingPresentationLoadInProgress = false;
            }
        }
    }

    private bool IsKingDataReadyForHash(int keyHash)
    {
        return keyHash != 0
               && _kingDataLoadedHash == keyHash
               && _selectedKingData != null
               && _selectedKingData.baseUnitData != null
               && _selectedKingData.kingBuff != null
               && _selectedKingData.kingSkill != null;
    }

    internal static void ApplyKingHeadLookTuning(
        HeadLookController headLookController,
        KingUnitData data)
    {
        if (headLookController == null || data == null || !data.overridePresentationHeadLook)
        {
            return;
        }

        headLookController.lookAtWeight = Mathf.Clamp01(data.presentationHeadLookWeight);
        headLookController.tiltAngle = Mathf.Clamp(data.presentationHeadLookTiltAngle, 0f, 5f);
        headLookController.lookAtBodyWeight = Mathf.Clamp01(data.presentationHeadLookBodyWeight);
        headLookController.lookAtHeadWeight = Mathf.Clamp01(data.presentationHeadLookHeadWeight);
        headLookController.lookAtClampWeight = Mathf.Clamp01(data.presentationHeadLookClampWeight);
    }

    private void RegisterKingDataLoadFailure(int keyHash, string detail)
    {
        if (keyHash != _kingLoadRequestedHash)
        {
            return;
        }

        _selectedKingData = null;
        _kingDataLoadedHash = 0;
        _kingDataLoadFailureCount++;
        float retryDelay = ComputeKingLoadRetryDelay(_kingDataLoadFailureCount);
        _kingDataRetryNotBefore = Time.unscaledTime + retryDelay;
        Debug.LogError(
            $"[King] Data load failed for '{KingSelectionCatalog.GetAssetKey(keyHash)}'; "
            + $"retry in {retryDelay:0.00}s. {detail}",
            this);
    }

    private void RegisterKingPresentationLoadFailure(string detail)
    {
        _kingPresentationLoadFailureCount++;
        float retryDelay = ComputeKingLoadRetryDelay(_kingPresentationLoadFailureCount);
        _kingPresentationRetryNotBefore = Time.unscaledTime + retryDelay;
        Debug.LogWarning(
            $"[King] Presentation load failed; retry in {retryDelay:0.00}s. {detail}",
            this);
    }

    public static float ComputeKingLoadRetryDelay(int failureCount)
    {
        int exponent = Mathf.Clamp(failureCount - 1, 0, 8);
        return Mathf.Min(
            KING_LOAD_RETRY_MAX_SECONDS,
            KING_LOAD_RETRY_MIN_SECONDS * Mathf.Pow(2f, exponent));
    }

    private void ConfigureKingRigPresentation(
        GameObject sourcePrefab,
        Dictionary<Transform, Transform> transformMap)
    {
        _kingRigRoot = null;
        _kingRigLocalPosition = Vector3.zero;
        _kingRigLocalRotation = Quaternion.identity;
        _kingRigLocalScale = Vector3.one;
        _kingRigPinRequired = false;
        _kingHasRigLocalRotation = false;

        UnitOrientationFixer sourceFixer = sourcePrefab != null
            ? sourcePrefab.GetComponentInChildren<UnitOrientationFixer>(true)
            : null;
        _kingFacesCamera = sourceFixer == null
            || sourceFixer.faceCameraOnSpawn
            || sourceFixer.faceCameraEveryFrame;
        _kingCameraYawOffsetDegrees = sourceFixer != null ? sourceFixer.yawOffsetDeg : 180f;
        if (sourceFixer == null)
        {
            return;
        }

        Transform sourceRigRoot = sourceFixer.rigRoot;
        if (sourceRigRoot == null && !string.IsNullOrWhiteSpace(sourceFixer.rigRootName))
        {
            sourceRigRoot = FindKingSourceRig(sourceFixer.transform, sourceFixer.rigRootName);
        }

        if (sourceRigRoot == null)
        {
            return;
        }

        if (sourcePrefab != null && sourceRigRoot == sourcePrefab.transform)
        {
            // Some character prefabs point UnitOrientationFixer.rigRoot at the prefab root.
            // Enforcing that transform here would overwrite the King's camera-facing root yaw.
            return;
        }

        _kingRigPinRequired = true;
        if (!transformMap.TryGetValue(sourceRigRoot, out Transform clonedRigRoot)
            || clonedRigRoot == null
            || (_kingPresentation != null && clonedRigRoot == _kingPresentation.transform))
        {
            return;
        }

        _kingRigRoot = clonedRigRoot;
        _kingRigLocalPosition = clonedRigRoot.localPosition;
        _kingRigLocalRotation = Quaternion.Euler(sourceFixer.rigLocalEulerTarget);
        _kingRigLocalScale = clonedRigRoot.localScale;
        _kingHasRigLocalRotation = true;
    }

    private static Transform FindKingSourceRig(Transform root, string rigName)
    {
        if (root == null || string.IsNullOrWhiteSpace(rigName))
        {
            return null;
        }

        Transform direct = root.Find(rigName);
        if (direct != null)
        {
            return direct;
        }

        Transform[] descendants = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < descendants.Length; i++)
        {
            Transform candidate = descendants[i];
            if (candidate != null && candidate.name == rigName)
            {
                return candidate;
            }
        }

        return null;
    }

    private void CaptureKingCanonicalTransform()
    {
        if (_kingPresentation == null)
        {
            _kingHasCanonicalTransform = false;
            return;
        }

        Transform kingTransform = _kingPresentation.transform;
        _kingCanonicalLocalPosition = kingTransform.localPosition;
        _kingCanonicalLocalRotation = kingTransform.localRotation;
        _kingCanonicalLocalScale = kingTransform.localScale;
        _kingHasCanonicalTransform = true;
    }

    private void CaptureKingRendererPropertyBlocks(GameObject instance)
    {
        _kingRenderers = instance != null
            ? instance.GetComponentsInChildren<Renderer>(true)
            : Array.Empty<Renderer>();
        _kingPropertyBlocks = new MaterialPropertyBlock[_kingRenderers.Length];
        _kingOriginalPropertyBlocks = new MaterialPropertyBlock[_kingRenderers.Length];
        for (int i = 0; i < _kingRenderers.Length; i++)
        {
            _kingPropertyBlocks[i] = new MaterialPropertyBlock();
            _kingOriginalPropertyBlocks[i] = new MaterialPropertyBlock();
            if (_kingRenderers[i] != null)
            {
                _kingRenderers[i].GetPropertyBlock(_kingOriginalPropertyBlocks[i]);
            }
        }
    }

    private void EnforceKingRigPresentationTransform()
    {
        if (_kingHasRigLocalRotation && _kingRigRoot != null)
        {
            _kingRigRoot.localPosition = _kingRigLocalPosition;
            _kingRigRoot.localRotation = _kingRigLocalRotation;
            _kingRigRoot.localScale = _kingRigLocalScale;
        }
    }

    private void LateUpdate()
    {
        if (_kingPresentation != null && _kingPresentation.activeInHierarchy)
        {
            AlignKingPresentationAnchor(_kingPresentationAnchor, goalTransform);
            if (!_kingDamageReactionPlaying && _kingHasCanonicalTransform)
            {
                Transform kingTransform = _kingPresentation.transform;
                kingTransform.localPosition = _kingCanonicalLocalPosition;
                kingTransform.localRotation = _kingCanonicalLocalRotation;
                kingTransform.localScale = _kingCanonicalLocalScale;
            }
            ApplyKingCameraFacingRotation();
            RefreshKingHeadLookTarget();
            EnforceKingRigPresentationTransform();
        }
    }

    private Camera ResolveKingPresentationCamera()
    {
        Camera camera = fieldManager != null ? fieldManager.PlayerCamera : null;
        return camera != null ? camera : Camera.main;
    }

    private void RefreshKingHeadLookTarget()
    {
        if (_kingHeadLookController == null)
        {
            return;
        }

        Camera camera = _selectedKingData != null && _selectedKingData.presentationHeadLookAtCamera
            ? ResolveKingPresentationCamera()
            : null;
        if (camera != null)
        {
            _kingHeadLookController.SetRuntimeLookAtTarget(camera.transform);
        }
        else
        {
            _kingHeadLookController.ClearRuntimeLookAtTarget();
        }
    }

    private bool ApplyKingCameraFacingRotation()
    {
        if (!_kingFacesCamera || _kingPresentation == null)
        {
            return false;
        }

        Transform presentation = _kingPresentation.transform;
        Camera camera = ResolveKingPresentationCamera();
        if (!UnitOrientationFixer.TryResolveCameraFacingYaw(
                presentation.position,
                camera,
                _kingCameraYawOffsetDegrees,
                out Quaternion cameraFacingRotation))
        {
            return false;
        }

        Quaternion presentationOffset = _selectedKingData != null
            ? Quaternion.Euler(_selectedKingData.presentationEulerAngles)
            : Quaternion.identity;
        presentation.rotation = cameraFacingRotation * presentationOffset;
        _kingCanonicalLocalRotation = presentation.localRotation;
        return true;
    }

    private Transform GetOrCreateKingPresentationAnchor()
    {
        if (goalTransform == null)
        {
            return null;
        }

        if (_kingPresentationAnchor == null)
        {
            var anchorObject = new GameObject($"KingGoalAnchor_P{playerId}");
            _kingPresentationAnchor = anchorObject.transform;
        }

        AlignKingPresentationAnchor(_kingPresentationAnchor, goalTransform);
        return _kingPresentationAnchor;
    }

    internal static void AlignKingPresentationAnchor(Transform anchor, Transform goal)
    {
        if (anchor == null || goal == null || anchor == goal)
        {
            return;
        }

        Transform desiredParent = goal.parent;
        if (anchor.parent != desiredParent)
        {
            anchor.SetParent(desiredParent, true);
        }

        anchor.position = goal.position;
        anchor.rotation = Quaternion.identity;

        // Goal is a flattened floor marker (Y scale 0.02). Keep the King on a sibling
        // anchor with unit world scale so the marker cannot squash or displace it.
        Vector3 parentScale = desiredParent != null ? desiredParent.lossyScale : Vector3.one;
        anchor.localScale = new Vector3(
            SafeInverseScale(parentScale.x),
            SafeInverseScale(parentScale.y),
            SafeInverseScale(parentScale.z));
    }

    private static float SafeInverseScale(float value)
    {
        return Mathf.Abs(value) > 0.0001f ? 1f / value : 1f;
    }

    internal bool TryCaptureKingPresentationDiagnostics(
        out float goalDistance,
        out float configuredScaleMultiplier,
        out float worldScaleDrift,
        out float presentationTransformDrift,
        out float rigTransformDrift,
        out bool usesNeutralGoalAnchor,
        out bool rigPinRequired,
        out bool rigPinActive)
    {
        goalDistance = -1f;
        configuredScaleMultiplier = _selectedKingData != null
            ? Mathf.Max(0.1f, _selectedKingData.presentationScale)
            : -1f;
        worldScaleDrift = -1f;
        presentationTransformDrift = -1f;
        rigTransformDrift = -1f;
        usesNeutralGoalAnchor = false;
        rigPinRequired = _kingRigPinRequired;
        rigPinActive = _kingHasRigLocalRotation && _kingRigRoot != null;

        if (_kingPresentation == null || _kingPresentationAnchor == null || goalTransform == null)
        {
            return false;
        }

        Transform presentation = _kingPresentation.transform;
        Vector3 presentationPosition = presentation.position;
        Vector3 goalPosition = goalTransform.position;
        goalDistance = Vector2.Distance(
            new Vector2(presentationPosition.x, presentationPosition.z),
            new Vector2(goalPosition.x, goalPosition.z));
        worldScaleDrift = Vector3.Distance(presentation.lossyScale, _kingExpectedWorldScale);
        usesNeutralGoalAnchor =
            _kingPresentationAnchor != goalTransform &&
            _kingPresentationAnchor.parent == goalTransform.parent &&
            presentation.parent == _kingPresentationAnchor;

        presentationTransformDrift = _kingHasCanonicalTransform
            ? Mathf.Max(
                Vector3.Distance(presentation.localPosition, _kingCanonicalLocalPosition),
                Mathf.Max(
                    Quaternion.Angle(presentation.localRotation, _kingCanonicalLocalRotation) / 180f,
                    Vector3.Distance(presentation.localScale, _kingCanonicalLocalScale)))
            : -1f;

        rigTransformDrift = _kingHasRigLocalRotation && _kingRigRoot != null
            ? Mathf.Max(
                Vector3.Distance(_kingRigRoot.localPosition, _kingRigLocalPosition),
                Mathf.Max(
                    Quaternion.Angle(_kingRigRoot.localRotation, _kingRigLocalRotation) / 180f,
                    Vector3.Distance(_kingRigRoot.localScale, _kingRigLocalScale)))
            : 0f;
        return true;
    }

    internal bool TryCaptureKingOrientationDiagnostics(
        out float cameraFacingAngle,
        out bool headLookActive,
        out bool headLookApplied)
    {
        cameraFacingAngle = -1f;
        headLookActive = _kingHeadLookController != null
                         && _kingHeadLookController.isActiveAndEnabled
                         && _kingAnimator != null
                         && _kingAnimator.isActiveAndEnabled;
        headLookApplied = headLookActive
                          && _kingHeadLookController.WasLookAtAppliedRecently(3);
        if (!_kingFacesCamera || _kingPresentation == null)
        {
            return false;
        }

        Camera camera = ResolveKingPresentationCamera();
        if (!UnitOrientationFixer.TryResolveCameraFacingYaw(
                _kingPresentation.transform.position,
                camera,
                _kingCameraYawOffsetDegrees,
                out Quaternion expectedRotation))
        {
            return false;
        }

        Quaternion presentationOffset = _selectedKingData != null
            ? Quaternion.Euler(_selectedKingData.presentationEulerAngles)
            : Quaternion.identity;
        cameraFacingAngle = Quaternion.Angle(
            _kingPresentation.transform.rotation,
            expectedRotation * presentationOffset);
        return true;
    }

    private void EnsureKingPresentationAtGoal()
    {
        if (_kingPresentation == null)
        {
            if (IsKingDataReadyForHash(_kingLoadRequestedHash)
                && goalTransform != null
                && !_kingPresentationLoadInProgress
                && Time.unscaledTime >= _kingPresentationRetryNotBefore)
            {
                int generation = _kingLoadGeneration;
                CreateKingPresentationAsync(generation).Forget();
            }
            return;
        }

        if (goalTransform == null)
        {
            _kingPresentation.SetActive(false);
            return;
        }

        Transform presentationAnchor = GetOrCreateKingPresentationAnchor();
        if (presentationAnchor == null)
        {
            _kingPresentation.SetActive(false);
            return;
        }

        Transform kingTransform = _kingPresentation.transform;
        bool reparented = false;
        if (kingTransform.parent != presentationAnchor)
        {
            kingTransform.SetParent(presentationAnchor, false);
            reparented = true;
        }
        if (!_kingPresentation.activeSelf)
        {
            _kingPresentation.SetActive(true);
        }

        if (!_kingHasCanonicalTransform && _selectedKingData != null)
        {
            _kingCanonicalLocalPosition = _selectedKingData.presentationOffset;
            _kingCanonicalLocalRotation = Quaternion.Euler(_selectedKingData.presentationEulerAngles);
            _kingCanonicalLocalScale = kingTransform.localScale.sqrMagnitude > 0.001f
                ? kingTransform.localScale
                : Vector3.one * Mathf.Max(0.1f, _selectedKingData.presentationScale);
            _kingHasCanonicalTransform = true;
        }

        if (!_kingDamageReactionPlaying && _kingHasCanonicalTransform)
        {
            kingTransform.localPosition = _kingCanonicalLocalPosition;
            kingTransform.localRotation = _kingCanonicalLocalRotation;
            kingTransform.localScale = _kingCanonicalLocalScale;
        }
        else if (reparented)
        {
            // A goal replacement changes the local coordinate space. Restarting from canonical state
            // avoids carrying a partially-played bounce into the new field hierarchy.
            if (_kingDamageReactionRoutine != null)
            {
                StopCoroutine(_kingDamageReactionRoutine);
                _kingDamageReactionRoutine = null;
            }
            ResetKingDamageReactionPresentation();
        }
    }

    private bool PlayKingAttackPresentation(Vector3 targetPosition)
    {
        if (_kingPresentation == null)
        {
            return false;
        }

        // Placed units remain camera-facing while their attack animation plays. The King follows
        // the same presentation rule instead of permanently adopting the last target's yaw.
        _ = targetPosition;

        if (_kingAnimator != null && _kingAnimator.isActiveAndEnabled)
        {
            _kingAnimator.SetTrigger(KingAttackTrigger);
        }
        return true;
    }

    private bool PlayKingSkillPresentation()
    {
        if (_kingPresentation == null)
        {
            return false;
        }

        if (_kingAnimator != null && _kingAnimator.isActiveAndEnabled)
        {
            _kingAnimator.SetTrigger(KingAttackTrigger);
        }
        return true;
    }

    private bool PlayKingDamageReactionPresentation()
    {
        if (_kingPresentation == null || !isActiveAndEnabled)
        {
            return false;
        }

        if (_kingDamageReactionRoutine != null)
        {
            StopCoroutine(_kingDamageReactionRoutine);
            _kingDamageReactionRoutine = null;
        }
        ResetKingDamageReactionPresentation();
        _kingDamageReactionRoutine = StartCoroutine(KingDamageReactionRoutine());
        return true;
    }

    private IEnumerator KingDamageReactionRoutine()
    {
        _kingDamageReactionPlaying = true;
        Transform kingTransform = _kingPresentation != null ? _kingPresentation.transform : null;
        if (kingTransform == null)
        {
            ResetKingDamageReactionPresentation();
            _kingDamageReactionRoutine = null;
            yield break;
        }

        Vector3 baseScale = _kingHasCanonicalTransform ? _kingCanonicalLocalScale : kingTransform.localScale;
        Vector3 baseLocalPosition = _kingHasCanonicalTransform
            ? _kingCanonicalLocalPosition
            : kingTransform.localPosition;
        float elapsed = 0f;
        while (elapsed < KING_DAMAGE_REACTION_SECONDS && kingTransform != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(elapsed / KING_DAMAGE_REACTION_SECONDS);
            float flash = 1f - normalized;
            float squash = Mathf.Sin(normalized * Mathf.PI);
            float bounce = Mathf.Sin(normalized * Mathf.PI) * 0.28f;
            kingTransform.localScale = Vector3.Scale(
                baseScale,
                new Vector3(1f + squash * 0.12f, 1f - squash * 0.22f, 1f + squash * 0.12f));
            kingTransform.localPosition = baseLocalPosition + Vector3.up * bounce;
            ApplyKingFlashColor(Color.Lerp(Color.white, new Color(1f, 0.15f, 0.1f, 1f), flash));
            yield return null;
        }

        ResetKingDamageReactionPresentation();
        _kingDamageReactionRoutine = null;
    }

    private void ApplyKingFlashColor(Color color)
    {
        int count = Mathf.Min(_kingRenderers.Length, _kingPropertyBlocks.Length);
        for (int i = 0; i < count; i++)
        {
            Renderer renderer = _kingRenderers[i];
            MaterialPropertyBlock block = _kingPropertyBlocks[i];
            if (renderer == null || block == null)
            {
                continue;
            }
            if (i < _kingOriginalPropertyBlocks.Length && _kingOriginalPropertyBlocks[i] != null)
            {
                renderer.SetPropertyBlock(_kingOriginalPropertyBlocks[i]);
            }
            block.Clear();
            renderer.GetPropertyBlock(block);
            block.SetColor(KingBaseColor, color);
            block.SetColor(KingColor, color);
            renderer.SetPropertyBlock(block);
        }
    }

    private void RestoreKingRendererPropertyBlocks()
    {
        int count = Mathf.Min(_kingRenderers.Length, _kingOriginalPropertyBlocks.Length);
        for (int i = 0; i < count; i++)
        {
            Renderer renderer = _kingRenderers[i];
            MaterialPropertyBlock block = _kingOriginalPropertyBlocks[i];
            if (renderer == null || block == null)
            {
                continue;
            }
            renderer.SetPropertyBlock(block);
        }
    }

    private void ResetKingDamageReactionPresentation()
    {
        if (_kingPresentation != null && _kingHasCanonicalTransform)
        {
            Transform kingTransform = _kingPresentation.transform;
            kingTransform.localPosition = _kingCanonicalLocalPosition;
            kingTransform.localRotation = _kingCanonicalLocalRotation;
            kingTransform.localScale = _kingCanonicalLocalScale;
        }

        RestoreKingRendererPropertyBlocks();
        _kingDamageReactionPlaying = false;
    }

    private void ApplyKingBuffsToOwnedUnits()
    {
        if (ownedUnits == null)
        {
            return;
        }

        for (int i = ownedUnits.Count - 1; i >= 0; i--)
        {
            Unit unit = ownedUnits[i];
            if (unit == null)
            {
                ownedUnits.RemoveAt(i);
                continue;
            }
            unit.RefreshPermanentBonuses();
        }
    }

    public KingRuntimeMigrationState CaptureKingRuntimeMigrationState()
    {
        float remaining = 0f;
        if (Runner != null && KingNextAttackTimer.IsRunning)
        {
            remaining = Mathf.Max(0f, KingNextAttackTimer.RemainingTime(Runner) ?? 0f);
        }

        return new KingRuntimeMigrationState
        {
            SelectedKingUnitKeyHash = SelectedKingUnitKeyHash,
            SkillUsedThisDefense = KingSkillUsedForDefense,
            DefenseSequenceId = KingDefenseSequenceId,
            DamageReactionSequence = KingDamageReactionSequence,
            AttackPresentationSequence = KingAttackPresentationSequence,
            AttackPresentationTargetPosition = KingAttackPresentationTargetPosition,
            SkillPresentationSequence = KingSkillPresentationSequence,
            AttackDamageBonusPermille = KingAttackDamageBonusPermille,
            AttackSpeedBonusPermille = KingAttackSpeedBonusPermille,
            SkillPowerBonusPermille = KingSkillPowerBonusPermille,
            NextAttackRemainingSeconds = remaining
        };
    }

    public bool RestoreKingRuntimeAfterHostMigration(KingRuntimeMigrationState state, string context)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
        {
            Debug.LogError($"[King] Host migration restore requires State Authority ({context})", this);
            return false;
        }

        SelectedKingUnitKeyHash = KingSelectionCatalog.NormalizeOrDefaultHash(state.SelectedKingUnitKeyHash);
        KingSkillUsedForDefense = state.SkillUsedThisDefense;
        KingDefenseSequenceId = state.DefenseSequenceId;
        KingDamageReactionSequence = Mathf.Max(0, state.DamageReactionSequence);
        KingAttackPresentationSequence = Mathf.Max(0, state.AttackPresentationSequence);
        KingAttackPresentationTargetPosition = state.AttackPresentationTargetPosition;
        KingSkillPresentationSequence = Mathf.Max(0, state.SkillPresentationSequence);
        KingAttackDamageBonusPermille = Mathf.Max(0, state.AttackDamageBonusPermille);
        KingAttackSpeedBonusPermille = Mathf.Max(0, state.AttackSpeedBonusPermille);
        KingSkillPowerBonusPermille = Mathf.Max(0, state.SkillPowerBonusPermille);
        KingNextAttackTimer = state.NextAttackRemainingSeconds > 0f
            ? TickTimer.CreateFromSeconds(Runner, state.NextAttackRemainingSeconds)
            : TickTimer.None;
        _lastObservedKingHash = SelectedKingUnitKeyHash;
        QueueKingDataLoad(SelectedKingUnitKeyHash);
        return true;
    }

    private void DisposeKingRuntime()
    {
        _kingLoadGeneration++;
        DestroyKingPresentation();
        _selectedKingData = null;
        _kingDataLoadedHash = 0;
        _kingDataLoadInProgress = false;
        _kingDataLoadInProgressHash = 0;
        _kingDataRetryNotBefore = 0f;
    }

    private void DestroyKingPresentation()
    {
        if (_kingDamageReactionRoutine != null)
        {
            StopCoroutine(_kingDamageReactionRoutine);
            _kingDamageReactionRoutine = null;
        }
        ResetKingDamageReactionPresentation();

        if (_kingHeadLookController != null)
        {
            _kingHeadLookController.ClearRuntimeLookAtTarget();
        }

        if (_kingPresentation != null)
        {
            DestroyKingPresentationObject(_kingPresentation);
        }
        if (_kingPresentationAnchor != null)
        {
            DestroyKingPresentationObject(_kingPresentationAnchor.gameObject);
        }
        _kingPresentationAnchor = null;
        _kingPresentation = null;
        _kingAnimator = null;
        _kingHeadLookController = null;
        _kingRenderers = Array.Empty<Renderer>();
        _kingPropertyBlocks = Array.Empty<MaterialPropertyBlock>();
        _kingOriginalPropertyBlocks = Array.Empty<MaterialPropertyBlock>();
        _kingDamageReactionPlaying = false;
        _kingPresentationLoadInProgress = false;
        _kingPresentationLoadGeneration = 0;
        _kingPresentationLoadFailureCount = 0;
        _kingPresentationRetryNotBefore = 0f;
        _kingHasCanonicalTransform = false;
        _kingCanonicalLocalPosition = Vector3.zero;
        _kingCanonicalLocalRotation = Quaternion.identity;
        _kingCanonicalLocalScale = Vector3.one;
        _kingExpectedWorldScale = Vector3.one;
        _kingRigRoot = null;
        _kingRigLocalPosition = Vector3.zero;
        _kingRigLocalRotation = Quaternion.identity;
        _kingRigLocalScale = Vector3.one;
        _kingRigPinRequired = false;
        _kingHasRigLocalRotation = false;
        _kingFacesCamera = false;
        _kingCameraYawOffsetDegrees = 0f;
    }

    private static void DestroyKingPresentationObject(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }

    private static int NextKingPresentationSequence(int current)
    {
        return current >= int.MaxValue - 1 ? 1 : current + 1;
    }

    private static int AddEncodedKingBonus(int current, float bonus)
    {
        int encoded = Mathf.RoundToInt(Mathf.Max(0f, bonus) * KING_BONUS_NETWORK_SCALE);
        long sum = (long)Mathf.Max(0, current) + encoded;
        return sum >= int.MaxValue ? int.MaxValue : (int)sum;
    }

    private static float DecodeKingBonus(int encoded)
    {
        return Mathf.Max(0, encoded) / KING_BONUS_NETWORK_SCALE;
    }
}
