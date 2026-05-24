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
    /// Giống như một miếng pizza hướng ra ngoài đĩa.
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
/// Each circle is divided into <see cref="_sectorCount"/> equal sector-slots.
/// Slots can hold any GameObject via <see cref="PlaceObject"/> / <see cref="RemoveObject"/>.
///
/// Usage:
///   1. Attach this component to an empty GameObject.
///   2. Tune the Inspector fields (rows, columns, radius, sector count …).
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

    [Header("Grid Settings")]
    [Tooltip("Number of rows in the grid.")]
    [SerializeField] private int _rows = 3;

    [Tooltip("Number of columns in the grid.")]
    [SerializeField] private int _columns = 4;

    [Tooltip("Radius of each circle.")]
    [SerializeField] private float _radius = 1f;

    [Tooltip("Gap between circles (edge-to-edge distance).")]
    [SerializeField] private float _spacing = 0.3f;

    [Header("Circle & Slot Settings")]
    [Tooltip("Number of segments used to approximate the circle outline.")]
    [SerializeField] private int _circleSegments = 60;

    [Tooltip("Number of equal sectors (slots) each circle is divided into.")]
    [SerializeField] private int _sectorCount = 6;

    [Tooltip("Radial fraction at which the slot anchor sits inside the circle (0 = center, 1 = edge).")]
    [Range(0f, 1f)]
    [SerializeField] private float _slotAnchorFraction = 0f;

    [Header("Gizmo Colors")]
    [SerializeField] private Color _circleColor  = new Color(0.2f, 0.8f, 1f,  0.85f);
    [SerializeField] private Color _sectorColor  = new Color(1f,   0.6f, 0.1f, 0.7f);
    [SerializeField] private Color _emptySlot    = new Color(0.4f, 1f,   0.4f, 0.6f);
    [SerializeField] private Color _occupiedSlot = new Color(1f,   0.2f, 0.2f, 0.9f);
    [SerializeField] private Color _centerColor  = new Color(1f,   1f,   1f,   0.9f);

    [Header("Test Objects")]
    [Tooltip("Kéo thả Prefabs vào đây. Nhấn chuột phải vào component → Place Test Objects để spawn vào scene.")]
    [SerializeField] private List<GameObject> _testPrefabs = new List<GameObject>();

    // ─── Private state ────────────────────────────────────────────────────────

    /// <summary>Flat list of every slot in the grid, built once in Awake.</summary>
    private List<CircleSlot> _slots = new List<CircleSlot>();

    /// <summary>Tracks GameObjects spawned by PlaceTestObjects so they can be destroyed on Clear.</summary>
    private readonly List<GameObject> _spawnedTestObjects = new List<GameObject>();

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        BuildSlots();
    }

    private void Start()
    {
        // Tự động spawn prefabs vào slots khi bắt đầu Play mode (nếu có).
        if (_testPrefabs != null && _testPrefabs.Count > 0)
            PlaceTestObjects();
    }

    // ─── Test Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Instantiate lần lượt từng prefab trong <see cref="_testPrefabs"/> vào các slot trống
    /// theo thứ tự từ trên-trái xuống dưới-phải.
    /// Nếu số prefab ít hơn số slot, prefab sẽ được dùng vòng lại (wrap-around).
    /// Có thể gọi từ ContextMenu (chuột phải vào component trong Inspector).
    /// </summary>
    [ContextMenu("Place Test Objects")]
    public void PlaceTestObjects()
    {
        if (_testPrefabs == null || _testPrefabs.Count == 0)
        {
            Debug.LogWarning("[CircleGridDrawer] _testPrefabs rỗng. Hãy kéo Prefabs vào list trước.");
            return;
        }

        // Đảm bảo slots đã được build (cần thiết khi gọi từ editor).
        if (_slots.Count == 0) BuildSlots();

        int placed = 0;
        int prefabCount = _testPrefabs.Count;

        foreach (CircleSlot slot in _slots)
        {
            if (slot.IsOccupied) continue;

            // Wrap-around: nếu hết prefab thì quay lại từ đầu.
            GameObject prefab = _testPrefabs[placed % prefabCount];
            if (prefab == null) { placed++; continue; }

            // Spawn prefab tại vị trí slot anchor, xoay theo hướng ra ngoài của sector.
            GameObject instance = Instantiate(prefab, slot.worldPosition, slot.worldRotation, transform);
            instance.name = $"{prefab.name}_r{slot.circleRow}c{slot.circleCol}s{slot.sectorIndex}";

            slot.occupant = instance;
            _spawnedTestObjects.Add(instance);
            OnSlotOccupied?.Invoke(slot);
            placed++;

            Debug.Log($"[CircleGridDrawer] Spawn '{instance.name}' tại slot " +
                      $"(row={slot.circleRow}, col={slot.circleCol}, sector={slot.sectorIndex})");
        }

        Debug.Log($"[CircleGridDrawer] Đã spawn {placed} object(s) vào {_slots.Count} slot(s).");
    }

    /// <summary>
    /// Destroy tất cả các instance đã được spawn bởi <see cref="PlaceTestObjects"/>
    /// và giải phóng toàn bộ slots.
    /// Có thể gọi từ ContextMenu.
    /// </summary>
    [ContextMenu("Clear Test Objects")]
    public void ClearTestObjects()
    {
        // Destroy tất cả instance đã spawn.
        foreach (GameObject obj in _spawnedTestObjects)
        {
            if (obj != null) Destroy(obj);
        }
        _spawnedTestObjects.Clear();

        // Giải phóng slots.
        int cleared = 0;
        foreach (CircleSlot slot in _slots)
        {
            if (slot.IsOccupied)
            {
                OnSlotVacated?.Invoke(slot);
                slot.occupant = null;
                cleared++;
            }
        }

        Debug.Log($"[CircleGridDrawer] Đã xóa và Destroy {cleared} instance(s).");
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
        obj.transform.position = slot.worldPosition;

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

    // ─── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Builds the flat <see cref="_slots"/> list from the current Inspector settings.
    /// Called once in Awake and re-called in the Editor Gizmo path when not playing.
    /// </summary>
    private void BuildSlots()
    {
        _slots.Clear();

        float step = _radius * 2f + _spacing;
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

                    // Xoay object sao cho Forward (Z+) của nó hướng ra ngoài.
                    Quaternion outwardRot = Quaternion.LookRotation(outwardDir, Vector3.up);

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
        // Rebuild geometry in edit mode so the Gizmo reflects live Inspector changes.
        if (!Application.isPlaying)
            BuildSlots();

        float step = _radius * 2f + _spacing;
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
