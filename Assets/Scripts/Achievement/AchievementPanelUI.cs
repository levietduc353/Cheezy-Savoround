using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Quản lý giao diện tổng quan của Achievement (chấm đỏ, nút nhận tất cả) trong Scene.
/// Khắc phục lỗi mất reference UI khi chuyển Scene của AchievementManager.
/// </summary>
public class AchievementPanelUI : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("GameObject hiển thị chấm đỏ báo hiệu có thưởng.")]
    [SerializeField] private GameObject _notificationBadge;

    [Tooltip("Nút Collect dùng chung để nhận tất cả phần thưởng hiện có.")]
    [SerializeField] private Button _collectAllButton;

    private void Start()
    {
        // Gắn sự kiện click cho nút
        if (_collectAllButton != null)
        {
            _collectAllButton.onClick.AddListener(() => 
            {
                if (AchievementManager.Instance != null)
                    AchievementManager.Instance.ClaimAllRewards();
            });
        }

        // Lắng nghe sự thay đổi trạng thái từ AchievementManager
        if (AchievementManager.Instance != null)
        {
            AchievementManager.Instance.OnUnclaimedStatusChanged += HandleStatusChanged;
            
            // Cập nhật trạng thái ngay lúc panel bật lên
            HandleStatusChanged(AchievementManager.Instance.HasAnyUnclaimedAchievement());
        }
        else
        {
            // Fallback nếu chưa có Manager
            HandleStatusChanged(false);
        }
    }

    private void OnDestroy()
    {
        if (AchievementManager.Instance != null)
        {
            AchievementManager.Instance.OnUnclaimedStatusChanged -= HandleStatusChanged;
        }
    }

    /// <summary>
    /// Hàm xử lý khi có cập nhật về trạng thái phần thưởng chưa nhận
    /// </summary>
    private void HandleStatusChanged(bool hasUnclaimed)
    {
        if (_notificationBadge != null)
            _notificationBadge.SetActive(hasUnclaimed);

        if (_collectAllButton != null)
            _collectAllButton.gameObject.SetActive(hasUnclaimed);
    }
}
