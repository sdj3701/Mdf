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
    public static bool IsUnitPlacement = false;    // ���� ��ġ ����
    public static bool IsWallPlacement = false;    // �� ��ġ ����
    public float PlacementTime = 10.0f;     // ��ġ �ð� ����
    public static int CreateWallCount = 5;         // ������ ���� ����

    [Header("�� Ÿ�ϸ� ����")]
    public Tilemap Groundtilemap;            // Ÿ���� ��ġ�� Ÿ�ϸ�
    public Tilemap BreakWalltilemap;            // Ÿ���� ��ġ�� Ÿ�ϸ�
    public TileBase BreakWalltileToPlace;       // ��ġ�� Ÿ��
    public Camera PlayerCamera;                 // ���� ī�޶�

    [Header("ĳ���� ���� ����")]
    public Sprite CharacterSprite;              // ������ ĳ���� ��������Ʈ
    public GameObject CharacterPrefab;          // ĳ���� ������ (���û���)
    public float CharacterSortingOrder = 5f;    // ĳ���� ������ ����

    [Header("������ ����")]
    public bool ShowPreview = true;   // ���콺 ��ġ ������ ǥ�� ����
    public Color PreviewColor = Color.green;  // ������ ����

    protected GameObject PreviewObject; // ������� ������Ʈ
    protected Vector3Int CurrentMouseGridPosition; // ���� ���콺�� �׸��� ��ǥ

    // ������ ĳ���͵��� �����ϴ� ��ųʸ� (��ġ���� ����)
    public static Dictionary<Vector3Int, GameObject> SpawnedCharacters = new Dictionary<Vector3Int, GameObject>();

    private void Update()
    {
        // TODO : ��ġ �ð� ����
        /*
         * placementTime -= Time.deltaTime;
         */

        // �� ���� UI ������Ʈ
        wallCountText.text = CreateWallCount.ToString();
    }

    // ��ư ������ �� ��ġ Ȱ��ȭ/��Ȱ��ȭ
    public void IsCreateButtonActive()
    {
        IsWallPlacement = !IsWallPlacement;
        IsUnitPlacement = false; // �� ��ġ ����� �� ���� ��ġ ��Ȱ��ȭ
    }

    // ��ư ������ ���� ��ġ Ȱ��ȭ/��Ȱ��ȭ
    public void IsCreateUnitButtonActive()
    {
        IsUnitPlacement = !IsUnitPlacement;
        IsWallPlacement = false; // ���� ��ġ ����� �� �� ��ġ ��Ȱ��ȭ
    }

    // ��� ������ ĳ���� ����
    public static void ClearAllCharacters()
    {
        foreach (var character in SpawnedCharacters.Values)
        {
            if (character != null)
                DestroyImmediate(character);
        }
        SpawnedCharacters.Clear();
        Debug.Log("��� ĳ���Ͱ� ���ŵǾ����ϴ�!");
    }
}
