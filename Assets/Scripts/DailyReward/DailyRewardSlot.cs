using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Các trạng thái hiển thị của một ô ngày trong Daily Reward panel.</summary>
public enum DailyRewardSlotState
{
    /// <summary>Ngày chưa đến — không thể claim.</summary>
    Locked,

    /// <summary>Ngày hôm nay — có thể claim (highlight).</summary>
    Available,

    /// <summary>Ngày đã được claim trong chu kỳ này.</summary>
    Claimed
}

/// <summary>
/// Component gắn trên mỗi trong 7 ô của Daily Reward panel.
///
/// Nhận lệnh SetState() từ DailyRewardUI để:
///   - Đổi sprite nền (_backgroundImage) theo trạng thái.
///   - Hiển thị số ngày (_dayLabel).
///   - Hiển thị thông tin phần thưởng (_rewardLabel).
///
/// Setup trong Inspector:
///   - _dayNumber    : số thứ tự ngày (1–7), set thủ công cho từng slot.
///   - _backgroundImage : Image component của ô.
///   - _dayLabel     : TMP_Text hiển thị "Day X".
///   - _rewardLabel  : TMP_Text hiển thị coin và power-up.
///   - _lockedSprite / _availableSprite / _claimedSprite : sprite cho 3 trạng thái.
/// </summary>
public class DailyRewardSlot : MonoBehaviour
{
    // ─── Inspector fields ─────────────────────────────────────────────────────

    [Header("Day Config")]
    [Tooltip("Số thứ tự ngày trong chu kỳ (1–7). Set thủ công trong Inspector cho từng slot.")]
    [SerializeField] private int _dayNumber;

    [Header("UI References")]
    [Tooltip("Image component làm nền của ô — sprite sẽ thay đổi theo trạng thái.")]
    [SerializeField] private Image _backgroundImage;

    [Tooltip("TMP_Text hiển thị số thứ tự ngày (\"Day 1\", \"Day 2\", ...).")]
    [SerializeField] private TMP_Text _dayLabel;

    [Tooltip("TMP_Text hiển thị thông tin phần thưởng (coin, power-up).")]
    [SerializeField] private TMP_Text _rewardLabel;

    [Header("Sprites")]
    [Tooltip("Sprite khi ngày chưa đến (locked).")]
    [SerializeField] private Sprite _lockedSprite;

    [Tooltip("Sprite khi ngày hôm nay có thể claim (highlighted).")]
    [SerializeField] private Sprite _availableSprite;

    [Tooltip("Sprite khi ngày đã được claim.")]
    [SerializeField] private Sprite _claimedSprite;

    // ─── Public properties ────────────────────────────────────────────────────

    /// <summary>Số thứ tự ngày của slot này (1–7).</summary>
    public int DayNumber => _dayNumber;

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Cập nhật hiển thị của ô dựa trên <paramref name="state"/> và dữ liệu phần thưởng.
    /// Được gọi bởi DailyRewardUI.RefreshAll().
    /// </summary>
    /// <param name="state">Trạng thái hiển thị (Locked / Available / Claimed).</param>
    /// <param name="rewardData">Dữ liệu phần thưởng của ngày này từ config.</param>
    public void SetState(DailyRewardSlotState state, DailyRewardDayData rewardData)
    {
        // ── Đổi sprite nền ────────────────────────────────────────────────────
        if (_backgroundImage != null)
        {
            _backgroundImage.sprite = state switch
            {
                DailyRewardSlotState.Claimed   => _claimedSprite,
                DailyRewardSlotState.Available => _availableSprite,
                _                              => _lockedSprite
            };
        }

        // ── Cập nhật label ngày ───────────────────────────────────────────────
        if (_dayLabel != null)
            _dayLabel.text = $"Day {_dayNumber}";

        // ── Cập nhật label phần thưởng ────────────────────────────────────────
        if (_rewardLabel != null)
        {
            string rewardStr = $"+{rewardData.coin} coin";

            if (!string.IsNullOrEmpty(rewardData.powerUpId) && rewardData.powerUpAmount > 0)
                rewardStr += $"\n+{rewardData.powerUpAmount} {rewardData.powerUpId}";

            _rewardLabel.text = rewardStr;
        }
    }
}
