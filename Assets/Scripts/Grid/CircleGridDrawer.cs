using System;
using System.Collections.Generic;
using UnityEngine;

// ─── Data ────────────────────────────────────────────────────────────────────

/// <summary>
/// Represents a single sector-slot inside one circle of the grid.
/// Each slot lives at the angular midpoint of its sector.
/// </summary>
[Serializable]
public class CircleSlot
{
    /// <summary>Row index of the parent circle in the grid.</summary>
    public int circleRow;

    /// <summary>Column index of the parent circle in the grid.</summary>
    public int circleCol;

    /// <summary>Index of this sector within the circle (0 … sectorCount-1).</summary>
    public int sectorIndex;

    /// <summary>World-space anchor position at the centroid of the sector.</summary>
    public Vector3 worldPosition;

    /// <summary>
    /// Rotation sao cho trục Z (Forward) của object hướng từ tâm ra phía ngoài của sector.
    /// </summary>
    public Quaternion worldRotation;

    /// <summary>Object currently occupying this slot, or null if empty.</summary>
    public GameObject occupant;

    /// <summary>True when an object is placed in this slot.</summary>
    public bool IsOccupied => occupant != null;

    public CircleSlot(int row, int col, int sector, Vector3 position, Quaternion rotation)
    {
        circleRow     = row;
        circleCol     = col;
        sectorIndex   = sector;
        worldPosition = position;
        worldRotation = rotation;
        occupant      = null;
    }
}

// ─── Main Component ──────────────────────────────────────────────────────────

/// <summary>
/// Draws a grid of circles in the Scene view via Gizmos.
/// All parameters are loaded from grid_config.json (Resources/Configs/grid_config)
/// using the assigned Grid Id — no hardcoded values in the Inspector.
///
/// Usage:
///   1. Attach this component to an empty GameObject.
///   2. Set "Grid Id" in the Inspector to match an entry in grid_config.json
///      (e.g. "grid_plate").
///   3. Enable Gizmos in the Scene view to see the grid.
///   4. Subscribe to <see cref="OnSlotOccupied"/> / <see cref="OnSlotVacated"/>
///      for slot-change notifications.
/// </summary>
public class CircleGridDrawer : MonoBehaviour
{
    // ─── Events (Observer Pattern) ────────────────────────────────────────────

    /// <summary>Fired when an object is placed into a slot.</summary>
    public event Action<CircleSlot> OnSlotOccupied;

    /// <summary>Fired when an object is removed from a slot.</summary>
    public event Action<CircleSlot> OnSlotVacated;

    // ─── Inspector fields ─────────────────────────────────────────────────────

    [Header("Config")]
    [Tooltip("Path inside Resources/ folder (no extension).")]
    [SerializeField] private string _configPath = "Configs/grid_config";

    [Tooltip("Which grid entry to load from the config file (must match 'id' field in JSON).")]
    [SerializeField] private string _gridId = "grid_plate";

    [Header("Gizmo Colors")]
    [SerializeField] private Color _circleColor  = new Color(0.2f, 0.8f, 1f,  0.85f);
    [SerializeField] private Color _sectorColor  = new Color(1f,   0.6f, 0.1f, 0.7f);
    [SerializeField] private Color _emptySlot    = new Color(0.4f, 1f,   0.4f, 0.6f);
    [SerializeField] private Color _occupiedSlot = new Color(1f,   0.2f, 0.2f, 0.9f);
    [SerializeField] private Color _centerColor  = new Color(1f,   1f,   1f,   0.9f);

    // ─── Private state ────────────────────────────────────────────────────────

    /// <summary>Config loaded from JSON for this grid id.</summary>
    private GridConfigData _config;

    // Cached values read from config, used by BuildSlots and Gizmos.
    private int   _rows;
    private int   _columns;
    private float _radius;
    private float _spacing;
    private int   _circleSegments;
    private int   _sectorCount;
    private float _slotAnchorFraction;
    /// <summary>
    /// Extra Y rotation (degrees) baked into each slot's worldRotation to
    /// compensate for the slice model's local forward axis not being Z+.
    /// </summary>
    private float _sliceRotationOffsetY;

