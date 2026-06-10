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
/// Lifecycle notes:
///   Unity KHÔNG đảm bảo tất cả Awake() chạy xong trước OnEnable(). Thứ tự thực
///   tế là: mỗi object chạy Awake() → OnEnable() của CHÍNH NÓ trước khi chuyển
///   sang object tiếp theo. Do đó nếu PlayerDataUI nằm trước PlayerDataManager
///   trong hierarchy, OnEnable() của PlayerDataUI sẽ thấy Instance = null.
///
///   Giải pháp: dùng cả OnEnable lẫn Start thông qua TrySubscribeAndRefresh()
///   với flag _isSubscribed để tránh double-subscribe:
///     - OnEnable : xử lý trường hợp quay lại scene (Instance đã tồn tại
///                  từ DontDestroyOnLoad) hoặc PlayerDataManager đứng trước.
///     - Start    : fallback cho lần đầu load khi OnEnable thấy Instance = null.
///     - OnDisable: unsubscribe, reset flag.
///
/// Setup:
///   1. Gắn component này vào bất kỳ GameObject nào trong Canvas.
///   2. Gán các TMP_Text tương ứng trong Inspector (có thể để trống nếu không cần).
///   3. Đảm bảo PlayerDataManager tồn tại trong scene hoặc DontDestroyOnLoad.
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

    // ─── Private state ────────────────────────────────────────────────────────

    /// <summary>
    /// True khi đã subscribe vào PlayerDataManager.
    /// Ngăn TrySubscribeAndRefresh() chạy nhiều lần cho cùng 1 lần kích hoạt.
    /// </summary>
    private bool _isSubscribed;

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    private void OnEnable()
    {
        // Hiệu quả khi: quay lại scene (Instance DontDestroyOnLoad đã sẵn sàng)
        // hoặc PlayerDataManager đứng trước PlayerDataUI trong hierarchy.
        TrySubscribeAndRefresh();
    }

    private void Start()
    {
        // Fallback: chạy sau TẤT CẢ Awake() trong scene — đảm bảo bắt được
        // trường hợp OnEnable() thấy Instance = null do PlayerDataUI đứng trước
        // PlayerDataManager trong hierarchy khi lần đầu load scene.
        TrySubscribeAndRefresh();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Subscribe vào PlayerDataManager và refresh toàn bộ UI.
    /// Bỏ qua nếu đã subscribe hoặc Instance chưa sẵn sàng.
    /// </summary>
    private void TrySubscribeAndRefresh()
    {
        if (_isSubscribed) return;

        PlayerDataManager pdm = PlayerDataManager.Instance;
        if (pdm == null) return;

        pdm.OnCoinChanged    += UpdateCoin;
        pdm.OnPowerUpChanged += UpdatePowerUp;
        _isSubscribed = true;

        RefreshAll(pdm);
    }

    /// <summary>Unsubscribe khỏi PlayerDataManager và reset flag.</summary>
    private void Unsubscribe()
    {
        if (!_isSubscribed) return;

        PlayerDataManager pdm = PlayerDataManager.Instance;
        if (pdm != null)
        {
            pdm.OnCoinChanged    -= UpdateCoin;
            pdm.OnPowerUpChanged -= UpdatePowerUp;
        }

        _isSubscribed = false;
    }

    /// <summary>Refresh toàn bộ UI với giá trị hiện tại từ PlayerDataManager.</summary>
    private void RefreshAll(PlayerDataManager pdm)
    {
        UpdateCoin(pdm.Coin);
        UpdatePowerUp("sausage",  pdm.SausageQty);
        UpdatePowerUp("cutter",   pdm.CutterQty);
        UpdatePowerUp("trashCan", pdm.TrashCanQty);
        UpdatePowerUp("swap",     pdm.SwapQty);
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
