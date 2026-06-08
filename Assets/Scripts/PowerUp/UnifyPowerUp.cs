using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Power-up: "Unify" — converts all minority-type slices on a selected plate to the
/// plate's dominant pizza type, producing a single-type plate in one tap.
///
/// Rules:
///   • Dominant type = the pizza type that appears most often on the plate.
///   • On tie, the first type encountered in slot order wins (stable, predictable).
///   • Plate must contain MORE than one distinct pizza type — single-type plates are
///     an invalid selection (click is ignored; power-up stays active).
///
/// Flow:
///   1. Player activates the button → button highlights purple, FSM → UnifySelectingState.
///   2. Player clicks a plate on the main grid.
///        a. Single-type plate → invalid; log a hint, keep power-up active.
///        b. Multi-type plate  → determine dominant type, convert minority slices
///           (slot-for-slot swap via the pool), update PizzaTypeId, deactivate power-up.
///   3. Pressing the button again while active → cancel.
///
/// Design notes:
///   • Conversion is instant (no flying animation): each minority slice GO is returned to
///     its pool and replaced with a fresh slice GO of the dominant type in the same slot.
///   • _sliceCount is unchanged for successful swaps (1-for-1 exchange).
///   • After unification the plate is now a clean single-type plate; neighbouring plates
///     of the same type will merge on the NEXT normal placement, not automatically.
///
/// Setup:
///   - Attach to any persistent GameObject.
///   - Assign all SerializeField references in the Inspector.
///   - Wire the Unify Button's OnClick → OnUnifyButtonClicked().
///   - Every plate prefab must have a Collider for raycast detection.
/// </summary>
public class UnifyPowerUp : MonoBehaviour
{
    // ─── Inspector fields ─────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("Main camera used for raycasting plate selection.")]
    [SerializeField] private Camera _camera;

    [Tooltip("The main GridManager where plates live.")]
    [SerializeField] private GridManager _mainGrid;

    [Tooltip("The game state machine used to signal state transitions.")]
    [SerializeField] private GameStateMachine _fsm;

    [Tooltip("The UI Image component of the Unify Button (for colour highlight).")]
    [SerializeField] private Image _buttonImage;

    [Header("Highlight Colors")]
    [Tooltip("Button image colour when unify mode is active.")]
    [SerializeField] private Color _activeColor = new Color(0.60f, 0.18f, 1.00f, 1f); // vivid purple

    [Tooltip("Button image colour when unify mode is inactive.")]
    [SerializeField] private Color _normalColor = Color.white;

    [Header("Input")]
    [Tooltip("Layer mask for detecting plate colliders during unify selection.")]
    [SerializeField] private LayerMask _plateLayerMask = ~0;

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        if (_camera == null) _camera = Camera.main;
    }

    private void Update()
    {
        // Only handle plate-click input while unify mode is active.
        if (_fsm == null || _fsm.CurrentState != _fsm.UnifySelecting) return;

        // Respect merge animations already in progress.
        if (MergeAnimator.Instance != null && MergeAnimator.Instance.IsMergeAnimating) return;

        Pointer pointer = Pointer.current;
        if (pointer == null) return;

        if (pointer.press.wasPressedThisFrame)
            HandlePlateClick(pointer.position.ReadValue());
    }

    // ─── Public API (Button OnClick) ──────────────────────────────────────────

    /// <summary>
    /// Called by the Unify Button's OnClick event.
    /// Activates unify mode if idle, or cancels it if already active.
    /// </summary>
    public void OnUnifyButtonClicked()
    {
        // Already active → cancel.
        if (_fsm != null && _fsm.CurrentState == _fsm.UnifySelecting)
        {
            CancelUnify();
            return;
        }

        // Guard: don't activate during animations or wrong FSM state.
        if (MergeAnimator.Instance != null && MergeAnimator.Instance.IsMergeAnimating) return;
        if (_fsm == null || _fsm.CurrentState != _fsm.Playing) return;

        _fsm.ChangeState(_fsm.UnifySelecting);
        SetButtonHighlight(true);

        Debug.Log("[UnifyPowerUp] Unify mode activated — select a mixed plate.");
    }

    // ─── Private input handler ────────────────────────────────────────────────

    /// <summary>
    /// Handles a click/tap while unify mode is active.
    ///   • Invalid click (single-type plate, no plate hit) → ignored; power-up stays active.
    ///   • Valid click (multi-type plate) → unify, then deactivate power-up.
    /// </summary>
    private void HandlePlateClick(Vector2 screenPos)
    {
        Ray ray = _camera.ScreenPointToRay(screenPos);
        if (!Physics.Raycast(ray, out RaycastHit hit, 200f, _plateLayerMask)) return;

        PlateController plate = hit.collider.GetComponentInParent<PlateController>();
        if (plate == null) return;

        // Only plates currently registered on the main grid are valid targets.
        if (plate.GridRow < 0 || plate.GridCol < 0) return;

        // ── Validation: must have more than one distinct pizza type ───────────
        if (!plate.HasMultipleSliceTypes())
        {
            Debug.Log("[UnifyPowerUp] Selected plate already has only one slice type — " +
                      "pick a mixed plate.");
            // Keep power-up active so the player can try a different plate.
            return;
        }

        ExecuteUnify(plate);
    }

    // ─── Core unify logic ─────────────────────────────────────────────────────

    /// <summary>
    /// Determines the dominant pizza type on <paramref name="plate"/>, converts all
    /// minority slices in-place (same slot, different pool object), updates PizzaTypeId,
    /// then deactivates the power-up.
    /// </summary>
    private void ExecuteUnify(PlateController plate)
    {
        // ── Find dominant type ────────────────────────────────────────────────
        // GetDominantSliceType() uses first-found as stable tiebreaker, so the
        // behaviour is deterministic and matches the design spec.
        string dominantType = plate.GetDominantSliceType();

        if (string.IsNullOrEmpty(dominantType))
        {
            Debug.LogWarning("[UnifyPowerUp] GetDominantSliceType() returned null — " +
                             "plate appears empty. Cancelling.");
            CancelUnify();
            return;
        }

        // ── Convert minority slices ───────────────────────────────────────────
        int converted = plate.UnifySlicesToType(dominantType);

        Debug.Log($"[UnifyPowerUp] Plate at ({plate.GridRow},{plate.GridCol}) unified to " +
                  $"'{dominantType}' — {converted} slice(s) converted.");

        // ── Deactivate power-up ───────────────────────────────────────────────
        SetButtonHighlight(false);
        _fsm.ChangeState(_fsm.Playing);
    }

    // ─── Cancel helper ────────────────────────────────────────────────────────

    /// <summary>Cancels unify mode without modifying any plate.</summary>
    private void CancelUnify()
    {
        SetButtonHighlight(false);
        _fsm?.ChangeState(_fsm.Playing);

        Debug.Log("[UnifyPowerUp] Unify power-up cancelled.");
    }

    // ─── UI helper ────────────────────────────────────────────────────────────

    /// <summary>Tints the button image to signal whether unify mode is active.</summary>
    private void SetButtonHighlight(bool isActive)
    {
        if (_buttonImage == null) return;
        _buttonImage.color = isActive ? _activeColor : _normalColor;
    }
}
