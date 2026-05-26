/// <summary>
/// Interface for all FSM game states.
/// Implement Enter, Execute, and Exit to define state behavior.
/// </summary>
public interface IGameState
{
    /// <summary>Called once when entering this state.</summary>
    void Enter();

    /// <summary>Called every frame while in this state (driven by GameStateMachine.Update).</summary>
    void Execute();

    /// <summary>Called once when leaving this state.</summary>
    void Exit();
}
