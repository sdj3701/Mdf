using UnityEngine;
using AI.BehaviorTree;
using AI.BehaviorTree.Nodes;
using AI.BehaviorTree.Nodes.Actions;
using AI.BehaviorTree.Nodes.Conditions;

public class AIPlayerController : MonoBehaviour
{
    private PlayerManager _playerManager;
    private CommandProcessor _commandProcessor;
    private int _registeredPlayerId = -1;
    private float _decisionTimer = 0f;
    private const float DecisionCooldown = 0.1f;

    private BehaviorTree _preparePhaseBT;
    private BehaviorTree _combatPhaseBT;

    public void Initialize(PlayerManager playerManager, CommandProcessor commandProcessor)
    {
        if (playerManager == null || commandProcessor == null)
        {
            Debug.LogWarning("[AIPlayerController] Initialize failed: missing references.");
            return;
        }

        if (_registeredPlayerId >= 0)
        {
            ComponentRegistry.Unregister<AIPlayerController>(_registeredPlayerId.ToString());
        }

        _playerManager = playerManager;
        _commandProcessor = commandProcessor;
        BuildBehaviorTrees();

        _registeredPlayerId = playerManager.playerId;
        ComponentRegistry.Unregister<AIPlayerController>(_registeredPlayerId.ToString());
        ComponentRegistry.Register(_registeredPlayerId.ToString(), this);
    }

    private void OnDestroy()
    {
        if (_registeredPlayerId >= 0)
        {
            ComponentRegistry.Unregister<AIPlayerController>(_registeredPlayerId.ToString());
            _registeredPlayerId = -1;
        }
    }

    private void Update()
    {
        if (_playerManager == null || GameManagers.Instance == null)
        {
            return;
        }

        var migrationHandler = HostMigrationHandler.Instance;
        if (migrationHandler != null && migrationHandler.IsMigrating && !migrationHandler.IsAiTakeoverReady)
        {
            return;
        }

        _decisionTimer += Time.deltaTime;
        if (_decisionTimer < DecisionCooldown)
        {
            return;
        }

        _decisionTimer = 0f;

        switch (GameManagers.Instance.GetGameState())
        {
            case GameManagers.GameState.Prepare:
                _preparePhaseBT?.Tick();
                break;
            case GameManagers.GameState.Battle1:
            case GameManagers.GameState.Battle2:
                _combatPhaseBT?.Tick();
                break;
        }
    }

    private void BuildBehaviorTrees()
    {
        _preparePhaseBT = new BehaviorTree(
            new SelectorNode(
                new IsAugmentPhaseCondition(_playerManager,
                    new ChooseBestAugmentAction(_playerManager, _commandProcessor)
                ),

                new SequenceNode(
                    new InverterNode(new IsMazeConstructionCompleteCondition(_playerManager, new AlwaysSuccessNode())),
                    new BuildMazeAction(_playerManager, _commandProcessor)
                ),

                new SequenceNode(
                    new IsMazeConstructionCompleteCondition(_playerManager, new AlwaysSuccessNode()),
                    new InverterNode(new IsUnitPurchaseCompleteCondition(_playerManager, new AlwaysSuccessNode())),
                    new BuyBestUnitAction(_playerManager, _commandProcessor)
                ),

                new SequenceNode(
                    new IsMazeConstructionCompleteCondition(_playerManager, new AlwaysSuccessNode()),
                    new IsUnitPurchaseCompleteCondition(_playerManager, new AlwaysSuccessNode()),
                    new RearrangeAllUnitsAction(_playerManager, _commandProcessor)
                ),

                new RerollShopAction(_playerManager, _commandProcessor)
            )
        );

        _combatPhaseBT = new BehaviorTree(
            new SelectorNode(
            )
        );
    }

    public BehaviorTree GetActiveTree()
    {
        if (GameManagers.Instance == null)
        {
            return null;
        }

        switch (GameManagers.Instance.GetGameState())
        {
            case GameManagers.GameState.Prepare:
                return _preparePhaseBT;
            case GameManagers.GameState.Battle1:
            case GameManagers.GameState.Battle2:
                return _combatPhaseBT;
            default:
                return null;
        }
    }
}
