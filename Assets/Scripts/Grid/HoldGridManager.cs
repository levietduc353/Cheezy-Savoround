using System;
using UnityEngine;

/// <summary>
/// Manages the 4-slot Hold Grid where plates wait for the player to drag them.
///
/// Rules:
///   - On Start, fills all 4 slots with random plates and pizza types.
///   - When a plate is dragged away, the slot is marked empty.
///   - When ALL 4 slots are empty, Refill() is called to spawn 4 new plates.
///
/// Usage:
///   Call NotifyPlateDragged(plate) from the drag/input system when a plate leaves Hold Grid.
/// </summary>
public class HoldGridManager : MonoBehaviour
{
    // ─── Events (Observer Pattern) ────────────────────────────────────────────

    /// <summary>Fired after all 4 slots have been refilled with new plates.</summary>
    public event Action OnHoldGridRefilled;

    // ─── Inspector fields ─────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("The GridManager component that represents the hold_grid.")]
    [SerializeField] private GridManager _holdGrid;

    [Header("Config")]
    [Tooltip("Path inside Resources/ folder (no extension).")]
    [SerializeField] private string _pizzaConfigPath = "Configs/pizza_config";

    // ─── Constants ────────────────────────────────────────────────────────────

    private const int _holdSlotCount = 4;

    /// <summary>Pool ids for all plate types defined in pool_config.json.</summary>
    private static readonly string[] _platePoolIds =
    {
        "plate_0", "plate_1", "plate_2", "plate_3", "plate_4", "plate_5"
    };

    // ─── Private state ────────────────────────────────────────────────────────

