using UnityEngine;

/// <summary>
/// Manages the game's Finite State Machine.
/// Holds one instance of each concrete state and handles transitions.
///
/// States:
///   PlayingState       — waiting for player input
///   DraggingState      — a plate is being dragged
///   CheckingMergeState — checking 4-directional neighbors for same pizza type
///   MergingState       — transferring slices between plates
///   ClearingState      — removing full/empty plates from grid and returning to pool
/// </summary>
public class GameStateMachine : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────

    public static GameStateMachine Instance { get; private set; }

    // ─── States ───────────────────────────────────────────────────────────────

    public PlayingState       Playing      { get; private set; }
    public DraggingState      Dragging     { get; private set; }
    public CheckingMergeState CheckingMerge { get; private set; }
    public MergingState       Merging      { get; private set; }
    public ClearingState      Clearing     { get; private set; }

    // ─── Runtime state ────────────────────────────────────────────────────────

    private IGameState _currentState;
    public  IGameState CurrentState => _currentState;

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Instantiate state objects once — they are plain C# classes, not MonoBehaviours.
        Playing       = new PlayingState(this);
        Dragging      = new DraggingState(this);
        CheckingMerge = new CheckingMergeState(this);
        Merging       = new MergingState(this);
        Clearing      = new ClearingState(this);
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