    /// <summary>Flat list of every slot in the grid, built once in Awake.</summary>
    private List<CircleSlot> _slots = new List<CircleSlot>();

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        LoadConfig();
        BuildSlots();
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Places <paramref name="obj"/> into the slot identified by
    /// (<paramref name="row"/>, <paramref name="col"/>, <paramref name="sectorIndex"/>).
    /// Moves the object's transform to the slot anchor position.
    /// </summary>
    /// <returns>True on success, false if the slot is already occupied or not found.</returns>
    public bool PlaceObject(int row, int col, int sectorIndex, GameObject obj)
    {
        CircleSlot slot = FindSlot(row, col, sectorIndex);

        if (slot == null)
        {
            Debug.LogWarning($"[CircleGridDrawer] Slot ({row},{col},{sectorIndex}) not found.");
            return false;
        }

        if (slot.IsOccupied)
        {
            Debug.LogWarning($"[CircleGridDrawer] Slot ({row},{col},{sectorIndex}) is already occupied.");
            return false;
        }

        slot.occupant = obj;

        // Parent the pizza slice to this drawer's transform so it moves with the plate.
        obj.transform.SetParent(transform);
        obj.transform.position = slot.worldPosition;
        obj.transform.rotation = slot.worldRotation; // apply sector rotation + model offset

        OnSlotOccupied?.Invoke(slot);
        return true;
    }

    /// <summary>
    /// Removes the occupant from the slot identified by
    /// (<paramref name="row"/>, <paramref name="col"/>, <paramref name="sectorIndex"/>).
    /// Does NOT destroy the object — caller is responsible for that.
    /// </summary>
    /// <returns>The removed GameObject, or null if the slot was empty or not found.</returns>
    public GameObject RemoveObject(int row, int col, int sectorIndex)
    {
        CircleSlot slot = FindSlot(row, col, sectorIndex);

        if (slot == null)
        {
            Debug.LogWarning($"[CircleGridDrawer] Slot ({row},{col},{sectorIndex}) not found.");
            return null;
        }

        if (!slot.IsOccupied)
        {
            Debug.LogWarning($"[CircleGridDrawer] Slot ({row},{col},{sectorIndex}) is already empty.");
            return null;
        }

        GameObject removed = slot.occupant;
        slot.occupant = null;

        OnSlotVacated?.Invoke(slot);
        return removed;
    }

    /// <summary>Returns all slots that are currently unoccupied.</summary>
    public IReadOnlyList<CircleSlot> GetEmptySlots()
    {
        var result = new List<CircleSlot>();
        foreach (CircleSlot s in _slots)
            if (!s.IsOccupied) result.Add(s);
        return result;
    }

    /// <summary>Returns all slots that are currently occupied.</summary>
    public IReadOnlyList<CircleSlot> GetOccupiedSlots()
    {
        var result = new List<CircleSlot>();
        foreach (CircleSlot s in _slots)
            if (s.IsOccupied) result.Add(s);
        return result;
    }

    /// <summary>Returns the world position of a specific slot anchor.</summary>
    public Vector3 GetSlotPosition(int row, int col, int sectorIndex)
    {
        CircleSlot slot = FindSlot(row, col, sectorIndex);
        return slot?.worldPosition ?? Vector3.zero;
    }

    /// <summary>
    /// Number of equal sectors each circle is divided into.
    /// Used by PlateController to know how many pizza slots are available.
    /// </summary>
    public int SectorCount => _sectorCount;

    /// <summary>
    /// Recalculates all slot world positions based on the current transform.position.
    /// Call this after repositioning the plate (e.g. before spawning pizza slices).
    /// </summary>
    public void RebuildSlots() => BuildSlots();

