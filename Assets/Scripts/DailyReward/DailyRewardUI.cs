using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Quản lý toàn bộ UI của Daily Reward panel.
///
/// Trách nhiệm:
///   - Subscribe DailyRewardManager.OnClaimStateChanged và OnRewardClaimed.
///   - Enable/disable _rewardAvailableIndicator khi có reward chưa claim.
///   - Enable/disable interactable của _claimButton.
///   - Cập nhật trạng thái (Locked / Available / Claimed) của 7 _slots.
///   - Đóng panel qua ClosePanel() (wired tới nút đóng trong panel).
///   - Forward sự kiện bấm Claim tới DailyRewardManager.ClaimTodayReward().
///
/// Lưu ý: Việc mở panel do script khác trong scene đảm nhiệm.
///
/// Setup trong Inspector:
///   - _panel                   : GameObject chứa toàn bộ UI panel (disabled theo mặc định).
///   - _rewardAvailableIndicator: Image hiển thị khi có reward chờ claim (badge/notification).
///   - _claimButton             : Button Claim reward.
///   - _slots                   : Mảng 7 DailyRewardSlot (index 0 = Day 1, index 6 = Day 7).
///
/// Lưu ý: DailyRewardUI dùng Start/OnDestroy để subscribe vì DailyRewardManager
/// là non-DontDestroyOnLoad (cùng scene), nên Instance luôn có trong Start().
/// </summary>
public class DailyRewardUI : MonoBehaviour
{
    // ─── Inspector fields ─────────────────────────────────────────────────────

    [Header("Panel")]
    [Tooltip("Root GameObject của Daily Reward panel. Mặc định nên disabled trong scene.")]
    [SerializeField] private GameObject _panel;

    [Header("Indicator")]
    [Tooltip("Image hiển thị (enable) khi có reward chưa claim — notification badge.")]
    [SerializeField] private Image _rewardAvailableIndicator;

    [Header("Claim Button")]
    [Tooltip("Button để player claim reward. Sẽ bị disable sau khi claim.")]
    [SerializeField] private Button _claimButton;

    [Tooltip("DisabledColor của _claimButton khi không thể claim. Alpha luôn = 1 để không giảm opacity.")]
    [SerializeField] private Color _claimButtonDisabledColor = new Color(0x94 / 255f, 0x7C / 255f, 0x7C / 255f, 1f);

    [Header("Day Slots")]
    [Tooltip("7 DailyRewardSlot tương ứng Day 1–7. Index 0 = Day 1, index 6 = Day 7.")]
    [SerializeField] private DailyRewardSlot[] _slots;

    [Header("Audio")]
    [SerializeField] private AudioClip _clickSound;
    [SerializeField] private AudioSource _audioSource;

    // ─── Private state ─────────────────────────────────────────────────────────

    /// <summary>
    /// DisabledColor gốc của _claimButton, lưu trong Start() để khôi phục khi enable lại.
    /// Unity dùng disabledColor (không phải normalColor) khi interactable = false.
    /// </summary>
    private Color _claimButtonOriginalDisabledColor;

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    private void Start()
    {
        // Lưu disabledColor gốc trước bất kỳ thay đổi nào.
        // Unity dùng disabledColor khi interactable = false — đây là field cần đổi,
        // không phải normalColor.
        if (_claimButton != null)
            _claimButtonOriginalDisabledColor = _claimButton.colors.disabledColor;

        DailyRewardManager drm = DailyRewardManager.Instance;

        if (drm == null)
        {
            Debug.LogError("[DailyRewardUI] DailyRewardManager.Instance not found. " +
                           "Đảm bảo DailyRewardManager có trong scene và khởi tạo trước.");
            return;
        }

        drm.OnClaimStateChanged += HandleClaimStateChanged;
        drm.OnRewardClaimed     += HandleRewardClaimed;

        // Hiển thị trạng thái ban đầu ngay khi Start.
        RefreshAll();
    }

    private void OnDestroy()
    {
        if (DailyRewardManager.Instance == null) return;
        DailyRewardManager.Instance.OnClaimStateChanged -= HandleClaimStateChanged;
        DailyRewardManager.Instance.OnRewardClaimed     -= HandleRewardClaimed;
    }

    // ─── Public API (wired to buttons in Inspector) ──────────────────────────────────────────

