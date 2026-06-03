using System;

// ─── Score Config Data Models ─────────────────────────────────────────────────

/// <summary>
/// Root wrapper deserialized from score_config.json.
/// Defines the starting threshold for the score fill bar.
/// </summary>
[Serializable]
public class ScoreConfigData
{
    /// <summary>
    /// Number of plate completions required to fill the bar for the first time.
    /// Each time the bar fills, the threshold is multiplied by 1.5.
    /// </summary>
    public int initialScoreThreshold;
}
