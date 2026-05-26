using System;

// ─── Pizza Config Data Models ─────────────────────────────────────────────────

/// <summary>
/// Root wrapper deserialized from pizza_config.json.
/// Contains all pizza type definitions and spawn parameters.
/// </summary>
[Serializable]
public class PizzaConfigCollection
{
    public PizzaTypeData[] pizzaTypes;

    /// <summary>Minimum number of slices to spawn on a new plate.</summary>
    public int spawnCountMin;

    /// <summary>Maximum number of slices to spawn on a new plate.</summary>
    public int spawnCountMax;

    /// <summary>Maximum slices a plate can hold (must equal CircleGridDrawer sectorCount).</summary>
    public int maxSlicesPerPlate;

    /// <summary>
    /// Weighted probabilities for each possible slice count from spawnCountMin to spawnCountMax.
    /// Index 0 = weight for spawnCountMin, last index = weight for spawnCountMax.
    /// Higher weight = more likely. Values are relative (don't need to sum to 100).
    /// Example: [18, 22, 22, 20, 13, 5] → only 5% chance of 6 slices.
    /// </summary>
    public int[] spawnCountWeights;

    /// <summary>Minimum number of filler (different-type) slices when a full plate is spawned.</summary>
    public int minFillerSlicesWhenFull;

    /// <summary>Maximum number of filler (different-type) slices when a full plate is spawned.</summary>
    public int maxFillerSlicesWhenFull;
}

/// <summary>
/// Data for a single pizza type, mapped from JSON.
/// </summary>
[Serializable]
public class PizzaTypeData
{
    /// <summary>Unique identifier matching the pool id in pool_config.json.</summary>
    public string id;

    /// <summary>Human-readable name for UI display.</summary>
    public string displayName;

    /// <summary>Pool id used to Get/Release this pizza type from PoolManager.</summary>
    public string poolId;
}
