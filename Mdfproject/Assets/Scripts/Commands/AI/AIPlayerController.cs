using UnityEngine;
using Fusion;
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

    private PrepareDecisionPolicy _prepareDecisionPolicy;
    private BattleDecisionPolicy _battleDecisionPolicy;
    private ServerAiCommandEmitter _serverAiCommandEmitter;

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
        _prepareDecisionPolicy = new PrepareDecisionPolicy("balanced");
        _battleDecisionPolicy = new BattleDecisionPolicy();
        _serverAiCommandEmitter = new ServerAiCommandEmitter(GameManagers.Instance, playerManager, "server_ai_controller");
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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var mpOptions = MPTestCommandLine.GetOptions();
        if (mpOptions.Enabled && mpOptions.FreezeGameFlow)
        {
            return;
        }
#endif

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

        var gm = GameManagers.Instance;
        if (gm.Object == null || !gm.Object.HasStateAuthority)
        {
            return;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (mpOptions.Enabled &&
            mpOptions.Scenario == "human_bot_prepare_progression" &&
            _playerManager.Object != null &&
            _playerManager.Object.InputAuthority == PlayerRef.None)
        {
            return;
        }
#endif

        switch (gm.GetGameState())
        {
            case GameManagers.GameState.Prepare:
                TryRunPolicy(_prepareDecisionPolicy, gm);
                break;
            case GameManagers.GameState.Battle1:
            case GameManagers.GameState.Battle2:
                TryRunPolicy(_battleDecisionPolicy, gm);
                break;
        }
    }

    private void TryRunPolicy(IMdfDecisionPolicy policy, GameManagers gm)
    {
        if (policy == null || gm == null || _playerManager == null)
        {
            return;
        }

        _serverAiCommandEmitter = new ServerAiCommandEmitter(gm, _playerManager, "server_ai_controller");
        var context = MdfDecisionContext.Create(
            gm,
            _playerManager,
            CommandExecutionScope.ServerAuthorityOnly,
            "balanced",
            isHumanBot: false,
            isServerAi: true,
            isTestAutomation: false);

        if (!policy.TryChoose(context, out var decision) || decision == null || !decision.HasCommandPayload)
        {
            return;
        }

        _serverAiCommandEmitter.TryEmit(decision, out _);
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
