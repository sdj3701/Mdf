using UnityEngine;

/// <summary>
/// Development-oriented grid visualization kept separate from FieldManager gameplay responsibilities.
/// </summary>
public partial class FieldManager
{
    [Header("Grid Debug Visualization")]
    [Tooltip("Displays the field grid at runtime when debug visualization is allowed.")]
    public bool showGridDebug = true;
    [Tooltip("Explicitly allows grid debug lines in non-development builds.")]
    [SerializeField] private bool allowGridLinesInRelease;
    [Tooltip("Grid line color.")]
    public Color gridLineColor = new Color(0.5f, 0.5f, 0.5f, 0.8f);
    [Tooltip("Border line color for permanent-wall positions.")]
    public Color borderLineColor = new Color(1f, 0.5f, 0f, 1f);
    [Tooltip("Spawn gap marker color.")]
    public Color spawnGoalColor = new Color(0f, 1f, 0f, 1f);
    [Tooltip("Goal marker color.")]
    public Color goalColor = Color.red;
    [Tooltip("Grid line Y offset.")]
    public float gridLineYOffset = 0.05f;
    [Tooltip("Grid line width.")]
    public float gridLineWidth = 0.02f;

    private GameObject _gridLinesParent;
    private bool _gridLinesCreated;
    private Material _lineMaterial;

    private bool ShouldCreateGridLines()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        const bool isDevelopmentBuild = true;
#else
        const bool isDevelopmentBuild = false;
#endif

        return IsGridDebugVisualizationAllowed(showGridDebug, isDevelopmentBuild, allowGridLinesInRelease);
    }

    private static bool IsGridDebugVisualizationAllowed(
        bool requested,
        bool isDevelopmentBuild,
        bool explicitlyAllowedInRelease)
    {
        return requested && (isDevelopmentBuild || explicitlyAllowedInRelease);
    }

    public void CreateGridLines()
    {
        if (_gridLinesCreated || !ShouldCreateGridLines())
        {
            return;
        }

        DestroyGridLines();
        _gridLinesParent = new GameObject("GridLines_Debug");
        _gridLinesParent.transform.SetParent(transform);
        _gridLinesParent.transform.localPosition = Vector3.zero;

        float y = gridOrigin.y + gridLineYOffset;
        int centerX = gridSize.x / 2;
        int centerY = gridSize.y / 2;

        for (int x = 0; x <= gridSize.x; x++)
        {
            Vector3 start = new Vector3(gridOrigin.x + x * cellSize, y, gridOrigin.z);
            Vector3 end = new Vector3(gridOrigin.x + x * cellSize, y, gridOrigin.z + gridSize.y * cellSize);
            CreateGridDebugLine($"VertLine_{x}", start, end, gridLineColor);
        }

        for (int z = 0; z <= gridSize.y; z++)
        {
            Vector3 start = new Vector3(gridOrigin.x, y, gridOrigin.z + z * cellSize);
            Vector3 end = new Vector3(gridOrigin.x + gridSize.x * cellSize, y, gridOrigin.z + z * cellSize);
            CreateGridDebugLine($"HorizLine_{z}", start, end, gridLineColor);
        }

        for (int x = 0; x < gridSize.x; x++)
        {
            for (int z = 0; z < gridSize.y; z++)
            {
                bool isLeftEdge = x == 0;
                bool isRightEdge = x == gridSize.x - 1;
                bool isBottomEdge = z == 0;
                bool isTopEdge = z == gridSize.y - 1;
                if (!isLeftEdge && !isRightEdge && !isBottomEdge && !isTopEdge)
                {
                    continue;
                }

                bool isGap = ((isTopEdge || isBottomEdge) && x == centerX) ||
                             ((isRightEdge || isLeftEdge) && z == centerY);
                CreateGridDebugCellOutline(x, z, y, isGap ? spawnGoalColor : borderLineColor);
            }
        }

        float goalX = gridOrigin.x + (centerX + 0.5f) * cellSize;
        float goalZ = gridOrigin.z + (centerY + 0.5f) * cellSize;
        float goalSize = cellSize * 0.4f;
        CreateGridDebugLine("Goal_X1",
            new Vector3(goalX - goalSize, y + 0.1f, goalZ - goalSize),
            new Vector3(goalX + goalSize, y + 0.1f, goalZ + goalSize),
            goalColor);
        CreateGridDebugLine("Goal_X2",
            new Vector3(goalX - goalSize, y + 0.1f, goalZ + goalSize),
            new Vector3(goalX + goalSize, y + 0.1f, goalZ - goalSize),
            goalColor);

        _gridLinesCreated = true;
    }

    private void CreateGridDebugLine(string name, Vector3 start, Vector3 end, Color color)
    {
        GameObject lineObject = new GameObject(name);
        lineObject.transform.SetParent(_gridLinesParent.transform);

        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.SetPosition(0, start);
        line.SetPosition(1, end);
        line.startWidth = gridLineWidth;
        line.endWidth = gridLineWidth;
        line.material = GetGridDebugLineMaterial();
        line.startColor = color;
        line.endColor = color;
        line.useWorldSpace = true;
    }

    private void CreateGridDebugCellOutline(int x, int z, float y, Color color)
    {
        float padding = cellSize * 0.05f;
        float size = cellSize * 0.9f;
        Vector3 p0 = new Vector3(gridOrigin.x + x * cellSize + padding, y, gridOrigin.z + z * cellSize + padding);
        Vector3 p1 = new Vector3(p0.x + size, y, p0.z);
        Vector3 p2 = new Vector3(p0.x + size, y, p0.z + size);
        Vector3 p3 = new Vector3(p0.x, y, p0.z + size);

        GameObject lineObject = new GameObject($"Cell_{x}_{z}");
        lineObject.transform.SetParent(_gridLinesParent.transform);

        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.positionCount = 5;
        line.SetPosition(0, p0);
        line.SetPosition(1, p1);
        line.SetPosition(2, p2);
        line.SetPosition(3, p3);
        line.SetPosition(4, p0);
        line.startWidth = gridLineWidth * 1.5f;
        line.endWidth = gridLineWidth * 1.5f;
        line.material = GetGridDebugLineMaterial();
        line.startColor = color;
        line.endColor = color;
        line.useWorldSpace = true;
        line.loop = false;
    }

    private Material GetGridDebugLineMaterial()
    {
        if (_lineMaterial == null)
        {
            _lineMaterial = new Material(Shader.Find("Sprites/Default"))
            {
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        return _lineMaterial;
    }

    public void DestroyGridLines()
    {
        if (_gridLinesParent != null)
        {
            DestroyGridDebugObject(_gridLinesParent);
            _gridLinesParent = null;
        }

        _gridLinesCreated = false;
    }

    public void ToggleGridLines(bool show)
    {
        showGridDebug = show;
        bool shouldShow = ShouldCreateGridLines();
        if (_gridLinesParent != null)
        {
            _gridLinesParent.SetActive(shouldShow);
        }
        else if (shouldShow && !_gridLinesCreated)
        {
            CreateGridLines();
        }
    }

    private void DisposeGridDebugVisualization()
    {
        DestroyGridLines();
        if (_lineMaterial != null)
        {
            DestroyGridDebugObject(_lineMaterial);
            _lineMaterial = null;
        }
    }

    private static void DestroyGridDebugObject(UnityEngine.Object target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(target);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
