using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gắn vào Button mua power-up.
/// Wire Button.OnClick → OnBuyClicked() trong Inspector.
///
/// Cấu hình trong Inspector:
///   • Target PowerUp — chọn loại power-up muốn mua.
///   • Cost           — số coin cần trả.
///
/// Logic:
///   - Nếu player đủ coin → SpendCoin(cost) + AddPowerUp(1) + highlight mua thành công.
///   - Nếu không đủ coin → không làm gì, log warning.
/// </summary>
public class BuyPowerUpButton : MonoBehaviour
{
    // ─── Nested enum ──────────────────────────────────────────────────────────

    /// <summary>Các loại power-up có thể mua. Khớp với key trong PlayerDataManager.</summary>
    public enum PowerUpType
    {
        Sausage,
        Cutter,
        TrashCan,
        Swap
    }

    // ─── Inspector fields ─────────────────────────────────────────────────────

    [Header("Purchase Config")]
    [Tooltip("Loại power-up sẽ được cộng thêm 1 khi mua thành công.")]
    [SerializeField] private PowerUpType _targetPowerUp = PowerUpType.Sausage;

    [Tooltip("Số coin cần trả để mua 1 đơn vị power-up này.")]
    [SerializeField] private int _cost = 10;

    [Header("Feedback (tuỳ chọn)")]
    [Tooltip("Graphic của button — sẽ flash màu khi mua thành công / thất bại. Có thể để trống.")]
    [SerializeField] private Graphic _buttonGraphic;

    [Tooltip("Màu flash khi mua thành công.")]
    [SerializeField] private Color _successColor = new Color(0.4f, 1f, 0.4f, 1f); // xanh lá nhạt

    [Tooltip("Màu flash khi không đủ coin.")]
    [SerializeField] private Color _failColor = new Color(1f, 0.35f, 0.35f, 1f); // đỏ nhạt

    [Tooltip("Thời gian (giây) giữ màu flash trước khi trả lại màu gốc.")]
    [SerializeField] private float _flashDuration = 0.35f;

    // ─── Private state ────────────────────────────────────────────────────────

    private Color _originalColor;
    private bool  _isFlashing;

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        if (_buttonGraphic != null)
            _originalColor = _buttonGraphic.color;
    }

    // ─── Public API (Button OnClick) ──────────────────────────────────────────

    /// <summary>
    /// Gọi từ Button.OnClick trong Inspector.
    /// Thực hiện giao dịch mua: trừ coin và cộng power-up nếu đủ tiền.
    /// </summary>
    public void OnBuyClicked()
    {
        PlayerDataManager pdm = PlayerDataManager.Instance;

        if (pdm == null)
        {
            Debug.LogWarning("[BuyPowerUpButton] PlayerDataManager.Instance not found.");
            return;
        }

        string powerUpId = GetPowerUpId(_targetPowerUp);

        // ── Kiểm tra đủ coin ──────────────────────────────────────────────────
        if (pdm.Coin < _cost)
        {
            Debug.Log($"[BuyPowerUpButton] Không đủ coin: cần {_cost}, hiện có {pdm.Coin}.");
            FlashColor(_failColor);
            return;
        }

        // ── Thực hiện giao dịch ───────────────────────────────────────────────
        bool spent = pdm.SpendCoin(_cost);
        if (!spent) return; // safety guard

        pdm.AddPowerUp(powerUpId);

        Debug.Log($"[BuyPowerUpButton] Mua thành công '{powerUpId}' với giá {_cost} coin.");
        FlashColor(_successColor);
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    /// <summary>Chuyển enum → string id dùng trong PlayerDataManager.</summary>
    private static string GetPowerUpId(PowerUpType type) => type switch
    {
        PowerUpType.Sausage  => "sausage",
        PowerUpType.Cutter   => "cutter",
        PowerUpType.TrashCan => "trashCan",
        PowerUpType.Swap     => "swap",
        _                    => "sausage"
    };

    /// <summary>
    /// Flash màu của _buttonGraphic trong <see cref="_flashDuration"/> giây,
    /// sau đó trả lại màu gốc.
    /// </summary>
    private void FlashColor(Color flashColor)
    {
        if (_buttonGraphic == null || _isFlashing) return;
        StartCoroutine(FlashCoroutine(flashColor));
    }

    private System.Collections.IEnumerator FlashCoroutine(Color flashColor)
    {
        _isFlashing = true;
        _buttonGraphic.color = flashColor;
        yield return new WaitForSeconds(_flashDuration);
        _buttonGraphic.color = _originalColor;
        _isFlashing = false;
    }
}
