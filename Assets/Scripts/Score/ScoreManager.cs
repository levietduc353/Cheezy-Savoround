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
///
/// Subscribes to MergeAnimator.OnPlateCompleted (Observer Pattern).
/// Broadcasts OnFillChanged(float) for ScoreUI to consume.
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

    // ─── Private state ────────────────────────────────────────────────────────

    private int _currentScore;
    private int _currentThreshold;

    // ─── Public properties ────────────────────────────────────────────────────

    public int   CurrentScore     => _currentScore;
    public int   CurrentThreshold => _currentThreshold;

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

        // Check if fill bar is complete.
        if (_currentScore >= _currentThreshold)
        {
            _currentScore = 0;

            // Increase threshold by 1.5× for the next cycle.
            _currentThreshold = Mathf.RoundToInt(_currentThreshold * 1.5f);

            OnBarCompleted?.Invoke(_currentThreshold);
            Debug.Log($"[ScoreManager] Bar complete! New threshold: {_currentThreshold}");
        }

        float fill = FillAmount;
        OnFillChanged?.Invoke(fill);
        Debug.Log($"[ScoreManager] Score: {_currentScore}/{_currentThreshold} (fill={fill:P0})");
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

        Debug.Log($"[ScoreManager] Loaded. Initial threshold: {_currentThreshold}");
    }
}
