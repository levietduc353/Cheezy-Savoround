using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Listens for plates placed on the Main Grid and queues merge operations for
/// MergeAnimator to execute with full animation support.
///
/// This class is now a pure "operation collector":
///   1. HandlePlatePlaced() identifies all valid merge candidates.
///   2. For each candidate it builds a MergeOperation (donor, receiver, amount).
///   3. The entire list is handed to MergeAnimator.ExecuteMergeSequence().
///
/// MergeAnimator owns the data-transfer (ExtractSlicesOfType / AcceptAnimatedSlice)
/// and the pool-return (ReturnToPool) — MergeChecker no longer calls either directly.
///
/// Standard Merge (Pass 1 — same main pizza type):
///   - Fewer slices → DONOR;  more slices → RECEIVER.
///   - Equal counts → placed (dragged plate) is DONOR, neighbour is RECEIVER.
///   - If the chosen receiver is already full, roles are swapped.
///   - Only the shared main pizza type is transferred.
///
/// Mixed Merge (Pass 2 — different main type, filler match):
///   - Neighbour holds filler slices whose type matches placed.PizzaTypeId.
///   - Neighbour must have >= total slices than placed.
///   - Neighbour donates only those matching filler slices to placed.
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
    /// Collects all valid merge operations and hands them to MergeAnimator.
    /// </summary>
    private void HandlePlatePlaced(int row, int col, PlateController placed)
    {
        List<NeighborInfo> allNeighbors = _mainGrid.GetNeighbors(row, col);
        var operations = new List<MergeAnimator.MergeOperation>();

        // ── Pass 1: Standard merge — neighbour shares the same main pizza type ─────
        foreach (NeighborInfo info in allNeighbors)
        {
            if (info.plate == null || info.plate.PizzaTypeId != placed.PizzaTypeId) continue;

            // ── Determine donor / receiver ────────────────────────────────────
            PlateController donor, receiver;
            int donorRow, donorCol, receiverRow, receiverCol;

            if (info.plate.SliceCount > placed.SliceCount)
            {
                // Neighbour has more → neighbour receives, placed donates.
                receiver    = info.plate; receiverRow = info.row; receiverCol = info.col;
                donor       = placed;     donorRow    = row;      donorCol    = col;
            }
            else if (placed.SliceCount > info.plate.SliceCount)
            {
                // Placed has more → placed receives, neighbour donates.
                receiver    = placed;     receiverRow = row;      receiverCol = col;
                donor       = info.plate; donorRow    = info.row; donorCol    = info.col;
            }
            else
            {
                // Equal counts: placed (the dragged plate) is the DONOR.
                donor       = placed;     donorRow    = row;      donorCol    = col;
                receiver    = info.plate; receiverRow = info.row; receiverCol = info.col;
            }

            // ── Full-receiver guard: swap so full plate becomes donor ──────────
            if (receiver.IsFull)
            {
                (donor, receiver) = (receiver, donor);
                (donorRow,    receiverRow)    = (receiverRow,    donorRow);
                (donorCol,    receiverCol)    = (receiverCol,    donorCol);
            }

            // ── Calculate amount ──────────────────────────────────────────────
            int matchingCount = donor.CountSlicesOfType(placed.PizzaTypeId);
            int amount        = Mathf.Min(matchingCount, receiver.MaxSlices - receiver.SliceCount);
            if (amount <= 0) continue;

            operations.Add(new MergeAnimator.MergeOperation
            {
                donor       = donor,    donorRow    = donorRow,    donorCol    = donorCol,
                receiver    = receiver, receiverRow = receiverRow, receiverCol = receiverCol,
                typeId      = placed.PizzaTypeId,
                amount      = amount,
            });

            Debug.Log($"[MergeChecker] Pass1 op queued: {donor.name}({donor.SliceCount})" +
                      $" → {receiver.name}({receiver.SliceCount}) ×{amount} '{placed.PizzaTypeId}'");
        }

        // ── Pass 2: Mixed merge — neighbour has filler slices of placed's type ──────
        foreach (NeighborInfo info in allNeighbors)
        {
            if (info.plate == null) continue;
            // Same main type already handled in Pass 1.
            if (info.plate.PizzaTypeId == placed.PizzaTypeId) continue;
            // Neighbour must carry at least one filler of placed's type.
            if (!info.plate.HasSlicesOfType(placed.PizzaTypeId)) continue;
            // Direction guard: neighbour (donor) must have >= slices than placed (receiver).
            if (info.plate.SliceCount < placed.SliceCount) continue;

            int donorMatchCount   = info.plate.CountSlicesOfType(placed.PizzaTypeId);
            int receiverFreeSlots = placed.MaxSlices - placed.SliceCount;
            int amount            = Mathf.Min(donorMatchCount, receiverFreeSlots);
            if (amount <= 0) continue;

            operations.Add(new MergeAnimator.MergeOperation
            {
                donor       = info.plate, donorRow    = info.row, donorCol    = info.col,
                receiver    = placed,     receiverRow = row,      receiverCol = col,
                typeId      = placed.PizzaTypeId,
                amount      = amount,
            });

            Debug.Log($"[MergeChecker] Pass2 op queued: {info.plate.name}({info.plate.SliceCount})" +
                      $" → {placed.name}({placed.SliceCount}) ×{amount} '{placed.PizzaTypeId}' (mixed)");
        }

        // ── Delegate to MergeAnimator ─────────────────────────────────────────
        if (operations.Count > 0)
        {
            if (MergeAnimator.Instance == null)
            {
                Debug.LogError("[MergeChecker] MergeAnimator.Instance is null. " +
                               "Add MergeAnimator to the scene.");
                return;
            }

            Debug.Log($"[MergeChecker] Handing {operations.Count} op(s) to MergeAnimator.");
            MergeAnimator.Instance.ExecuteMergeSequence(operations, _mainGrid, _fsm);
        }
    }
}