    /// <summary>Đóng Daily Reward panel. Wire tới nút đóng / nút X trong panel.</summary>
    public void ClosePanel()
    {
        if (_panel != null)
            _panel.SetActive(false);
    }

    /// <summary>
    /// Gọi DailyRewardManager.ClaimTodayReward().
    /// Wire tới OnClick của _claimButton trong Inspector.
    /// </summary>
    public void OnClaimButtonClicked()
    {
        if (_clickSound != null && _audioSource != null)
        {
            _audioSource.PlayOneShot(_clickSound);
        }
        DailyRewardManager.Instance?.ClaimTodayReward();
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Refresh toàn bộ UI: indicator, claim button, và tất cả 7 slot.
    /// Tính trạng thái từng slot dựa vào TodayDay và CanClaimToday của DailyRewardManager.
    /// </summary>
    private void RefreshAll()
    {
        DailyRewardManager drm = DailyRewardManager.Instance;
        if (drm == null || drm.Config == null) return;

        bool canClaim = drm.CanClaimToday;
        int  todayDay = drm.TodayDay;

        // ── Indicator ─────────────────────────────────────────────────────────
        if (_rewardAvailableIndicator != null)
            _rewardAvailableIndicator.gameObject.SetActive(canClaim);

        // ── Claim button ────────────────────────────────────────────────────────────
        if (_claimButton != null)
        {
            _claimButton.interactable = canClaim;

            // Đổi disabledColor (field Unity thực sự dùng khi interactable = false):
            //   - Disabled: _claimButtonDisabledColor (#947C7C, alpha = 1 — không giảm opacity)
            //   - Enabled : khôi phục disabledColor gốc
            ColorBlock colors       = _claimButton.colors;
            colors.disabledColor    = canClaim ? _claimButtonOriginalDisabledColor : _claimButtonDisabledColor;
            _claimButton.colors     = colors;
        }

        // ── 7 slots ───────────────────────────────────────────────────────────
        for (int i = 0; i < _slots.Length && i < drm.Config.days.Length; i++)
        {
            int slotDay = i + 1; // slots array là 0-indexed, day là 1-indexed

            DailyRewardSlotState state;

            if (slotDay < todayDay)
            {
                // Ngày đã qua trong chu kỳ này → đã claim.
                state = DailyRewardSlotState.Claimed;
            }
            else if (slotDay == todayDay)
            {
                // Ngày hôm nay: Available nếu chưa claim, Claimed nếu đã claim.
                state = canClaim ? DailyRewardSlotState.Available : DailyRewardSlotState.Claimed;
            }
            else
            {
                // Ngày chưa đến trong chu kỳ.
                state = DailyRewardSlotState.Locked;
            }

            _slots[i].SetState(state, drm.Config.days[i]);
        }
    }

    // ─── Event handlers ───────────────────────────────────────────────────────

    /// <summary>
    /// G\u1ecdi RefreshAll() \u0111\u1ec3 c\u1eadp nh\u1eadt to\u00e0n b\u1ed9 UI (indicator, button, v\u00e0 c\u1ea3 7 slots).
    ///
    /// T\u1ea1i sao d\u00f9ng RefreshAll() thay v\u00ec ch\u1ec9 update button/indicator?
    ///   DailyRewardManager.Start() fire event n\u00e0y sau ComputeTodayState() \u0111\u1ec3 fix race
    ///   condition v\u1edbi DailyRewardUI.Start(). Khi \u0111\u00f3 TodayDay c\u0169ng c\u00f3 th\u1ec3 ch\u01b0a \u0111\u01b0\u1ee3c
    ///   hi\u1ec3n th\u1ecb \u0111\u00fang tr\u00ean c\u00e1c slot \u2014 RefreshAll() \u0111\u1ea3m b\u1ea3o c\u1ea3 slots c\u0169ng \u0111\u01b0\u1ee3c c\u1eadp nh\u1eadt.
    /// </summary>
    private void HandleClaimStateChanged(bool canClaim)
    {
        RefreshAll();
    }

    /// <summary>Refresh toàn bộ UI sau khi claim để cập nhật slot vừa được claim.</summary>
    private void HandleRewardClaimed(int dayNumber)
    {
        RefreshAll();
    }
}
