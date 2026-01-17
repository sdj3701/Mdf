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
    private float _decisionCooldown = 0.1f; // 더 잦은 틱으로 0.3~0.8초 간격 건설을 부드럽게 반영

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
        if (_playerManager != null && _playerManager.Object != null && _playerManager.HasStateAuthority)
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
            case GameManagers.GameState.Battle1:
            case GameManagers.GameState.Battle2:
                if (_combatPhaseBT != null) _combatPhaseBT.Tick();
                break;
        }
    }

    private void BuildBehaviorTrees()
    {
        // --- 준비 단계 행동 트리 ---
        // 순차적 실행: 증강 선택 → 미로 건설 → 유닛 구매 → 유닛 재배치 → 리롤
        _preparePhaseBT = new BehaviorTree(
            new SelectorNode(
                // 1. 증강 선택 (가장 높은 우선순위)
                new IsAugmentPhaseCondition(_playerManager,
                    new ChooseBestAugmentAction(_playerManager, _commandProcessor)
                ),

                // 2. 미로 건설 (완료될 때까지)
                // 미로 미완료면 건설 실행
                new SequenceNode(
                    new InverterNode(new IsMazeConstructionCompleteCondition(_playerManager, new AlwaysSuccessNode())),
                    new BuildMazeAction(_playerManager, _commandProcessor)
                ),

                // 3. 유닛 구매 (미로 건설 완료 후, 구매 완료될 때까지)
                new SequenceNode(
                    new IsMazeConstructionCompleteCondition(_playerManager, new AlwaysSuccessNode()),
                    new InverterNode(new IsUnitPurchaseCompleteCondition(_playerManager, new AlwaysSuccessNode())),
                    new BuyBestUnitAction(_playerManager, _commandProcessor)
                ),

                // 4. 유닛 재배치 (미로 건설 + 유닛 구매 완료 후)
                new SequenceNode(
                    new IsMazeConstructionCompleteCondition(_playerManager, new AlwaysSuccessNode()),
                    new IsUnitPurchaseCompleteCondition(_playerManager, new AlwaysSuccessNode()),
                    new RearrangeAllUnitsAction(_playerManager, _commandProcessor)
                ),

                // 5. 리롤 (모든 작업 완료 후)
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
            case GameManagers.GameState.Battle1:
            case GameManagers.GameState.Battle2:
                return _combatPhaseBT;
            default:
                return null;
        }
    }
}
