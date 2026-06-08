using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Controls a single plate GameObject in the game.
/// Manages pizza slices via CircleGridDrawer, handles merge transfers,
/// and returns itself + slices to their respective pools when done.
///
/// Lifecycle:
///   1. PoolManager.Get("plate_X")         → plate is activated
///   2. plate.Initialize(...)              → pizza type set, slices spawned
///   3. plate.TransferTo(other, amount)    → merge logic moves slices
///   4. plate.ReturnToPool()              → all slices + plate released back to pools
/// </summary>
public class PlateController : MonoBehaviour
{
    // ─── Events (Observer Pattern) ────────────────────────────────────────────

    /// <summary>Fired when this plate reaches maxSlices (full).</summary>
    public event Action<PlateController> OnPlateFull;

    /// <summary>Fired when this plate drops to 0 slices (empty).</summary>
    public event Action<PlateController> OnPlateEmpty;

    // ─── Private state ────────────────────────────────────────────────────────

    private string _pizzaTypeId;
    private string _poolId;
    private int    _sliceCount;
    private int    _maxSlices;

    /// <summary>Position of this plate on the main grid. (-1,-1) = not on main grid.</summary>
    private int _gridRow = -1;
    private int _gridCol = -1;

    private CircleGridDrawer _drawer;

    // ─── Public properties ────────────────────────────────────────────────────

    public string  PizzaTypeId  => _pizzaTypeId;
    public string  PoolId       => _poolId;
    public int     SliceCount   => _sliceCount;
    public int     MaxSlices    => _maxSlices;
    public bool    IsEmpty      => _sliceCount <= 0;
    public bool    IsFull       => _sliceCount >= _maxSlices;
    public int     GridRow      => _gridRow;
    public int     GridCol      => _gridCol;

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        // CircleGridDrawer nằm trên một child object của plate prefab,
        // dùng GetComponentInChildren để tìm đúng.
        _drawer = GetComponentInChildren<CircleGridDrawer>();

        if (_drawer == null)
            Debug.LogError($"[PlateController] No CircleGridDrawer found in children of '{name}'. " +
                           "Add CircleGridDrawer to a child object and set Grid Id = 'grid_plate'.");
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Initializes this plate after being retrieved from the pool.
    /// Spawns <paramref name="mainSliceCount"/> slices of the main pizza type, plus any
    /// filler slices listed in <paramref name="fillerPoolIds"/> (different types, for full plates).
    /// </summary>
    /// <param name="mainSliceCount">Number of main-type pizza slices to spawn.</param>
    /// <param name="fillerPoolIds">
    /// Optional pool IDs of filler (different-type) slices spawned after main slices.
    /// Used when the plate spawns full (6 slices) to add game-balance variety.
    /// May be null or empty for non-full plates.
    /// </param>
    public void Initialize(string pizzaTypeId, string poolId, int mainSliceCount,
                           int maxSlices = 6, string[] fillerPoolIds = null)
    {
        _pizzaTypeId = pizzaTypeId;
        _poolId      = poolId;
        _maxSlices   = maxSlices;
        _sliceCount  = 0;
        _gridRow     = -1;
        _gridCol     = -1;

        if (_drawer == null)
        {
            Debug.LogError($"[PlateController] Cannot initialize '{name}': CircleGridDrawer is missing. "
                         + "Add the component to the plate prefab and set Grid Id = 'grid_plate'.");
            return;
        }

        // Fully reset the drawer — clears all slot occupants so the plate starts clean.
        // Must use RebuildAndClearSlots (not RebuildSlots) here; the plate is freshly
        // retrieved from the pool and must not carry over stale occupant references.
        _drawer.RebuildAndClearSlots();

        SpawnSlices(mainSliceCount, fillerPoolIds);
    }

    /// <summary>Marks the position of this plate in the main grid.</summary>
    public void SetGridPosition(int row, int col)
    {
        _gridRow = row;
        _gridCol = col;
    }

    /// <summary>Clears the main grid position (plate removed from main grid).</summary>
    public void ClearGridPosition()
    {
        _gridRow = -1;
        _gridCol = -1;
    }