    private PlateController[]    _plates;
    private PizzaConfigCollection _pizzaConfig;
    private int                  _emptyCount;

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        _plates = new PlateController[_holdSlotCount];
        LoadPizzaConfig();
    }

    private void Start()
    {
        RefillAll();
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Called by the drag/input system when the player drags a plate away from the Hold Grid.
    /// If all 4 slots become empty, triggers a full refill.
    /// </summary>
    public void NotifyPlateDragged(PlateController plate)
    {
        for (int i = 0; i < _holdSlotCount; i++)
        {
            if (_plates[i] != plate) continue;

            _plates[i] = null;
            _emptyCount++;

            Debug.Log($"[HoldGridManager] Slot {i} emptied. Empty count: {_emptyCount}/{_holdSlotCount}");
            break;
        }

        if (_emptyCount >= _holdSlotCount)
            RefillAll();
    }

    /// <summary>Returns the PlateController at <paramref name="slotIndex"/>, or null if empty.</summary>
    public PlateController GetPlateAtSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _holdSlotCount) return null;
        return _plates[slotIndex];
    }

    /// <summary>Returns true if <paramref name="plate"/> is currently waiting in the hold grid.</summary>
    public bool IsPlateInHold(PlateController plate) => GetHoldSlotIndex(plate) >= 0;

    /// <summary>
    /// Returns the hold slot index of <paramref name="plate"/>, or -1 if not found.
    /// </summary>
    public int GetHoldSlotIndex(PlateController plate)
    {
        for (int i = 0; i < _holdSlotCount; i++)
            if (_plates[i] == plate) return i;
        return -1;
    }

    /// <summary>Returns the world position of the given hold slot.</summary>
    public Vector3 GetSlotWorldPosition(int slotIndex)
    {
        if (_holdGrid == null) return transform.position;
        return _holdGrid.GetCellWorldPosition(0, slotIndex);
    }

    /// <summary>
    /// Registers an externally-created plate into the given hold slot so that
    /// DragController can pick it up normally.
    ///
    /// Use this from test scripts or other systems that need to inject a plate
    /// directly into the hold grid without going through the normal refill flow.
    /// The slot's previous plate reference (if any) is overwritten — caller must
    /// ensure the slot is empty first via GetPlateAtSlot().
    /// </summary>
    public void RegisterPlateAtSlot(int slotIndex, PlateController plate)
    {
        if (slotIndex < 0 || slotIndex >= _holdSlotCount)
        {
            Debug.LogError($"[HoldGridManager] RegisterPlateAtSlot: " +
                           $"slot index {slotIndex} is out of range (0–{_holdSlotCount - 1}).");
            return;
        }

        _plates[slotIndex] = plate;
        Debug.Log($"[HoldGridManager] External plate registered at hold slot {slotIndex}.");
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    /// <summary>Spawns 4 new random plates across all hold slots.</summary>
    private void RefillAll()
    {
        _emptyCount = 0;

        for (int i = 0; i < _holdSlotCount; i++)
            SpawnPlateAtSlot(i);

        OnHoldGridRefilled?.Invoke();
        Debug.Log("[HoldGridManager] Hold grid refilled with 4 new plates.");
    }

    /// <summary>
    /// Spawns a single plate with random plate type and random pizza slices
    /// at hold grid slot <paramref name="slotIndex"/>.
    /// </summary>
    private void SpawnPlateAtSlot(int slotIndex)
    {
        // Random plate visual type.
        string platePoolId = _platePoolIds[UnityEngine.Random.Range(0, _platePoolIds.Length)];
        GameObject plateGo = PoolManager.Instance.Get(platePoolId);

        if (plateGo == null)
        {
            Debug.LogError($"[HoldGridManager] Failed to get plate from pool '{platePoolId}'.");
            return;
        }

        PlateController plate = plateGo.GetComponent<PlateController>();

        if (plate == null)
        {
            Debug.LogError($"[HoldGridManager] Prefab '{platePoolId}' has no PlateController component.");
            PoolManager.Instance.Release(platePoolId, plateGo);
            return;
        }

        // ── Position plate FIRST so CircleGridDrawer slot positions are correct ──────
        // Initialize() → SpawnSlices() calls _drawer.PlaceObject() which uses
        // slot.worldPosition. Those positions are re-built based on the plate's
        // current transform.position, so we must position the plate before spawning.
        Vector3 worldPos = _holdGrid.GetCellWorldPosition(0, slotIndex);
        plateGo.transform.SetParent(transform);  // Keep hold-grid plates organised.
        plateGo.transform.position = worldPos;

        // ── Random pizza type ─────────────────────────────────────────────────────
        int typeIndex = UnityEngine.Random.Range(0, _pizzaConfig.pizzaTypes.Length);
        PizzaTypeData pizzaType = _pizzaConfig.pizzaTypes[typeIndex];

        // ── Weighted random slice count ──────────────────────────────────────────
        int totalCount = GetWeightedRandomCount();

        // ── Filler slices when plate is full ─────────────────────────────────
        string[] fillerPoolIds = null;
        int mainCount = totalCount;

        bool isFull = totalCount == _pizzaConfig.maxSlicesPerPlate;
        bool fillerEnabled = _pizzaConfig.maxFillerSlicesWhenFull > 0;

        if (isFull && fillerEnabled)
        {
            int fillerCount = UnityEngine.Random.Range(
                _pizzaConfig.minFillerSlicesWhenFull,
                _pizzaConfig.maxFillerSlicesWhenFull + 1);

            mainCount    = totalCount - fillerCount;
            fillerPoolIds = PickFillerPoolIds(pizzaType.id, fillerCount);
        }

        // Initialize AFTER position is set — slices will spawn at correct world positions.
        plate.Initialize(pizzaType.id, platePoolId, mainCount, _pizzaConfig.maxSlicesPerPlate, fillerPoolIds);

        _plates[slotIndex] = plate;

        Debug.Log($"[HoldGridManager] Slot {slotIndex}: {pizzaType.displayName} ×{mainCount} main"
                + (fillerPoolIds != null ? $" + {fillerPoolIds.Length} filler" : "")
                + $" on '{platePoolId}'.");
    }

    /// <summary>Reads pizza_config.json and caches the configuration.</summary>
    private void LoadPizzaConfig()
    {
        TextAsset jsonAsset = Resources.Load<TextAsset>(_pizzaConfigPath);

        if (jsonAsset == null)
        {
            Debug.LogError($"[HoldGridManager] Pizza config not found at Resources/{_pizzaConfigPath}.json");
            return;
        }

        _pizzaConfig = JsonUtility.FromJson<PizzaConfigCollection>(jsonAsset.text);

        if (_pizzaConfig == null)
            Debug.LogError("[HoldGridManager] Failed to parse PizzaConfigCollection from JSON.");
    }

    /// <summary>
    /// Returns a random slice count using the weighted distribution in pizza_config.json.
    /// Falls back to uniform distribution if weights are missing or empty.
    /// </summary>
    private int GetWeightedRandomCount()
    {
        int[] weights = _pizzaConfig.spawnCountWeights;

        // Fallback: no weights defined — use uniform random.
        if (weights == null || weights.Length == 0)
            return UnityEngine.Random.Range(_pizzaConfig.spawnCountMin, _pizzaConfig.spawnCountMax + 1);

        // Sum all weights to get the total pool.
        int totalWeight = 0;
        foreach (int w in weights) totalWeight += w;

        int roll       = UnityEngine.Random.Range(0, totalWeight);
        int cumulative = 0;
        int countRange = _pizzaConfig.spawnCountMax - _pizzaConfig.spawnCountMin + 1;

        for (int i = 0; i < countRange && i < weights.Length; i++)
        {
            cumulative += weights[i];
            if (roll < cumulative)
                return _pizzaConfig.spawnCountMin + i;
        }

        return _pizzaConfig.spawnCountMax; // Fallback to max.
    }

    /// <summary>
    /// Returns an array of <paramref name="count"/> pool IDs chosen randomly from pizza types
    /// other than <paramref name="excludeTypeId"/>.
    /// </summary>
    private string[] PickFillerPoolIds(string excludeTypeId, int count)
    {
        // Collect pool IDs of all pizza types except the main type.
        var otherIds = new System.Collections.Generic.List<string>();
        foreach (PizzaTypeData t in _pizzaConfig.pizzaTypes)
            if (t.id != excludeTypeId) otherIds.Add(t.poolId);

        if (otherIds.Count == 0 || count <= 0) return null;

        string[] result = new string[count];
        for (int i = 0; i < count; i++)
            result[i] = otherIds[UnityEngine.Random.Range(0, otherIds.Count)];

        return result;
    }
}