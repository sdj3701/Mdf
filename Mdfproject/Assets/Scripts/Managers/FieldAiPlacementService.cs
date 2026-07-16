using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using AI.UtilitySystem;
using AI.UtilitySystem.Considerations.Placement;

/// <summary>
/// Owns AI unit placement candidate filtering and scoring for one field.
/// Field occupancy and authoritative mutation remain in FieldManager.
/// </summary>
public sealed class FieldAiPlacementService
{
    private readonly FieldManager _field;
    private readonly List<Consideration> _placementConsiderations = new List<Consideration>
    {
        new MeleePlacementConsideration { weight = 3.0f },
        new OnMonsterPathConsideration { weight = 10.0f },
        new MeleeProtectsRangedConsideration { weight = 1.6f },
        new ProximityToAlliesConsideration { weight = 1.2f },
        new AttackRangeCoverageConsideration { weight = 1.0f },
        new RangedUnitSynergyConsideration { weight = 1.5f },
    };
    private readonly Dictionary<Vector3Int, float> _debugTileScores = new Dictionary<Vector3Int, float>();
    private readonly Dictionary<Vector3Int, DebugScoreBreakdown> _debugScoreBreakdowns = new Dictionary<Vector3Int, DebugScoreBreakdown>();
    private bool _showDebugScores;
    private UnitData _debugUnitData;

    private struct DebugScoreBreakdown
    {
        public float groundScore;
        public float pathScore;
        public float allyScore;
        public float totalScore;
    }

    public FieldAiPlacementService(FieldManager field)
    {
        _field = field ?? throw new ArgumentNullException(nameof(field));
    }

    private PlayerManager playerManager => _field.playerManager;
    private Vector2Int gridSize => _field.gridSize;
    private List<Vector3Int> GetValidPlacementTiles(UnitType unitType) => _field.GetValidPlacementTiles(unitType);
    private List<Unit> GetAlliedUnitsOnField() => _field.GetAlliedUnitsOnField();
    private bool IsUnitAt(Vector3Int position) => _field.IsUnitAt(position);
    private bool HasWallAt(Vector3Int position) => _field.HasWallAt(position);
    private bool IsValidGridPosition(Vector3Int position) => _field.IsValidGridPosition(position);
    private Vector3Int? FindFirstEmptySlot(UnitData unitData) => _field.FindFirstEmptySlot(unitData);
    private Vector3Int? GetUnitPosition(Unit unit) => _field.GetUnitPosition(unit);
    private Vector3Int WorldToGridInt(Vector3 position) => _field.WorldToGridInt(position);
    private List<Vector3Int> GetBorderGapCells() => _field.GetBorderGapCells();

    private void ScheduleClearDebugScores()
    {
        _field.ScheduleAiPlacementDebugClear();
    }

    public void ClearDebugScores()
    {
        _showDebugScores = false;
        _debugTileScores.Clear();
        _debugScoreBreakdowns.Clear();
        _debugUnitData = null;
    }

