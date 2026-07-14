using AI.BehaviorTree.Nodes;
using UnityEngine;

namespace AI.BehaviorTree.Nodes.Conditions
{
    public class IsAugmentPhaseCondition : DecoratorNode
    {
        private static class Debug
        {
            [System.Diagnostics.Conditional("MDF_VERBOSE_AI_LOGS")]
            public static void Log(object message)
            {
                UnityEngine.Debug.Log(message);
            }
        }

        private PlayerManager _playerManager;

        public IsAugmentPhaseCondition(PlayerManager playerManager, Node child) : base(child)
        {
            _playerManager = playerManager;
        }

        public override NodeStatus Tick()
        {
            int augmentCount = _playerManager.augmentManager.GetPresentedAugments().Count;

            if (augmentCount > 0)
            {
                status = child.Tick(); // 조건 만족 시 자식 노드 실행
                Debug.Log($"<color=magenta>[IsAugmentPhaseCondition] 증강 선택 실행 결과: {status}</color>");
                return status;
            }
            status = NodeStatus.Failure;
            return status; // 조건 불만족
        }
    }
}
