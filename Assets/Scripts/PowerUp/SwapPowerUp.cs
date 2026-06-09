using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Power-up that lets the player swap two plates on the Main Grid.
///
/// Flow:
///   1. Player taps the Swap button → button highlights blue, FSM → SwapSelectingState.
///   2. Player taps any plate on the main grid → plate lifts on the Y axis.
///   3. Player taps a second (different) plate → both plates fly to each other's
///      ground positions, then settle back down. FSM → Playing.
///   4. Tapping the button again while 0 or 1 plate is selected → cancel swap,
///      any lifted plate lowers back, FSM → Playing.
///
/// Setup:
///   - Attach this component to the Swap Button GameObject (or any persistent GO).
///   - Assign all SerializeField references in the Inspector.
///   - Wire the Swap Button's OnClick event to OnSwapButtonClicked().
///   - Every plate prefab must have a Collider for raycast detection.
/// </summary>
public class SwapPowerUp : MonoBehaviour
{
    // ─── Inspector fields ─────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("Main camera used for raycasting plate selection.")]
    [SerializeField] private Camera _camera;

    [Tooltip("The main GridManager where plates are placed.")]
    [SerializeField] private GridManager _mainGrid;

    [Tooltip("The game state machine used to signal state transitions.")]
    [SerializeField] private GameStateMachine _fsm;

    [Tooltip("The UI Image component of the Swap Button (for colour highlight).")]
    [SerializeField] private Image _buttonImage;

    [Header("Highlight Colors")]
    [Tooltip("Button image colour when swap mode is active.")]
    [SerializeField] private Color _activeColor = new Color(0.18f, 0.67f, 1f, 1f);

    [Tooltip("Button image colour when swap mode is inactive (normal state).")]
    [SerializeField] private Color _normalColor = Color.white;

    [Header("Animation Settings")]
    [Tooltip("How high (world units) a selected plate is lifted above its grid position.")]
    [SerializeField] private float _liftHeight = 1.2f;

    [Tooltip("Duration (seconds) of the lift / lower animations.")]
    [SerializeField] private float _liftDuration = 0.18f;

    [Tooltip("Duration (seconds) of the horizontal swap travel animation.")]
    [SerializeField] private float _swapDuration = 0.38f;

    [Header("Input")]
    [Tooltip("Layer mask for detecting plate colliders during swap selection.")]
    [SerializeField] private LayerMask _plateLayerMask = ~0;

    // ─── Private state ────────────────────────────────────────────────────────

    /// <summary>First plate the player selected (lifted and waiting for second pick).</summary>
    private PlateController _firstPlate;

    /// <summary>Ground-level world position of the first plate before it was lifted.</summary>
    private Vector3 _firstGroundPos;

    /// <summary>Grid coordinates of the first plate at selection time.</summary>
    private int _firstRow, _firstCol;

