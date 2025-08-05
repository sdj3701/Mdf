using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// �ı� ������ ���� �����ϱ� ���� ������Ʈ
[System.Serializable]
public class DestructibleWall : MonoBehaviour
{
    [Header("�� �ı� ����")]
    public float destructionTime = 1.0f; // �ı� �ð�
    public GameObject destructionEffect; // �ı� ����Ʈ
    public AudioClip destructionSound; // �ı� ����

    public void DestroyWall()
    {
        StartCoroutine(DestroyWallCoroutine());
    }

    private IEnumerator DestroyWallCoroutine()
    {
        // �ı� ����Ʈ ���
        if (destructionEffect != null)
        {
            Instantiate(destructionEffect, transform.position, transform.rotation);
        }

        // �ı� ���� ���
        if (destructionSound != null)
        {
            AudioSource.PlayClipAtPoint(destructionSound, transform.position);
        }

        // ��� ��� �� �ı�
        yield return new WaitForSeconds(destructionTime);

        // ���� ������Ʈ �ı�
        Destroy(gameObject);
    }
}
