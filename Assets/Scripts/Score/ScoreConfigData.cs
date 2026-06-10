using System;

// ─── Score Config Data Models ─────────────────────────────────────────────────

/// <summary>
/// Root wrapper deserialized from score_config.json.
/// Defines the starting threshold for the score fill bar and coin reward params.
/// </summary>
[Serializable]
public class ScoreConfigData
{
    /// <summary>
    /// Number of plate completions required to fill the bar for the first time.
    /// Each time the bar fills, the threshold is multiplied by 1.5.
    /// </summary>
    public int initialScoreThreshold;

    /// <summary>
    /// Cứ mỗi bao nhiêu level sẽ thưởng coin một lần.
    /// Mặc định 3 nếu không set hoặc set = 0.
    /// </summary>
    public int coinRewardEveryLevels;

    /// <summary>
    /// Số coin thưởng mỗi mốc coinRewardEveryLevels level.
    /// Mặc định 20 nếu không set hoặc set = 0.
    /// </summary>
    public int coinRewardAmount;
}