    // ─── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Loads this component's config entry from the shared JSON file by matching _gridId.
    /// Applies fallback defaults if the config cannot be found or parsed.
    /// </summary>
    private void LoadConfig()
    {
        TextAsset jsonAsset = Resources.Load<TextAsset>(_configPath);

        if (jsonAsset == null)
        {
            Debug.LogError($"[CircleGridDrawer:{_gridId}] Config file not found at " +
                           $"Resources/{_configPath}.json. Using fallback values.");
            ApplyFallback();
            return;
        }

        GridConfigCollection collection = JsonUtility.FromJson<GridConfigCollection>(jsonAsset.text);

        if (collection == null || collection.grids == null)
        {
            Debug.LogError($"[CircleGridDrawer:{_gridId}] Failed to parse GridConfigCollection.");
            ApplyFallback();
            return;
        }

        _config = System.Array.Find(collection.grids, g => g.id == _gridId);

        if (_config == null)
        {
            Debug.LogError($"[CircleGridDrawer] No grid entry with id='{_gridId}' found in config.");
            ApplyFallback();
            return;
        }

        // Apply values from config.
        _rows                 = _config.rows;
        _columns              = _config.columns;
        _radius               = _config.radius;
        _spacing              = _config.spacing;
        _circleSegments       = _config.circleSegments > 0 ? _config.circleSegments : 60;
        _sectorCount          = _config.sectorCount > 0    ? _config.sectorCount    : 6;
        _slotAnchorFraction   = _config.slotAnchorFraction;
        _sliceRotationOffsetY = _config.sliceRotationOffsetY;

        Debug.Log($"[CircleGridDrawer] Loaded '{_gridId}': {_rows}×{_columns} circles, " +
                  $"radius={_radius}, sectors={_sectorCount}");
    }

    /// <summary>Fallback values used when config cannot be loaded.</summary>
    private void ApplyFallback()
    {
        _rows                 = 1;
        _columns              = 1;
        _radius               = 1.7f;
        _spacing              = 0f;
        _circleSegments       = 60;
        _sectorCount          = 6;
        _slotAnchorFraction   = 0f;
        _sliceRotationOffsetY = 0f;
    }

    /// <summary>
    /// Builds the flat <see cref="_slots"/> list from the current config values.
    /// Called once in Awake and re-called in the Editor Gizmo path when not playing.
    /// </summary>
    private void BuildSlots()
    {
        _slots.Clear();

        float step       = _radius * 2f + _spacing;
        float totalWidth = _columns * step - _spacing;
        float totalDepth = _rows    * step - _spacing;

        Vector3 origin = transform.position
                         - new Vector3(totalWidth * 0.5f - _radius, 0f, totalDepth * 0.5f - _radius);

        float angleSector = 360f / _sectorCount;

        for (int row = 0; row < _rows; row++)
        {
            for (int col = 0; col < _columns; col++)
            {
                Vector3 center = origin + new Vector3(col * step, 0f, row * step);

                for (int s = 0; s < _sectorCount; s++)
                {
                    // Góc giữa sector (tính bằng radian).
                    float midAngleRad = (s + 0.5f) * angleSector * Mathf.Deg2Rad;

                    // Vị trí anchor tại phân số _slotAnchorFraction từ tâm ra.
                    Vector3 anchorPos = center + new Vector3(
                        Mathf.Cos(midAngleRad) * _radius * _slotAnchorFraction,
                        0f,
                        Mathf.Sin(midAngleRad) * _radius * _slotAnchorFraction);

                    // Hướng từ tâm ra ngoài theo góc sector (XZ plane).
                    Vector3 outwardDir = new Vector3(Mathf.Cos(midAngleRad), 0f, Mathf.Sin(midAngleRad));

                    // Xoay object: Z+ hướng ra ngoài, sau đó apply offset để bù model orientation.
                    // Ví dụ: nếu model chỉ về X+ thay vì Z+, dùng sliceRotationOffsetY = -90.
                    Quaternion outwardRot = Quaternion.LookRotation(outwardDir, Vector3.up)
                                           * Quaternion.Euler(0f, _sliceRotationOffsetY, 0f);

                    _slots.Add(new CircleSlot(row, col, s, anchorPos, outwardRot));
                }
            }
        }
    }

    /// <summary>Finds a slot by its (row, col, sector) address. Returns null if not found.</summary>
    private CircleSlot FindSlot(int row, int col, int sectorIndex)
    {
        return _slots.Find(s =>
            s.circleRow   == row   &&
            s.circleCol   == col   &&
            s.sectorIndex == sectorIndex);
    }

    // ─── Editor Gizmos ───────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        // Trong Edit mode: load config trực tiếp từ AssetDatabase để Gizmo phản ánh JSON.
        if (!Application.isPlaying)
            LoadConfigEditor();

