using System;

// ─── Daily Reward Config Data Models ──────────────────────────────────────────

/// <summary>
/// Root wrapper deserialized từ daily_reward_config.json.
/// Chứa danh sách phần thưởng cho 7 ngày trong một chu kỳ.
/// </summary>
[Serializable]
public class DailyRewardConfigData
{
    /// <summary>Mảng 7 phần thưởng, index 0 = ngày 1, index 6 = ngày 7.</summary>
    public DailyRewardDayData[] days;
}

/// <summary>
/// Dữ liệu phần thưởng cho 1 ngày trong chu kỳ daily reward.
/// </summary>
[Serializable]
public class DailyRewardDayData
{
    /// <summary>Số thứ tự ngày trong chu kỳ (1–7).</summary>
    public int day;

    /// <summary>Số coin thưởng. 0 nếu không có coin.</summary>
    public int coin;

    /// <summary>
    /// Id của power-up được thưởng ("sausage"|"cutter"|"trashCan"|"swap").
    /// Chuỗi rỗng nếu không thưởng power-up.
    /// </summary>
    public string powerUpId;

    /// <summary>Số lượng power-up được thưởng. 0 nếu không có.</summary>
    public int powerUpAmount;
}
