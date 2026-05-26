using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages a pool of GameObjects for a single prefab type.
/// Objects are reused via Get/Release instead of Instantiate/Destroy to avoid GC spikes.
/// </summary>
public class ObjectPool
{
    // ─── Private state ────────────────────────────────────────────────────────

    private readonly string    _poolId;
    private readonly GameObject _prefab;
    private readonly int       _maxSize;
    private readonly Transform _parent;

    private readonly Queue<GameObject> _available = new Queue<GameObject>();
    private int _totalCount = 0;

    // ─── Public properties ────────────────────────────────────────────────────

    public string PoolId       => _poolId;
    public int    AvailableCount => _available.Count;
    public int    TotalCount    => _totalCount;

    // ─── Constructor ─────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the pool, pre-warming it with <paramref name="initialSize"/> instances.
    /// </summary>
    public ObjectPool(string poolId, GameObject prefab, int initialSize, int maxSize, Transform parent)
    {
        _poolId  = poolId;
        _prefab  = prefab;
        _maxSize = maxSize;
        _parent  = parent;

        PreWarm(initialSize);
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Activates and returns a pooled GameObject.
    /// If the queue is empty, a new instance is created on demand.
    /// </summary>
    public GameObject Get()
    {
        GameObject go;

        if (_available.Count > 0)
        {
            go = _available.Dequeue();
        }
        else
        {
            // Pool exhausted — create on demand up to maxSize
            if (_totalCount >= _maxSize)
                Debug.LogWarning($"[ObjectPool:{_poolId}] Pool at max capacity ({_maxSize}). Creating overflow instance.");

            go = CreateInstance();
        }

        go.SetActive(true);
        return go;
    }

    /// <summary>
    /// Deactivates <paramref name="go"/> and returns it to the queue for reuse.
    /// If the pool is over capacity, the instance is destroyed instead.
    /// </summary>
    public void Release(GameObject go)
    {
        if (go == null) return;

        go.SetActive(false);
        go.transform.SetParent(_parent);
        go.transform.localPosition = Vector3.zero;

        // If over max capacity (due to overflow instances), destroy instead of pooling.
        if (_totalCount > _maxSize)
        {
            Object.Destroy(go);
            _totalCount--;
            return;
        }

        _available.Enqueue(go);
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    /// <summary>Pre-instantiates <paramref name="count"/> inactive objects into the queue.</summary>
    private void PreWarm(int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (_totalCount >= _maxSize) break;

            GameObject go = CreateInstance();
            go.SetActive(false);
            _available.Enqueue(go);
        }
    }

    /// <summary>Instantiates a new instance under the pool's parent transform.</summary>
    private GameObject CreateInstance()
    {
        GameObject go = Object.Instantiate(_prefab, _parent);
        go.name = $"{_poolId}_{_totalCount}";
        _totalCount++;
        return go;
    }
}
