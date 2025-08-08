using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Tilemaps;

public class GameDataCenter : MonoBehaviour
{
    [Header("GameObject ���� ����")]
    public GameObject CreateWall;          // �÷��̾� ������Ʈ
    public TMP_Text wallCountText;      // �� ���� ǥ�� UI �ؽ�Ʈ

    [Header("���ӷ� ����")]
    public bool UsUnitPlacement = false;    // ���� ��ġ ����
    public static bool isWallPlacement = false;    // �� ��ġ ����
    public float placementTime = 10.0f;     // ��ġ �ð� ����
    public static int createWallCount = 5;         // ������ ���� ����

    [Header("�� Ÿ�ϸ� ����")]
    public Tilemap breakWalltilemap;            // Ÿ���� ��ġ�� Ÿ�ϸ�
    public TileBase breakWalltileToPlace;       // ��ġ�� Ÿ��
    public Camera playerCamera;                 // ���� ī�޶�

    [Header("������ ����")]
    public bool showPreview = true;   // ���콺 ��ġ ������ ǥ�� ����
    public Color previewColor = Color.green;  // ������ ����

    protected GameObject previewObject; // ������� ������Ʈ
    protected Vector3Int currentMouseGridPosition; // ���� ���콺�� �׸��� ��ǥ

    // �̺�Ʈ �ý������� �ڽ� ������ ����
    public static event System.Action<bool> OnWallModeChanged;
    private bool currentWallMode = false;

    private void Update()
    {
        bool newWallMode = CheckWallCondition();

        // ���°� ����� ��츸 �̺�Ʈ �߻�
        if (newWallMode != currentWallMode)
        {
            Debug.Log("GameDataCenter : " + newWallMode);
            currentWallMode = newWallMode;
            OnWallModeChanged?.Invoke(currentWallMode);
        }

        // TODO : ��ġ �ð� ����
        /*
         * placementTime -= Time.deltaTime;
         */

        // �� ���� UI ������Ʈ
        wallCountText.text = createWallCount.ToString();
    }

    // �ڽ��� ����� Ȱ��ȭ ���� ������ �Ǵ��ϴ� �Լ�
    bool CheckWallCondition()
    {
        // �ð��� �� ��ġ ��ư�� �������� Ȯ��
        bool isCreateWall;
        if (placementTime > 0.0f && isWallPlacement)
            // �� ��ġ�� Ȱ��ȭ�Ǿ� �ְ�, ��ġ �ð��� �����ִ� ���
            isCreateWall = true;
        else
            // �� ��ġ�� ��Ȱ��ȭ�ǰų� ��ġ �ð��� �ʰ��� ���
            isCreateWall = false;
        
        return isCreateWall;
    }

    // ��ư ������ �� ��ġ Ȱ��ȭ/��Ȱ��ȭ
    public void IsCreateButtonActive()
    {
        isWallPlacement = !isWallPlacement;
    }
}
