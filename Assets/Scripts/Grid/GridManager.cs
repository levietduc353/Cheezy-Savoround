using UnityEngine;

/// <summary>
/// Manages one independent grid instance.
/// Reads its config entry from the shared grid_config.json by matching the Grid Id field.
///
/// Usage:
///   1. Create an empty GameObject, attach this component.
///   2. Set "Grid Id" in the Inspector to match an entry in grid_config.json
///      (e.g. "main_grid" or "hold_grid").
///   3. The grid draws itself in the Scene view immediately (Gizmos must be on).
///   4. Optionally assign a Cell Prefab to spawn real objects at runtime.
/// </summary>
/// <summary>Info about a neighboring grid cell, returned by GetNeighbors.</summary>
public struct NeighborInfo
{
    public int             row;
    public int             col;
    public PlateController plate;
}

public class GridManager : MonoBehaviour
{
    // ─── Inspector fields ────────────────────────────────────────────────────

    [Header("Config")]
    [Tooltip("Path inside Resources/ folder (no extension).")]
    [SerializeField] private string _configPath = "Configs/grid_config";

    [Tooltip("Which grid entry to load from the config file (must match 'id' field in JSON).")]
    [SerializeField] private string _gridId = "main_grid";

    [Header("Optional Runtime Visuals")]
    [Tooltip("If assigned, this prefab is instantiated at each cell position at runtime.")]
    [SerializeField] private GameObject _cellPrefab;

    [Header("Gizmo Colors (Editor only)")]
    [SerializeField] private Color _gizmoLineColor   = new Color(0.2f, 0.8f, 1f, 0.8f);
    [SerializeField] private Color _gizmoCenterColor = new Color(1f, 0.6f, 0.1f, 0.9f);

    // ─── Events (Observer Pattern) ─────────────────────────────────────────────

    /// <summary>
    /// Fired when a plate is placed into a cell.
    /// Subscribers (e.g. MergeChecker) use this to trigger merge logic.
    /// </summary>
    public event System.Action<int, int, PlateController> OnPlatePlaced;

    // ─── Private state ───────────────────────────────────────────────────────

    private GridConfigData _config;

    /// <summary>2-D array tracking which PlateController occupies each cell.</summary>
    private PlateController[,] _plates;

    // Cached values for runtime & Gizmo use.
    private int     _rows;
    private int     _columns;
    private float   _cellSize;
    private float   _cellSpacing;
    private Vector3 _originOffset;

    /// <summary>
    /// True  → portrait  : rows along X (short), cols along Z (long).
    /// False → landscape : cols along X (wide),  rows along Z (shallow).
    /// </summary>
    private bool _isPortrait;

    // ─── Unity lifecycle ─────────────────────────────────────────────────────

    // ─── Public read-only properties ──────────────────────────────────────────

    public int   Rows     => _rows;
    public int   Columns  => _columns;
    public float CellSize => _cellSize;

    // ─── Unity lifecycle ─────────────────────────────────────────────────────

    private void Awake()
    {
        LoadConfig();
        // Initialize plate tracking array after _rows/_columns are set by LoadConfig.
        _plates = new PlateController[_rows, _columns];
    }

