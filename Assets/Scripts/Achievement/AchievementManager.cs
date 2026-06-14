using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Singleton quản lý toàn bộ logic achievement của game.
///
/// DontDestroyOnLoad — tồn tại qua nhiều scene/session, chỉ có 1 instance toàn game.
///
/// 5 Achievements:
///   #0 Pizza Master  — ghép 50 đĩa (tích lũy all-time).
///   #1 Collector     — unlock 5 loại slice (tích lũy qua save data).
///   #2 Pure Skill    — hoàn thành 1 ván: không dùng power-up + đủ 6 slice + level ≥ 12.
///   #3 Rich Baker    — thu thập 1.000 coin tích lũy từ mọi nguồn.
///   #4 Loyal Fan     — đăng nhập 7 ngày liên tiếp.
///
/// Observer Pattern:
///   OnProgressChanged(int achievementId, int progress, int target) — UI cập nhật slider.
///   OnAchievementUnlocked(int achievementId)                       — hiển thị popup / fx.
///
/// Event sources:
///   MergeAnimator.OnPlateCompleted       → #0
///   PlayerDataManager.OnCoinEarned       → #3 (triggered bởi AddCoin bất kỳ nguồn nào)
///   UnlockManager.OnTypeUnlocked         → #1 (đọc lại SliceUnlocked từ save)
///   DailyRewardManager.OnRewardClaimed   → #4
///   GameOverManager.OnGameOver           → #2 (check điều kiện)
/// </summary>
public class AchievementManager : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────

    public static AchievementManager Instance { get; private set; }

    // ─── Inspector fields ─────────────────────────────────────────────────────

    [Header("Config")]
    [Tooltip("Path inside Resources/ folder (no extension).")]
    [SerializeField] private string _configPath = "Configs/achievement_config";

    [Header("Audio")]
    [SerializeField] private AudioClip _clickSound;
    [SerializeField] private AudioSource _audioSource;

    // ─── Events (Observer Pattern) ────────────────────────────────────────────

    /// <summary>
    /// Fired khi tiến trình của 1 achievement thay đổi (kể cả khi complete).
    /// args: achievementId, progress hiện tại, target value.
    /// UI dùng event này để cập nhật fillAmount của Slider.
    /// </summary>
    public event System.Action<int, int, int> OnProgressChanged;

    /// <summary>
    /// Fired đúng 1 lần khi achievement được hoàn thành lần đầu (nhưng chưa nhận thưởng).
    /// Argument: achievementId vừa hoàn thành.
    /// UI có thể dùng event này để hiển thị popup "Achievement Unlocked!".
    /// </summary>
    public event System.Action<int> OnAchievementUnlocked;

    /// <summary>
    /// Fired khi trạng thái có/không có achievement chưa nhận thưởng thay đổi.
    /// Dùng để bật/tắt cái chấm đỏ thông báo (Red Dot).
    /// </summary>
    public event System.Action<bool> OnUnclaimedStatusChanged;

    // ─── Private state ────────────────────────────────────────────────────────

    private AchievementConfigCollection _config;

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        LoadConfig();
    }

    private void Start()
    {
        // Subscribe trong Start() để đảm bảo tất cả Awake() đã hoàn tất.
        // MergeAnimator sử dụng static event nên không cần instance.
        MergeAnimator.OnPlateCompleted += HandlePlateCompleted;

        if (PlayerDataManager.Instance != null)
            PlayerDataManager.Instance.OnCoinEarned += HandleCoinEarned;

        // UnlockManager và DailyRewardManager có thể không cùng scene → guard bằng null check.
        if (UnlockManager.Instance != null)
            UnlockManager.Instance.OnTypeUnlocked += HandleTypeUnlocked;

        if (DailyRewardManager.Instance != null)
            DailyRewardManager.Instance.OnRewardClaimed += HandleDailyRewardClaimed;

        // GameOverManager dùng static event — không cần reference.
        GameOverManager.OnGameOver += HandleGameOver;

        // Đồng bộ tiến trình Achievement #1 từ dữ liệu SliceUnlocked
        if (PlayerDataManager.Instance != null)
        {
            UpdateProgress(1, PlayerDataManager.Instance.SliceUnlocked);
        }

        // Sync UI ngay khi bắt đầu để slider hiển thị đúng từ save data.
        SyncAllProgressToUI();
        
        // Bắn event trạng thái chấm đỏ ngay khi start cho UI mới khởi tạo.
        OnUnclaimedStatusChanged?.Invoke(HasAnyUnclaimedAchievement());
    }

    private void OnDestroy()
    {
        MergeAnimator.OnPlateCompleted -= HandlePlateCompleted;

        if (PlayerDataManager.Instance != null)
            PlayerDataManager.Instance.OnCoinEarned -= HandleCoinEarned;

        if (UnlockManager.Instance != null)
            UnlockManager.Instance.OnTypeUnlocked -= HandleTypeUnlocked;

        if (DailyRewardManager.Instance != null)
            DailyRewardManager.Instance.OnRewardClaimed -= HandleDailyRewardClaimed;

        GameOverManager.OnGameOver -= HandleGameOver;
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Trả về AchievementData của achievement <paramref name="id"/>.
    /// Null nếu config chưa load hoặc id không hợp lệ.
    /// </summary>
    public AchievementData GetAchievementData(int id)
    {
        if (_config?.achievements == null) return null;
        foreach (var ach in _config.achievements)
        {
            if (ach.id == id) return ach;
        }
        return null;
    }

    /// <summary>
    /// Nhận tất cả các phần thưởng của những achievement đã hoàn thành nhưng chưa nhận.
    /// Gắn hàm này vào sự kiện OnClick của nút Collect dùng chung.
    /// </summary>
    public void ClaimAllRewards()
    {
        if (_clickSound != null && _audioSource != null)
        {
            _audioSource.PlayOneShot(_clickSound);
        }

        if (_config?.achievements == null || PlayerDataManager.Instance == null) return;

        bool hasClaimedAnything = false;

        foreach (var ach in _config.achievements)
        {
            int progress = PlayerDataManager.Instance.GetAchievementProgress(ach.id);
            bool isClaimed = PlayerDataManager.Instance.IsAchievementClaimed(ach.id);

            // Nếu đủ điều kiện nhận
            if (progress >= ach.targetValue && !isClaimed)
            {
                Debug.Log($"[AchievementManager] Claiming reward for #{ach.id}");

                // Đánh dấu đã nhận thưởng
                PlayerDataManager.Instance.SetAchievementClaimed(ach.id);

                // Phát phần thưởng
                if (ach.reward != null)
                {
                    if (ach.reward.coin > 0)
                        PlayerDataManager.Instance.AddCoin(ach.reward.coin);

                    if (!string.IsNullOrEmpty(ach.reward.powerUpId) && ach.reward.powerUpAmount > 0)
                        PlayerDataManager.Instance.AddPowerUp(ach.reward.powerUpId, ach.reward.powerUpAmount);
                }

                // Cập nhật lại thanh UI Slot của achievement này
                OnProgressChanged?.Invoke(ach.id, progress, ach.targetValue);
                hasClaimedAnything = true;
            }
        }

        // Bắn event cập nhật UI sau khi nhận xong
        if (hasClaimedAnything)
        {
            OnUnclaimedStatusChanged?.Invoke(HasAnyUnclaimedAchievement());
        }
    }

    /// <summary>
    /// Trả về true nếu có bất kỳ achievement nào đã hoàn thành nhưng chưa nhận thưởng.
    /// </summary>
    public bool HasAnyUnclaimedAchievement()
    {
        if (_config?.achievements == null || PlayerDataManager.Instance == null) return false;

        foreach (var ach in _config.achievements)
        {
            int progress = PlayerDataManager.Instance.GetAchievementProgress(ach.id);
            bool isClaimed = PlayerDataManager.Instance.IsAchievementClaimed(ach.id);

            if (progress >= ach.targetValue && !isClaimed)
                return true;
        }

        return false;
    }

    // ─── Private event handlers ───────────────────────────────────────────────

    /// <summary>
    /// Achievement #0: mỗi plate hoàn thành → tăng all-time counter và kiểm tra.
    /// </summary>
    private void HandlePlateCompleted()
    {
        if (PlayerDataManager.Instance == null) return;

        PlayerDataManager.Instance.AddTotalPlate();
        int progress = PlayerDataManager.Instance.TotalPlatesAllTime;
        UpdateProgress(0, progress);
    }

    /// <summary>
    /// Achievement #3: mỗi khi coin được cộng vào (từ bất kỳ nguồn nào).
    /// Argument: totalCoinEarned sau khi cộng.
    /// </summary>
    private void HandleCoinEarned(int totalEarned)
    {
        UpdateProgress(3, totalEarned);
    }

    /// <summary>
    /// Achievement #1: mỗi khi 1 loại slice mới được unlock.
    /// Đọc SliceUnlocked từ save (đã được AddSliceUnlocked() tăng trước đó).
    /// </summary>
    private void HandleTypeUnlocked(string _typeId)
    {
        if (PlayerDataManager.Instance == null) return;
        int progress = PlayerDataManager.Instance.SliceUnlocked;
        UpdateProgress(1, progress);
    }

    /// <summary>
    /// Achievement #4: khi player claim daily reward.
    /// Argument: ngày trong chu kỳ vừa được claim (1–7).
    /// Progress = ngày claim (tăng dần đến 7). Khi complete → save cố định ở 7.
    /// </summary>
    private void HandleDailyRewardClaimed(int dayNumber)
    {
        if (PlayerDataManager.Instance == null) return;

        // Lấy progress hiện tại — có thể đã được completed ở ván trước.
        int storedProgress = PlayerDataManager.Instance.GetAchievementProgress(4);
        AchievementData data = GetAchievementData(4);
        if (data == null) return;

        // Không overwrite nếu đã complete.
        if (storedProgress >= data.targetValue) return;

        // Cập nhật theo ngày claim hiện tại (không giảm dù streak reset).
        int newProgress = Mathf.Max(storedProgress, dayNumber);
        UpdateProgress(4, newProgress);
    }

    /// <summary>
    /// Achievement #2 (Pure Skill): kiểm tra điều kiện khi game over.
    /// Điều kiện: không dùng power-up + đủ 6 loại slice + level trong ván ≥ 12.
    /// </summary>
    private void HandleGameOver()
    {
        AchievementData data = GetAchievementData(2);
        if (data == null) return;

        // Đã hoàn thành rồi thì không check lại.
        if (PlayerDataManager.Instance != null &&
            PlayerDataManager.Instance.GetAchievementProgress(2) >= data.targetValue)
            return;

        // Điều kiện 1: không dùng power-up trong session này.
        if (GameStateMachine.Instance != null && GameStateMachine.Instance.PowerUpUsedThisSession)
        {
            Debug.Log("[AchievementManager] #2 fail: power-up was used this session.");
            return;
        }

        // Điều kiện 2: đã unlock đủ 6 loại slice (cả 3 loại mới pizza_4/5/6).
        bool allSlicesUnlocked = UnlockManager.Instance != null &&
                                 UnlockManager.Instance.UnlockedTypeIds.Count >= 6;
        if (!allSlicesUnlocked)
        {
            Debug.Log("[AchievementManager] #2 fail: not all slice types unlocked.");
            return;
        }

        // Điều kiện 3: đạt level ≥ minLevelRequired trong ván này.
        int currentLevel = ScoreManager.Instance != null ? ScoreManager.Instance.CurrentLevel : 0;
        if (currentLevel < data.minLevelRequired)
        {
            Debug.Log($"[AchievementManager] #2 fail: level {currentLevel} < {data.minLevelRequired}.");
            return;
        }

        // Tất cả điều kiện đạt → ghi progress = 1 (targetValue).
        Debug.Log("[AchievementManager] #2 ACHIEVED: Pure Skill!");
        UpdateProgress(2, 1);
    }

    // ─── Core progress logic ──────────────────────────────────────────────────

    /// <summary>
    /// Cập nhật tiến trình achievement <paramref name="achievementId"/> với <paramref name="newProgress"/>.
    /// Clamp về [current, targetValue]. Fire OnProgressChanged và (nếu complete) OnAchievementUnlocked.
    /// </summary>
    private void UpdateProgress(int achievementId, int newProgress)
    {
        AchievementData data = GetAchievementData(achievementId);
        if (data == null) return;

        PlayerDataManager pdm = PlayerDataManager.Instance;
        if (pdm == null) return;

        int stored = pdm.GetAchievementProgress(achievementId);

        // Chỉ tăng, không bao giờ giảm. Clamp tối đa target.
        int clamped = Mathf.Clamp(newProgress, stored, data.targetValue);
        if (clamped == stored) return; // không có thay đổi gì

        pdm.SetAchievementProgress(achievementId, clamped);
        OnProgressChanged?.Invoke(achievementId, clamped, data.targetValue);

        Debug.Log($"[AchievementManager] #{achievementId} progress: {clamped}/{data.targetValue}");

        // Kiểm tra complete.
        if (clamped >= data.targetValue)
            CompleteAchievement(achievementId, data);
    }

    /// <summary>
    /// Xử lý khi achievement vừa được hoàn thành lần đầu.
    /// Không tự động phát reward nữa, chỉ cập nhật trạng thái UI/Red Dot.
    /// </summary>
    private void CompleteAchievement(int achievementId, AchievementData data)
    {
        Debug.Log($"[AchievementManager] Achievement #{achievementId} COMPLETED!");

        // ── Fire event (UI popup, SFX, v.v.) ─────────────────────────────────
        OnAchievementUnlocked?.Invoke(achievementId);
        
        // Cập nhật lại UI thông qua event
        OnUnclaimedStatusChanged?.Invoke(HasAnyUnclaimedAchievement());
    }

    /// <summary>
    /// Đọc toàn bộ progress từ save và fire OnProgressChanged cho UI.
    /// Gọi trong Start() để slider hiển thị đúng ngay khi mở panel.
    /// </summary>
    private void SyncAllProgressToUI()
    {
        if (_config?.achievements == null) return;
        if (PlayerDataManager.Instance == null) return;

        foreach (AchievementData data in _config.achievements)
        {
            int progress = PlayerDataManager.Instance.GetAchievementProgress(data.id);
            OnProgressChanged?.Invoke(data.id, progress, data.targetValue);
        }
    }

    // ─── Config loading ───────────────────────────────────────────────────────

    private void LoadConfig()
    {
        TextAsset jsonAsset = Resources.Load<TextAsset>(_configPath);

        if (jsonAsset == null)
        {
            Debug.LogError($"[AchievementManager] Config not found at Resources/{_configPath}.json");
            return;
        }

        _config = JsonUtility.FromJson<AchievementConfigCollection>(jsonAsset.text);

        if (_config?.achievements == null)
        {
            Debug.LogError("[AchievementManager] Failed to parse AchievementConfigCollection.");
            return;
        }

        Debug.Log($"[AchievementManager] Loaded {_config.achievements.Length} achievement(s).");
    }
}