    /// <summary>
    /// Transfers up to <paramref name="maxAmount"/> pizza slices from this plate
    /// to <paramref name="receiver"/>, starting from the oldest occupied slots.
    /// Fires <see cref="OnPlateEmpty"/> if this plate reaches 0 slices.
    /// </summary>
    /// <returns>Actual number of slices transferred.</returns>
    public int TransferTo(PlateController receiver, int maxAmount)
    {
        if (receiver == null || receiver.IsFull || maxAmount <= 0) return 0;

        // Snapshot occupied slots to avoid mutating the list during iteration.
        var occupiedSnapshot = new List<CircleSlot>(_drawer.GetOccupiedSlots());
        int transferred = 0;

        foreach (CircleSlot slot in occupiedSnapshot)
        {
            if (transferred >= maxAmount || receiver.IsFull) break;

            // Remove slice from this plate's drawer.
            GameObject go = _drawer.RemoveObject(slot.circleRow, slot.circleCol, slot.sectorIndex);
            if (go == null) continue;

            _sliceCount--;

            // Hand off to receiver.
            if (receiver.ReceiveSlice(go))
            {
                transferred++;
            }
            else
            {
                // Safety: receiver rejected — put slice back.
                _drawer.PlaceObject(slot.circleRow, slot.circleCol, slot.sectorIndex, go);
                _sliceCount++;
                break;
            }
        }

        if (IsEmpty) OnPlateEmpty?.Invoke(this);
        return transferred;
    }

    /// <summary>
    /// Accepts a single pizza slice GameObject into the first available empty slot.
    /// Fires <see cref="OnPlateFull"/> if this plate becomes full after receiving.
    /// </summary>
    /// <returns>True if the slice was successfully placed.</returns>
    public bool ReceiveSlice(GameObject go)
    {
        if (IsFull) return false;

        // Rebuild slot world positions in case this plate was moved (e.g. dragged and placed).
        _drawer.RebuildSlots();

        var emptySlots = _drawer.GetEmptySlots();
        if (emptySlots.Count == 0) return false;

        // Capture slot indices before PlaceObject mutates the slot's occupant field.
        CircleSlot target = emptySlots[0];
        int targetRow     = target.circleRow;
        int targetCol     = target.circleCol;
        int targetSector  = target.sectorIndex;

        bool placed = _drawer.PlaceObject(targetRow, targetCol, targetSector, go);

        if (placed)
        {
            _sliceCount++;
            if (IsFull) OnPlateFull?.Invoke(this);
        }

        return placed;
    }

    /// <summary>
    /// Releases all pizza slices back to their pools, clears the grid position,
    /// then returns this plate itself to its pool.
    /// After this call the GameObject is deactivated — do not reference it.
    /// </summary>
    public void ReturnToPool()
    {
        ReleaseAllSlices();
        ClearGridPosition();
        PoolManager.Instance.Release(_poolId, gameObject);
    }

    // ─── Animation-support API (used by MergeAnimator) ──────────────────────────

    /// <summary>
    /// Extracts up to <paramref name="maxAmount"/> slices of <paramref name="typeId"/>
    /// from this plate for animation.
    /// Each extracted slice is removed from the CircleGridDrawer, its _sliceCount is
    /// decremented, and it is un-parented so MergeAnimator can move it freely.
    /// Does NOT fire OnPlateEmpty — the caller (MergeAnimator) is responsible for
    /// checking IsEmpty and triggering dismiss + pool return.
    /// </summary>
    /// <returns>List of (GameObject, world-space position at time of extraction).</returns>
    public List<(GameObject go, Vector3 worldPos)> ExtractSlicesOfType(
        string typeId, int maxAmount)
    {
        var result = new List<(GameObject go, Vector3 worldPos)>();

        // Build a snapshot of matching occupied slots.
        var matchingSlots = new List<CircleSlot>();
        foreach (CircleSlot slot in _drawer.GetOccupiedSlots())
        {
            if (matchingSlots.Count >= maxAmount) break;
            PizzaSlice slice = slot.occupant != null
                ? slot.occupant.GetComponent<PizzaSlice>() : null;
            if (slice != null && slice.PizzaTypeId == typeId)
                matchingSlots.Add(slot);
        }

        foreach (CircleSlot slot in matchingSlots)
        {
            if (result.Count >= maxAmount) break;

            // Remove the slice from the drawer first, then read its ACTUAL world
            // position from go.transform.position.
            // DO NOT use slot.worldPosition here — it is stale (set when the plate
            // was last in the hold grid) and does not reflect the plate's current
            // position on the main grid after dragging.
            GameObject go = _drawer.RemoveObject(
                slot.circleRow, slot.circleCol, slot.sectorIndex);
            if (go == null) continue;

            _sliceCount--;
            Vector3 fromPos = go.transform.position; // correct world position post-drag
            go.transform.SetParent(null);             // free-floating for animation
            result.Add((go, fromPos));
        }

        return result;
    }

