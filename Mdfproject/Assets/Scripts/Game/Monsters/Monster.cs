// Assets/Scripts/Game/Monsters/Monster.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public struct MonsterData
{
    public int Hp;
    public int Damage;

    public MonsterData(int hp, int damage)
    {
        Hp = hp;
        Damage = damage;
    }
};

public enum MonsterType { Ground, Flying }

public interface IEnemy
{
    void TakeDamage(int damage);
}

public class Monster : MonoBehaviour
{
    [Header("Test")]
    private GameObject PlaneObject;

    [Header("MonsterData")]
    public MonsterData md = new MonsterData(100, 1);
    public MonsterType monsterType;

    [Header("이동 설정")]
    public float moveSpeed = 2f;
    public float arrivalThreshold = 0.1f;
    public bool smoothMovement = true;

    [Header("디버깅")]
    public bool showPath = true;
    public bool showCurrentTarget = true;

    // --- 내부 상태 변수 ---
    private Transform goalTransform;
    private List<AstarNode> currentPath;
    private int currentPathIndex = 0;
    private Vector2 currentTarget;
    private bool isMoving = false;
    private AstarGrid pathfinder;
    private List<Vector2Int> wallsToBreak = new List<Vector2Int>();
    private bool isBlocked = false;
    private Unit blockingUnit;
    private PlayerManager ownerPlayer;
    private Coroutine movementCoroutine;

    private static bool isQuitting = false; // ✅ [수정] 게임 종료 상태를 저장할 static 변수

    // ✅ [수정] 게임이 종료될 때 isQuitting을 true로 설정하는 이벤트 함수
    void OnApplicationQuit()
    {
        isQuitting = true;
    }

    void Start()
    {
        pathfinder = FindObjectOfType<AstarGrid>();
        currentHP = maxHP;
        PlaneObject = GameObject.Find("Plane");
    }

    /// <summary>
    /// 몬스터 생성 시 주인(PlayerManager)과 목표 지점(Transform)을 주입받습니다.
    /// 이 함수는 MonsterSpawner에 의해 호출됩니다.
    /// </summary>
    public void Initialize(PlayerManager owner, Transform goal)
    {
        this.ownerPlayer = owner;
        this.goalTransform = goal;
    }

    [Header("체력 설정")]
    public int maxHP = 100;
    public int currentHP;

    public void TakeDamage(int damage)
    {
        currentHP -= damage;
        Debug.Log($"{gameObject.name} HP: {currentHP}/{maxHP}");

        if (currentHP <= 0)
        {
            Die();
        }
    }

    public void ApplyBuff(float healthMultiplier, float speedMultiplier)
    {
        maxHP = (int)(maxHP * healthMultiplier);
        currentHP = maxHP;
        moveSpeed *= speedMultiplier;
        Debug.Log($"{gameObject.name}이 강화되었습니다! HP: {maxHP}, Speed: {moveSpeed}");
    }

    void Die()
    {
        Debug.Log($"{gameObject.name}이(가) 죽었습니다!");

        if (isBlocked && blockingUnit != null)
        {
            blockingUnit.ReleaseBlockedMonster(this);
        }

        Destroy(gameObject);
    }

    public void StartFollowingPath(List<AstarNode> path)
    {
        if (!this.gameObject.activeInHierarchy)
        {
            this.gameObject.SetActive(true);
        }

        if (movementCoroutine != null)
        {
            StopCoroutine(movementCoroutine);
        }

        if (monsterType == MonsterType.Flying)
        {
            isMoving = true;
            Debug.Log($"🎯 공중 몬스터 이동 시작! 목표: {goalTransform.name}");
            movementCoroutine = StartCoroutine(FlyDirectlyCoroutine());
            return;
        }

        if (path == null || path.Count == 0)
        {
            Debug.LogWarning("❌ 유효하지 않은 경로입니다!");
            return;
        }

        currentPath = new List<AstarNode>(path);
        currentPathIndex = 1;
        isMoving = true;

        Debug.Log($"🎯 지상 몬스터 이동 시작! 총 {currentPath.Count}개 지점");

        if (smoothMovement)
            movementCoroutine = StartCoroutine(SmoothMoveCoroutine());
        else
            movementCoroutine = StartCoroutine(InstantMoveCoroutine());
    }

    private IEnumerator FlyDirectlyCoroutine()
    {
        if (goalTransform == null)
        {
            Debug.LogError("공중 몬스터의 목표(Goal)가 설정되지 않았습니다!");
            yield break;
        }

        Vector2 targetPosition = goalTransform.position;
        while (Vector2.Distance(transform.position, targetPosition) > arrivalThreshold && isMoving)
        {
            transform.position = Vector2.MoveTowards(transform.position, targetPosition, moveSpeed * Time.deltaTime);
            yield return null;
        }

        OnPathCompleted();
    }

    private IEnumerator SmoothMoveCoroutine()
    {
        while (currentPathIndex < currentPath.Count && isMoving)
        {
            currentTarget = new Vector2(currentPath[currentPathIndex].x, currentPath[currentPathIndex].y);
            Vector2 startPos = transform.position;
            float journeyLength = Vector2.Distance(startPos, currentTarget);
            float journeyTime = journeyLength / moveSpeed;
            float elapsedTime = 0;

            while (elapsedTime < journeyTime && isMoving)
            {
                elapsedTime += Time.deltaTime;
                float fractionOfJourney = elapsedTime / journeyTime;
                transform.position = Vector2.Lerp(startPos, currentTarget, fractionOfJourney);
                yield return null;
            }

            transform.position = currentTarget;
            currentPathIndex++;
            yield return new WaitForSeconds(0.1f);
        }

        OnPathCompleted();
    }

