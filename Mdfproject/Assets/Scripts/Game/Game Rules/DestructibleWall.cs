using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 파괴 가능한 벽을 구분하기 위한 컴포넌트
[System.Serializable]
public class DestructibleWall : MonoBehaviour
{
    [Header("벽 파괴 설정")]
    public float destructionTime = 1.0f; // 파괴 시간
    public GameObject destructionEffect; // 파괴 이펙트
    public AudioClip destructionSound; // 파괴 사운드

    public void DestroyWall()
    {
        StartCoroutine(DestroyWallCoroutine());
    }

    private IEnumerator DestroyWallCoroutine()
    {
        // 파괴 이펙트 재생
        if (destructionEffect != null)
        {
            Instantiate(destructionEffect, transform.position, transform.rotation);
        }

        // 파괴 사운드 재생
        if (destructionSound != null)
        {
            AudioSource.PlayClipAtPoint(destructionSound, transform.position);
        }

        // 잠시 대기 후 파괴
        yield return new WaitForSeconds(destructionTime);

        // 실제 오브젝트 파괴
        Destroy(gameObject);
    }
}