    public Vector3Int? FindBestSpot(UnitData unitData, List<AstarNode> monsterPathContext, List<Unit> alliedUnitsContext = null, HashSet<Vector3Int> occupiedTiles = null, Vector3Int? movingUnitOriginalPos = null)
    {

        // 디버그 정보 초기화
        _debugTileScores.Clear();
        _debugScoreBreakdowns.Clear();
        _showDebugScores = true;
        _debugUnitData = unitData;

        var allValidTiles = GetValidPlacementTiles(unitData.unitType);
        if (allValidTiles == null || allValidTiles.Count == 0)
        {
            // Debug.LogWarning($"AI가 {unitData.unitType} 타입의 유닛을 배치할 유효한 타일을 찾지 못했습니다.");
            return null;
        }

        List<Vector3Int> candidateTiles = allValidTiles;
        HashSet<Vector3Int> rangedPathTiles = null;
        HashSet<Vector3Int> meleePathTiles = null;

        // [핵심 수정] 근접 유닛의 경우, 배치 후보지를 몬스터 경로 위로 먼저 한정합니다.
        if (unitData.unitType == UnitType.Melee && monsterPathContext != null && monsterPathContext.Count > 0)
        {
            // 디버그: AI 필드 타일 범위와 몬스터 경로 범위 확인 (필요시 주석 해제)
            // var fieldTileRange = $"필드 타일 범위: ({allValidTiles.Min(t => t.x)}, {allValidTiles.Min(t => t.y)}) ~ ({allValidTiles.Max(t => t.x)}, {allValidTiles.Max(t => t.y)})";
            // var pathRange = $"받은 몬스터 경로 범위: ({monsterPathContext.Min(n => n.x)}, {monsterPathContext.Min(n => n.y)}) ~ ({monsterPathContext.Max(n => n.x)}, {monsterPathContext.Max(n => n.y)})";
            // Debug.Log($"[AI Placement Debug] {fieldTileRange}");
            // Debug.Log($"[AI Placement Debug] {pathRange}");
            // Debug.Log($"[AI Placement Debug] 받은 경로 첫 번째 노드: ({monsterPathContext[0].x}, {monsterPathContext[0].y}), 마지막 노드: ({monsterPathContext[monsterPathContext.Count-1].x}, {monsterPathContext[monsterPathContext.Count-1].y})");

            meleePathTiles = BuildMonsterPathTileSet(monsterPathContext);
            var onPathTiles = allValidTiles.Where(tile => meleePathTiles.Contains(tile)).ToList();

            // 경로 위에 배치 가능한 타일이 있다면, 후보지를 그 타일들로 제한합니다.
            if (onPathTiles.Count > 0)
            {
                candidateTiles = onPathTiles;
                // Debug.Log($"[AI Placement] 근접 유닛 {unitData.unitName} 배치: 몬스터 경로 위 {onPathTiles.Count}개 타일로 후보지 제한");
            }
            else
            {
                // Debug.Log($"[AI Placement] 근접 유닛 {unitData.unitName} 배치: 몬스터 경로 위에 배치 가능한 타일이 없어 전체 {allValidTiles.Count}개 타일 대상");
            }
            // 경로 위에 배치할 곳이 없다면, 원래의 모든 유효 타일을 대상으로 점수를 계산합니다(폴백).
        }

        if (unitData.unitType == UnitType.Ranged && monsterPathContext != null && monsterPathContext.Count > 0)
        {
            rangedPathTiles = BuildMonsterPathTileSet(monsterPathContext);
            candidateTiles = FilterRangedCandidatesForMonsterPath(allValidTiles, monsterPathContext, unitData.attackRange);
            if (candidateTiles == null || candidateTiles.Count == 0)
            {
                candidateTiles = allValidTiles;
            }
        }

        if (unitData.unitType == UnitType.Ranged)
        {
            candidateTiles = SelectPreferredRangedCandidateTier(candidateTiles, occupiedTiles, movingUnitOriginalPos);
            if ((candidateTiles == null || candidateTiles.Count == 0) && !ReferenceEquals(candidateTiles, allValidTiles))
            {
                candidateTiles = SelectPreferredRangedCandidateTier(allValidTiles, occupiedTiles, movingUnitOriginalPos);
            }
        }

        var alliedUnits = alliedUnitsContext ?? GetAlliedUnitsOnField();

        Vector3Int bestPosition = Vector3Int.zero;
        float highestScore = -1f;

        // 후보 타일들('candidateTiles')을 순회하며 최고 점수 위치를 찾습니다.
        foreach (var tilePos in candidateTiles)
        {
            if (occupiedTiles != null)
            {
                if (occupiedTiles.Contains(tilePos)) continue;
            }
            else
            {
                // 현재 이동시키려는 유닛의 원래 위치가 아니라면, 점유된 타일은 건너뜁니다.
                bool isSpotOfMovingUnit = movingUnitOriginalPos.HasValue && tilePos == movingUnitOriginalPos.Value;
                if (IsUnitAt(tilePos) && !isSpotOfMovingUnit)
                {
                    continue;
                }
            }

            var context = new AIContext(playerManager, unitData, tilePos, alliedUnits, monsterPathContext);
            float currentScore = CalculateScore(context, _placementConsiderations, tilePos);
            if (unitData.unitType == UnitType.Ranged && rangedPathTiles != null && rangedPathTiles.Count > 0)
            {
                currentScore += CalculateRangedPathPriorityBonus(tilePos, rangedPathTiles, unitData.attackRange);
            }
            else if (unitData.unitType == UnitType.Melee && meleePathTiles != null && meleePathTiles.Contains(tilePos))
            {
                currentScore += CalculateNearbyRangedAllyBonus(tilePos, alliedUnits);
            }

            // 디버그용 점수 저장
            _debugTileScores[tilePos] = currentScore;

            if (currentScore > highestScore)
            {
                highestScore = currentScore;
                bestPosition = tilePos;
            }
        }

        if (highestScore > -1f)
        {
            if (movingUnitOriginalPos.HasValue &&
                bestPosition == movingUnitOriginalPos.Value &&
                ShouldForceMoveAwayFromOriginal(movingUnitOriginalPos))
            {
                var fallback = unitData.unitType == UnitType.Ranged
                    ? FindBestRangedFallbackAwayFromOriginal(
                        unitData,
                        allValidTiles,
                        alliedUnits,
                        monsterPathContext,
                        rangedPathTiles,
                        occupiedTiles,
                        movingUnitOriginalPos)
                    : FindBestMeleeFallbackAwayFromOriginal(
                        unitData,
                        allValidTiles,
                        alliedUnits,
                        monsterPathContext,
                        occupiedTiles,
                        movingUnitOriginalPos);
                if (fallback.HasValue)
                {
                    ClearDebugScores();
                    return fallback.Value;
                }
            }
            // Debug.Log($"[AI Placement] {unitData.unitName}을(를) {bestPosition}에 배치 (점수: {highestScore:F2})");

            // 3초 후 디버그 표시 끄기
            ScheduleClearDebugScores();

            return bestPosition;
        }

        // 점수 계산에 실패했더라도, 배치 가능한 첫 번째 위치라도 반환합니다.
        if (unitData.unitType == UnitType.Ranged)
        {
            var fallback = FindBestRangedFallbackAwayFromOriginal(
                unitData,
                allValidTiles,
                alliedUnits,
                monsterPathContext,
                rangedPathTiles,
                occupiedTiles,
                movingUnitOriginalPos);
            if (fallback.HasValue)
            {
                ClearDebugScores();
                return fallback.Value;
            }

            if (rangedPathTiles != null && rangedPathTiles.Count > 0)
            {
                ClearDebugScores();
                return movingUnitOriginalPos;
            }
        }

        if (unitData.unitType == UnitType.Melee)
        {
            var fallback = FindBestMeleeFallbackAwayFromOriginal(
                unitData,
                allValidTiles,
                alliedUnits,
                monsterPathContext,
                occupiedTiles,
                movingUnitOriginalPos);
            if (fallback.HasValue)
            {
                ClearDebugScores();
                return fallback.Value;
            }
        }

        ClearDebugScores();
        return FindFirstEmptySlot(unitData);
    }

