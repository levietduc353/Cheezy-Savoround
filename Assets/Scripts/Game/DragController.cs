using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Handles drag-and-drop input for moving plates from the Hold Grid to the Main Grid.
///
/// Flow:
///   1. Player presses on a plate in the hold grid.
///   2. Plate is lifted to _dragHeightY and follows the cursor/touch.
///   3. On release:
///      a. Dropped near a valid main grid cell → smooth snap to cell center → PlacePlate.
///      b. Dropped outside grid → smooth snap back to original hold slot.
///
/// Setup:
///   - Attach this component to a GameObject (e.g. "GameController").
///   - Assign all SerializeField references in the Inspector.
///   - Every plate prefab must have a Collider for raycast detection.
///   - Set _draggableLayerMask to the plate layer for best performance.
/// </summary>
public class DragController : MonoBehaviour
{
    // ─── Inspector fields ─────────────────────────────────────────────────────

    [Header("References")]
    [SerializeField] private Camera           _camera;
    [SerializeField] private GridManager      _mainGrid;
    [SerializeField] private HoldGridManager  _holdGrid;
    [SerializeField] private GameStateMachine _fsm;

    [Header("Drag Settings")]
    [Tooltip("Y position of the plate while being dragged (slightly above the grid).")]
    [SerializeField] private float _dragHeightY = 0.5f;

    [Tooltip("Fraction of cellSize used as snap radius when dropping on main grid. 0.5 = half cell.")]
    [SerializeField] private float _snapRadiusMultiplier = 0.5f;

    [Tooltip("Duration (seconds) of snap-to-cell and snap-back animations.")]
    [SerializeField] private float _snapDuration = 0.15f;

    [Tooltip("Layer mask for detecting plate colliders. Leave as 'Everything' if no dedicated layer.")]
    [SerializeField] private LayerMask _draggableLayerMask = ~0;

    // ─── Private state ────────────────────────────────────────────────────────

    private PlateController _draggedPlate;
    private Transform       _draggedPlateOriginalParent;
    private Vector3         _draggedPlateOriginalPosition;
    private int             _draggedHoldSlotIndex = -1;

