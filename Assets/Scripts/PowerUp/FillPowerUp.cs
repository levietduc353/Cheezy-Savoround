using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Power-up: "Fill" — spawns a complement plate next to a selected plate on the Main Grid.
///
/// Logic:
///   1. Player activates the button → button highlights, FSM → FillSelectingState.
///   2. Player clicks a (non-full) plate on the main grid.
///   3. The power-up finds a random empty adjacent cell.
///   4. A new plate is spawned there with exactly (maxSlices − sliceCount) slices,
///      all of the same pizza type as the selected plate.
///   5. Because the new plate is adjacent and same-type, MergeChecker fires automatically,
///      slices fly to the selected plate, the plate becomes full, and both plates dismiss.
///   6. FSM returns to Playing; button returns to normal colour.
///
/// Cancel:
///   Pressing the button again while active (before selecting a plate) cancels the power-up.
///
/// Setup:
///   - Attach to any persistent GameObject (e.g. the same one holding GameStateMachine).
///   - Assign all SerializeField references in the Inspector.
///   - Wire the Fill Button's OnClick → OnFillButtonClicked().
///   - Every plate prefab must have a Collider for raycast detection.
/// </summary>
public class FillPowerUp : MonoBehaviour
{
    // ─── Inspector fields ─────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("Main camera used for raycasting plate selection.")]
    [SerializeField] private Camera _camera;

    [Tooltip("The main GridManager where plates live.")]
    [SerializeField] private GridManager _mainGrid;

    [Tooltip("The game state machine used to signal state transitions.")]
    [SerializeField] private GameStateMachine _fsm;

    [Tooltip("The UI Image component of the Fill Button (for colour highlight).")]
    [SerializeField] private Image _buttonImage;

    [Header("Highlight Colors")]
    [Tooltip("Button image colour when fill mode is active.")]
    [SerializeField] private Color _activeColor = new Color(1f, 0.55f, 0.1f, 1f);   // warm orange

    [Tooltip("Button image colour when fill mode is inactive.")]
    [SerializeField] private Color _normalColor = Color.white;

    [Header("Input")]
    [Tooltip("Layer mask for detecting plate colliders during fill selection.")]
    [SerializeField] private LayerMask _plateLayerMask = ~0;

    // ─── Constants ────────────────────────────────────────────────────────────

    /// <summary>Pool IDs for all plate visuals — mirrors the entries in pool_config.json.</summary>
    private static readonly string[] _platePoolIds =
    {
        "plate_0", "plate_1", "plate_2", "plate_3", "plate_4", "plate_5"
    };