    private void Start()
    {
        if (_cellPrefab != null)
            SpawnCells();
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Places <paramref name="plate"/> into cell (<paramref name="row"/>, <paramref name="col"/>).
    /// Fires <see cref="OnPlatePlaced"/> so MergeChecker can react.
    /// </summary>
    /// <returns>True on success; false if the cell is already occupied or out of bounds.</returns>
    public bool PlacePlate(int row, int col, PlateController plate)
    {
        if (!IsValidCell(row, col))
        {
            Debug.LogWarning($"[GridManager] Cell ({row},{col}) is out of bounds.");
            return false;
        }

        if (_plates[row, col] != null)
        {
            Debug.LogWarning($"[GridManager] Cell ({row},{col}) is already occupied.");
            return false;
        }

        _plates[row, col] = plate;
        plate.SetGridPosition(row, col);

        OnPlatePlaced?.Invoke(row, col, plate);
        return true;
    }

    /// <summary>Removes the plate reference from cell (<paramref name="row"/>, <paramref name="col"/>).</summary>
    public void RemovePlate(int row, int col)
    {
        if (!IsValidCell(row, col)) return;

        if (_plates[row, col] != null)
            _plates[row, col].ClearGridPosition();

        _plates[row, col] = null;
    }

    /// <summary>Returns the PlateController at cell (<paramref name="row"/>, <paramref name="col"/>), or null.</summary>
    public PlateController GetPlateAt(int row, int col)
    {
        if (!IsValidCell(row, col)) return null;
        return _plates[row, col];
    }

    /// <summary>
    /// Returns the four orthogonal neighbors of cell (<paramref name="row"/>, <paramref name="col"/>)
    /// that are currently occupied by a plate.
    /// </summary>
    public System.Collections.Generic.List<NeighborInfo> GetNeighbors(int row, int col)
    {
        var result = new System.Collections.Generic.List<NeighborInfo>();

        // Offsets for Up, Down, Left, Right in the grid.
        int[] dr = { -1,  1,  0,  0 };
        int[] dc = {  0,  0, -1,  1 };

        for (int i = 0; i < 4; i++)
        {
            int nr = row + dr[i];
            int nc = col + dc[i];

            if (!IsValidCell(nr, nc)) continue;

            PlateController neighbor = _plates[nr, nc];
            if (neighbor == null) continue;

            result.Add(new NeighborInfo { row = nr, col = nc, plate = neighbor });
        }

        return result;
    }

    /// <summary>Returns the world-space center of the cell at (row, col).</summary>
    public Vector3 GetCellWorldPosition(int row, int col)
    {
        return ComputeCellCenter(row, col, _rows, _columns, _cellSize, _cellSpacing,
                                 _isPortrait, transform.position + _originOffset);
    }



    // ─── Private helpers ──────────────────────────────────────────────────────

    private bool IsValidCell(int row, int col)
    {
        return row >= 0 && row < _rows && col >= 0 && col < _columns;
    }

    /// <summary>
    /// Core position math, shared by GetCellWorldPosition and OnDrawGizmos.
    /// Separated so the Gizmo path can call it without depending on cached runtime state.
    /// </summary>
    private static Vector3 ComputeCellCenter(int row, int col,
                                             int rows, int cols,
                                             float cellSize, float cellSpacing,
                                             bool isPortrait, Vector3 pivot)
    {
        float step = cellSize + cellSpacing;

        float totalWidth, totalDepth;
        Vector3 offset;

        if (isPortrait)
        {
            // Rows → X (short), Cols → Z (long)
            totalWidth = rows * step - cellSpacing;
            totalDepth = cols * step - cellSpacing;
            offset = new Vector3(row * step + cellSize * 0.5f, 0f, col * step + cellSize * 0.5f);
        }
        else
        {
            // Landscape: Cols → X (wide), Rows → Z (shallow)
            totalWidth = cols * step - cellSpacing;
            totalDepth = rows * step - cellSpacing;
            offset = new Vector3(col * step + cellSize * 0.5f, 0f, row * step + cellSize * 0.5f);
        }

        Vector3 origin = pivot - new Vector3(totalWidth * 0.5f, 0f, totalDepth * 0.5f);
        return origin + offset;
    }

    /// <summary>Loads this manager's grid config entry from the shared JSON file.</summary>
    private void LoadConfig()
    {
        TextAsset jsonAsset = Resources.Load<TextAsset>(_configPath);

        if (jsonAsset == null)
        {
            Debug.LogError($"[GridManager:{_gridId}] Config file not found at " +
                           $"Resources/{_configPath}.json. Using fallback values.");
            ApplyFallback();
            return;
        }

        GridConfigCollection collection = JsonUtility.FromJson<GridConfigCollection>(jsonAsset.text);

        if (collection == null || collection.grids == null)
        {
            Debug.LogError($"[GridManager:{_gridId}] Failed to parse GridConfigCollection.");
            ApplyFallback();
            return;
        }

        // Find the entry matching our _gridId.
        _config = System.Array.Find(collection.grids, g => g.id == _gridId);

        if (_config == null)
        {
            Debug.LogError($"[GridManager] No grid entry with id='{_gridId}' found in config.");
            ApplyFallback();
            return;
        }

        _rows         = _config.rows;
        _columns      = _config.columns;
        _cellSize     = _config.cellSize;
        _cellSpacing  = _config.cellSpacing;
        _isPortrait   = _config.orientation != "landscape";
        _originOffset = _config.gridOriginOffset != null
            ? _config.gridOriginOffset.ToVector3()
            : Vector3.zero;

        Debug.Log($"[GridManager] Loaded '{_gridId}': {_rows}×{_columns}, " +
                  $"orientation={_config.orientation}, cellSize={_cellSize}");
    }

    /// <summary>Fallback values used when config cannot be loaded.</summary>
    private void ApplyFallback()
    {
        _rows         = 4;
        _columns      = 6;
        _cellSize     = 1f;
        _cellSpacing  = 0.1f;
        _isPortrait   = true;
        _originOffset = Vector3.zero;
    }

    /// <summary>Instantiates _cellPrefab at every cell position (runtime only).</summary>
    private void SpawnCells()
    {
        for (int row = 0; row < _rows; row++)
        {
            for (int col = 0; col < _columns; col++)
            {
                Vector3 pos = GetCellWorldPosition(row, col);
                GameObject cell = Instantiate(_cellPrefab, pos, UnityEngine.Quaternion.identity, transform);
                cell.name = $"Cell_{row}_{col}";
            }
        }
    }

    // ─── Editor Gizmos ───────────────────────────────────────────────────────

#if UNITY_EDITOR
    /// <summary>
    /// Draws this grid's Gizmos in the Scene view.
    /// Reads config from AssetDatabase in Edit mode so no Play required.
    /// </summary>
    private void OnDrawGizmos()
    {
        int   rows      = 4;
        int   cols      = 6;
        float size      = 1f;
        float spacing   = 0.1f;
        bool  portrait  = true;

        if (Application.isPlaying)
        {
            rows     = _rows;
            cols     = _columns;
            size     = _cellSize;
            spacing  = _cellSpacing;
            portrait = _isPortrait;
        }
        else
        {
            // Load from AssetDatabase (editor-safe, no runtime needed).
            TextAsset jsonAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(
                $"Assets/Resources/{_configPath}.json");

            if (jsonAsset != null)
            {
                GridConfigCollection collection =
                    JsonUtility.FromJson<GridConfigCollection>(jsonAsset.text);

                if (collection?.grids != null)
                {
                    GridConfigData cfg = System.Array.Find(collection.grids, g => g.id == _gridId);
                    if (cfg != null)
                    {
                        rows     = cfg.rows;
                        cols     = cfg.columns;
                        size     = cfg.cellSize;
                        spacing  = cfg.cellSpacing;
                        portrait = cfg.orientation != "landscape";
                    }
                }
            }
        }

        // Draw all cells.
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                Vector3 center = ComputeCellCenter(row, col, rows, cols, size, spacing,
                                                   portrait, transform.position);

                Gizmos.color = _gizmoLineColor;
                DrawWireSquare(center, size);

                Gizmos.color = _gizmoCenterColor;
                Gizmos.DrawSphere(center, size * 0.05f);
            }
        }

        // Bounding box.
        float step = size + spacing;
        float totalWidth = portrait ? rows * step - spacing : cols * step - spacing;
        float totalDepth = portrait ? cols * step - spacing : rows * step - spacing;

        Gizmos.color = new Color(_gizmoLineColor.r, _gizmoLineColor.g, _gizmoLineColor.b, 1f);
        Gizmos.DrawWireCube(transform.position, new Vector3(totalWidth, 0.01f, totalDepth));
    }

    /// <summary>
    /// Draws a wireframe square flat on the XZ plane, centered at 'center'.
    /// </summary>
    private void DrawWireSquare(Vector3 center, float size)
    {
        float half = size * 0.5f;

        Vector3 c00 = center + new Vector3(-half, 0f, -half);
        Vector3 c10 = center + new Vector3( half, 0f, -half);
        Vector3 c11 = center + new Vector3( half, 0f,  half);
        Vector3 c01 = center + new Vector3(-half, 0f,  half);

        Gizmos.DrawLine(c00, c10);
        Gizmos.DrawLine(c10, c11);
        Gizmos.DrawLine(c11, c01);
        Gizmos.DrawLine(c01, c00);
    }
#endif
}