    private Plane _dragPlane;
    private bool  _isDragging;
    private bool  _isAnimating; // true while snap coroutine is running — blocks new input

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        if (_camera == null) _camera = Camera.main;
    }

    private void Update()
    {
        // Block all input while a snap animation is in progress.
        if (_isAnimating) return;

        // ── Read New Input System (Unified Pointer for Mouse + Touch) ─────────────
        bool    pressed  = false;
        bool    held     = false;
        bool    released = false;
        Vector2 inputPos = Vector2.zero;

        Pointer pointer = Pointer.current;
        if (pointer != null)
        {
            inputPos = pointer.position.ReadValue();
            pressed  = pointer.press.wasPressedThisFrame;
            held     = pointer.press.isPressed;
            released = pointer.press.wasReleasedThisFrame;
        }

        if (pressed)                HandlePress(inputPos);
        if (held     && _isDragging) HandleDrag(inputPos);
        if (released && _isDragging) HandleRelease(inputPos);
    }

    // ─── Private input handlers ───────────────────────────────────────────────

    /// <summary>Detects a hold-grid plate under the press and begins dragging.</summary>
    private void HandlePress(Vector2 screenPos)
    {
        // Block new drag input while merge animations are running to prevent
        // race conditions between concurrent animation coroutines.
        if (MergeAnimator.Instance != null && MergeAnimator.Instance.IsMergeAnimating) return;

        // Block drag input while any power-up is in the middle of plate selection,
        // or while the game is in Game Over state.
        if (_fsm != null && (_fsm.CurrentState == _fsm.SwapSelecting   ||
                             _fsm.CurrentState == _fsm.FillSelecting   ||
                             _fsm.CurrentState == _fsm.RemoveSelecting ||
                             _fsm.CurrentState == _fsm.UnifySelecting  ||
                             _fsm.CurrentState == _fsm.GameOver)) return;

        Ray ray = _camera.ScreenPointToRay(screenPos);

        if (!Physics.Raycast(ray, out RaycastHit hit, 200f, _draggableLayerMask)) return;

        PlateController plate = hit.collider.GetComponentInParent<PlateController>();
        if (plate == null) return;

        int slotIndex = _holdGrid.GetHoldSlotIndex(plate);
        if (slotIndex < 0) return;

        // ── Begin drag ────────────────────────────────────────────────────────
        _draggedPlate                 = plate;
        _draggedHoldSlotIndex         = slotIndex;
        _draggedPlateOriginalParent   = plate.transform.parent;
        _draggedPlateOriginalPosition = plate.transform.position;

        _dragPlane  = new Plane(Vector3.up, new Vector3(0f, _dragHeightY, 0f));
        _isDragging = true;

        plate.transform.SetParent(null);
        _fsm?.ChangeState(_fsm.Dragging);

        Debug.Log($"[DragController] Dragging '{plate.PizzaTypeId}' from hold slot {slotIndex}.");
    }

    /// <summary>Moves the dragged plate along the drag plane to follow the cursor.</summary>
    private void HandleDrag(Vector2 screenPos)
    {
        _draggedPlate.transform.position = ScreenToWorldOnDragPlane(screenPos);
    }

    /// <summary>
    /// On release: snaps to the nearest valid main grid cell, or snaps back to hold.
    /// </summary>
    private void HandleRelease(Vector2 screenPos)
    {
        _isDragging = false;

        Vector3 dropPos = ScreenToWorldOnDragPlane(screenPos);

        if (TryFindDropCell(dropPos, out int row, out int col))
        {
            Vector3 cellCenter = _mainGrid.GetCellWorldPosition(row, col);
            StartCoroutine(SnapToMainGrid(_draggedPlate, cellCenter, row, col));
        }
        else
        {
            // Snap back to the exact center of its original hold slot.
            Vector3 holdSlotPos = _holdGrid.GetSlotWorldPosition(_draggedHoldSlotIndex);
            StartCoroutine(SnapBackToHold(_draggedPlate,
                                          _draggedPlateOriginalParent,
                                          holdSlotPos));
        }

        // Clear drag references — the coroutine holds its own local copies.
        _draggedPlate         = null;
        _draggedHoldSlotIndex = -1;
    }

    // ─── Snap coroutines ──────────────────────────────────────────────────────

    /// <summary>
    /// Smoothly moves <paramref name="plate"/> to <paramref name="cellCenter"/>,
    /// then registers it on the main grid and notifies the hold grid.
    /// </summary>
    private IEnumerator SnapToMainGrid(PlateController plate, Vector3 cellCenter, int row, int col)
    {
        _isAnimating = true;

        // Smooth animation to cell center.
        yield return StartCoroutine(SmoothMove(plate.transform, cellCenter));

        // Register on main grid — fires OnPlatePlaced → MergeChecker reacts.
        bool placed = _mainGrid.PlacePlate(row, col, plate);

        if (placed)
        {
            // Remove slot from hold (may trigger refill if all 4 gone).
            _holdGrid.NotifyPlateDragged(plate);
            _fsm?.ChangeState(_fsm.Playing);
            Debug.Log($"[DragController] Plate placed at main grid ({row},{col}).");
        }
        else
        {
            // Cell was taken between detection and snap — fall back to hold.
            Debug.LogWarning($"[DragController] PlacePlate({row},{col}) failed — snapping back.");
            yield return StartCoroutine(SmoothMove(plate.transform, _draggedPlateOriginalPosition));
            plate.transform.SetParent(_draggedPlateOriginalParent);
            _fsm?.ChangeState(_fsm.Playing);
        }

        _isAnimating = false;
    }

    /// <summary>
    /// Smoothly moves <paramref name="plate"/> back to its hold slot position.
    /// </summary>
    private IEnumerator SnapBackToHold(PlateController plate, Transform originalParent, Vector3 originalPos)
    {
        _isAnimating = true;

        yield return StartCoroutine(SmoothMove(plate.transform, originalPos));

        // Re-parent so the plate is once again under HoldGridManager's transform.
        plate.transform.SetParent(originalParent);
        plate.transform.position = originalPos; // Clamp floating-point drift.

        _fsm?.ChangeState(_fsm.Playing);
        Debug.Log("[DragController] Plate snapped back to hold.");

        _isAnimating = false;
    }

    /// <summary>
    /// Coroutine that lerps <paramref name="target"/> from its current position to
    /// <paramref name="destination"/> over <see cref="_snapDuration"/> seconds using SmoothStep.
    /// </summary>
    private IEnumerator SmoothMove(Transform target, Vector3 destination)
    {
        Vector3 start   = target.position;
        float   elapsed = 0f;

        while (elapsed < _snapDuration)
        {
            elapsed += Time.deltaTime;
            // SmoothStep gives an ease-in/ease-out curve.
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / _snapDuration));
            target.position = Vector3.Lerp(start, destination, t);
            yield return null;
        }

        target.position = destination; // Guarantee exact final position.
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Finds the nearest empty main grid cell to <paramref name="worldPos"/>.
    /// Iterates through all cells and finds the one whose center is closest.
    /// </summary>
    private bool TryFindDropCell(Vector3 worldPos, out int row, out int col)
    {
        row = col = -1;
        float closestDist = float.MaxValue;

        // Iterate through all cells to find the closest center
        for (int r = 0; r < _mainGrid.Rows; r++)
        {
            for (int c = 0; c < _mainGrid.Columns; c++)
            {
                if (_mainGrid.GetPlateAt(r, c) != null) continue; // Skip occupied cells

                Vector3 cellCenter = _mainGrid.GetCellWorldPosition(r, c);

                // Calculate distance on the XZ plane
                float dist = new Vector2(worldPos.x - cellCenter.x, worldPos.z - cellCenter.z).magnitude;

                if (dist < closestDist)
                {
                    closestDist = dist;
                    row = r;
                    col = c;
                }
            }
        }

        // A cell is considered a valid drop target if the drop position is within
        // a reasonable radius (e.g. 75% of cell size to cover corners and edges).
        // Since CellSize is the width/height of the cell, 0.75f gives a nice forgiving catch area.
        float maxDropRadius = _mainGrid.CellSize * 0.75f;

        if (row >= 0 && closestDist <= maxDropRadius)
        {
            return true;
        }

        row = col = -1;
        return false;
    }

    /// <summary>Projects screen position onto the horizontal drag plane and returns world position.</summary>
    private Vector3 ScreenToWorldOnDragPlane(Vector2 screenPos)
    {
        Ray ray = _camera.ScreenPointToRay(screenPos);
        _dragPlane.Raycast(ray, out float enter);
        return ray.GetPoint(enter);
    }
}
