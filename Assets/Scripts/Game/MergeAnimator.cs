using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives all merge animations for the game.
///
/// Responsibilities:
///   1. SliceTravel  — the REAL pizza-slice GameObject flies an arc from donor
///                     to receiver; data-transfer (drawer / _sliceCount) happens
///                     entirely inside the coroutine, not before it.
///   2. PlateDismiss — a completed (full) or emptied plate punches up in scale
///                     then shrinks away before being returned to the pool.
///
/// Integration:
///   • MergeChecker calls ExecuteMergeSequence() with a list of MergeOperation
///     structs.  MergeChecker does NOT call TransferSlicesOfType or ReturnToPool;
///     all of that happens here, correctly timed.
///   • DragController queries IsMergeAnimating to block new drags during animation.
///
/// Setup:
///   Attach to any persistent GameObject (e.g. the same one that holds
///   GameStateMachine).  A single instance is required.
/// </summary>
public class MergeAnimator : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────

    public static MergeAnimator Instance { get; private set; }

    // ─── Inspector fields ─────────────────────────────────────────────────────

    [Header("Slice Travel")]
    [Tooltip("Total time (seconds) for one slice to travel from donor to receiver.")]
    [SerializeField] private float _sliceTravelDuration = 0.35f;

    [Tooltip("Peak height of the arc (world units) at the midpoint of travel.")]
    [SerializeField] private float _sliceArcHeight = 1.5f;

    [Tooltip("Extra delay (seconds) added between each successive slice launch.")]
    [SerializeField] private float _sliceStagger = 0.06f;

    [Header("Plate Dismiss")]
    [Tooltip("Scale multiplier at the punch peak (e.g. 1.15 = 15% bigger than normal).")]
    [SerializeField] private float _dismissPunchScale = 1.15f;

    [Tooltip("Duration of the initial punch-up phase.")]
    [SerializeField] private float _dismissPunchDuration = 0.08f;

    [Tooltip("Duration of the shrink-to-zero phase after the punch.")]
    [SerializeField] private float _dismissShrinkDuration = 0.25f;

    // ─── Events (Observer Pattern) ────────────────────────────────────────────

    /// <summary>
    /// Fired each time a receiver plate fills up completely and is dismissed.
    /// Static so ScoreManager can subscribe without holding a direct reference.
    /// </summary>
    public static event System.Action OnPlateCompleted;

    // ─── Public state ─────────────────────────────────────────────────────────

    /// <summary>
    /// True while any merge (slice travel + dismiss) animation is in progress.
    /// DragController uses this to block new drag input during the animation.
    /// </summary>
    public bool IsMergeAnimating { get; private set; }

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ─── Nested data class ────────────────────────────────────────────────────

    /// <summary>
    /// Describes one atomic merge transfer: which plate donates how many slices
    /// of a given type to which receiving plate.
    /// </summary>
    public class MergeOperation
    {
        public PlateController donor;
        public int             donorRow, donorCol;
        public PlateController receiver;
        public int             receiverRow, receiverCol;
        public string          typeId;
        public int             amount;
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Kicks off a sequential list of merge animations.
    /// Each operation is executed in order; the next starts only after the
    /// previous one's slices have all arrived and any dismiss animation has ended.
    /// </summary>
    public void ExecuteMergeSequence(
        List<MergeOperation> operations,
        GridManager          mainGrid,
        GameStateMachine     fsm)
    {
        StartCoroutine(MergeSequenceCoroutine(operations, mainGrid, fsm));
    }

    // ─── Private coroutines ───────────────────────────────────────────────────

    /// <summary>Runs each MergeOperation in sequence, guarded by IsMergeAnimating.</summary>
    private IEnumerator MergeSequenceCoroutine(
        List<MergeOperation> operations,
        GridManager          mainGrid,
        GameStateMachine     fsm)
    {
        IsMergeAnimating = true;
        fsm?.ChangeState(fsm.Merging);

        foreach (MergeOperation op in operations)
        {
            // Re-validate: donor/receiver might have been modified by an earlier op.
            if (op.donor   == null || !op.donor.gameObject.activeInHierarchy)   continue;
            if (op.receiver == null || !op.receiver.gameObject.activeInHierarchy) continue;

            // Recalculate actual transfer amount against the CURRENT plate state.
            int actualAmount = Mathf.Min(
                op.donor.CountSlicesOfType(op.typeId),
                op.receiver.MaxSlices - op.receiver.SliceCount);

            if (actualAmount <= 0)
            {
                Debug.Log($"[MergeAnimator] Op skipped (no slices to transfer): " +
                          $"{op.donor.name} → {op.receiver.name} type='{op.typeId}'");
                continue;
            }

            yield return StartCoroutine(
                SingleMergeCoroutine(op, actualAmount, mainGrid, fsm));
        }

        IsMergeAnimating = false;
        fsm?.ChangeState(fsm.Playing);
        Debug.Log("[MergeAnimator] Merge sequence complete → Playing.");
    }

    /// <summary>
    /// Executes one merge: extracts slices → flies them → accepts them →
    /// optionally dismisses completed plates.
    /// </summary>
    private IEnumerator SingleMergeCoroutine(
        MergeOperation   op,
        int              amount,
        GridManager      mainGrid,
        GameStateMachine fsm)
    {
        // ── 1. Extract real slices from donor ─────────────────────────────────
        // Slices are removed from the donor's CircleGridDrawer and unparented so
        // they can move freely in world space during the animation.
        List<(GameObject go, Vector3 fromPos)> extracted =
            op.donor.ExtractSlicesOfType(op.typeId, amount);

        if (extracted.Count == 0) yield break;

        // ── 2. Peek receiver target positions BEFORE any slice arrives ─────────
        // PeekEmptySlotPositions does NOT modify receiver state — it just tells
        // us where the first N empty slots are so we can aim the flight paths.
        List<Vector3> targetPositions = op.receiver.PeekEmptySlotPositions(extracted.Count);

        // ── 3. Launch staggered slice travel coroutines ───────────────────────
        int arrivedCount = 0;
        int totalCount   = extracted.Count;

        for (int i = 0; i < extracted.Count; i++)
        {
            int     idx   = i; // capture by value for the closure
            Vector3 toPos = idx < targetPositions.Count
                ? targetPositions[idx]
                : op.receiver.transform.position;

            StartCoroutine(SliceTravelCoroutine(
                extracted[idx].go,
                extracted[idx].fromPos,
                toPos,
                idx * _sliceStagger,
                () =>
                {
                    // Accept the real slice into the receiver's drawer.
                    op.receiver.AcceptAnimatedSlice(extracted[idx].go);
                    arrivedCount++;
                }));
        }

        // ── 4. Wait until every slice has arrived ─────────────────────────────
        yield return new WaitUntil(() => arrivedCount >= totalCount);

        // ── 5. Dismiss completed plates ───────────────────────────────────────
        fsm?.ChangeState(fsm.Clearing);

        // Receiver became full → remove from grid, notify score system, then animate dismiss.
        if (op.receiver.IsFull)
        {
            mainGrid.RemovePlate(op.receiverRow, op.receiverCol);

            // Notify ScoreManager (and any other subscribers) that a plate was completed.
            OnPlateCompleted?.Invoke();

            bool done = false;
            StartCoroutine(PlateDismissCoroutine(op.receiver, () =>
            {
                op.receiver.ReturnToPool();
                done = true;
            }));
            yield return new WaitUntil(() => done);
            Debug.Log($"[MergeAnimator] Receiver ({op.receiverRow},{op.receiverCol}) full → dismissed.");
        }

        // Donor became empty → remove from grid, then animate dismiss.
        if (op.donor.gameObject.activeInHierarchy && op.donor.IsEmpty)
        {
            mainGrid.RemovePlate(op.donorRow, op.donorCol);
            bool done = false;
            StartCoroutine(PlateDismissCoroutine(op.donor, () =>
            {
                op.donor.ReturnToPool();
                done = true;
            }));
            yield return new WaitUntil(() => done);
            Debug.Log($"[MergeAnimator] Donor ({op.donorRow},{op.donorCol}) empty → dismissed.");
        }
    }

    /// <summary>
    /// Moves <paramref name="slice"/> from <paramref name="fromPos"/> to
    /// <paramref name="toPos"/> along a parabolic arc, with an optional
    /// <paramref name="delay"/> before launch.
    /// Calls <paramref name="onArrived"/> when the slice reaches its destination.
    /// </summary>
    private IEnumerator SliceTravelCoroutine(
        GameObject    slice,
        Vector3       fromPos,
        Vector3       toPos,
        float         delay,
        System.Action onArrived)
    {
        // Optional stagger delay before this slice launches.
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        // Guard: slice might have been deactivated if pool was recycled.
        if (slice == null || !slice.activeInHierarchy)
        {
            onArrived?.Invoke();
            yield break;
        }

        slice.transform.position = fromPos;

        float elapsed = 0f;
        while (elapsed < _sliceTravelDuration)
        {
            if (slice == null) break; // safety

            elapsed += Time.deltaTime;
            float t       = Mathf.Clamp01(elapsed / _sliceTravelDuration);
            float smoothT = Mathf.SmoothStep(0f, 1f, t);

            // Straight lerp in XZ, plus a sin-based Y arc.
            Vector3 pos = Vector3.Lerp(fromPos, toPos, smoothT);
            pos.y += Mathf.Sin(t * Mathf.PI) * _sliceArcHeight;
            slice.transform.position = pos;

            yield return null;
        }

        if (slice != null)
            slice.transform.position = toPos;

        onArrived?.Invoke();
    }

    /// <summary>
    /// Plays a "punch then shrink" dismiss animation on <paramref name="plate"/>.
    /// Phase 1: scale up to the punch peak over <see cref="_dismissPunchDuration"/>.
    /// Phase 2: shrink to zero with cubic ease-in over <see cref="_dismissShrinkDuration"/>.
    /// Resets the scale to its original value before invoking <paramref name="onComplete"/>
    /// so the plate is in a clean state when returned to the pool.
    /// </summary>
    private IEnumerator PlateDismissCoroutine(PlateController plate, System.Action onComplete)
    {
        if (plate == null) { onComplete?.Invoke(); yield break; }

        // Capture the exact local scale the plate has right now.
        // Because the plate is already parented to the Main Grid (or similar),
        // Unity has already adjusted its localScale so its world scale is correct.
        // We animate relative to this startScale and restore it at the end
        // so it goes back to the pool in a clean state.
        Vector3 startScale  = plate.transform.localScale;
        Vector3 punchTarget = startScale * _dismissPunchScale;

        // Phase 1: punch scale up.
        float elapsed = 0f;
        while (elapsed < _dismissPunchDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / _dismissPunchDuration);
            plate.transform.localScale = Vector3.Lerp(startScale, punchTarget, t);
            yield return null;
        }
        plate.transform.localScale = punchTarget;

        // Phase 2: shrink to zero with cubic ease-in.
        elapsed = 0f;
        while (elapsed < _dismissShrinkDuration)
        {
            elapsed += Time.deltaTime;
            float t     = Mathf.Clamp01(elapsed / _dismissShrinkDuration);
            float eased = 1f - Mathf.Pow(1f - t, 3f); // cubic ease-in
            plate.transform.localScale = Vector3.Lerp(punchTarget, Vector3.zero, eased);
            yield return null;
        }
        plate.transform.localScale = Vector3.zero;

        // Reset to its correct local scale BEFORE pool return.
        plate.transform.localScale = startScale;
        onComplete?.Invoke();
    }
}
