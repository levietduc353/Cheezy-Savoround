using UnityEngine;

/// <summary>
/// Singleton that manages the score fill bar logic.
///
/// Rules:
///   - Each plate completion increments the internal score counter by 1.
///   - Fill amount = currentScore / currentThreshold (0..1).
///   - When fill reaches 1 (currentScore >= threshold):
///       • currentScore resets to 0
///       • threshold = Mathf.RoundToInt(threshold * 1.5f)
///       • _currentLevel increments by 1
///   - Every coinRewardEveryLevels levels (e.g. every 3), coinRewardAmount coin is awarded
///     via PlayerDataManager.AddCoin().
///
/// Subscribes to MergeAnimator.OnPlateCompleted (Observer Pattern).
/// Broadcasts OnFillChanged(float) and OnLevelChanged(int) for ScoreUI to consume.
/// All parameters loaded from Resources/Configs/score_config.json.
/// </summary>
public class ScoreManager : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────

    public static ScoreManager Instance { get; private set; }

    // ─── Inspector fields ─────────────────────────────────────────────────────

    [Header("Config")]
    [Tooltip("Path inside Resources/ folder (no extension).")]
    [SerializeField] private string _configPath = "Configs/score_config";

    [Header("Audio")]
    [Tooltip("Sound to play when the player levels up.")]
    [SerializeField] private AudioClip _levelUpSound;
    [Tooltip("AudioSource to play the level up sound.")]
    [SerializeField] private AudioSource _audioSource;

    // ─── Events (Observer Pattern) ────────────────────────────────────────────

    /// <summary>
    /// Fired every time the fill bar value changes.
    /// Argument: normalized fill amount in [0, 1].
    /// </summary>
    public event System.Action<float> OnFillChanged;

    /// <summary>
    /// Fired each time the bar completes a full cycle (fill reached 1.0).
    /// Carries the new (increased) threshold value.
    /// </summary>
    public event System.Action<int> OnBarCompleted;

    /// <summary>
    /// Fired each time the player levels up (fill bar completed).
    /// Argument: the new current level (1-based).
    /// </summary>
    public event System.Action<int> OnLevelChanged;

    /// <summary>
    /// Fired every time a plate is completed (total cumulative count).
    /// Argument: the new total plates completed count.
    /// </summary>
    public event System.Action<int> OnTotalScoreChanged;

    // ─── Private state ─────────────────────────────────────────────────────────

    private int _currentScore;
    private int _currentThreshold;
    private int _currentLevel;

    // ─ Coin reward config (loaded from score_config.json) ─────────────────
    private int _coinRewardAmount      = 20; // coin thưởng mỗi mốc
    private int _coinRewardEveryLevels = 3;  // cứ mỗi X level thì thưởng

    /// <summary>Level cao nhất đã được nhận thưởng coin, tránh thưởng trung.</summary>
    private int _lastCoinRewardLevel;

    /// <summary>Tổng số plate đã hoàn thành từ đầu game — không bao giờ reset.</summary>
    private int _totalPlatesCompleted;

    // ─── Public properties ────────────────────────────────────────────────────

    public int   CurrentScore     => _currentScore;
    public int   CurrentThreshold => _currentThreshold;

    /// <summary>Current level (starts at 1, increments each time the fill bar completes).</summary>
    public int   CurrentLevel     => _currentLevel;

    /// <summary>Total plates completed since the game started — never resets mid-session.</summary>
    public int   TotalPlatesCompleted => _totalPlatesCompleted;

    /// <summary>Normalized fill amount in [0, 1].</summary>
    public float FillAmount => _currentThreshold > 0
        ? (float)_currentScore / _currentThreshold
        : 0f;

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        LoadConfig();
    }

    private void OnEnable()
    {
        MergeAnimator.OnPlateCompleted += HandlePlateCompleted;
    }

    private void OnDisable()
    {
        MergeAnimator.OnPlateCompleted -= HandlePlateCompleted;
    }

    // ─── Private logic ────────────────────────────────────────────────────────

    /// <summary>
    /// Called by MergeAnimator each time a plate is fully completed and dismissed.
    /// Increments score, checks for bar-fill, and broadcasts the new fill amount.
    /// </summary>
    private void HandlePlateCompleted()
    {
        _currentScore++;

        // Accumulate total — never resets; used for Game Over score display.
        _totalPlatesCompleted++;
        OnTotalScoreChanged?.Invoke(_totalPlatesCompleted);

        // Check if fill bar is complete.
        if (_currentScore >= _currentThreshold)
        {
            _currentScore = 0;

            // Increase threshold by 1.5× for the next cycle.
            _currentThreshold = Mathf.RoundToInt(_currentThreshold * 1.5f);

            // Increment level and notify subscribers (e.g. ScoreUI's TMP labels).
            _currentLevel++;
            OnLevelChanged?.Invoke(_currentLevel);
            PlayLevelUpSound();

            // ── Coin reward: thưởng mỗi _coinRewardEveryLevels level ────────────────────
            // Dùng _lastCoinRewardLevel để đảm bảo mỗi mốc chỉ thưởng đúng 1 lần
            // dù HandlePlateCompleted có thể được gọi nhiều lần liên tiếp.
            int rewardMilestone = (_currentLevel / _coinRewardEveryLevels) * _coinRewardEveryLevels;
            if (rewardMilestone > 0 && rewardMilestone > _lastCoinRewardLevel
                && _currentLevel % _coinRewardEveryLevels == 0)
            {
                _lastCoinRewardLevel = rewardMilestone;
                PlayerDataManager.Instance?.AddCoin(_coinRewardAmount);
                Debug.Log($"[ScoreManager] Level {_currentLevel} → coin reward +{_coinRewardAmount}!");
            }    

            OnBarCompleted?.Invoke(_currentThreshold);
            Debug.Log($"[ScoreManager] Bar complete! Level: {_currentLevel}, New threshold: {_currentThreshold}");
        }

        float fill = FillAmount;
        OnFillChanged?.Invoke(fill);
        Debug.Log($"[ScoreManager] Score: {_currentScore}/{_currentThreshold} | Total: {_totalPlatesCompleted} (fill={fill:P0})");
    }

    /// <summary>Reads score_config.json and initialises threshold.</summary>
    private void LoadConfig()
    {
        TextAsset jsonAsset = Resources.Load<TextAsset>(_configPath);

        if (jsonAsset == null)
        {
            Debug.LogError($"[ScoreManager] Config not found at Resources/{_configPath}.json. " +
                           "Using fallback threshold = 5.");
            _currentThreshold = 5;
            return;
        }

        ScoreConfigData config = JsonUtility.FromJson<ScoreConfigData>(jsonAsset.text);

        if (config == null || config.initialScoreThreshold <= 0)
        {
            Debug.LogError("[ScoreManager] Failed to parse ScoreConfigData. Using fallback threshold = 5.");
            _currentThreshold = 5;
            return;
        }

        _currentThreshold = config.initialScoreThreshold;
        _currentScore     = 0;
        _currentLevel     = 1;  // Level bắt đầu từ 1.

        // Load coin reward params (optional — fall back to defaults if absent).
        if (config.coinRewardEveryLevels > 0)
            _coinRewardEveryLevels = config.coinRewardEveryLevels;
        if (config.coinRewardAmount > 0)
            _coinRewardAmount = config.coinRewardAmount;

        // Khởi tạo _lastCoinRewardLevel = 0 (chưa thưởng lần nào).
        _lastCoinRewardLevel = 0;

        Debug.Log($"[ScoreManager] Loaded. Initial threshold: {_currentThreshold}, " +
                  $"coin reward: +{_coinRewardAmount} every {_coinRewardEveryLevels} levels.");
    }

    private void PlayLevelUpSound()
    {
        if (_levelUpSound != null && _audioSource != null)
        {
            _audioSource.PlayOneShot(_levelUpSound);
        }
    }
}
