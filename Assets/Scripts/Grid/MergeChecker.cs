using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Listens for plates placed on the Main Grid and executes merge logic.
///
/// Standard Merge (Pass 1 — same main pizza type):
///   - Fewer slices → DONOR;  more slices → RECEIVER.
///   - Equal counts → placed (dragged plate) is DONOR, neighbour is RECEIVER.
///   - If the chosen receiver is already full, roles are swapped so the full
///     plate donates instead.
///   - Only the shared main pizza type is transferred — filler slices of other
///     types stay on the donor plate so they are never accidentally lost.
///   - A plate that becomes full after receiving  → removed from grid (pool).
///   - A plate that becomes empty after donating → removed from grid (pool).
///
/// Mixed Merge (Pass 2 — different main type, filler match):
///   - Triggered when a neighbour's main type differs from placed's type, but
///     the neighbour holds filler slices whose type matches placed.PizzaTypeId.
///   - The neighbour (must have >= total slices than placed) DONATES only those
///     matching filler slices to placed.
///   - Does NOT require the neighbour to be full — after a first mixed merge the
///     neighbour may be non-full but still carry fillers of a third type.
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
    /// Runs both standard same-type merges (Pass 1) and mixed-type merges (Pass 2).
    /// </summary>
    private void HandlePlatePlaced(int row, int col, PlateController placed)
    {
        List<NeighborInfo> allNeighbors = _mainGrid.GetNeighbors(row, col);
        bool anyMerge = false;

        // ── Pass 1: Standard merge — neighbour shares the same main pizza type ───────
        foreach (NeighborInfo info in allNeighbors)
        {
            // Stop if placed was returned to pool during an earlier merge this frame.
            if (placed.IsEmpty) break;
            if (info.plate == null || info.plate.PizzaTypeId != placed.PizzaTypeId) continue;

            if (!anyMerge) { _fsm?.ChangeState(_fsm.CheckingMerge); anyMerge = true; }
            ExecuteMerge(placed, row, col, info.plate, info.row, info.col);
        }

        // ── Pass 2: Mixed merge — neighbour has filler slices of placed's type ───────
        // placed acts as RECEIVER; the neighbour donates only its matching-type slices.
        //
        // Eligible neighbour:
        //   • Different main pizza type from placed (same-type handled in Pass 1).
        //   • Has at least one filler slice of placed.PizzaTypeId.
        //   • Has >= total slices than placed ("chuyển từ đĩa nhiều sang đĩa ít"):
        //     if neighbour has FEWER slices it is the "ít" side → skip.
        foreach (NeighborInfo info in allNeighbors)
        {
            if (placed.IsEmpty) break;
            if (info.plate == null) continue;
            // Skip plates already handled by Pass 1 (same main type).
            if (info.plate.PizzaTypeId == placed.PizzaTypeId) continue;
            // Neighbour must carry at least one filler of placed's type.
            if (!info.plate.HasSlicesOfType(placed.PizzaTypeId)) continue;
            // Direction guard: neighbour (donor) must have >= slices than placed (receiver).
            if (info.plate.SliceCount < placed.SliceCount) continue;

            if (!anyMerge) { _fsm?.ChangeState(_fsm.CheckingMerge); anyMerge = true; }
            ExecuteMixedMerge(info.plate, info.row, info.col,
                              placed,     row,      col,
                              placed.PizzaTypeId);
        }

        if (anyMerge) _fsm?.ChangeState(_fsm.Playing);
    }

    /// <summary>
    /// Standard merge between two plates that share the same main pizza type.
    /// Determines donor/receiver, then transfers ONLY the matching pizza type so
    /// that filler slices on a mixed plate are never accidentally moved.
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
            // Neighbour has more slices → neighbour receives, placed donates.
            receiver    = neighbor; receiverRow = neighborRow; receiverCol = neighborCol;
            donor       = placed;   donorRow    = placedRow;   donorCol    = placedCol;
        }
        else if (placed.SliceCount > neighbor.SliceCount)
        {
            // Placed has more slices → placed receives, neighbour donates.
            receiver    = placed;   receiverRow = placedRow;   receiverCol = placedCol;
            donor       = neighbor; donorRow    = neighborRow; donorCol    = neighborCol;
        }
        else
        {
            // Equal counts: placed (the dragged plate) is always the DONOR.
            // Drag-drop UX: you drag a plate and drop it, it gives its slices away.
            donor       = placed;   donorRow    = placedRow;   donorCol    = placedCol;
            receiver    = neighbor; receiverRow = neighborRow; receiverCol = neighborCol;
        }

        // ── Full-receiver guard: swap roles so the full plate becomes donor ───
        // A full plate as receiver would have 0 free slots → no transfer, and the
        // IsFull check below would then incorrectly remove it from the grid.
        // Swapping lets the full plate donate its matching-type slices instead.
        if (receiver.IsFull)
        {
            PlateController tmpPlate = receiver;
            int tmpRow = receiverRow, tmpCol = receiverCol;
            receiver    = donor;   receiverRow = donorRow;   receiverCol = donorCol;
            donor       = tmpPlate; donorRow   = tmpRow;     donorCol    = tmpCol;
        }

        // ── Type-safe transfer ────────────────────────────────────────────────
        // Both plates share the same PizzaTypeId (Pass 1 guarantee).
        // We use TransferSlicesOfType instead of TransferTo so that filler slices
        // of other types on a mixed plate are NEVER moved to the receiver.
        //
        // Without this, TransferTo would move fillers (type Y, Z …) along with
        // the matching type, causing them to silently disappear when the receiver
        // plate is later returned to the pool.
        string matchingType = placed.PizzaTypeId; // == neighbor.PizzaTypeId

        // Cap amount by the donor's matching-type count (not total slices),
        // so a mixed donor with 2X + 1Y at amount=3 correctly transfers only 2X.
        int matchingCount = donor.CountSlicesOfType(matchingType);
        int amount        = Mathf.Min(matchingCount, receiver.MaxSlices - receiver.SliceCount);

        if (amount <= 0)
        {
            // Nothing to transfer (donor has no matching slices left, or receiver
            // is at capacity) — abort cleanly without touching either plate.
            Debug.Log($"[MergeChecker] Skipping merge at ({placedRow},{placedCol}): no '{matchingType}' slices to transfer.");
            return;
        }

        donor.TransferSlicesOfType(receiver, matchingType, amount);

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
    /// Mixed merge: <paramref name="donor"/> is a plate (possibly non-full) that holds
    /// filler slices of <paramref name="matchingTypeId"/>. Transfers those slices to
    /// <paramref name="receiver"/>. Non-matching slices on donor are left untouched.
    /// </summary>
    private void ExecuteMixedMerge(
        PlateController donor,    int donorRow,    int donorCol,
        PlateController receiver, int receiverRow, int receiverCol,
        string matchingTypeId)
    {
        _fsm?.ChangeState(_fsm.Merging);

        int donorMatchCount   = donor.CountSlicesOfType(matchingTypeId);
        int receiverFreeSlots = receiver.MaxSlices - receiver.SliceCount;
        int amount = Mathf.Min(donorMatchCount, receiverFreeSlots);

        if (amount <= 0)
        {
            Debug.Log($"[MergeChecker] Mixed merge skipped: no '{matchingTypeId}' slices to transfer.");
            return;
        }

        donor.TransferSlicesOfType(receiver, matchingTypeId, amount);

        _fsm?.ChangeState(_fsm.Clearing);

        if (receiver.IsFull)
        {
            _mainGrid.RemovePlate(receiverRow, receiverCol);
            receiver.ReturnToPool();
            Debug.Log($"[MergeChecker] Mixed merge: receiver ({receiverRow},{receiverCol}) full → removed.");
        }

        if (donor.IsEmpty)
        {
            _mainGrid.RemovePlate(donorRow, donorCol);
            donor.ReturnToPool();
            Debug.Log($"[MergeChecker] Mixed merge: donor ({donorRow},{donorCol}) empty → removed.");
        }
    }

    // FindSameTypeNeighbors is no longer used by HandlePlatePlaced (it iterates
    // GetNeighbors directly). Kept as a dead-but-harmless helper for future use.
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
