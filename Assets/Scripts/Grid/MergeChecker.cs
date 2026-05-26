using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Listens for plates placed on the Main Grid and executes merge logic.
///
/// Merge Rules:
///   - A merge occurs when a newly-placed plate has a neighbor with the same pizza type.
///   - The plate with fewer slices is the DONOR; the one with more is the RECEIVER.
///   - If counts are equal, the neighbor (older plate) is the donor.
///   - Slices transfer until the receiver is full (maxSlices) or the donor is empty.
///   - A full plate is removed from the grid and returned to pool.
///   - An empty plate is removed from the grid and returned to pool.
/// </summary>
public class MergeChecker : MonoBehaviour
{
    // ─── Inspector fields ─────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("The main GridManager where plates are placed.")]
    [SerializeField] private GridManager _mainGrid;

    [Tooltip("The game state machine used to signal merge transitions.")]
    [SerializeField] private GameStateMachine _fsm;

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    private void OnEnable()
    {
        if (_mainGrid != null)
            _mainGrid.OnPlatePlaced += HandlePlatePlaced;
    }

    private void OnDisable()
    {
        if (_mainGrid != null)
            _mainGrid.OnPlatePlaced -= HandlePlatePlaced;
    }

    // ─── Private logic ────────────────────────────────────────────────────────

    /// <summary>
    /// Invoked by GridManager whenever a plate lands on a cell.
    /// Finds same-type neighbors and runs the merge algorithm for each.
    /// </summary>
    private void HandlePlatePlaced(int row, int col, PlateController placed)
    {
        List<NeighborInfo> sameTypeNeighbors = FindSameTypeNeighbors(row, col, placed.PizzaTypeId);

        if (sameTypeNeighbors.Count == 0) return;

        _fsm?.ChangeState(_fsm.CheckingMerge);

        // Process each same-type neighbor.
        // Note: 'placed' may become full mid-loop if multiple neighbors merge into it.
        foreach (NeighborInfo info in sameTypeNeighbors)
        {
            // Re-check: if placed was removed (returned to pool) during an earlier merge, stop.
            if (placed == null || placed.IsEmpty) break;

            ExecuteMerge(placed, row, col, info.plate, info.row, info.col);
        }

        _fsm?.ChangeState(_fsm.Playing);
    }

    /// <summary>
    /// Determines donor and receiver, transfers slices, then removes depleted/full plates.
    /// </summary>
    private void ExecuteMerge(
        PlateController placed,   int placedRow,   int placedCol,
        PlateController neighbor, int neighborRow, int neighborCol)
    {
        _fsm?.ChangeState(_fsm.Merging);

        // ── Determine donor / receiver ────────────────────────────────────────
        PlateController donor, receiver;
        int donorRow, donorCol, receiverRow, receiverCol;

        if (neighbor.SliceCount > placed.SliceCount)
        {
            // Neighbor has more → neighbor receives, placed donates.
            receiver    = neighbor; receiverRow = neighborRow; receiverCol = neighborCol;
            donor       = placed;   donorRow    = placedRow;   donorCol    = placedCol;
        }
        else
        {
            // Placed has more OR equal slices → placed receives, neighbor donates.
            receiver    = placed;   receiverRow = placedRow;   receiverCol = placedCol;
            donor       = neighbor; donorRow    = neighborRow; donorCol    = neighborCol;
        }

        // ── Transfer slices ───────────────────────────────────────────────────
        // Transfer only as many slices as the receiver needs to reach full capacity.
        int emptySlots = _fsm != null
            ? (receiver.IsFull ? 0 : Mathf.Min(donor.SliceCount, 6 - receiver.SliceCount))
            : Mathf.Min(donor.SliceCount, 6 - receiver.SliceCount);

        // Calculate transfer amount: limited by donor supply and receiver capacity.
        int amount = Mathf.Min(donor.SliceCount, 6 - receiver.SliceCount);

        donor.TransferTo(receiver, amount);

        // ── Clear depleted plates ─────────────────────────────────────────────
        _fsm?.ChangeState(_fsm.Clearing);

        if (receiver.IsFull)
        {
            _mainGrid.RemovePlate(receiverRow, receiverCol);
            receiver.ReturnToPool();
            Debug.Log($"[MergeChecker] Plate at ({receiverRow},{receiverCol}) full → removed.");
        }

        if (donor.IsEmpty)
        {
            _mainGrid.RemovePlate(donorRow, donorCol);
            donor.ReturnToPool();
            Debug.Log($"[MergeChecker] Plate at ({donorRow},{donorCol}) empty → removed.");
        }
    }

    /// <summary>
    /// Returns all occupied neighbors of (<paramref name="row"/>, <paramref name="col"/>)
    /// whose pizza type matches <paramref name="pizzaTypeId"/>.
    /// </summary>
    private List<NeighborInfo> FindSameTypeNeighbors(int row, int col, string pizzaTypeId)
    {
        var result    = new List<NeighborInfo>();
        var neighbors = _mainGrid.GetNeighbors(row, col);

        foreach (NeighborInfo info in neighbors)
        {
            if (info.plate != null && info.plate.PizzaTypeId == pizzaTypeId)
                result.Add(info);
        }

        return result;
    }
}
