using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Điều khiển 1 slot achievement trong Achievement Panel UI.
///
/// Mỗi slot hiển thị:
///   - Progress label: "X / Y" (TMP_Text)
///   - Thanh tiến trình: Image fillAmount (phải set Image Type = Filled trong Inspector)
///
/// Setup:
///   Assign _achievementId (0–4) và các UI reference trong Inspector.
///   AchievementPanelUI sẽ gọi Refresh() khi AchievementManager fire event.
/// </summary>
public class AchievementSlotUI : MonoBehaviour
{
    // ─── Inspector fields ─────────────────────────────────────────────────────

    [Header("Achievement")]
    [Tooltip("Id của achievement này (0–4). Phải khớp với AchievementData.id trong config.")]
    [SerializeField] private int _achievementId;

    [Header("UI References")]
    [Tooltip("Image dùng làm thanh tiến trình (phải set Image Type = Filled trong Inspector).")]
    [SerializeField] private Image _progressFill;

    [Tooltip("TMP_Text hiển thị 'X / Y' (tiến trình / mục tiêu).")]
    [SerializeField] private TMP_Text _progressLabel;

    [Tooltip("GameObject hiển thị khi achievement đã hoàn thành (badge / checkmark).")]
    [SerializeField] private GameObject _completedBadge;

    // ─── Public property ──────────────────────────────────────────────────────

    public int AchievementId => _achievementId;

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    private void OnEnable()
    {
        // Đăng ký nhận event
        if (AchievementManager.Instance != null)
        {
            AchievementManager.Instance.OnProgressChanged += HandleProgressChanged;
            
            // Lấy dữ liệu mới nhất hiển thị ngay khi bật UI lên (vì mặc định UI bị ẩn)
            if (PlayerDataManager.Instance != null)
            {
                AchievementData data = AchievementManager.Instance.GetAchievementData(_achievementId);
                if (data != null)
                {
                    int progress = PlayerDataManager.Instance.GetAchievementProgress(_achievementId);
                    Refresh(progress, data.targetValue);
                }
            }
        }
    }

    private void OnDisable()
    {
        // Hủy đăng ký để tránh memory leak
        if (AchievementManager.Instance != null)
        {
            AchievementManager.Instance.OnProgressChanged -= HandleProgressChanged;
        }
    }

    private void HandleProgressChanged(int id, int progress, int target)
    {
        if (id == _achievementId)
        {
            Refresh(progress, target);
        }
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Cập nhật slider fillAmount và progress label.
    /// Gọi bởi AchievementPanelUI mỗi khi AchievementManager.OnProgressChanged fire.
    /// </summary>
    public void Refresh(int progress, int target)
    {
        bool isComplete = progress >= target;

        // Cập nhật thanh Image fill.
        if (_progressFill != null)
        {
            float fill = target > 0 ? Mathf.Clamp01((float)progress / target) : 0f;
            _progressFill.fillAmount = fill;
        }

        // Progress label "X / Y" — khi complete hiển thị "X / X" (clamped).
        if (_progressLabel != null)
        {
            int displayProgress = Mathf.Min(progress, target);
            _progressLabel.text = $"{displayProgress} / {target}";
        }

        // Lấy trạng thái đã claim chưa từ PlayerDataManager
        bool isClaimed = PlayerDataManager.Instance != null && 
                         PlayerDataManager.Instance.IsAchievementClaimed(_achievementId);

        // Completed badge chỉ hiện khi: ĐÃ CLAIM.
        if (_completedBadge != null)
            _completedBadge.SetActive(isClaimed);
    }
}
