using UnityEngine;

/// <summary>
/// Manages the game's Finite State Machine.
/// Holds one instance of each concrete state and handles transitions.
///
/// States:
///   PlayingState        — waiting for player input
///   DraggingState       — a plate is being dragged
///   CheckingMergeState  — checking 4-directional neighbors for same pizza type
///   MergingState        — transferring slices between plates
///   ClearingState       — removing full/empty plates from grid and returning to pool
///   SwapSelectingState  — swap power-up active; player picks up to 2 plates to swap
///   FillSelectingState  — fill power-up active; player picks 1 plate to spawn its complement
///   RemoveSelectingState — remove power-up active; player picks 1 plate to discard silently
///   UnifySelectingState  — unify power-up active; player picks 1 mixed plate to normalise
/// </summary>
public class GameStateMachine : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────

    public static GameStateMachine Instance { get; private set; }

    // ─── States ───────────────────────────────────────────────────────────────

    public PlayingState        Playing        { get; private set; }
    public DraggingState       Dragging       { get; private set; }
    public CheckingMergeState  CheckingMerge  { get; private set; }
    public MergingState        Merging        { get; private set; }
    public ClearingState       Clearing       { get; private set; }
    public SwapSelectingState   SwapSelecting   { get; private set; }
    public FillSelectingState   FillSelecting   { get; private set; }
    public RemoveSelectingState RemoveSelecting { get; private set; }
    public UnifySelectingState  UnifySelecting  { get; private set; }

    // ─── Runtime state ────────────────────────────────────────────────────────

    private IGameState _currentState;
    public  IGameState CurrentState => _currentState;

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Instantiate state objects once — they are plain C# classes, not MonoBehaviours.
        Playing        = new PlayingState(this);
        Dragging       = new DraggingState(this);
        CheckingMerge  = new CheckingMergeState(this);
        Merging        = new MergingState(this);
        Clearing       = new ClearingState(this);
        SwapSelecting   = new SwapSelectingState(this);
        FillSelecting   = new FillSelectingState(this);
        RemoveSelecting = new RemoveSelectingState(this);
        UnifySelecting  = new UnifySelectingState(this);
    }

    private void Start()
    {
        ChangeState(Playing);
    }

    private void Update()
    {
        _currentState?.Execute();
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Transitions from the current state to <paramref name="newState"/>.
    /// Calls Exit() on the outgoing state and Enter() on the incoming state.
    /// </summary>
    public void ChangeState(IGameState newState)
    {
        if (_currentState == newState) return;

        _currentState?.Exit();
        _currentState = newState;
        _currentState?.Enter();

        Debug.Log($"[GameStateMachine] → {newState.GetType().Name}");
    }
}

// ─── Concrete State Implementations ──────────────────────────────────────────

/// <summary>Normal gameplay — waiting for the player to grab a plate.</summary>
public class PlayingState : IGameState
{
    private readonly GameStateMachine _fsm;
    public PlayingState(GameStateMachine fsm) => _fsm = fsm;

    public void Enter()   { /* Enable drag input */ }
    public void Execute() { /* Idle tick logic */ }
    public void Exit()    { /* Disable drag input */ }
}

/// <summary>A plate is being dragged by the player.</summary>
public class DraggingState : IGameState
{
    private readonly GameStateMachine _fsm;
    public DraggingState(GameStateMachine fsm) => _fsm = fsm;

    public void Enter()   { /* Highlight valid drop targets */ }
    public void Execute() { /* Move plate with cursor */ }
    public void Exit()    { /* Clear highlights */ }
}

/// <summary>A plate was just dropped — checking neighbors for merge candidates.</summary>
public class CheckingMergeState : IGameState
{
    private readonly GameStateMachine _fsm;
    public CheckingMergeState(GameStateMachine fsm) => _fsm = fsm;

    public void Enter()   { }
    public void Execute() { }
    public void Exit()    { }
}

/// <summary>Slices are being transferred between two plates.</summary>
public class MergingState : IGameState
{
    private readonly GameStateMachine _fsm;
    public MergingState(GameStateMachine fsm) => _fsm = fsm;

    public void Enter()   { /* Play merge animation */ }
    public void Execute() { }
    public void Exit()    { /* End merge animation */ }
}

/// <summary>A full or empty plate is being removed from the grid and returned to pool.</summary>
public class ClearingState : IGameState
{
    private readonly GameStateMachine _fsm;
    public ClearingState(GameStateMachine fsm) => _fsm = fsm;

    public void Enter()   { /* Play clear animation / score effect */ }
    public void Execute() { }
    public void Exit()    { }
}

/// <summary>
/// Swap power-up is active — player is selecting up to 2 plates on the main grid to swap.
/// SwapPowerUp owns all logic while in this state; DragController is blocked.
/// </summary>
public class SwapSelectingState : IGameState
{
    private readonly GameStateMachine _fsm;
    public SwapSelectingState(GameStateMachine fsm) => _fsm = fsm;

    public void Enter()   { /* SwapPowerUp highlights button, enables plate click detection */ }
    public void Execute() { }
    public void Exit()    { /* SwapPowerUp clears highlight on cancel or completion */ }
}

/// <summary>
/// Fill power-up is active — player selects 1 plate on the main grid.
/// FillPowerUp spawns a complement plate (exactly the missing slices) in an adjacent empty cell,
/// which automatically triggers a merge to complete the selected plate.
/// DragController is blocked while in this state.
/// </summary>
public class FillSelectingState : IGameState
{
    private readonly GameStateMachine _fsm;
    public FillSelectingState(GameStateMachine fsm) => _fsm = fsm;

    public void Enter()   { /* FillPowerUp highlights button, enables plate click detection */ }
    public void Execute() { }
    public void Exit()    { /* FillPowerUp clears highlight on cancel or completion */ }
}

/// <summary>
/// Remove power-up is active — player selects 1 plate on the main grid to discard.
/// RemovePowerUp plays the standard dismiss animation then returns the plate to the pool,
/// WITHOUT firing OnPlateCompleted so no score is awarded.
/// DragController is blocked while in this state.
/// </summary>
public class RemoveSelectingState : IGameState
{
    private readonly GameStateMachine _fsm;
    public RemoveSelectingState(GameStateMachine fsm) => _fsm = fsm;

    public void Enter()   { /* RemovePowerUp highlights button, enables plate click detection */ }
    public void Execute() { }
    public void Exit()    { /* RemovePowerUp clears highlight on cancel or completion */ }
}

/// <summary>
/// Unify power-up is active — player selects 1 plate that holds multiple pizza types.
/// UnifyPowerUp converts all minority-type slices to the dominant type on that plate.
/// On tie, the first-found type wins. Plates with only 1 type are invalid selections.
/// DragController is blocked while in this state.
/// </summary>
public class UnifySelectingState : IGameState
{
    private readonly GameStateMachine _fsm;
    public UnifySelectingState(GameStateMachine fsm) => _fsm = fsm;

    public void Enter()   { /* UnifyPowerUp highlights button, enables plate click detection */ }
    public void Execute() { }
    public void Exit()    { /* UnifyPowerUp clears highlight on cancel or completion */ }
}