    /// <summary>True while a lift / swap / lower coroutine is running — blocks new clicks.</summary>
    private bool _isAnimating;

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        if (_camera == null) _camera = Camera.main;
    }

    private void Update()
    {
        // Only handle plate-click input while the swap power-up is active.
        if (_fsm == null || _fsm.CurrentState != _fsm.SwapSelecting) return;

        // Block clicks while an animation is already in progress.
        if (_isAnimating) return;

        // ── Read pointer input (unified mouse + touch via New Input System) ──────
        Pointer pointer = Pointer.current;
        if (pointer == null) return;

        if (pointer.press.wasPressedThisFrame)
            HandlePlateClick(pointer.position.ReadValue());
    }

    // ─── Public API (Button OnClick) ──────────────────────────────────────────

    /// <summary>
    /// Called by the Swap Button's OnClick event in the Inspector.
    /// Activates swap mode if idle, or cancels it if already active.
    /// </summary>
    public void OnSwapButtonClicked()
    {
        // ── If already in swap mode → cancel ─────────────────────────────────────────
        if (_fsm != null && _fsm.CurrentState == _fsm.SwapSelecting)
        {
            CancelSwap();
            return;
        }

        // ── Guard: don't activate during merge animations or wrong FSM state ──
        if (MergeAnimator.Instance != null && MergeAnimator.Instance.IsMergeAnimating) return;
        if (_fsm == null || _fsm.CurrentState != _fsm.Playing) return;

        // ── Guard: không đủ lượt sử dụng ───────────────────────────────────────────
        if (PlayerDataManager.Instance == null ||
            PlayerDataManager.Instance.GetPowerUpQty("swap") <= 0)
        {
            Debug.Log("[SwapPowerUp] Không còn Swap power-up.");
            return;
        }

        // ── Enter swap-selecting mode ─────────────────────────────────────────────
        _fsm.ChangeState(_fsm.SwapSelecting);
        SetButtonHighlight(true);

        Debug.Log("[SwapPowerUp] Swap mode activated — select two plates.");
    }

    // ─── Private input handler ────────────────────────────────────────────────

    /// <summary>
    /// Handles a click/tap while swap mode is active.
    /// Selects the first plate (lifts it) or the second plate (executes swap).
    /// </summary>
    private void HandlePlateClick(Vector2 screenPos)
    {
        Ray ray = _camera.ScreenPointToRay(screenPos);

        if (!Physics.Raycast(ray, out RaycastHit hit, 200f, _plateLayerMask)) return;

        PlateController plate = hit.collider.GetComponentInParent<PlateController>();
        if (plate == null) return;

        // Only plates currently registered on the main grid are valid targets.
        if (plate.GridRow < 0 || plate.GridCol < 0) return;

        if (_firstPlate == null)
        {
            // ── Select first plate ────────────────────────────────────────────
            _firstPlate    = plate;
            _firstGroundPos = plate.transform.position;
            _firstRow       = plate.GridRow;
            _firstCol       = plate.GridCol;

            StartCoroutine(LiftPlate(plate, _firstGroundPos + Vector3.up * _liftHeight));

            Debug.Log($"[SwapPowerUp] First plate selected at ({_firstRow},{_firstCol}).");
        }
        else
        {
            // Prevent selecting the same plate twice.
            if (plate == _firstPlate) return;

            // ── Select second plate → execute swap ────────────────────────────
            int     secondRow      = plate.GridRow;
            int     secondCol      = plate.GridCol;
            Vector3 secondGroundPos = plate.transform.position;

            StartCoroutine(ExecuteSwap(
                _firstPlate,  _firstRow,  _firstCol,  _firstGroundPos,
                plate,        secondRow,  secondCol,  secondGroundPos));
        }
    }

    // ─── Swap coroutine ───────────────────────────────────────────────────────

    /// <summary>
    /// Full swap sequence:
    ///   1. Lift second plate.
    ///   2. Fly both plates to each other's lifted target positions.
    ///   3. Lower both plates to their new ground positions.
    ///   4. Update grid data.
    ///   5. Return to Playing state.
    /// </summary>
    private IEnumerator ExecuteSwap(
        PlateController plateA, int rowA, int colA, Vector3 groundA,
        PlateController plateB, int rowB, int colB, Vector3 groundB)
    {
        _isAnimating = true;

        // ── Step 1: Lift second plate to match the first ──────────────────────
        yield return StartCoroutine(LiftPlate(plateB, groundB + Vector3.up * _liftHeight));

        // ── Step 2: Remove both from the grid BEFORE moving them ─────────────
        // This prevents MergeChecker from reacting mid-animation.
        _mainGrid.RemovePlate(rowA, colA);
        _mainGrid.RemovePlate(rowB, colB);

        // Capture current lifted positions as animation start points.
        Vector3 liftedA = plateA.transform.position; // groundA + liftHeight
        Vector3 liftedB = plateB.transform.position; // groundB + liftHeight

        // Targets: each plate flies horizontally to the other's lifted position.
        Vector3 targetLiftedA = groundB + Vector3.up * _liftHeight;
        Vector3 targetLiftedB = groundA + Vector3.up * _liftHeight;

        // ── Step 3: Horizontal swap travel ───────────────────────────────────
        float elapsed = 0f;
        while (elapsed < _swapDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / _swapDuration));
            plateA.transform.position = Vector3.Lerp(liftedA, targetLiftedA, t);
            plateB.transform.position = Vector3.Lerp(liftedB, targetLiftedB, t);
            yield return null;
        }
        // Clamp to exact targets to avoid floating-point drift.
        plateA.transform.position = targetLiftedA;
        plateB.transform.position = targetLiftedB;

        // ── Step 4: Lower both plates to new ground positions simultaneously ──
        float lowerElapsed   = 0f;
        Vector3 startLowerA = plateA.transform.position; // = targetLiftedA
        Vector3 startLowerB = plateB.transform.position; // = targetLiftedB
        Vector3 finalGroundA = groundB; // A lands where B originally was
        Vector3 finalGroundB = groundA; // B lands where A originally was

        while (lowerElapsed < _liftDuration)
        {
            lowerElapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(lowerElapsed / _liftDuration));
            plateA.transform.position = Vector3.Lerp(startLowerA, finalGroundA, t);
            plateB.transform.position = Vector3.Lerp(startLowerB, finalGroundB, t);
            yield return null;
        }
        plateA.transform.position = finalGroundA;
        plateB.transform.position = finalGroundB;

        // ── Step 5: Re-register swapped plates on the grid ───────────────────
        // PlacePlate updates each plate's GridRow/GridCol and fires OnPlatePlaced,
        // which may trigger MergeChecker. This is intentional — a smart swap can
        // set up valid merges as a reward for the player.
        _mainGrid.PlacePlate(rowB, colB, plateA); // A now lives at B's old cell
        _mainGrid.PlacePlate(rowA, colA, plateB); // B now lives at A's old cell

        Debug.Log($"[SwapPowerUp] Swap complete: ({rowA},{colA}) ↔ ({rowB},{colB}).");

        // ── Step 6: Cleanup ─────────────────────────────────────────────────────────────
        _firstPlate  = null;
        _isAnimating = false;

        // Trừ 1 lượt sử dụng sau khi swap thành công.
        PlayerDataManager.Instance?.UsePowerUp("swap");

        SetButtonHighlight(false);

        // Return to Playing. If MergeAnimator was triggered by PlacePlate above,
        // it will override this to Merging then back to Playing automatically.
        _fsm.ChangeState(_fsm.Playing);
    }

    // ─── Cancel helper ────────────────────────────────────────────────────────

    /// <summary>
    /// Cancels swap mode. If a plate was already lifted, it smoothly lowers back.
    /// </summary>
    private void CancelSwap()
    {
        if (_firstPlate != null)
            StartCoroutine(LowerPlate(_firstPlate, _firstGroundPos));

        _firstPlate  = null;
        _isAnimating = false;

        SetButtonHighlight(false);
        _fsm?.ChangeState(_fsm.Playing);

        Debug.Log("[SwapPowerUp] Swap cancelled.");
    }

    // ─── Animation helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Smoothly moves <paramref name="plate"/> from its current position to
    /// <paramref name="targetPos"/> over <see cref="_liftDuration"/> seconds.
    /// Sets <see cref="_isAnimating"/> for the duration.
    /// </summary>
    private IEnumerator LiftPlate(PlateController plate, Vector3 targetPos)
    {
        _isAnimating = true;

        Vector3 start   = plate.transform.position;
        float   elapsed = 0f;

        while (elapsed < _liftDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / _liftDuration));
            plate.transform.position = Vector3.Lerp(start, targetPos, t);
            yield return null;
        }

        plate.transform.position = targetPos;
        _isAnimating = false;
    }

    /// <summary>
    /// Smoothly moves <paramref name="plate"/> back to its original ground position.
    /// Used exclusively by <see cref="CancelSwap"/> to restore a lifted plate.
    /// </summary>
    private IEnumerator LowerPlate(PlateController plate, Vector3 groundPos)
    {
        Vector3 start   = plate.transform.position;
        float   elapsed = 0f;

        while (elapsed < _liftDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / _liftDuration));
            plate.transform.position = Vector3.Lerp(start, groundPos, t);
            yield return null;
        }

        plate.transform.position = groundPos;
    }

    // ─── UI helper ────────────────────────────────────────────────────────────

    /// <summary>Tints the button image to signal whether swap mode is active.</summary>
    private void SetButtonHighlight(bool isActive)
    {
        if (_buttonImage == null) return;
        _buttonImage.color = isActive ? _activeColor : _normalColor;
    }
}
