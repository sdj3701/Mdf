using UnityEngine;
using AI.BehaviorTree;
using AI.BehaviorTree.Nodes;
using AI.BehaviorTree.Nodes.Actions;
using AI.BehaviorTree.Nodes.Conditions;

public class AIPlayerController : MonoBehaviour
{
    private PlayerManager _playerManager;
    private CommandProcessor _commandProcessor;
    private float _decisionTimer = 0f;
    private float _decisionCooldown = 1.0f; // 1초마다 의사결정

    private BehaviorTree _preparePhaseBT;
    private BehaviorTree _combatPhaseBT;

    public void Initialize(PlayerManager playerManager, CommandProcessor commandProcessor)
    {
        _playerManager = playerManager;
        _commandProcessor = commandProcessor;
        BuildBehaviorTrees();
        
        ComponentRegistry.Register(playerManager.playerId.ToString(), this);
    }

    private void OnDestroy()
    {
        if (_playerManager != null)
        {
            ComponentRegistry.Unregister<AIPlayerController>(_playerManager.playerId.ToString());
        }
    }

    void Update()
    {
        if (_playerManager == null) return;

        _decisionTimer += Time.deltaTime;
        if (_decisionTimer < _decisionCooldown) return;
        _decisionTimer = 0f;

        var gameState = GameManagers.Instance.GetGameState();
        switch (gameState)
        {
            case GameManagers.GameState.Prepare:
                if (_preparePhaseBT != null) _preparePhaseBT.Tick();
                break;
            case GameManagers.GameState.Combat:
                if (_combatPhaseBT != null) _combatPhaseBT.Tick();
                break;
        }
    }

    private void BuildBehaviorTrees()
    {
        // --- 준비 단계 행동 트리 ---
        _preparePhaseBT = new BehaviorTree(
            new SelectorNode(
                // 1. 증강 선택 (가장 높은 우선순위)
                new IsAugmentPhaseCondition(_playerManager,
                    new ChooseBestAugmentAction(_playerManager, _commandProcessor)
                ),
                
                // 2. 유닛 배치 (구매한 유닛이 있다면)
                new PlaceBestUnitAction(_playerManager, _commandProcessor),

                // 3. 유닛 구매
                new BuyBestUnitAction(_playerManager, _commandProcessor),
                
                // 4. 리롤 (위의 모든 행동을 할 수 없을 때 마지막으로 고려)
                new RerollShopAction(_playerManager, _commandProcessor)
            )
        );

        // --- 전투 단계 행동 트리 (현재는 비어있음) ---
        _combatPhaseBT = new BehaviorTree(
            new SelectorNode(
                // new CanUseManualSkillCondition(_playerManager, 
                //     new UseBestManualSkillAction(_playerManager, _commandProcessor)
                // )
            )
        );
    }

    // 뷰어가 호출할 수 있도록 public으로 변경하고, BehaviorTree를 반환합니다.
    public BehaviorTree GetActiveTree()
    {
        if (GameManagers.Instance == null) return null;

        var gameState = GameManagers.Instance.GetGameState();
        switch (gameState)
        {
            case GameManagers.GameState.Prepare:
                return _preparePhaseBT;
            case GameManagers.GameState.Combat:
                return _combatPhaseBT;
            default:
                return null;
        }
    }
}
