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

// 적 인터페이스 (선택사항)
public interface IEnemy
{
    void TakeDamage(int damage);
}

public class Monster : MonoBehaviour
{
    [Header("Test")]
    private GameObject PlaneObject;
    public GameObject EndUI;

    [Header("MonsterData")]
    public MonsterData md = new MonsterData(100, 1);

    [Header("이동 설정")]
    public float moveSpeed = 2f;              // 이동 속도
    public float arrivalThreshold = 0.1f;     // 도착 판정 거리
    public bool smoothMovement = true;        // 부드러운 이동 여부
    
    [Header("디버깅")]
    public bool showPath = true;              // 경로 표시
    public bool showCurrentTarget = true;     // 현재 목표점 표시
    
    private List<Node> currentPath;           // 현재 따라가는 경로
    private int currentPathIndex = 0;         // 현재 목표하는 경로상의 인덱스
    private Vector2 currentTarget;            // 현재 목표 좌표
    private bool isMoving = false;            // 이동 중인지 여부
    private TestCode pathfinder;              // PathFinding 스크립트 참조

    void Start()
    {
        pathfinder = FindObjectOfType<TestCode>();
        currentHP = maxHP;
        PlaneObject = GameObject.Find("Plane");
        

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
        Destroy(gameObject);
    }

    /// <summary>
    /// 새로운 경로로 이동 시작
    /// </summary>
    public void StartFollowingPath(List<Node> path)
    {
        if (!this.gameObject.activeInHierarchy)
        {
            this.gameObject.SetActive(true);
        }

        if (path == null || path.Count == 0)
        {
            Debug.LogWarning("❌ 유효하지 않은 경로입니다!");
            return;
        }

        currentPath = new List<Node>(path); // 복사본 생성
        currentPathIndex = 1; // 0번은 시작점이므로 1번부터 시작
        isMoving = true;

        Debug.Log($"🎯 몬스터 이동 시작! 총 {currentPath.Count}개 지점");

        if (smoothMovement)
            StartCoroutine(SmoothMoveCoroutine());
        else
            StartCoroutine(InstantMoveCoroutine());
    }

    /// <summary>
    /// 부드러운 이동 (Lerp 사용)
    /// </summary>
    private IEnumerator SmoothMoveCoroutine()
    {
        while (currentPathIndex < currentPath.Count && isMoving)
        {
            currentTarget = new Vector2(currentPath[currentPathIndex].x, currentPath[currentPathIndex].y);
            Vector2 startPos = transform.position;
            float journeyLength = Vector2.Distance(startPos, currentTarget);
            float journeyTime = journeyLength / moveSpeed;
            float elapsedTime = 0;

            Debug.Log($"🏃 {currentPathIndex}번째 목표로 이동: ({currentTarget.x}, {currentTarget.y})");

            while (elapsedTime < journeyTime && isMoving)
            {
                elapsedTime += Time.deltaTime;
                float fractionOfJourney = elapsedTime / journeyTime;
                transform.position = Vector2.Lerp(startPos, currentTarget, fractionOfJourney);
                yield return null;
            }

            // 목표점에 정확히 도착
            transform.position = currentTarget;
            currentPathIndex++;

            // 잠깐 대기 (선택사항)
            yield return new WaitForSeconds(0.1f);
        }

        OnPathCompleted();
    }

    /// <summary>
    /// 즉시 이동 (MoveTowards 사용)
    /// </summary>
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

    /// <summary>
    /// 경로 완주 시 호출
    /// </summary>
    private void OnPathCompleted()
    {
        isMoving = false;
        Debug.Log("🏆 목표 지점에 도착했습니다!");
        
        // 도착 후 처리 (예: 플레이어 공격, 아이템 획득 등)
        OnReachedDestination();
    }

    /// <summary>
    /// 목표 도달 시 실행할 로직
    /// </summary>
    private void OnReachedDestination()
    {
        // 여기에 목표 도달 시 실행할 코드 작성
        Debug.Log("💀 몬스터가 목표에 도달했습니다!");

        PlaneObject.SetActive(false);

        if (EndUI == null)
            EndUI = GameObject.Find("EndUI");

        EndUI.SetActive(true);
    }

    /// <summary>
    /// 이동 중단
    /// </summary>
    public void StopMovement()
    {
        isMoving = false;
        StopAllCoroutines();
        Debug.Log("⏹️ 몬스터 이동이 중단되었습니다.");
    }

    /// <summary>
    /// 새로운 경로 계산 및 이동 시작
    /// </summary>
    public void FindAndFollowPath(Vector2Int targetPosition)
    {
        StopMovement(); // 기존 이동 중단
        
        // 현재 위치를 시작점으로 설정
        Vector2Int currentPos = new Vector2Int(Mathf.RoundToInt(transform.position.x), Mathf.RoundToInt(transform.position.y));
        pathfinder.startPos = currentPos;
        pathfinder.targetPos = targetPosition;
        
        // 경로 계산
        pathfinder.PathFinding();
        
        // 계산된 경로로 이동 시작
        if (pathfinder.FinalNodeList != null && pathfinder.FinalNodeList.Count > 0)
        {
            StartFollowingPath(pathfinder.FinalNodeList);
        }
        else
        {
            Debug.LogError("❌ 경로를 찾을 수 없습니다!");
        }
    }

    /// <summary>
    /// 경로상의 장애물 감지 시 재계산
    /// </summary>
    public void RecalculatePathIfBlocked()
    {
        if (!isMoving || currentPath == null) return;

        // 현재 목표점이 막혔는지 확인
        Vector2Int checkPos = new Vector2Int(currentPath[currentPathIndex].x, currentPath[currentPathIndex].y);
        
        // 장애물 감지 로직 (예시)
        Collider2D obstacle = Physics2D.OverlapCircle(new Vector2(checkPos.x, checkPos.y), 0.4f, LayerMask.GetMask("Wall"));
        
        if (obstacle != null)
        {
            Debug.LogWarning("⚠️ 경로상에 새로운 장애물 발견! 경로 재계산 중...");
            Vector2Int finalDestination = new Vector2Int(currentPath[currentPath.Count - 1].x, currentPath[currentPath.Count - 1].y);
            FindAndFollowPath(finalDestination);
        }
    }

    void OnDrawGizmos()
    {
        if (!showPath || currentPath == null || currentPath.Count == 0) return;

        // 경로 선 그리기
        Gizmos.color = Color.green;
        for (int i = 0; i < currentPath.Count - 1; i++)
        {
            Vector3 from = new Vector3(currentPath[i].x, currentPath[i].y, 0);
            Vector3 to = new Vector3(currentPath[i + 1].x, currentPath[i + 1].y, 0);
            Gizmos.DrawLine(from, to);
        }

        // 경로상의 점들 그리기
        Gizmos.color = Color.yellow;
        foreach (Node node in currentPath)
        {
            Gizmos.DrawWireSphere(new Vector3(node.x, node.y, 0), 0.2f);
        }

        // 현재 목표점 강조
        if (showCurrentTarget && isMoving && currentPathIndex < currentPath.Count)
        {
            Gizmos.color = Color.red;
            Vector3 targetPos = new Vector3(currentPath[currentPathIndex].x, currentPath[currentPathIndex].y, 0);
            Gizmos.DrawWireSphere(targetPos, 0.4f);
        }
    }
}
