using TMPro;
using UnityEngine;

/// <summary>
/// Bind dữ liệu từ PlayerDataManager ra UI:
///   • _coinText      — hiển thị số coin hiện tại.
///   • _sausageText   — hiển thị số lượng power-up Sausage.
///   • _cutterText    — hiển thị số lượng power-up Cutter.
///   • _trashCanText  — hiển thị số lượng power-up TrashCan.
///   • _swapText      — hiển thị số lượng power-up Swap.
///
/// Subscribe vào PlayerDataManager.OnCoinChanged và OnPowerUpChanged
/// để tự động cập nhật mỗi khi dữ liệu thay đổi.
///
/// Setup:
///   1. Gắn component này vào bất kỳ GameObject nào trong Canvas.
///   2. Gán các TMP_Text tương ứng trong Inspector (có thể để trống nếu không cần hiển thị).
///   3. Đảm bảo PlayerDataManager đã tồn tại trong scene.
/// </summary>
public class PlayerDataUI : MonoBehaviour
{
    // ─── Inspector fields ─────────────────────────────────────────────────────

    [Header("Coin")]
    [Tooltip("TMP_Text hiển thị số coin của player.")]
    [SerializeField] private TMP_Text _coinText;

    [Header("PowerUp Quantities")]
    [Tooltip("TMP_Text hiển thị số lượng power-up Sausage.")]
    [SerializeField] private TMP_Text _sausageText;

    [Tooltip("TMP_Text hiển thị số lượng power-up Cutter.")]
    [SerializeField] private TMP_Text _cutterText;

    [Tooltip("TMP_Text hiển thị số lượng power-up TrashCan.")]
    [SerializeField] private TMP_Text _trashCanText;

    [Tooltip("TMP_Text hiển thị số lượng power-up Swap.")]
    [SerializeField] private TMP_Text _swapText;

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    private void Start()
    {
        PlayerDataManager pdm = PlayerDataManager.Instance;

        if (pdm == null)
        {
            Debug.LogWarning("[PlayerDataUI] PlayerDataManager.Instance not found. " +
                             "Đảm bảo PlayerDataManager đã có trong scene và khởi tạo trước PlayerDataUI.");
            return;
        }

        // Subscribe events.
        pdm.OnCoinChanged    += UpdateCoin;
        pdm.OnPowerUpChanged += UpdatePowerUp;

        // Hiển thị giá trị ban đầu ngay khi Start.
        UpdateCoin(pdm.Coin);
        UpdatePowerUp("sausage",  pdm.SausageQty);
        UpdatePowerUp("cutter",   pdm.CutterQty);
        UpdatePowerUp("trashCan", pdm.TrashCanQty);
        UpdatePowerUp("swap",     pdm.SwapQty);
    }

    private void OnDestroy()
    {
        if (PlayerDataManager.Instance == null) return;

        PlayerDataManager.Instance.OnCoinChanged    -= UpdateCoin;
        PlayerDataManager.Instance.OnPowerUpChanged -= UpdatePowerUp;
    }

    // ─── Private handlers ─────────────────────────────────────────────────────

    /// <summary>Cập nhật text hiển thị coin.</summary>
    private void UpdateCoin(int newCoin)
    {
        if (_coinText != null)
            _coinText.text = newCoin.ToString();
    }

    /// <summary>
    /// Cập nhật text hiển thị số lượng power-up tương ứng với <paramref name="powerUpId"/>.
    /// </summary>
    private void UpdatePowerUp(string powerUpId, int newQty)
    {
        TMP_Text target = powerUpId switch
        {
            "sausage"  => _sausageText,
            "cutter"   => _cutterText,
            "trashCan" => _trashCanText,
            "swap"     => _swapText,
            _          => null
        };

        if (target != null)
            target.text = newQty.ToString();
    }
}
