using System;

// ─── Pool Config Data Models ──────────────────────────────────────────────────

/// <summary>
/// Root wrapper deserialized from pool_config.json.
/// Contains all object pool definitions in the project.
/// </summary>
[Serializable]
public class PoolConfigCollection
{
    public PoolEntryData[] pools;
}

/// <summary>
/// Configuration for a single object pool entry.
/// </summary>
[Serializable]
public class PoolEntryData
{
    /// <summary>Unique identifier used to Get/Release from PoolManager.</summary>
    public string id;

    /// <summary>Path inside Resources/ folder (no extension) to the prefab.</summary>
    public string prefabPath;

    /// <summary>Number of instances to pre-warm at startup.</summary>
    public int initialSize;

    /// <summary>Maximum number of instances this pool is allowed to hold.</summary>
    public int maxSize;
}