    /// <summary>
    /// Returns the world-space positions of the next <paramref name="count"/> empty
    /// slots WITHOUT modifying any state.
    /// Used by MergeAnimator to know where slices will land before they start flying,
    /// so the flight paths can be aimed correctly.
    /// </summary>
    public List<Vector3> PeekEmptySlotPositions(int count)
    {
        // Rebuild so positions reflect the plate's current world location.
        _drawer.RebuildSlots();

        var emptySlots = _drawer.GetEmptySlots();
        var result     = new List<Vector3>();
        int take       = Mathf.Min(count, emptySlots.Count);
        for (int i = 0; i < take; i++)
            result.Add(emptySlots[i].worldPosition);
        return result;
    }

    /// <summary>
    /// Accepts one animated slice into this plate after its travel coroutine ends.
    /// Delegates to <see cref="ReceiveSlice"/>, which handles slot-finding,
    /// re-parenting, _sliceCount increment, and OnPlateFull.
    /// </summary>
    public bool AcceptAnimatedSlice(GameObject go) => ReceiveSlice(go);

    // ─── Slice-type query helpers ─────────────────────────────────────────────

    /// <summary>
    /// Returns true if this plate holds at least one slice of <paramref name="typeId"/>.
    /// Used by MergeChecker to detect mixed-merge candidates.
    /// </summary>
    public bool HasSlicesOfType(string typeId)
    {
        foreach (CircleSlot slot in _drawer.GetOccupiedSlots())
        {
            PizzaSlice slice = slot.occupant != null
                ? slot.occupant.GetComponent<PizzaSlice>() : null;
            if (slice != null && slice.PizzaTypeId == typeId) return true;
        }
        return false;
    }

    /// <summary>
    /// Returns the count of slices whose <see cref="PizzaSlice.PizzaTypeId"/> equals
    /// <paramref name="typeId"/>. Used by MergeChecker to compute mixed-merge amounts.
    /// </summary>
    public int CountSlicesOfType(string typeId)
    {
        int count = 0;
        foreach (CircleSlot slot in _drawer.GetOccupiedSlots())
        {
            PizzaSlice slice = slot.occupant != null
                ? slot.occupant.GetComponent<PizzaSlice>() : null;
            if (slice != null && slice.PizzaTypeId == typeId) count++;
        }
        return count;
    }

    // ─── Unify helpers (used by UnifyPowerUp) ────────────────────────────────

    /// <summary>
    /// Returns true if this plate holds slices of more than one distinct pizza type.
    /// Used by UnifyPowerUp to validate the selection before processing.
    /// </summary>
    public bool HasMultipleSliceTypes()
    {
        string firstType = null;
        foreach (CircleSlot slot in _drawer.GetOccupiedSlots())
        {
            PizzaSlice slice = slot.occupant != null
                ? slot.occupant.GetComponent<PizzaSlice>() : null;
            if (slice == null) continue;

            if (firstType == null) { firstType = slice.PizzaTypeId; continue; }
            if (slice.PizzaTypeId != firstType) return true;
        }
        return false;
    }

    /// <summary>
    /// Returns the pizza type ID that appears most often on this plate.
    /// On tie the first type encountered in slot order wins (stable tiebreak).
    /// Returns null if the plate has no slices.
    /// </summary>
    public string GetDominantSliceType()
    {
        // Ordered list preserves first-found ordering for stable tie-breaking.
        var orderedTypes = new List<string>();
        var counts       = new Dictionary<string, int>();

        foreach (CircleSlot slot in _drawer.GetOccupiedSlots())
        {
            PizzaSlice slice = slot.occupant != null
                ? slot.occupant.GetComponent<PizzaSlice>() : null;
            if (slice == null) continue;

            string typeId = slice.PizzaTypeId;
            if (!counts.ContainsKey(typeId))
            {
                counts[typeId] = 0;
                orderedTypes.Add(typeId); // track encounter order
            }
            counts[typeId]++;
        }

        if (orderedTypes.Count == 0) return null;

        // Find the type with the highest count.
        // orderedTypes[0] is used as the initial winner → first-found wins on tie.
        string dominant = orderedTypes[0];
        int    maxCount = counts[dominant];

        foreach (string typeId in orderedTypes)
        {
            if (counts[typeId] > maxCount)
            {
                maxCount = counts[typeId];
                dominant = typeId;
            }
        }

        return dominant;
    }