    private IEnumerator InstantMoveCoroutine()
    {
        while (currentPathIndex < currentPath.Count && isMoving)
        {
            currentTarget = new Vector2(currentPath[currentPathIndex].x, currentPath[currentPathIndex].y);

            while (Vector2.Distance(transform.position, currentTarget) > arrivalThreshold && isMoving)
            {
                transform.position = Vector2.MoveTowards(transform.position, currentTarget, moveSpeed * Time.deltaTime);
                yield return null;
            }

            Debug.Log($"✅ {currentPathIndex}번째 지점 도착: ({currentTarget.x}, {currentTarget.y})");
            currentPathIndex++;
        }

        OnPathCompleted();
    }

    private void OnPathCompleted()
    {
        isMoving = false;
        Debug.Log("🏆 목표 지점에 도착했습니다!");
        OnReachedDestination();
    }

    private void OnReachedDestination()
    {
        Debug.Log("💀 몬스터가 목표에 도달했습니다!");

        if (GameManagers.Instance != null && ownerPlayer != null)
        {
            GameManagers.Instance.OnMonsterReachedGoal(ownerPlayer);
        }

        Destroy(gameObject);
    }

    public void StopMovement()
    {
        isMoving = false;
        StopAllCoroutines();
        Debug.Log("⏹️ 몬스터 이동이 중단되었습니다.");
    }

    public void FindAndFollowPath(Vector2Int targetPosition)
    {
        StopMovement();
        Vector2Int currentPos = new Vector2Int(Mathf.RoundToInt(transform.position.x), Mathf.RoundToInt(transform.position.y));
        pathfinder.startPos = currentPos;
        pathfinder.targetPos = targetPosition;
        pathfinder.PathFinding();

        if (pathfinder.FinalNodeList != null && pathfinder.FinalNodeList.Count > 0)
        {
            StartFollowingPath(pathfinder.FinalNodeList);
        }
        else
        {
            Debug.LogError("❌ 경로를 찾을 수 없습니다!");
        }
    }

    public void RecalculatePathIfBlocked()
    {
        if (!isMoving || currentPath == null) return;
        Vector2Int checkPos = new Vector2Int(currentPath[currentPathIndex].x, currentPath[currentPathIndex].y);
        Collider2D obstacle = Physics2D.OverlapCircle(new Vector2(checkPos.x, checkPos.y), 0.4f, LayerMask.GetMask("Wall"));
        if (obstacle != null)
        {
            Debug.LogWarning("⚠️ 경로상에 새로운 장애물 발견! 경로 재계산 중...");
            Vector2Int finalDestination = new Vector2Int(currentPath[currentPath.Count - 1].x, currentPath[currentPath.Count - 1].y);
            FindAndFollowPath(finalDestination);
        }
    }

    #region 저지 시스템 관련 메서드
    public bool IsBlocked() { return isBlocked; }

    public void Block(Unit unit) 
    { 
        if (isBlocked) return;

        isBlocked = true; 
        blockingUnit = unit; 
        
        if (movementCoroutine != null)
        {
            StopCoroutine(movementCoroutine);
            movementCoroutine = null;
        }
        isMoving = false;

        Debug.Log($"{gameObject.name}이(가) {unit.name}에 의해 저지됨!"); 
    }

    public void Unblock() 
    { 
        // ✅ [수정] 게임이 종료되는 중이라면 아무 작업도 수행하지 않고 즉시 함수를 빠져나갑니다.
        if (isQuitting) return;

        if (!isBlocked) return;

        isBlocked = false; 
        blockingUnit = null; 
        Debug.Log($"{gameObject.name} 저지 해제! 경로 탐색 재시작."); 
        
        Vector2Int currentGridPos = new Vector2Int(Mathf.RoundToInt(transform.position.x), Mathf.RoundToInt(transform.position.y));
        Vector2Int targetGridPos = new Vector2Int(Mathf.RoundToInt(goalTransform.position.x), Mathf.RoundToInt(goalTransform.position.y));
        
        List<AstarNode> newPath = pathfinder.FindPath(currentGridPos, targetGridPos);
        if (newPath != null && newPath.Count > 0)
        {
            StartFollowingPath(newPath);
        }
        else
        {
            Debug.LogError($"{gameObject.name} 저지 해제 후 경로를 찾을 수 없습니다!");
        }
    }
    #endregion

    #region 부수기 벽
    public void SetWallsToBreak(List<Vector2Int> walls)
    {
        wallsToBreak = walls;
        Debug.Log($"몬스터가 파괴할 벽 {walls.Count}개 설정됨");
        foreach (Vector2Int wall in walls)
        {
            Debug.Log($"   - 파괴 대상 벽: ({wall.x}, {wall.y})");
        }
    }
    #endregion

    void OnDrawGizmos()
    {
        if (!showPath || currentPath == null || currentPath.Count == 0) return;

        Gizmos.color = Color.green;
        for (int i = 0; i < currentPath.Count - 1; i++)
        {
            Vector3 from = new Vector3(currentPath[i].x, currentPath[i].y, 0);
            Vector3 to = new Vector3(currentPath[i + 1].x, currentPath[i + 1].y, 0);
            Gizmos.DrawLine(from, to);
        }

        Gizmos.color = Color.yellow;
        foreach (AstarNode node in currentPath)
        {
            Gizmos.DrawWireSphere(new Vector3(node.x, node.y, 0), 0.2f);
        }

        if (showCurrentTarget && isMoving && currentPathIndex < currentPath.Count)
        {
            Gizmos.color = Color.red;
            Vector3 targetPos = new Vector3(currentPath[currentPathIndex].x, currentPath[currentPathIndex].y, 0);
            Gizmos.DrawWireSphere(targetPos, 0.4f);
        }
    }
}