    /// <summary>4-directional offsets (Up, Down, Left, Right) for adjacent-cell search.</summary>
    private static readonly int[] _dr = { -1,  1,  0,  0 };
    private static readonly int[] _dc = {  0,  0, -1,  1 };

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        if (_camera == null) _camera = Camera.main;
    }

    private void Update()
    {
        // Only handle plate-click input while fill mode is active.
        if (_fsm == null || _fsm.CurrentState != _fsm.FillSelecting) return;

        Pointer pointer = Pointer.current;
        if (pointer == null) return;

        if (pointer.press.wasPressedThisFrame)
            HandlePlateClick(pointer.position.ReadValue());
    }

    // ─── Public API (Button OnClick) ──────────────────────────────────────────

    /// <summary>
    /// Called by the Fill Button's OnClick event.
    /// Activates fill mode if idle, or cancels it if already active.
    /// </summary>
    public void OnFillButtonClicked()
    {
        // Already active → cancel.
        if (_fsm != null && _fsm.CurrentState == _fsm.FillSelecting)
        {
            CancelFill();
            return;
        }

        // Guard: don't activate during merge animation or wrong state.
        if (MergeAnimator.Instance != null && MergeAnimator.Instance.IsMergeAnimating) return;
        if (_fsm == null || _fsm.CurrentState != _fsm.Playing) return;

        // Guard: không đủ lượt sử dụng.
        if (PlayerDataManager.Instance == null ||
            PlayerDataManager.Instance.GetPowerUpQty("cutter") <= 0)
        {
            Debug.Log("[FillPowerUp] Không còn Cutter power-up.");
            return;
        }

        _fsm.ChangeState(_fsm.FillSelecting);
        SetButtonHighlight(true);

        Debug.Log("[FillPowerUp] Fill mode activated — select a plate to complete.");
    }

    // ─── Private input handler ────────────────────────────────────────────────

    /// <summary>
    /// Handles a click/tap while fill mode is active.
    /// Validates the selected plate then triggers the fill spawn.
    /// </summary>
    private void HandlePlateClick(Vector2 screenPos)
    {
        Ray ray = _camera.ScreenPointToRay(screenPos);
        if (!Physics.Raycast(ray, out RaycastHit hit, 200f, _plateLayerMask)) return;

        PlateController plate = hit.collider.GetComponentInParent<PlateController>();
        if (plate == null) return;

        // Must be a plate currently placed on the main grid.
        if (plate.GridRow < 0 || plate.GridCol < 0) return;

        // No point filling a plate that is already full.
        int neededSlices = plate.MaxSlices - plate.SliceCount;
        if (neededSlices <= 0)
        {
            Debug.Log("[FillPowerUp] Selected plate is already full — pick a different one.");
            return;
        }

        // Find one of the empty adjacent cells at random.
        if (!TryFindEmptyNeighbor(plate.GridRow, plate.GridCol, out int emptyRow, out int emptyCol))
        {
            Debug.LogWarning("[FillPowerUp] No empty adjacent cell found — fill impossible. Cancelling.");
            CancelFill();
            return;
        }

        SpawnFillPlate(plate, emptyRow, emptyCol, neededSlices);
    }

    // ─── Core spawn logic ─────────────────────────────────────────────────────

    /// <summary>
    /// Spawns a new plate at (<paramref name="emptyRow"/>, <paramref name="emptyCol"/>)
    /// with exactly <paramref name="sliceCount"/> slices of the same type as
    /// <paramref name="targetPlate"/>, then places it on the grid.
    ///
    /// Because the new plate:
    ///   • is adjacent to the target plate, AND
    ///   • shares the same pizza type,
    /// MergeChecker fires automatically, slices fly to the target plate,
    /// and — if the target plate fills up — both plates are dismissed with score.
    /// </summary>
    private void SpawnFillPlate(PlateController targetPlate,
                                int emptyRow, int emptyCol,
                                int sliceCount)
    {
        // Pick a random plate visual from the pool.
        string platePoolId = _platePoolIds[Random.Range(0, _platePoolIds.Length)];
        GameObject plateGo = PoolManager.Instance.Get(platePoolId);

        if (plateGo == null)
        {
            Debug.LogError($"[FillPowerUp] Failed to get plate from pool '{platePoolId}'.");
            CancelFill();
            return;
        }

        PlateController newPlate = plateGo.GetComponent<PlateController>();
        if (newPlate == null)
        {
            Debug.LogError($"[FillPowerUp] Prefab '{platePoolId}' is missing a PlateController component.");
            PoolManager.Instance.Release(platePoolId, plateGo);
            CancelFill();
            return;
        }

        // ── Position plate BEFORE Initialize so CircleGridDrawer slot world ──────
        // positions are calculated relative to the correct world location.
        // (This mirrors the same pattern used in HoldGridManager.SpawnPlateAtSlot.)
        Vector3 cellWorldPos = _mainGrid.GetCellWorldPosition(emptyRow, emptyCol);
        plateGo.transform.SetParent(null);
        plateGo.transform.position = cellWorldPos;

        // ── Initialize with the exact number of slices the target plate is missing ──
        // No filler slices — every slice on this plate is the same type as the target
        // so they all qualify for the standard (Pass 1) merge in MergeChecker.
        newPlate.Initialize(
            pizzaTypeId:    targetPlate.PizzaTypeId,
            poolId:         platePoolId,
            mainSliceCount: sliceCount,
            maxSlices:      targetPlate.MaxSlices);

        // ── Deactivate power-up UI BEFORE PlacePlate ─────────────────────────────
        // This ensures clean FSM transitions when MergeAnimator takes over:
        //   FillSelecting → Playing  (here)
        //   Playing       → Merging  (MergeAnimator.ExecuteMergeSequence)
        //   Merging       → Playing  (MergeAnimator sequence complete)
        SetButtonHighlight(false);
        _fsm.ChangeState(_fsm.Playing);

        // ── Register on grid — fires OnPlatePlaced → MergeChecker → MergeAnimator ──
        bool placed = _mainGrid.PlacePlate(emptyRow, emptyCol, newPlate);

        if (!placed)
        {
            // Cell became occupied between detection and spawn (extremely rare race).
            Debug.LogWarning($"[FillPowerUp] PlacePlate({emptyRow},{emptyCol}) failed — " +
                             "returning fill plate to pool.");
            newPlate.ReturnToPool();
            return;
        }

        // Trừ 1 lượt sử dụng sau khi thực sự đặt thành công.
        PlayerDataManager.Instance?.UsePowerUp("cutter");

        // Đánh dấu đã dùng power-up trong session này (dùng cho achievement #3).
        _fsm.MarkPowerUpUsed();

        Debug.Log($"[FillPowerUp] Fill plate spawned at ({emptyRow},{emptyCol}) — " +
                  $"{sliceCount}× '{targetPlate.PizzaTypeId}' → merge triggered.");
    }

    // ─── Neighbour search ─────────────────────────────────────────────────────

    /// <summary>
    /// Searches all 4 orthogonal neighbours of (<paramref name="row"/>, <paramref name="col"/>)
    /// and returns one empty cell chosen at random, so the spawn position feels varied.
    /// </summary>
    /// <returns>True if at least one empty neighbour exists.</returns>
    private bool TryFindEmptyNeighbor(int row, int col, out int emptyRow, out int emptyCol)
    {
        // Gather all valid empty neighbours first, then pick randomly for variety.
        var candidates = new List<(int r, int c)>();

        for (int i = 0; i < 4; i++)
        {
            int nr = row + _dr[i];
            int nc = col + _dc[i];

            // Skip out-of-bounds cells.
            if (nr < 0 || nr >= _mainGrid.Rows || nc < 0 || nc >= _mainGrid.Columns) continue;

            // Skip occupied cells.
            if (_mainGrid.GetPlateAt(nr, nc) != null) continue;

            candidates.Add((nr, nc));
        }

        if (candidates.Count == 0)
        {
            emptyRow = emptyCol = -1;
            return false;
        }

        var chosen = candidates[Random.Range(0, candidates.Count)];
        emptyRow = chosen.r;
        emptyCol = chosen.c;
        return true;
    }

    // ─── Cancel helper ────────────────────────────────────────────────────────

    /// <summary>Cancels fill mode without spawning anything.</summary>
    private void CancelFill()
    {
        SetButtonHighlight(false);
        _fsm?.ChangeState(_fsm.Playing);

        Debug.Log("[FillPowerUp] Fill power-up cancelled.");
    }

    // ─── UI helper ────────────────────────────────────────────────────────────

    /// <summary>Tints the button image to signal whether fill mode is active.</summary>
    private void SetButtonHighlight(bool isActive)
    {
        if (_buttonImage == null) return;
        _buttonImage.color = isActive ? _activeColor : _normalColor;
    }
}
