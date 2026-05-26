using UnityEngine;

/// <summary>
/// Component attached to every pizza slice prefab.
/// Stores the pizza type id so it can be correctly returned to its pool.
/// Set via <see cref="Initialize"/> immediately after Get() from PoolManager.
/// </summary>
public class PizzaSlice : MonoBehaviour
{
    // ─── Private state ────────────────────────────────────────────────────────

    private string _pizzaTypeId;
    private string _poolId;

    // ─── Public properties ────────────────────────────────────────────────────

    public string PizzaTypeId => _pizzaTypeId;
    public string PoolId      => _poolId;

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Called by PlateController right after Get() from PoolManager.
    /// Must be called before the object is used.
    /// </summary>
    public void Initialize(string pizzaTypeId, string poolId)
    {
        _pizzaTypeId = pizzaTypeId;
        _poolId      = poolId;
    }

    /// <summary>
    /// Returns this slice to its correct pool.
    /// Equivalent to PoolManager.Instance.Release(poolId, gameObject).
    /// </summary>
    public void ReturnToPool()
    {
        PoolManager.Instance.Release(_poolId, gameObject);
    }
}