    private Vector3Int? FindBestRangedFallbackAwayFromOriginal(
        UnitData unitData,
        List<Vector3Int> allValidTiles,
        List<Unit> alliedUnits,
        List<AstarNode> monsterPathContext,
        HashSet<Vector3Int> rangedPathTiles,
        HashSet<Vector3Int> occupiedTiles,
        Vector3Int? movingUnitOriginalPos)
    {
        if (unitData == null || allValidTiles == null || allValidTiles.Count == 0)
        {
            return null;
        }

        var fallbackTiles = SelectPreferredRangedCandidateTier(allValidTiles, occupiedTiles, movingUnitOriginalPos);
        if (fallbackTiles == null || fallbackTiles.Count == 0)
        {
            return null;
        }

        Vector3Int bestPosition = Vector3Int.zero;
        float highestScore = -1f;
        foreach (var tilePos in fallbackTiles)
        {
            if (movingUnitOriginalPos.HasValue && tilePos == movingUnitOriginalPos.Value)
            {
                continue;
            }

            if (occupiedTiles != null && occupiedTiles.Contains(tilePos))
            {
                continue;
            }

            if (IsUnitAt(tilePos))
            {
                continue;
            }

            var context = new AIContext(playerManager, unitData, tilePos, alliedUnits, monsterPathContext);
            float currentScore = CalculateScore(context, _placementConsiderations, tilePos);
            currentScore += CalculateRangedPathPriorityBonus(tilePos, rangedPathTiles, unitData.attackRange);
            currentScore += CalculateFieldCenterScore(tilePos) * 3.0f;

            if (IsOuterRingCell(tilePos) || IsBorderGapCell(tilePos))
            {
                currentScore -= 20.0f;
            }
            else if (IsNearFieldEdgeCell(tilePos))
            {
                currentScore -= 8.0f;
            }

            if (currentScore > highestScore)
            {
                highestScore = currentScore;
                bestPosition = tilePos;
            }
        }

        return highestScore > -1f ? bestPosition : (Vector3Int?)null;
    }

