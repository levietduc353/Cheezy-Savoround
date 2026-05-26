using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Singleton that manages all object pools in the game.
/// Reads pool definitions from pool_config.json and pre-warms each pool on Awake.
///
/// Usage:
///   PoolManager.Instance.Get("pizza_1")        → activate and return a pizza_1 object
///   PoolManager.Instance.Release("pizza_1", go) → deactivate and return to queue
/// </summary>
public class PoolManager : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────

    public static PoolManager Instance { get; private set; }

    // ─── Inspector fields ─────────────────────────────────────────────────────

    [Header("Config")]
    [Tooltip("Path inside Resources/ folder (no extension).")]
    [SerializeField] private string _configPath = "Configs/pool_config";

    // ─── Private state ────────────────────────────────────────────────────────

    private readonly Dictionary<string, ObjectPool> _pools = new Dictionary<string, ObjectPool>();

    /// <summary>Root transform that keeps all pooled objects grouped in the hierarchy.</summary>
    private Transform _poolRoot;

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        // Singleton enforcement
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Create a hidden root to keep pooled objects organized in the hierarchy.
        _poolRoot = new GameObject("[PoolRoot]").transform;
        _poolRoot.SetParent(transform);

        LoadAndCreatePools();
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Gets an active GameObject from the pool identified by <paramref name="poolId"/>.
    /// Returns null if the pool does not exist.
    /// </summary>
    public GameObject Get(string poolId)
    {
        if (_pools.TryGetValue(poolId, out ObjectPool pool))
            return pool.Get();

        Debug.LogError($"[PoolManager] Pool '{poolId}' not found. Check pool_config.json.");
        return null;
    }

    /// <summary>
    /// Returns <paramref name="go"/> to the pool identified by <paramref name="poolId"/>.
    /// If the pool does not exist, the object is destroyed as fallback.
    /// </summary>
    public void Release(string poolId, GameObject go)
    {
        if (_pools.TryGetValue(poolId, out ObjectPool pool))
        {
            pool.Release(go);
            return;
        }

        Debug.LogError($"[PoolManager] Cannot release to unknown pool '{poolId}'. Destroying instead.");
        Destroy(go);
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    /// <summary>Reads pool_config.json and creates an ObjectPool for each entry.</summary>
    private void LoadAndCreatePools()
    {
        TextAsset jsonAsset = Resources.Load<TextAsset>(_configPath);

        if (jsonAsset == null)
        {
            Debug.LogError($"[PoolManager] Config not found at Resources/{_configPath}.json");
            return;
        }

        PoolConfigCollection config = JsonUtility.FromJson<PoolConfigCollection>(jsonAsset.text);

        if (config?.pools == null)
        {
            Debug.LogError("[PoolManager] Failed to parse PoolConfigCollection from JSON.");
            return;
        }

        foreach (PoolEntryData entry in config.pools)
            RegisterPool(entry);

        Debug.Log($"[PoolManager] Initialized {_pools.Count} pool(s).");
    }

    /// <summary>
    /// Creates a single ObjectPool from a <see cref="PoolEntryData"/> entry.
    /// Skips if the pool id is already registered or the prefab cannot be found.
    /// </summary>
    private void RegisterPool(PoolEntryData entry)
    {
        if (_pools.ContainsKey(entry.id))
        {
            Debug.LogWarning($"[PoolManager] Pool '{entry.id}' already registered. Skipping duplicate.");
            return;
        }

        GameObject prefab = Resources.Load<GameObject>(entry.prefabPath);

        if (prefab == null)
        {
            Debug.LogError($"[PoolManager] Prefab not found at Resources/{entry.prefabPath}");
            return;
        }

        // Each pool gets its own child transform so the hierarchy stays tidy.
        Transform poolParent = new GameObject($"Pool_{entry.id}").transform;
        poolParent.SetParent(_poolRoot);

        ObjectPool pool = new ObjectPool(entry.id, prefab, entry.initialSize, entry.maxSize, poolParent);
        _pools[entry.id] = pool;
    }
}
