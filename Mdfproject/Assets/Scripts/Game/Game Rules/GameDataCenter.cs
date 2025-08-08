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
    public bool IsUnitPlacement = false;    // ���� ��ġ ����
    public static bool IsWallPlacement = false;    // �� ��ġ ����
    public float PlacementTime = 10.0f;     // ��ġ �ð� ����
    public static int CreateWallCount = 5;         // ������ ���� ����

    [Header("�� Ÿ�ϸ� ����")]
    public Tilemap Groundtilemap;            // Ÿ���� ��ġ�� Ÿ�ϸ�
    public Tilemap BreakWalltilemap;            // Ÿ���� ��ġ�� Ÿ�ϸ�
    public TileBase BreakWalltileToPlace;       // ��ġ�� Ÿ��
    public Camera PlayerCamera;                 // ���� ī�޶�

    [Header("������ ����")]
    public bool ShowPreview = true;   // ���콺 ��ġ ������ ǥ�� ����
    public Color PreviewColor = Color.green;  // ������ ����

    protected GameObject PreviewObject; // ������� ������Ʈ
    protected Vector3Int CurrentMouseGridPosition; // ���� ���콺�� �׸��� ��ǥ

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
        wallCountText.text = CreateWallCount.ToString();
    }

    // �ڽ��� ����� Ȱ��ȭ ���� ������ �Ǵ��ϴ� �Լ�
    bool CheckWallCondition()
    {
        // �ð��� �� ��ġ ��ư�� �������� Ȯ��
        bool isCreateWall;
        if (PlacementTime > 0.0f && IsWallPlacement)
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
        IsWallPlacement = !IsWallPlacement;
    }
}