    private Vector3Int? FindBestMeleeFallbackAwayFromOriginal(
        UnitData unitData,
        List<Vector3Int> allValidTiles,
        List<Unit> alliedUnits,
        List<AstarNode> monsterPathContext,
        HashSet<Vector3Int> occupiedTiles,
        Vector3Int? movingUnitOriginalPos)
    {
        if (unitData == null || allValidTiles == null || allValidTiles.Count == 0)
        {
            return null;
        }

        var pathTiles = BuildMonsterPathTileSet(monsterPathContext);
        var pathCandidates = pathTiles.Count > 0
            ? allValidTiles.Where(tile => pathTiles.Contains(tile)).ToList()
            : new List<Vector3Int>();
        var fallbackTiles = pathCandidates.Count > 0 ? pathCandidates : allValidTiles;

        Vector3Int bestPosition = Vector3Int.zero;
        float highestScore = -1f;
        foreach (var tilePos in fallbackTiles)
        {
            if (movingUnitOriginalPos.HasValue && tilePos == movingUnitOriginalPos.Value)
            {
                continue;
            }

            if (occupiedTiles != null && occupiedTiles.Contains(tilePos))
            {
                continue;
            }

            if (IsUnitAt(tilePos) || HasWallAt(tilePos))
            {
                continue;
            }

            var context = new AIContext(playerManager, unitData, tilePos, alliedUnits, monsterPathContext);
            float currentScore = CalculateScore(context, _placementConsiderations, tilePos);
            if (pathTiles.Contains(tilePos))
            {
                currentScore += 20.0f;
                currentScore += CalculateNearbyRangedAllyBonus(tilePos, alliedUnits);
            }

            currentScore += CalculateFieldCenterScore(tilePos) * 2.0f;
            if (IsOuterRingCell(tilePos) || IsBorderGapCell(tilePos))
            {
                currentScore -= 20.0f;
            }
            else if (IsNearFieldEdgeCell(tilePos))
            {
                currentScore -= 8.0f;
            }

            if (currentScore > highestScore)
            {
                highestScore = currentScore;
                bestPosition = tilePos;
            }
        }

        return highestScore > -1f ? bestPosition : (Vector3Int?)null;
    }

    private bool ShouldForceMoveAwayFromOriginal(Vector3Int? movingUnitOriginalPos)
    {
        if (!movingUnitOriginalPos.HasValue)
        {
            return false;
        }

        var original = movingUnitOriginalPos.Value;
        return IsNearFieldEdgeCell(original) || IsBorderGapCell(original);
    }

