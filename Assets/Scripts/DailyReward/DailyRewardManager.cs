using System;
using UnityEngine;

/// <summary>
/// Singleton quản lý toàn bộ logic Daily Reward.
///
/// Trách nhiệm:
///   - Load daily_reward_config.json khi Awake.
///   - Tính toán trạng thái claim của ngày hôm nay trong Start()
///     (sau khi PlayerDataManager.Awake() đã chạy xong).
///   - Expose CanClaimToday, TodayDay cho UI.
///   - Thực hiện ClaimTodayReward(): cộng coin/power-up, cập nhật save, fire events.
///
/// Logic xác định ngày:
///   - lastClaimDate == hôm nay       → đã claim, _canClaimToday = false
///   - lastClaimDate == hôm qua       → streak tiếp tục, _todayDay = lastClaimedDay + 1
///   - lastClaimedDay == 7 và hôm qua → cycle mới, _todayDay = 1
///   - lastClaimDate rỗng             → lần đầu chơi, _todayDay = 1
///   - lastClaimDate < hôm qua        → bỏ ngày, reset streak, _todayDay = 1
///
/// Events (Observer Pattern):
///   OnClaimStateChanged(bool canClaim) — fired khi trạng thái claim thay đổi.
///   OnRewardClaimed(int dayNumber)     — fired sau khi claim thành công.
///
/// Setup:
///   Gắn vào một GameObject trong scene. Gán _configPath nếu cần override.
/// </summary>
public class DailyRewardManager : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────

    public static DailyRewardManager Instance { get; private set; }

    // ─── Inspector fields ─────────────────────────────────────────────────────

    [Header("Config")]
    [Tooltip("Path inside Resources/ folder (no extension).")]
    [SerializeField] private string _configPath = "Configs/daily_reward_config";

    // ─── Events (Observer Pattern) ────────────────────────────────────────────

    /// <summary>
    /// Fired mỗi khi trạng thái có thể claim thay đổi.
    /// Argument: true nếu có thể claim hôm nay, false nếu đã claim rồi.
    /// </summary>
    public event Action<bool> OnClaimStateChanged;

    /// <summary>
    /// Fired ngay sau khi player claim reward thành công.
    /// Argument: số thứ tự ngày vừa được claim (1–7).
    /// </summary>
    public event Action<int> OnRewardClaimed;

    // ─── Private state ────────────────────────────────────────────────────────

    private DailyRewardConfigData _config;

    /// <summary>Ngày trong chu kỳ (1–7) tương ứng với hôm nay.</summary>
    private int _todayDay;

    /// <summary>True nếu player chưa claim hôm nay.</summary>
    private bool _canClaimToday;

    // ─── Public properties ────────────────────────────────────────────────────

    public DailyRewardConfigData Config       => _config;
    public int                   TodayDay     => _todayDay;
    public bool                  CanClaimToday => _canClaimToday;

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        LoadConfig();
    }

    private void Start()
    {
        // Gọi trong Start() để đảm bảo PlayerDataManager.Awake() đã chạy xong.
        ComputeTodayState();

        // Fire event sau khi tính xong state — fix race condition:
        // Nếu DailyRewardUI.Start() đã chạy TRƯỚC và thấy canClaim=false (default bool),
        // event này sẽ trigger RefreshAll() để UI cập nhật đúng trạng thái.
        // Nếu DailyRewardUI.Start() chưa chạy, event đi vào void (chưa có subscriber) —
        // sau đó DailyRewardUI.Start() sẽ gọi RefreshAll() và thấy đúng state.
        OnClaimStateChanged?.Invoke(_canClaimToday);
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Claim phần thưởng ngày hôm nay.
    /// Guard: bỏ qua nếu đã claim hoặc config chưa load.
    /// Cộng reward vào PlayerDataManager, cập nhật save, fire events.
    /// </summary>
    public void ClaimTodayReward()
    {
        if (!_canClaimToday)
        {
            Debug.LogWarning("[DailyRewardManager] Cannot claim — already claimed today or not ready.");
            return;
        }

        if (_config == null || _config.days == null || _todayDay < 1 || _todayDay > _config.days.Length)
        {
            Debug.LogError("[DailyRewardManager] Config invalid — cannot claim.");
            return;
        }

        // Lấy phần thưởng của ngày hôm nay (array 0-indexed, day 1-indexed).
        DailyRewardDayData reward = _config.days[_todayDay - 1];

        // ── Áp dụng phần thưởng ──────────────────────────────────────────────
        if (reward.coin > 0)
            PlayerDataManager.Instance.AddCoin(reward.coin);

        if (!string.IsNullOrEmpty(reward.powerUpId) && reward.powerUpAmount > 0)
            PlayerDataManager.Instance.AddPowerUp(reward.powerUpId, reward.powerUpAmount);

        // ── Persist trạng thái claim ──────────────────────────────────────────
        DailyRewardSaveData save = PlayerDataManager.Instance.DailyReward;
        save.lastClaimDate  = DateTime.Now.ToString("yyyy-MM-dd");
        save.lastClaimedDay = _todayDay;
        PlayerDataManager.Instance.Save();

        // ── Cập nhật in-memory state ──────────────────────────────────────────
        _canClaimToday = false;

        int claimedDay = _todayDay;
        Debug.Log($"[DailyRewardManager] Claimed day {claimedDay}: " +
                  $"+{reward.coin} coin, +{reward.powerUpAmount} '{reward.powerUpId}'.");

        OnRewardClaimed?.Invoke(claimedDay);
        OnClaimStateChanged?.Invoke(false);
    }

    // ─── Private logic ────────────────────────────────────────────────────────

    /// <summary>Đọc daily_reward_config.json và khởi tạo _config.</summary>
    private void LoadConfig()
    {
        TextAsset jsonAsset = Resources.Load<TextAsset>(_configPath);

        if (jsonAsset == null)
        {
            Debug.LogError($"[DailyRewardManager] Config not found at Resources/{_configPath}.json");
            return;
        }

        _config = JsonUtility.FromJson<DailyRewardConfigData>(jsonAsset.text);

        if (_config?.days == null || _config.days.Length == 0)
        {
            Debug.LogError("[DailyRewardManager] Config parsed but days array is empty.");
            _config = null;
            return;
        }

        Debug.Log($"[DailyRewardManager] Loaded {_config.days.Length} day(s) of rewards.");
    }

    /// <summary>
    /// Tính toán _todayDay và _canClaimToday dựa trên save data hiện tại.
    /// Gọi trong Start() để đảm bảo PlayerDataManager.Instance sẵn sàng.
    ///
    /// Điều kiện "lần đầu vào" (isFirstTime) = lastClaimDate rỗng HOẶC lastClaimedDay == 0.
    /// Cả hai trường hợp đều → ngày 1, có thể claim.
    /// Điều này đảm bảo đúng kể cả với save file cũ (không có field dailyReward).
    /// </summary>
    private void ComputeTodayState()
    {
        if (PlayerDataManager.Instance == null)
        {
            Debug.LogError("[DailyRewardManager] PlayerDataManager.Instance not found in Start.");
            return;
        }

        DailyRewardSaveData save = PlayerDataManager.Instance.DailyReward;

        // Guard: save null xảy ra khi save file cũ không có field dailyReward
        // và JsonUtility không khởi tạo nested object.
        if (save == null)
        {
            _todayDay      = 1;
            _canClaimToday = true;
            Debug.LogWarning("[DailyRewardManager] DailyReward save data is null — defaulting to day 1.");
            return;
        }

        string todayStr     = DateTime.Now.ToString("yyyy-MM-dd");
        string yesterdayStr = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd");

        // "Lần đầu vào": chưa bao giờ claim (lastClaimDate rỗng) HOẶC
        // lastClaimedDay == 0 (save file cũ không có trường này).
        bool isFirstTime         = string.IsNullOrEmpty(save.lastClaimDate) || save.lastClaimedDay == 0;
        bool alreadyClaimedToday = !isFirstTime && save.lastClaimDate == todayStr;
        bool isConsecutiveDay    = !isFirstTime && save.lastClaimDate == yesterdayStr;

        if (isFirstTime)
        {
            // Lần đầu vào hoặc data bị reset → ngày 1 luôn available.
            _todayDay      = 1;
            _canClaimToday = true;
            Debug.Log("[DailyRewardManager] First time — day 1 available.");
        }
        else if (alreadyClaimedToday)
        {
            // Đã claim hôm nay → hiện trạng thái ngày đã claim, không thể claim thêm.
            _todayDay      = save.lastClaimedDay;
            _canClaimToday = false;
            Debug.Log($"[DailyRewardManager] Already claimed today (day {_todayDay}).");
        }
        else if (isConsecutiveDay)
        {
            // Streak tiếp tục: ngày tiếp theo sau ngày đã claim.
            // Nếu đã claim ngày 7 hôm qua → cycle mới → về ngày 1.
            _todayDay      = save.lastClaimedDay >= 7 ? 1 : save.lastClaimedDay + 1;
            _canClaimToday = true;
            Debug.Log($"[DailyRewardManager] Streak continues — day {_todayDay} available.");
        }
        else
        {
            // Bỏ ngày (missed) → reset streak về ngày 1.
            _todayDay      = 1;
            _canClaimToday = true;
            Debug.Log($"[DailyRewardManager] Streak broken (last: {save.lastClaimDate}) — reset to day 1.");
        }
    }
}
