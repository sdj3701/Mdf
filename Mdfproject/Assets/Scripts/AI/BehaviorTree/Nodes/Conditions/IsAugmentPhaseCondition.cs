using AI.BehaviorTree.Nodes;

namespace AI.BehaviorTree.Nodes.Conditions
{
    public class IsAugmentPhaseCondition : DecoratorNode
    {
        private PlayerManager _playerManager;

        public IsAugmentPhaseCondition(PlayerManager playerManager, Node child) : base(child)
        {
            _playerManager = playerManager;
        }

        public override NodeStatus Tick()
        {
            if (_playerManager.augmentManager.GetPresentedAugments().Count > 0)
            {
                return child.Tick(); // 조건 만족 시 자식 노드 실행
            }
            return NodeStatus.Failure; // 조건 불만족
        }
    }
}