        // Rebuild geometry mỗi frame trong Edit mode để phản ánh thay đổi JSON live.
        if (!Application.isPlaying)
            BuildSlots();

        float step       = _radius * 2f + _spacing;
        float totalWidth = _columns * step - _spacing;
        float totalDepth = _rows    * step - _spacing;

        Vector3 origin = transform.position
                         - new Vector3(totalWidth * 0.5f - _radius, 0f, totalDepth * 0.5f - _radius);

        for (int row = 0; row < _rows; row++)
        {
            for (int col = 0; col < _columns; col++)
            {
                Vector3 center = origin + new Vector3(col * step, 0f, row * step);

                DrawCircle(center, _radius);
                DrawSectors(center, _radius, _sectorCount);
                DrawSlotAnchors(row, col);
                DrawCenterDot(center, _radius);
            }
        }
    }

    /// <summary>
    /// Loads config via AssetDatabase (editor-safe, no Play required).
    /// Mirrors LoadConfig() but uses AssetDatabase path instead of Resources.Load.
    /// </summary>
    private void LoadConfigEditor()
    {
        TextAsset jsonAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(
            $"Assets/Resources/{_configPath}.json");

        if (jsonAsset == null)
        {
            ApplyFallback();
            return;
        }

        GridConfigCollection collection = JsonUtility.FromJson<GridConfigCollection>(jsonAsset.text);
        if (collection?.grids == null) { ApplyFallback(); return; }

        GridConfigData cfg = System.Array.Find(collection.grids, g => g.id == _gridId);
        if (cfg == null) { ApplyFallback(); return; }

        _rows               = cfg.rows;
        _columns            = cfg.columns;
        _radius             = cfg.radius;
        _spacing            = cfg.spacing;
        _circleSegments     = cfg.circleSegments > 0 ? cfg.circleSegments : 60;
        _sectorCount        = cfg.sectorCount > 0    ? cfg.sectorCount    : 6;
        _slotAnchorFraction = cfg.slotAnchorFraction;
    }

    /// <summary>
    /// Draws the outer circle outline as a polygon with <see cref="_circleSegments"/> sides,
    /// flat on the XZ plane.
    /// </summary>
    private void DrawCircle(Vector3 center, float radius)
    {
        Gizmos.color = _circleColor;

        float angleStep = 360f / _circleSegments;
        Vector3 prev = center + new Vector3(radius, 0f, 0f);

        for (int i = 1; i <= _circleSegments; i++)
        {
            float rad = i * angleStep * Mathf.Deg2Rad;
            Vector3 next = center + new Vector3(
                Mathf.Cos(rad) * radius,
                0f,
                Mathf.Sin(rad) * radius);

            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }

    /// <summary>
    /// Draws dividing lines from the center to the edge for each sector boundary.
    /// </summary>
    private void DrawSectors(Vector3 center, float radius, int count)
    {
        Gizmos.color = _sectorColor;
        float angleStep = 360f / count;

        for (int i = 0; i < count; i++)
        {
            float rad = i * angleStep * Mathf.Deg2Rad;
            Vector3 edge = center + new Vector3(
                Mathf.Cos(rad) * radius,
                0f,
                Mathf.Sin(rad) * radius);

            Gizmos.DrawLine(center, edge);
        }
    }

    /// <summary>
    /// Draws a small sphere at each slot anchor inside the given circle.
    /// Green = empty, Red = occupied.
    /// </summary>
    private void DrawSlotAnchors(int row, int col)
    {
        float dotRadius = _radius * 0.1f;

        foreach (CircleSlot slot in _slots)
        {
            if (slot.circleRow != row || slot.circleCol != col) continue;

            Gizmos.color = slot.IsOccupied ? _occupiedSlot : _emptySlot;
            Gizmos.DrawSphere(slot.worldPosition, dotRadius);
        }
    }

    /// <summary>Draws a small white sphere at the exact circle center.</summary>
    private void DrawCenterDot(Vector3 center, float radius)
    {
        Gizmos.color = _centerColor;
        Gizmos.DrawSphere(center, radius * 0.05f);
    }
#endif
}
