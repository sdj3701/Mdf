using System;
using Fusion;
using UnityEngine;

public class DestructibleWall : NetworkBehaviour, IEnemy, IHealth
{
    [Header("Wall progression")]
    [SerializeField] private WallProgressionData progressionData;
    [SerializeField] private MeshFilter visualMeshFilter;
    [SerializeField] private MeshRenderer visualRenderer;

    [Header("Legacy level 1 fallback")]
    [SerializeField] private float maxHealth = 200f;
    [SerializeField] private float defense = 5f;
    [SerializeField] private float magicResistance;

    [Networked] private int SyncedUpgradeLevel { get; set; }
    [Networked] private int SyncedInvestedUpgradeGold { get; set; }
    [Networked] private NetworkBool SyncedPlacementReady { get; set; }
    [Networked] private int SyncedOwnerPlayerId { get; set; }
    [Networked] private int SyncedGridX { get; set; }
    [Networked] private int SyncedGridY { get; set; }
    [Networked] private NetworkBool SyncedHealthReady { get; set; }
    [Networked] private float SyncedCurrentHealth { get; set; }
    [Networked] private int SyncedHealthRevision { get; set; }

    private float currentHealth;
    private int stateRevision;
    private int localUpgradeLevel = 1;
    private int localInvestedUpgradeGold;
    private int appliedPresentationLevel = -1;
    private int appliedSyncedHealthRevision = -1;
    private Vector3Int wallGridPosition;
    private FieldManager fieldManager;
    private StatusBarUI statusBarUI;

    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public int StateRevision => stateRevision;
    public int CurrentLevel => HasValidNetworkObject && SyncedUpgradeLevel > 0
        ? SyncedUpgradeLevel
        : Mathf.Max(1, localUpgradeLevel);
    public int InvestedUpgradeGold => HasValidNetworkObject
        ? Mathf.Max(0, SyncedInvestedUpgradeGold)
        : Mathf.Max(0, localInvestedUpgradeGold);
    public int MaxLevel => progressionData != null ? Mathf.Max(1, progressionData.MaxLevel) : 1;
    public Vector3Int GridPosition => wallGridPosition;
    public FieldManager OwnerFieldManager => fieldManager;
    public WallProgressionData ProgressionData => progressionData;

    public event Action<float, float> OnHealthChanged;
    public event Action<int, int> OnUpgradeChanged;

    private bool HasValidNetworkObject => Object != null && Object.IsValid;

    private void Awake()
    {
        ResolveVisualReferences();
        ApplyLevelStatsAndPresentation(1, preserveDamage: false, updateHealth: true);
    }

    private void OnValidate()
    {
        ResolveVisualReferences();
    }

    public override void Spawned()
    {
        ResetTransientStateForPoolReuse(clearBindingsAndSubscribers: false);
        if (Object.HasStateAuthority && SyncedUpgradeLevel <= 0)
        {
            // A pooled clone may have represented a level-three wall in its previous lifetime.
            // A genuinely new network spawn always begins at level one; migration state arrives
            // through the Networked values and therefore bypasses this default branch.
            SyncedUpgradeLevel = 1;
            SyncedInvestedUpgradeGold = 0;
        }

        // Spawned begins a new pooled lifecycle. Apply the replicated level's max HP immediately;
        // the authority migration restore can then replace it with the exact captured ratio.
        ApplyReplicatedProgressionState(updateHealth: true);
        ApplyReplicatedHealthState(force: true);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        ResetTransientStateForPoolReuse(clearBindingsAndSubscribers: true);
    }

    public override void Render()
    {
        ApplyReplicatedProgressionState(updateHealth: false);
        ApplyReplicatedHealthState(force: false);
    }

    public void SetStatusBar(StatusBarUI ui)
    {
        statusBarUI = ui;
    }

    public void Initialize(FieldManager manager, Vector3Int gridPosition)
    {
        fieldManager = manager;
        wallGridPosition = gridPosition;
        localUpgradeLevel = 1;
        localInvestedUpgradeGold = 0;
        if (HasValidNetworkObject && Object.HasStateAuthority)
        {
            SyncedUpgradeLevel = 1;
            SyncedInvestedUpgradeGold = 0;
            int ownerPlayerId = manager != null && manager.playerManager != null
                ? manager.playerManager.playerId
                : -1;
            SyncedOwnerPlayerId = ownerPlayerId;
            SyncedGridX = gridPosition.x;
            SyncedGridY = gridPosition.y;
            SyncedPlacementReady = ownerPlayerId >= 0;
        }

        ApplyLevelStatsAndPresentation(1, preserveDamage: false, updateHealth: true);
        currentHealth = maxHealth;
        stateRevision++;
        PublishAuthoritativeHealthState();
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        OnUpgradeChanged?.Invoke(CurrentLevel, InvestedUpgradeGold);
    }

    public bool TryGetReplicatedPlacement(out int ownerPlayerId, out Vector3Int gridPosition)
    {
        ownerPlayerId = -1;
        gridPosition = default;
        if (!HasValidNetworkObject || !SyncedPlacementReady)
        {
            return false;
        }

        ownerPlayerId = SyncedOwnerPlayerId;
        gridPosition = new Vector3Int(SyncedGridX, SyncedGridY, 0);
        return ownerPlayerId >= 0;
    }

