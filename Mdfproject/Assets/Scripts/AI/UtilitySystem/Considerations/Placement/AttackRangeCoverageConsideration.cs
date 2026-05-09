using System.Collections.Generic;
using UnityEngine;

namespace AI.UtilitySystem.Considerations.Placement
{
    public class AttackRangeCoverageConsideration : Consideration
    {
        public override float Score(AIContext context)
        {
            var fieldManager = context.Player.fieldManager;
            if (fieldManager == null) return 0.5f;

            float range = context.UnitToPlace.attackRange;
            if (range <= 0) return 1.0f;

            Vector3Int position = context.PlacementPosition;
            var targetTiles = BuildTargetTiles(context, fieldManager);
            if (targetTiles.Count == 0) return 0f;

            int totalTilesInCircle = 0;
            int coveredTargetTiles = 0;
            int intRange = Mathf.CeilToInt(range);

            for (int x = -intRange; x <= intRange; x++)
            {
                for (int y = -intRange; y <= intRange; y++)
                {
                    if (x * x + y * y > range * range)
                    {
                        continue;
                    }

                    totalTilesInCircle++;
                    Vector3Int currentTilePos = position + new Vector3Int(x, y, 0);
                    if (targetTiles.Contains(currentTilePos))
                    {
                        coveredTargetTiles++;
                    }
                }
            }

            if (totalTilesInCircle == 0) return 0f;

            int usefulTargetCount = Mathf.Min(targetTiles.Count, totalTilesInCircle);
            return usefulTargetCount > 0 ? (float)coveredTargetTiles / usefulTargetCount : 0f;
        }

        private static HashSet<Vector3Int> BuildTargetTiles(AIContext context, FieldManager fieldManager)
        {
            if (context.MonsterPath != null && context.MonsterPath.Count > 0)
            {
                var pathTiles = new HashSet<Vector3Int>();
                foreach (var node in context.MonsterPath)
                {
                    if (node == null)
                    {
                        continue;
                    }

                    var tile = new Vector3Int(node.x, node.y, 0);
                    if (fieldManager.IsValidGridPosition(tile))
                    {
                        pathTiles.Add(tile);
                    }
                }

                if (pathTiles.Count > 0)
                {
                    return pathTiles;
                }
            }

            return new HashSet<Vector3Int>(fieldManager.GetValidPlacementTiles(UnitType.Melee));
        }
    }
}
