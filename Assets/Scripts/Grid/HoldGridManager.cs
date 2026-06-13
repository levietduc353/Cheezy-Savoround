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

        // ── Random pizza type — only from currently unlocked types ──────────────
        // UnlockManager.UnlockedTypeIds starts with [pizza_1, pizza_2, pizza_3]
        // and grows as the player levels up within the session.
        string selectedTypeId = PickRandomUnlockedTypeId();
        PizzaTypeData pizzaType = FindPizzaTypeById(selectedTypeId);

        if (pizzaType == null)
        {
            Debug.LogError($"[HoldGridManager] Pizza type '{selectedTypeId}' not found in pizza_config. " +
                           "Returning plate to pool.");
            PoolManager.Instance.Release(platePoolId, plateGo);
            return;
        }

        // ── Weighted random slice count ──────────────────────────────────────────
        int totalCount = GetWeightedRandomCount();

        // ── Filler slices — gated by UnlockManager.CurrentFillerChance ────────
        // Filler only makes sense when: plate is full AND at least 2 types are
        // unlocked (otherwise no filler candidates exist) AND the RNG roll passes.
        string[] fillerPoolIds = null;
        int mainCount = totalCount;

        bool isFull          = totalCount == _pizzaConfig.maxSlicesPerPlate;
        bool fillerEnabled   = _pizzaConfig.maxFillerSlicesWhenFull > 0;
        bool hasFillerTypes  = UnlockManager.Instance != null &&
                               UnlockManager.Instance.UnlockedTypeIds.Count > 1;
        float fillerChance   = UnlockManager.Instance != null
                               ? UnlockManager.Instance.CurrentFillerChance : 0f;

        // Roll against the level-dependent filler chance before spawning fillers.
        bool fillerRoll = UnityEngine.Random.value < fillerChance;

        if (isFull && fillerEnabled && hasFillerTypes && fillerRoll)
        {
            int fillerCount = UnityEngine.Random.Range(
                _pizzaConfig.minFillerSlicesWhenFull,
                _pizzaConfig.maxFillerSlicesWhenFull + 1);

            mainCount     = totalCount - fillerCount;
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
        int maxAllowed = _pizzaConfig.spawnCountMax;

        // Ràng buộc: Trước level 6, tối đa chỉ được sinh ra đĩa có 4 lát.
        // Từ level 6 trở đi mới mở khóa mốc 5 và 6 lát.
        if (ScoreManager.Instance != null && ScoreManager.Instance.CurrentLevel < 6)
        {
            maxAllowed = Mathf.Min(maxAllowed, 4);
        }

        int[] weights = _pizzaConfig.spawnCountWeights;

        // Fallback: no weights defined — use uniform random.
        if (weights == null || weights.Length == 0)
            return UnityEngine.Random.Range(_pizzaConfig.spawnCountMin, maxAllowed + 1);

        int countRange = maxAllowed - _pizzaConfig.spawnCountMin + 1;
        int maxIndex = Mathf.Min(countRange, weights.Length);

        // Sum all valid weights to get the total pool.
        int totalWeight = 0;
        for (int i = 0; i < maxIndex; i++) 
            totalWeight += weights[i];

        if (totalWeight == 0) return maxAllowed;

        int roll       = UnityEngine.Random.Range(0, totalWeight);
        int cumulative = 0;

        for (int i = 0; i < maxIndex; i++)
        {
            cumulative += weights[i];
            if (roll < cumulative)
                return _pizzaConfig.spawnCountMin + i;
        }

        return maxAllowed; // Fallback to max.
    }

    /// <summary>
    /// Returns a random pizza type id chosen from the currently unlocked types.
    /// Falls back to "pizza_1" if UnlockManager is unavailable (e.g. during tests).
    /// </summary>
    private string PickRandomUnlockedTypeId()
    {
        if (UnlockManager.Instance == null || UnlockManager.Instance.UnlockedTypeIds.Count == 0)
        {
            Debug.LogWarning("[HoldGridManager] UnlockManager not available — using 'pizza_1' as fallback.");
            return "pizza_1";
        }

        var ids = UnlockManager.Instance.UnlockedTypeIds;
        return ids[UnityEngine.Random.Range(0, ids.Count)];
    }

    /// <summary>
    /// Looks up a <see cref="PizzaTypeData"/> entry by its id in the loaded pizza config.
    /// Returns null if not found.
    /// </summary>
    private PizzaTypeData FindPizzaTypeById(string typeId)
    {
        if (_pizzaConfig?.pizzaTypes == null) return null;
        foreach (PizzaTypeData t in _pizzaConfig.pizzaTypes)
            if (t.id == typeId) return t;
        return null;
    }

    /// <summary>
    /// Returns an array of <paramref name="count"/> pool IDs chosen randomly from
    /// pizza types OTHER than <paramref name="excludeTypeId"/> that are currently unlocked.
    /// This ensures filler slices only come from types the player has already seen.
    /// </summary>
    private string[] PickFillerPoolIds(string excludeTypeId, int count)
    {
        // Only consider unlocked types as filler candidates.
        var otherIds = new System.Collections.Generic.List<string>();

        if (UnlockManager.Instance != null)
        {
            foreach (string id in UnlockManager.Instance.UnlockedTypeIds)
                if (id != excludeTypeId) otherIds.Add(id);
        }
        else
        {
            // Fallback: use full pizza config list (shouldn't happen in normal play).
            foreach (PizzaTypeData t in _pizzaConfig.pizzaTypes)
                if (t.id != excludeTypeId) otherIds.Add(t.poolId);
        }

        if (otherIds.Count == 0 || count <= 0) return null;

        string[] result = new string[count];
        for (int i = 0; i < count; i++)
            result[i] = otherIds[UnityEngine.Random.Range(0, otherIds.Count)];

        return result;
    }
}