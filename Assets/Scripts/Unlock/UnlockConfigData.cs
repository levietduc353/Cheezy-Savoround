using System;

// ─── Unlock Config Data Models ────────────────────────────────────────────────

/// <summary>
/// Root wrapper deserialized from unlock_config.json.
/// Defines which pizza type is unlocked at each level milestone,
/// and how filler-slice chance scales with level.
/// </summary>
[Serializable]
public class UnlockConfigData
{
    /// <summary>All unlock rules, one entry per pizza type.</summary>
    public UnlockRule[] unlockRules;

    /// <summary>
    /// Filler-chance breakpoints, sorted ascending by fromLevel.
    /// UnlockManager picks the entry with the highest fromLevel
    /// that is still ≤ the current level.
    /// </summary>
    public FillerChanceEntry[] fillerChanceByLevel;
}

/// <summary>
/// Declares that <see cref="unlockedPizzaTypeId"/> becomes available
/// when <see cref="atLevel"/> is reached.
/// </summary>
[Serializable]
public class UnlockRule
{
    /// <summary>Level at which this pizza type is unlocked (1-based, matches ScoreManager.CurrentLevel).</summary>
    public int atLevel;

    /// <summary>Pool/type id of the pizza type to unlock (e.g. "pizza_4").</summary>
    public string unlockedPizzaTypeId;
}

/// <summary>
/// Maps a level milestone to a filler-slice spawn probability.
/// </summary>
[Serializable]
public class FillerChanceEntry
{
    /// <summary>The level at which this filler chance applies (inclusive).</summary>
    public int fromLevel;

    /// <summary>
    /// Probability [0, 1] that a full plate will contain filler slices.
    /// 0.0 = never, 1.0 = always.
    /// </summary>
    public float fillerChance;
}
