using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class RearrangeUnitsCommand : ICommand
{
    public int PlayerId { get; set; }

    public RearrangeUnitsCommand(int playerId)
    {
        this.PlayerId = playerId;
    }

    public void Execute()
    {
        var player = GameManagers.Instance.GetPlayer(PlayerId);
        if (player == null || player.fieldManager == null) return;

        var fieldManager = player.fieldManager;
        var allUnits = new List<Unit>(fieldManager.GetAlliedUnitsOnField());

        if (allUnits.Count == 0) return;

        Debug.Log($"[AI] {PlayerId}번 플레이어의 유닛 재배치를 시작합니다. (총 {allUnits.Count}기)");

        // 1. 모든 유닛의 등록을 해제합니다. (게임 오브젝트는 그대로 둡니다)
        fieldManager.UnregisterAllUnits();

        // 2. 몬스터 경로를 최신 상태로 다시 계산합니다.
        RecalculateMonsterPath(player);

        // 3. 원거리 유닛부터, 그다음 근접 유닛 순으로 정렬합니다.
        var sortedUnits = allUnits
            .OrderBy(u => u.Data.unitType == UnitType.Ranged ? 0 : 1)
            .ToList();

        // 4. 정렬된 순서대로 한 기씩 최적의 위치를 찾아 다시 등록합니다.
        foreach (var unit in sortedUnits)
        {
            // 이제 fieldManager.GetAlliedUnitsOnField()는 재배치된 유닛 목록을 동적으로 반환하므로
            // FindBestSpotForAI가 항상 최신 상태를 기반으로 최적의 위치를 계산할 수 있습니다.
            Vector3Int? bestPos = fieldManager.FindBestSpotForAI(unit.Data, player.astarGrid.FinalPath);

            if (bestPos.HasValue)
            {
                fieldManager.RegisterUnitAt(unit, bestPos.Value);
            }
            else
            {
                // 만약의 경우 최적 위치를 못 찾으면, 그냥 첫 번째 빈자리에 배치합니다.
                Debug.LogWarning($"[AI] {unit.Data.unitName}의 최적 위치를 찾지 못해, 임시 위치에 배치합니다.");
                Vector3Int? emergencyPos = fieldManager.FindFirstEmptySlot(unit.Data);
                if (emergencyPos.HasValue)
                {
                    fieldManager.RegisterUnitAt(unit, emergencyPos.Value);
                }
            }
        }

        // 5. 재배치 후 유닛 조합을 확인합니다.
        fieldManager.CheckForCombination();
    }

    private void RecalculateMonsterPath(PlayerManager player)
    {
        var grid = player.astarGrid;
        var goal = player.goalTransform;
        var fieldManager = player.fieldManager;

        if (grid == null || goal == null || fieldManager == null || !fieldManager.TryGetSingleOpenEntryCell(out var entryCell)) return;

        Vector2Int startPos = new Vector2Int(entryCell.x, entryCell.y);
        Vector2Int goalPos = grid.WorldToCell(grid.ClampToGrid(goal.position));

        bool pathFound = grid.FindPath(startPos, goalPos);


    }
}
