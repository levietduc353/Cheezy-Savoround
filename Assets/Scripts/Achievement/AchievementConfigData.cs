using System;

// ─── Achievement Config Data Models ───────────────────────────────────────────

/// <summary>
/// Root wrapper deserialized from achievement_config.json.
/// </summary>
[Serializable]
public class AchievementConfigCollection
{
    public AchievementData[] achievements;
}

/// <summary>
/// Defines one achievement: its identity, win condition, and reward.
/// </summary>
[Serializable]
public class AchievementData
{
    /// <summary>Zero-based index — must match the position in achievementProgresses[].</summary>
    public int id;

    /// <summary>
    /// The progress value at which this achievement is considered complete.
    /// Slider fillAmount = progress / targetValue, clamped [0, 1].
    /// </summary>
    public int targetValue;

    /// <summary>
    /// Only relevant for achievement #2 (Pure Skill).
    /// The in-session level the player must reach or exceed when the game ends.
    /// 0 means no level requirement.
    /// </summary>
    public int minLevelRequired;

    /// <summary>Reward granted once when the achievement is first completed.</summary>
    public AchievementRewardData reward;
}

/// <summary>
/// Reward given upon achievement completion.
/// Reuses the same coin + powerUp structure as DailyRewardDayData.
/// </summary>
[Serializable]
public class AchievementRewardData
{
    /// <summary>Flat coin reward. 0 = no coin.</summary>
    public int coin;

    /// <summary>Power-up id to grant (e.g. "swap", "cutter"). Empty string = no power-up.</summary>
    public string powerUpId;

    /// <summary>Number of power-up charges to grant. 0 = no power-up.</summary>
    public int powerUpAmount;
}