    public void RebindAfterMigration(FieldManager manager, Vector3Int gridPosition)
    {
        fieldManager = manager;
        wallGridPosition = gridPosition;
        ApplyReplicatedProgressionState(updateHealth: currentHealth <= 0f);
        ApplyReplicatedHealthState(force: false);

        if (currentHealth <= 0f && (!HasValidNetworkObject || !SyncedHealthReady))
        {
            currentHealth = maxHealth;
        }
    }

    public bool TryGetUpgradeQuote(
        int expectedCurrentLevel,
        out int nextLevel,
        out int cost,
        out string reason)
    {
        nextLevel = CurrentLevel;
        cost = 0;
        reason = string.Empty;
        if (progressionData == null)
        {
            reason = "wall_progression_missing";
            return false;
        }
        if (!progressionData.IsValid(out reason))
        {
            reason = string.IsNullOrEmpty(reason) ? "wall_progression_missing" : reason;
            return false;
        }

        int currentLevel = CurrentLevel;
        if (expectedCurrentLevel != currentLevel)
        {
            reason = $"wall_upgrade_stale_level:{expectedCurrentLevel}:{currentLevel}";
            return false;
        }

        if (!progressionData.TryGetNextLevel(currentLevel, out WallLevelData nextData))
        {
            reason = "wall_upgrade_max_level";
            return false;
        }

        nextLevel = nextData.Level;
        cost = nextData.UpgradeCostFromPreviousLevel;
        reason = string.Empty;
        return true;
    }

    public bool TryUpgradeFromAuthority(
        int expectedCurrentLevel,
        int paidGold,
        out string reason)
    {
        if (!CanMutateAuthoritatively())
        {
            reason = "wall_upgrade_state_authority_required";
            return false;
        }

        if (!TryGetUpgradeQuote(expectedCurrentLevel, out int nextLevel, out int expectedCost, out reason))
        {
            return false;
        }
        if (paidGold != expectedCost)
        {
            reason = $"wall_upgrade_cost_mismatch:{paidGold}:{expectedCost}";
            return false;
        }

        int nextInvestment = checked(InvestedUpgradeGold + paidGold);
        SetProgressionState(nextLevel, nextInvestment, preserveDamage: true, updateHealth: true);
        stateRevision++;
        PublishAuthoritativeHealthState();
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        OnUpgradeChanged?.Invoke(CurrentLevel, InvestedUpgradeGold);
        fieldManager?.PublishDestructibleWallHealthDelta(this, "wall_upgrade");
        reason = string.Empty;
        return true;
    }

    public bool RestoreProgressionAfterMigration(int restoredLevel, int restoredInvestment)
    {
        if (!CanMutateAuthoritatively() || restoredLevel < 1 || restoredInvestment < 0)
        {
            return false;
        }
        if (progressionData != null && !progressionData.TryGetLevel(restoredLevel, out _))
        {
            return false;
        }
        if (progressionData == null && restoredLevel != 1)
        {
            return false;
        }

        SetProgressionState(restoredLevel, restoredInvestment, preserveDamage: false, updateHealth: false);
        OnUpgradeChanged?.Invoke(CurrentLevel, InvestedUpgradeGold);
        return true;
    }

    public bool RestoreHealthAfterMigration(float restoredHealth, float restoredMaxHealth, int restoredRevision)
    {
        if (float.IsNaN(restoredHealth) || float.IsInfinity(restoredHealth)
            || float.IsNaN(restoredMaxHealth) || float.IsInfinity(restoredMaxHealth)
            || restoredMaxHealth <= 0f || restoredRevision < 0)
        {
            return false;
        }

        float normalizedHealth = Mathf.Clamp01(restoredHealth / restoredMaxHealth);
        currentHealth = maxHealth * normalizedHealth;
        stateRevision = Mathf.Max(stateRevision, restoredRevision);
        PublishAuthoritativeHealthState();
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        return true;
    }