    /// <summary>
    /// Converts every slice whose type differs from <paramref name="targetTypeId"/> into
    /// a new slice of that type. Each minority slice is returned to its original pool and
    /// replaced with a fresh slice from the target pool in the same sector slot.
    /// Also updates <see cref="PizzaTypeId"/> to <paramref name="targetTypeId"/>.
    ///
    /// <para>
    /// _sliceCount is unchanged for each successful 1-for-1 swap.
    /// It is decremented only when a pool Get() or PlaceObject() fails (slot left empty).
    /// </para>
    /// </summary>
    /// <returns>Number of slices actually converted.</returns>
    public int UnifySlicesToType(string targetTypeId)
    {
        if (_drawer == null || string.IsNullOrEmpty(targetTypeId)) return 0;

        // ── CRITICAL: Rebuild slot world positions before any PlaceObject call ────
        // slot.worldPosition is computed once in BuildSlots() based on the plate's
        // transform at that moment. If the plate has since moved (e.g. from hold grid
        // → main grid), the stored positions are stale. RebuildSlots() refreshes them
        // without clearing occupant references — safe to call mid-play.
        _drawer.RebuildSlots();

        // Snapshot minority slots before mutating to avoid modifying the collection mid-loop.
        var toConvert = new List<CircleSlot>();
        foreach (CircleSlot slot in _drawer.GetOccupiedSlots())
        {
            PizzaSlice slice = slot.occupant != null
                ? slot.occupant.GetComponent<PizzaSlice>() : null;
            if (slice != null && slice.PizzaTypeId != targetTypeId)
                toConvert.Add(slot);
        }

        int converted = 0;

        foreach (CircleSlot slot in toConvert)
        {
            // ── Step 1: Remove old (minority) slice from the drawer ───────────
            GameObject oldGo = _drawer.RemoveObject(slot.circleRow, slot.circleCol, slot.sectorIndex);
            if (oldGo == null) continue;

            // Return the old slice to its correct pool (no _sliceCount change yet —
            // we are about to put a replacement in the same slot).
            PizzaSlice oldSlice = oldGo.GetComponent<PizzaSlice>();
            if (oldSlice != null)
                oldSlice.ReturnToPool();
            else
                Destroy(oldGo); // Fallback: shouldn't happen in normal play.

            // ── Step 2: Get a new slice of the target type ────────────────────
            // In this game typeId == poolId (pizza_1 → pool "pizza_1").
            GameObject newGo = PoolManager.Instance.Get(targetTypeId);
            if (newGo == null)
            {
                // Pool exhausted — slot stays empty; decrement count to reflect reality.
                _sliceCount--;
                Debug.LogWarning($"[PlateController] UnifySlicesToType: pool '{targetTypeId}' " +
                                 "is exhausted. Slot left empty.");
                continue;
            }

            // ── Step 3: Initialize and place ─────────────────────────────────
            newGo.GetComponent<PizzaSlice>()?.Initialize(targetTypeId, targetTypeId);

            bool placed = _drawer.PlaceObject(slot.circleRow, slot.circleCol, slot.sectorIndex, newGo);
            if (placed)
            {
                converted++;
                // _sliceCount unchanged (removed 1, added 1 in the same slot).
            }
            else
            {
                // Slot unavailable (shouldn't happen since we just vacated it).
                PoolManager.Instance.Release(targetTypeId, newGo);
                _sliceCount--;
                Debug.LogWarning($"[PlateController] UnifySlicesToType: PlaceObject failed " +
                                 $"for slot ({slot.circleRow},{slot.circleCol},{slot.sectorIndex}).");
            }
        }

        // Update the plate's main pizza type to match the unified slices.
        _pizzaTypeId = targetTypeId;

        return converted;
    }