    private List<Vector3Int> FilterRangedCandidatesForMonsterPath(
        List<Vector3Int> allValidTiles,
        List<AstarNode> monsterPathContext,
        float attackRange)
    {
        if (allValidTiles == null || allValidTiles.Count == 0 || monsterPathContext == null || monsterPathContext.Count == 0)
        {
            return allValidTiles;
        }

        var pathTiles = BuildMonsterPathTileSet(monsterPathContext);

        if (pathTiles.Count == 0)
        {
            return new List<Vector3Int>();
        }

        var pathCoveringTiles = allValidTiles
            .Where(tile => CountCoveredMonsterPathTiles(tile, pathTiles, attackRange) > 0)
            .ToList();
        if (pathCoveringTiles.Count == 0)
        {
            return new List<Vector3Int>();
        }

        var coverageByTile = pathCoveringTiles
            .ToDictionary(tile => tile, tile => CountCoveredMonsterPathTiles(tile, pathTiles, attackRange));
        int maxCovered = coverageByTile.Values.Max();
        int minStrongCovered = Mathf.Max(1, Mathf.CeilToInt(maxCovered * 0.85f));
        var strongCoverageTiles = coverageByTile
            .Where(kvp => kvp.Value >= minStrongCovered)
            .Select(kvp => kvp.Key)
            .ToList();
        var maxCoverageTiles = coverageByTile
            .Where(kvp => kvp.Value == maxCovered)
            .Select(kvp => kvp.Key)
            .ToList();

        var centralStrongCoverageTiles = strongCoverageTiles
            .Where(tile => !IsOuterRingCell(tile) &&
                           !IsNearFieldEdgeCell(tile) &&
                           !IsBorderGapCell(tile) &&
                           CalculateFieldCenterScore(tile) >= 0.55f)
            .ToList();
        if (centralStrongCoverageTiles.Count > 0)
        {
            return centralStrongCoverageTiles;
        }

        var centralMaxCoverageTiles = maxCoverageTiles
            .Where(tile => !IsOuterRingCell(tile) &&
                           !IsNearFieldEdgeCell(tile) &&
                           !IsBorderGapCell(tile) &&
                           CalculateFieldCenterScore(tile) >= 0.55f)
            .ToList();
        if (centralMaxCoverageTiles.Count > 0)
        {
            return centralMaxCoverageTiles;
        }

        var interiorPathCoveringTiles = strongCoverageTiles
            .Where(tile => !IsOuterRingCell(tile) &&
                           !IsNearFieldEdgeCell(tile) &&
                           !IsBorderGapCell(tile))
            .ToList();
        if (interiorPathCoveringTiles.Count > 0)
        {
            var centralInteriorTiles = interiorPathCoveringTiles
                .Where(tile => CalculateFieldCenterScore(tile) >= 0.55f)
                .ToList();
            return centralInteriorTiles.Count > 0
                ? centralInteriorTiles
                : interiorPathCoveringTiles;
        }

        var centralAnyCoverageTiles = pathCoveringTiles
            .Where(tile => !IsOuterRingCell(tile) &&
                           !IsNearFieldEdgeCell(tile) &&
                           !IsBorderGapCell(tile) &&
                           CalculateFieldCenterScore(tile) >= 0.45f)
            .ToList();
        if (centralAnyCoverageTiles.Count > 0)
        {
            int centralMaxCovered = centralAnyCoverageTiles.Max(tile => coverageByTile[tile]);
            int minCentralCovered = Mathf.Max(1, Mathf.CeilToInt(centralMaxCovered * 0.70f));
            var centralCoverageBand = centralAnyCoverageTiles
                .Where(tile => coverageByTile[tile] >= minCentralCovered)
                .ToList();
            return centralCoverageBand.Count > 0 ? centralCoverageBand : centralAnyCoverageTiles;
        }

        return new List<Vector3Int>();
    }

    private List<Vector3Int> SelectPreferredRangedCandidateTier(
        List<Vector3Int> candidateTiles,
        HashSet<Vector3Int> occupiedTiles,
        Vector3Int? movingUnitOriginalPos)
    {
        if (candidateTiles == null || candidateTiles.Count == 0)
        {
            return candidateTiles;
        }

        var strictInterior = candidateTiles
            .Where(tile => IsStrictInteriorRangedCell(tile))
            .ToList();
        if (HasAvailablePlacementTile(strictInterior, occupiedTiles, movingUnitOriginalPos))
        {
            return strictInterior;
        }

        var innerRing = candidateTiles
            .Where(tile => !IsOuterRingCell(tile) && !IsBorderGapCell(tile))
            .ToList();
        if (HasAvailablePlacementTile(innerRing, occupiedTiles, movingUnitOriginalPos))
        {
            return innerRing;
        }

        var nonGap = candidateTiles
            .Where(tile => !IsBorderGapCell(tile))
            .ToList();
        if (HasAvailablePlacementTile(nonGap, occupiedTiles, movingUnitOriginalPos))
        {
            return nonGap;
        }

        return candidateTiles;
    }