    public void TakeDamage(float baseDamage, DamageType damageType)
    {
        if (!CanMutateAuthoritatively())
        {
            return;
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (MPTestCommandLine.IsGameFlowFrozen)
        {
            return;
        }
#endif
        int finalDamage = DamageCalculator.CalculateDamage(baseDamage, damageType, defense, magicResistance);
        currentHealth = Mathf.Max(0f, currentHealth - finalDamage);
        stateRevision++;
        PublishAuthoritativeHealthState();
        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        if (currentHealth > 0f)
        {
            fieldManager?.PublishDestructibleWallHealthDelta(this, "wall_damage");
        }

        if (currentHealth <= 0f)
        {
            if (fieldManager != null)
            {
                gameObject.SetActive(false);
                GameEvents.TriggerWallDestroyed(wallGridPosition, fieldManager);
                fieldManager.RemoveWallAt(wallGridPosition);
            }
            else
            {
                Debug.LogError("DestructibleWall has no FieldManager; it cannot be removed safely.", this);
            }
        }
    }

    public void Heal(float amount)
    {
        if (!CanMutateAuthoritatively() || currentHealth <= 0f || amount <= 0f)
        {
            return;
        }

        currentHealth = Mathf.Min(currentHealth + amount, maxHealth);
        stateRevision++;
        PublishAuthoritativeHealthState();
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        fieldManager?.PublishDestructibleWallHealthDelta(this, "wall_heal");
    }

    private void SetProgressionState(
        int level,
        int investedGold,
        bool preserveDamage,
        bool updateHealth)
    {
        localUpgradeLevel = Mathf.Max(1, level);
        localInvestedUpgradeGold = Mathf.Max(0, investedGold);
        if (HasValidNetworkObject && Object.HasStateAuthority)
        {
            SyncedUpgradeLevel = localUpgradeLevel;
            SyncedInvestedUpgradeGold = localInvestedUpgradeGold;
        }

        ApplyLevelStatsAndPresentation(localUpgradeLevel, preserveDamage, updateHealth);
    }

    private void ApplyReplicatedProgressionState(bool updateHealth)
    {
        if (!HasValidNetworkObject || SyncedUpgradeLevel <= 0)
        {
            return;
        }

        int level = SyncedUpgradeLevel;
        int investment = Mathf.Max(0, SyncedInvestedUpgradeGold);
        bool levelChanged = level != localUpgradeLevel || appliedPresentationLevel != level;
        bool investmentChanged = investment != localInvestedUpgradeGold;
        localUpgradeLevel = level;
        localInvestedUpgradeGold = investment;
        if (levelChanged)
        {
            ApplyLevelStatsAndPresentation(level, preserveDamage: false, updateHealth: updateHealth);
        }
        if (levelChanged || investmentChanged)
        {
            OnUpgradeChanged?.Invoke(level, investment);
        }
    }

    private void PublishAuthoritativeHealthState()
    {
        if (!HasValidNetworkObject || !Object.HasStateAuthority)
        {
            return;
        }

        SyncedCurrentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
        SyncedHealthRevision = Mathf.Max(0, stateRevision);
        SyncedHealthReady = true;
        appliedSyncedHealthRevision = SyncedHealthRevision;
    }

    private void ApplyReplicatedHealthState(bool force)
    {
        if (!HasValidNetworkObject || !SyncedHealthReady)
        {
            return;
        }

        int replicatedRevision = Mathf.Max(0, SyncedHealthRevision);
        if (!force && appliedSyncedHealthRevision == replicatedRevision)
        {
            return;
        }

        currentHealth = Mathf.Clamp(SyncedCurrentHealth, 0f, maxHealth);
        stateRevision = replicatedRevision;
        appliedSyncedHealthRevision = replicatedRevision;
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    private void ApplyLevelStatsAndPresentation(int level, bool preserveDamage, bool updateHealth)
    {
        float previousMaxHealth = Mathf.Max(1f, maxHealth);
        float previousDamage = Mathf.Max(0f, previousMaxHealth - currentHealth);

        if (progressionData != null && progressionData.TryGetLevel(level, out WallLevelData data))
        {
            maxHealth = Mathf.Max(1f, data.MaxHealth);
            defense = Mathf.Max(0f, data.Defense);
            magicResistance = Mathf.Max(0f, data.MagicResistance);
            ResolveVisualReferences();
            if (visualMeshFilter != null && data.VisualMesh != null)
            {
                visualMeshFilter.sharedMesh = data.VisualMesh;
            }
            if (visualRenderer != null && data.VisualMaterials != null && data.VisualMaterials.Length > 0)
            {
                visualRenderer.sharedMaterials = data.VisualMaterials;
            }
        }

        appliedPresentationLevel = level;
        if (updateHealth)
        {
            currentHealth = preserveDamage
                ? Mathf.Clamp(maxHealth - previousDamage, 1f, maxHealth)
                : maxHealth;
        }
    }

    private void ResolveVisualReferences()
    {
        if (visualMeshFilter == null)
        {
            visualMeshFilter = GetComponentInChildren<MeshFilter>(true);
        }
        if (visualRenderer == null)
        {
            visualRenderer = GetComponentInChildren<MeshRenderer>(true);
        }
    }

    private void ResetTransientStateForPoolReuse(bool clearBindingsAndSubscribers)
    {
        localUpgradeLevel = 1;
        localInvestedUpgradeGold = 0;
        appliedPresentationLevel = -1;
        appliedSyncedHealthRevision = -1;
        currentHealth = 0f;
        stateRevision = 0;
        ApplyLevelStatsAndPresentation(1, preserveDamage: false, updateHealth: true);

        if (!clearBindingsAndSubscribers)
        {
            return;
        }

        fieldManager = null;
        wallGridPosition = default;
        statusBarUI = null;
        OnHealthChanged = null;
        OnUpgradeChanged = null;
    }

    private bool CanMutateAuthoritatively()
    {
        if (!HasValidNetworkObject)
        {
            return true;
        }
        return Object.HasStateAuthority;
    }
}
