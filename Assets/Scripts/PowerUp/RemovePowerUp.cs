using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Power-up: "Remove" — silently discards one plate from the Main Grid.
///
/// Flow:
///   1. Player activates the button → button highlights red, FSM → RemoveSelectingState.
///   2. Player clicks any plate on the main grid.
///   3. The plate plays the standard dismiss animation (punch-then-shrink),
///      then is returned to the object pool.
///   4. NO score is awarded — MergeAnimator.OnPlateCompleted is NOT fired.
///   5. FSM returns to Playing; button returns to normal colour.
///
/// Cancel:
///   Pressing the button again while active (before selecting a plate) cancels.
///
/// Design rationale:
///   Reuses MergeAnimator.RemovePlateWithAnimation() so the dismiss animation
///   is identical to a normal plate completion, preserving visual consistency
///   while bypassing the score event.
///
/// Setup:
///   - Attach to any persistent GameObject.
///   - Assign all SerializeField references in the Inspector.
///   - Wire the Remove Button's OnClick → OnRemoveButtonClicked().
///   - Every plate prefab must have a Collider for raycast detection.
/// </summary>
public class RemovePowerUp : MonoBehaviour
{
    // ─── Inspector fields ─────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("Main camera used for raycasting plate selection.")]
    [SerializeField] private Camera _camera;

    [Tooltip("The main GridManager where plates live.")]
    [SerializeField] private GridManager _mainGrid;

    [Tooltip("The game state machine used to signal state transitions.")]
    [SerializeField] private GameStateMachine _fsm;

    [Tooltip("The UI Image component of the Remove Button (for colour highlight).")]
    [SerializeField] private Image _buttonImage;

    [Header("Highlight Colors")]
    [Tooltip("Button image colour when remove mode is active.")]
    [SerializeField] private Color _activeColor = new Color(0.92f, 0.22f, 0.22f, 1f); // vibrant red

    [Tooltip("Button image colour when remove mode is inactive.")]
    [SerializeField] private Color _normalColor = Color.white;

    [Header("Input")]
    [Tooltip("Layer mask for detecting plate colliders during remove selection.")]
    [SerializeField] private LayerMask _plateLayerMask = ~0;

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        if (_camera == null) _camera = Camera.main;
    }

    private void Update()
    {
        // Only handle plate-click input while remove mode is active.
        if (_fsm == null || _fsm.CurrentState != _fsm.RemoveSelecting) return;

        // Do not process clicks while MergeAnimator is running a dismiss.
        if (MergeAnimator.Instance != null && MergeAnimator.Instance.IsMergeAnimating) return;

        Pointer pointer = Pointer.current;
        if (pointer == null) return;

        if (pointer.press.wasPressedThisFrame)
            HandlePlateClick(pointer.position.ReadValue());
    }

    // ─── Public API (Button OnClick) ──────────────────────────────────────────

    /// <summary>
    /// Called by the Remove Button's OnClick event.
    /// Activates remove mode if idle, or cancels it if already active.
    /// </summary>
    public void OnRemoveButtonClicked()
    {
        // Already active → cancel.
        if (_fsm != null && _fsm.CurrentState == _fsm.RemoveSelecting)
        {
            CancelRemove();
            return;
        }

        // Guard: don't activate during animations or wrong FSM state.
        if (MergeAnimator.Instance != null && MergeAnimator.Instance.IsMergeAnimating) return;
        if (_fsm == null || _fsm.CurrentState != _fsm.Playing) return;

        _fsm.ChangeState(_fsm.RemoveSelecting);
        SetButtonHighlight(true);

        Debug.Log("[RemovePowerUp] Remove mode activated — select a plate to discard.");
    }

    // ─── Private input handler ────────────────────────────────────────────────

    /// <summary>
    /// Handles a click/tap while remove mode is active.
    /// Validates the target plate then delegates to MergeAnimator for the dismiss animation.
    /// </summary>
    private void HandlePlateClick(Vector2 screenPos)
    {
        Ray ray = _camera.ScreenPointToRay(screenPos);
        if (!Physics.Raycast(ray, out RaycastHit hit, 200f, _plateLayerMask)) return;

        PlateController plate = hit.collider.GetComponentInParent<PlateController>();
        if (plate == null) return;

        // Only plates currently registered on the main grid are valid targets.
        if (plate.GridRow < 0 || plate.GridCol < 0) return;

        RemovePlate(plate);
    }

    // ─── Core removal logic ───────────────────────────────────────────────────

    /// <summary>
    /// Deactivates the power-up UI then hands the plate to
    /// <see cref="MergeAnimator.RemovePlateWithAnimation"/> for dismiss + pool return.
    ///
    /// The power-up UI is cleared BEFORE calling MergeAnimator so that FSM
    /// transitions are clean:
    ///   RemoveSelecting → Playing  (here)
    ///   Playing         → Clearing (MergeAnimator.RemovePlateCoroutine)
    ///   Clearing        → Playing  (MergeAnimator.RemovePlateCoroutine done)
    /// </summary>
    private void RemovePlate(PlateController plate)
    {
        if (MergeAnimator.Instance == null)
        {
            Debug.LogError("[RemovePowerUp] MergeAnimator.Instance is null — " +
                           "add MergeAnimator to the scene.");
            CancelRemove();
            return;
        }

        // Deactivate power-up first so the FSM is clean when MergeAnimator takes over.
        SetButtonHighlight(false);
        _fsm.ChangeState(_fsm.Playing);

        // Delegate to MergeAnimator — reuses the dismiss animation without scoring.
        MergeAnimator.Instance.RemovePlateWithAnimation(plate, _mainGrid, _fsm);

        Debug.Log($"[RemovePowerUp] Plate '{plate.PizzaTypeId}' at " +
                  $"({plate.GridRow},{plate.GridCol}) queued for silent removal.");
    }

    // ─── Cancel helper ────────────────────────────────────────────────────────

    /// <summary>Cancels remove mode without touching any plate.</summary>
    private void CancelRemove()
    {
        SetButtonHighlight(false);
        _fsm?.ChangeState(_fsm.Playing);

        Debug.Log("[RemovePowerUp] Remove power-up cancelled.");
    }

    // ─── UI helper ────────────────────────────────────────────────────────────

    /// <summary>Tints the button image to signal whether remove mode is active.</summary>
    private void SetButtonHighlight(bool isActive)
    {
        if (_buttonImage == null) return;
        _buttonImage.color = isActive ? _activeColor : _normalColor;
    }
}