    /// <summary>
    /// Transfers up to <paramref name="maxAmount"/> slices whose
    /// <see cref="PizzaSlice.PizzaTypeId"/> equals <paramref name="typeId"/>
    /// from this plate to <paramref name="receiver"/>.
    /// Non-matching slices are left untouched.
    /// Fires <see cref="OnPlateEmpty"/> if this plate drops to 0 total slices.
    /// </summary>
    /// <returns>Actual number of slices transferred.</returns>
    public int TransferSlicesOfType(PlateController receiver, string typeId, int maxAmount)
    {
        if (receiver == null || receiver.IsFull || maxAmount <= 0) return 0;

        // Build a snapshot of matching slots only to avoid mutating the collection mid-loop.
        var matchingSlots = new System.Collections.Generic.List<CircleSlot>();
        foreach (CircleSlot slot in _drawer.GetOccupiedSlots())
        {
            PizzaSlice slice = slot.occupant != null
                ? slot.occupant.GetComponent<PizzaSlice>() : null;
            if (slice != null && slice.PizzaTypeId == typeId)
                matchingSlots.Add(slot);
        }

        int transferred = 0;
        foreach (CircleSlot slot in matchingSlots)
        {
            if (transferred >= maxAmount || receiver.IsFull) break;

            GameObject go = _drawer.RemoveObject(slot.circleRow, slot.circleCol, slot.sectorIndex);
            if (go == null) continue;

            _sliceCount--;

            if (receiver.ReceiveSlice(go))
            {
                transferred++;
            }
            else
            {
                // Safety: receiver rejected the slice — put it back.
                _drawer.PlaceObject(slot.circleRow, slot.circleCol, slot.sectorIndex, go);
                _sliceCount++;
                break;
            }
        }

        if (IsEmpty) OnPlateEmpty?.Invoke(this);
        return transferred;
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Spawns <paramref name="mainCount"/> slices of the main pizza type,
    /// followed by any filler slices listed in <paramref name="fillerPoolIds"/>.
    /// All slices go into randomly-chosen empty sector slots.
    /// </summary>
    private void SpawnSlices(int mainCount, string[] fillerPoolIds = null)
    {
        if (_drawer == null) return; // Guard: Initialize already logged the error.

        int fillerCount = fillerPoolIds?.Length ?? 0;
        int totalToSpawn = mainCount + fillerCount;

        // Build and shuffle a list of all sector indices.
        int sectorCount = _drawer.SectorCount;
        var sectors     = new List<int>(sectorCount);
        for (int i = 0; i < sectorCount; i++) sectors.Add(i);
        ShuffleList(sectors);

        int toSpawn = Mathf.Min(totalToSpawn, sectorCount);

        for (int i = 0; i < toSpawn; i++)
        {
            // Determine pool ID: first mainCount sectors get main type, rest get filler.
            string spawnPoolId = (i < mainCount) ? _pizzaTypeId
                                                  : fillerPoolIds[i - mainCount];

            GameObject go = PoolManager.Instance.Get(spawnPoolId);
            if (go == null) continue;

            PizzaSlice slice = go.GetComponent<PizzaSlice>();
            slice?.Initialize(spawnPoolId, spawnPoolId);

            bool placed = _drawer.PlaceObject(0, 0, sectors[i], go);
            if (placed)
            {
                _sliceCount++;
            }
            else
            {
                // Slot unavailable — return to pool immediately.
                PoolManager.Instance.Release(spawnPoolId, go);
            }
        }
    }

    /// <summary>
    /// Removes all occupied pizza slices from the drawer and returns each to its pool.
    /// </summary>
    private void ReleaseAllSlices()
    {
        var occupiedSnapshot = new List<CircleSlot>(_drawer.GetOccupiedSlots());

        foreach (CircleSlot slot in occupiedSnapshot)
        {
            GameObject go = _drawer.RemoveObject(slot.circleRow, slot.circleCol, slot.sectorIndex);
            if (go == null) continue;

            PizzaSlice slice = go.GetComponent<PizzaSlice>();
            if (slice != null)
                slice.ReturnToPool();
            else
                Destroy(go); // Fallback if PizzaSlice component is missing.
        }

        _sliceCount = 0;
    }

    /// <summary>
    /// Fisher-Yates in-place shuffle — ensures unbiased random ordering.
    /// </summary>
    private static void ShuffleList<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
