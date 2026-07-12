using Fusion;
using UnityEngine;

public partial class FieldManager
{
    private FieldCombatTargetRegistry _combatTargets;
    private bool _combatTargetsReady;
    private int _combatTargetsReadyRound = int.MinValue;
    private GameManagers.GameState _combatTargetsReadyState;
    private NetworkRunner _combatTargetsReadyRunner;
    private Transform _combatTargetsReadyMonsterParent;

    public FieldCombatTargetRegistry CombatTargets
    {
        get
        {
            if (_combatTargets == null)
            {
                _combatTargets = new FieldCombatTargetRegistry(this);
            }
            return _combatTargets;
        }
    }

    public void RegisterCombatUnit(Unit unit)
    {
        CombatTargets.RegisterUnit(unit);
    }

    public void UnregisterCombatUnit(Unit unit)
    {
        _combatTargets?.UnregisterUnit(unit);
    }

    public void RegisterCombatMonster(Monster monster)
    {
        CombatTargets.RegisterMonster(monster);
    }

    public void UnregisterCombatMonster(Monster monster)
    {
        _combatTargets?.UnregisterMonster(monster);
    }

    public bool IsCombatMonsterRegistered(Monster monster)
    {
        return _combatTargets != null && _combatTargets.ContainsMonster(monster);
    }

    public bool TryGetCombatTargetRegistry(out FieldCombatTargetRegistry registry)
    {
        registry = null;
        if (playerManager == null)
        {
            return false;
        }

        EnsureCombatTargetRegistryReady();
        registry = CombatTargets;
        return true;
    }

    private void EnsureCombatTargetRegistryReady()
    {
        GameManagers gameManagers = GameManagers.Instance;
        int round = gameManagers != null ? gameManagers.currentRound : 0;
        GameManagers.GameState state = gameManagers != null
            ? gameManagers.currentState
            : GameManagers.GameState.Setup;
        NetworkRunner runner = playerManager != null ? playerManager.Runner : null;
        Transform monsterParent = playerManager != null && playerManager.monsterSpawner != null
            ? playerManager.monsterSpawner.monsterParent
            : null;

        if (_combatTargetsReady &&
            _combatTargetsReadyRound == round &&
            _combatTargetsReadyState == state &&
            _combatTargetsReadyRunner == runner &&
            _combatTargetsReadyMonsterParent == monsterParent)
        {
            return;
        }

        RebuildCombatTargetRegistry(round, state, runner, monsterParent);
    }

    private void RebuildCombatTargetRegistry(
        int round,
        GameManagers.GameState state,
        NetworkRunner runner,
        Transform monsterParent)
    {
        CombatTargets.RebuildUnits(placedUnits.Values);

        if (monsterParent != null)
        {
            Monster[] monsters = monsterParent.GetComponentsInChildren<Monster>(true);
            CombatTargets.RebuildMonsters(monsters);
        }

        _combatTargetsReady = true;
        _combatTargetsReadyRound = round;
        _combatTargetsReadyState = state;
        _combatTargetsReadyRunner = runner;
        _combatTargetsReadyMonsterParent = monsterParent;
    }

    private void RefreshCombatTargetRegistryAfterRosterRebuild()
    {
        _combatTargetsReady = false;
        EnsureCombatTargetRegistryReady();
    }

    private void InvalidateCombatTargetRegistry()
    {
        _combatTargetsReady = false;
    }

    private void DisposeCombatTargetRegistry()
    {
        _combatTargets?.Clear();
        _combatTargets = null;
        _combatTargetsReady = false;
        _combatTargetsReadyRunner = null;
        _combatTargetsReadyMonsterParent = null;
    }
}