    private bool HasAvailablePlacementTile(
        List<Vector3Int> candidateTiles,
        HashSet<Vector3Int> occupiedTiles,
        Vector3Int? movingUnitOriginalPos)
    {
        if (candidateTiles == null || candidateTiles.Count == 0)
        {
            return false;
        }

        foreach (var tile in candidateTiles)
        {
            if (occupiedTiles != null && occupiedTiles.Contains(tile))
            {
                continue;
            }

            bool isSpotOfMovingUnit = movingUnitOriginalPos.HasValue && tile == movingUnitOriginalPos.Value;
            if (IsUnitAt(tile) && !isSpotOfMovingUnit)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private HashSet<Vector3Int> BuildMonsterPathTileSet(List<AstarNode> monsterPathContext)
    {
        var pathTiles = new HashSet<Vector3Int>();
        if (monsterPathContext == null)
        {
            return pathTiles;
        }

        foreach (var node in monsterPathContext)
        {
            if (node == null)
            {
                continue;
            }

            var tile = new Vector3Int(node.x, node.y, 0);
            if (IsValidGridPosition(tile))
            {
                pathTiles.Add(tile);
            }
        }

        return pathTiles;
    }

    private float CalculateRangedPathPriorityBonus(Vector3Int position, HashSet<Vector3Int> pathTiles, float attackRange)
    {
        if (pathTiles == null || pathTiles.Count == 0)
        {
            return 0f;
        }

        int covered = CountCoveredMonsterPathTiles(position, pathTiles, attackRange);
        if (covered <= 0)
        {
            return 0f;
        }

        int usefulTargetCount = Mathf.Max(1, Mathf.Min(pathTiles.Count, CountTilesInAttackCircle(attackRange)));
        float coverageScore = Mathf.Clamp01((float)covered / usefulTargetCount);
        float centerScore = CalculateFieldCenterScore(position);
        float edgePenalty = IsNearFieldEdgeCell(position) ? -3.5f : 0f;
        float borderPenalty = IsOuterRingCell(position) || IsBorderGapCell(position) ? -6.0f : 0f;
        return covered * 4.0f + coverageScore * 6.0f + centerScore * 8.0f + edgePenalty + borderPenalty;
    }

    private float CalculateNearbyRangedAllyBonus(Vector3Int position, List<Unit> alliedUnits)
    {
        if (alliedUnits == null || alliedUnits.Count == 0)
        {
            return 0f;
        }

        float bestBonus = 0f;
        foreach (var ally in alliedUnits)
        {
            if (ally == null || ally.Data == null || ally.Data.unitType != UnitType.Ranged)
            {
                continue;
            }

            if (!TryGetUnitGridPosition(ally, out var allyCell) || allyCell == position)
            {
                continue;
            }

            int distance = Mathf.Max(Mathf.Abs(allyCell.x - position.x), Mathf.Abs(allyCell.y - position.y));
            if (distance <= 1)
            {
                bestBonus = Mathf.Max(bestBonus, 34f);
            }
            else if (distance == 2)
            {
                bestBonus = Mathf.Max(bestBonus, 18f);
            }
            else if (distance == 3)
            {
                bestBonus = Mathf.Max(bestBonus, 8f);
            }
        }

        return bestBonus;
    }

    private bool TryGetUnitGridPosition(Unit unit, out Vector3Int position)
    {
        position = default(Vector3Int);
        if (unit == null)
        {
            return false;
        }

        var registeredPosition = GetUnitPosition(unit);
        if (registeredPosition.HasValue)
        {
            position = registeredPosition.Value;
            return true;
        }

        position = WorldToGridInt(unit.transform.position);
        return IsValidGridPosition(position);
    }

    private int CountTilesInAttackCircle(float attackRange)
    {
        int count = 0;
        int intRange = Mathf.CeilToInt(Mathf.Max(0f, attackRange));
        float sqrRange = attackRange * attackRange;
        for (int x = -intRange; x <= intRange; x++)
        {
            for (int y = -intRange; y <= intRange; y++)
            {
                if (x * x + y * y <= sqrRange)
                {
                    count++;
                }
            }
        }

        return Mathf.Max(1, count);
    }

    private float CalculateFieldCenterScore(Vector3Int position)
    {
        return CalculateFieldCenterScore(gridSize, position);
    }

    public static float CalculateFieldCenterScore(Vector2Int size, Vector3Int position)
    {
        float centerX = (size.x - 1) * 0.5f;
        float centerY = (size.y - 1) * 0.5f;
        float maxDistance = Mathf.Sqrt(centerX * centerX + centerY * centerY);
        if (maxDistance <= 0f)
        {
            return 1f;
        }

        float distance = Vector2.Distance(new Vector2(position.x, position.y), new Vector2(centerX, centerY));
        return 1f - Mathf.Clamp01(distance / maxDistance);
    }

    private int CountCoveredMonsterPathTiles(Vector3Int position, HashSet<Vector3Int> pathTiles, float attackRange)
    {
        return CountCoveredPathTiles(position, pathTiles, attackRange);
    }

    public static int CountCoveredPathTiles(
        Vector3Int position,
        HashSet<Vector3Int> pathTiles,
        float attackRange)
    {
        if (pathTiles == null || pathTiles.Count == 0)
        {
            return 0;
        }

        int covered = 0;
        int intRange = Mathf.CeilToInt(Mathf.Max(0f, attackRange));
        float sqrRange = attackRange * attackRange;
        for (int x = -intRange; x <= intRange; x++)
        {
            for (int y = -intRange; y <= intRange; y++)
            {
                if (x * x + y * y > sqrRange)
                {
                    continue;
                }

                if (pathTiles.Contains(position + new Vector3Int(x, y, 0)))
                {
                    covered++;
                }
            }
        }

        return covered;
    }

    private bool IsOuterRingCell(Vector3Int cell)
    {
        return cell.x <= 0 ||
               cell.y <= 0 ||
               cell.x >= gridSize.x - 1 ||
               cell.y >= gridSize.y - 1;
    }

    private bool IsNearFieldEdgeCell(Vector3Int cell)
    {
        return cell.x <= 1 ||
               cell.y <= 1 ||
               cell.x >= gridSize.x - 2 ||
               cell.y >= gridSize.y - 2;
    }

    private bool IsStrictInteriorRangedCell(Vector3Int cell)
    {
        return !IsNearFieldEdgeCell(cell) && !IsBorderGapCell(cell);
    }

    private bool IsBorderGapCell(Vector3Int cell)
    {
        foreach (var gap in GetBorderGapCells())
        {
            if (gap.x == cell.x && gap.y == cell.y)
            {
                return true;
            }
        }

        return false;
    }

    private float CalculateScore(AIContext context, List<Consideration> considerations, Vector3Int tilePos)
    {
        float totalScore = 0;
        float weightSum = 0;

        // 디버그용 세부 점수 저장
        var breakdown = new DebugScoreBreakdown();

        foreach (var consideration in considerations)
        {
            float score = consideration.Score(context);
            float weightedScore = score * consideration.weight;
            totalScore += weightedScore;
            weightSum += consideration.weight;

            // 주요 고려사항들의 점수를 별도로 저장
            if (consideration is MeleePlacementConsideration)
            {
                breakdown.groundScore = score;
            }
            else if (consideration is OnMonsterPathConsideration)
            {
                breakdown.pathScore = score;
            }
            else if (consideration is MeleeProtectsRangedConsideration)
            {
                breakdown.allyScore = score;
            }
        }

        float finalScore = (weightSum > 0) ? totalScore / weightSum : 0;
        breakdown.totalScore = finalScore;

        // 디버그 정보 저장
        _debugScoreBreakdowns[tilePos] = breakdown;



        return finalScore;
    }

}
